using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PriceSafari.Services.AllegroServices
{

    public record AllegroApiSummary(
        decimal? CustomerPrice,
        decimal? SellerRevenue,
        decimal? Commission,
        string? Ean,
        bool IsAnyPromoActive,
        bool IsSubsidyActive,
        bool IsInvitationActive,
        decimal? InvitationPrice
    );

    internal record BadgeData(string CampaignName, JsonNode? Prices);
    internal record AlleDiscountData(string CampaignName, JsonNode? Prices);

    public class AllegroApiBotService
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<AllegroApiBotService> _logger;
        private static readonly HttpClient _httpClient = new();

        public AllegroApiBotService(PriceSafariContext context, ILogger<AllegroApiBotService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ApiProcessingResult> ProcessOffersForActiveStoresAsync()
        {

            var result = new ApiProcessingResult();
            _logger.LogInformation("Rozpoczynam proces pobierania dodatkowych danych z API Allegro...");

            var activeStores = await _context.Stores
                .Where(s => s.OnAllegro && s.FetchExtendedAllegroData && s.IsAllegroTokenActive && !string.IsNullOrEmpty(s.AllegroApiToken))
                .AsNoTracking()
                .ToListAsync();

            if (!activeStores.Any())
            {
                _logger.LogInformation("Nie znaleziono aktywnych sklepów z włączoną opcją pobierania danych z API Allegro.");
                result.Messages.Add("Brak aktywnych sklepów do przetworzenia.");
                return result;
            }

            _logger.LogInformation("Znaleziono {Count} sklepów do przetworzenia: {StoreNames}",
                activeStores.Count, string.Join(", ", activeStores.Select(s => s.StoreName)));

            result.StoresProcessedCount = activeStores.Count;

            foreach (var store in activeStores)
            {
                var storeResult = await ProcessOffersForSingleStore(store);
                result.TotalOffersProcessed += storeResult.processedCount;
                if (!storeResult.success)
                {
                    result.Success = false;
                    result.Messages.Add(storeResult.message);
                }
            }

            _logger.LogInformation("Zakończono proces pobierania dodatkowych danych z API Allegro.");
            return result;
        }

        private async Task<(bool success, int processedCount, string message)> ProcessOffersForSingleStore(StoreClass store)
        {
            _logger.LogInformation("Przetwarzam sklep: {StoreName} (ID: {StoreId})", store.StoreName, store.StoreId);

            var offersToProcess = await _context.AllegroOffersToScrape
                .Where(o => o.StoreId == store.StoreId && o.IsScraped && o.IsApiProcessed != true)
                .ToListAsync();

            if (!offersToProcess.Any())
            {
                _logger.LogInformation("Brak nowych ofert do przetworzenia dla sklepu {StoreName}.", store.StoreName);
                return (true, 0, string.Empty);
            }

            _logger.LogInformation("Znaleziono {Count} ofert do przetworzenia dla sklepu {StoreName}.", offersToProcess.Count, store.StoreName);
            string accessToken = store.AllegroApiToken!;

            try
            {
                int batchSize = 100;
                int processedCount = 0;

                foreach (var offer in offersToProcess)
                {

                    var apiData = await FetchApiDataForOffer(accessToken, offer.AllegroOfferId.ToString());

                    if (apiData != null)
                    {

                        offer.ApiAllegroPriceFromUser = apiData.SellerRevenue;
                        offer.ApiAllegroPrice = apiData.CustomerPrice;
                        offer.ApiAllegroCommission = apiData.Commission;
                        offer.AllegroEan = apiData.Ean;
                        offer.AnyPromoActive = apiData.IsAnyPromoActive;

                        offer.IsSubsidyActive = apiData.IsSubsidyActive;
                        offer.IsInvitationActive = apiData.IsInvitationActive;
                        offer.InvitationPrice = apiData.InvitationPrice;
                    }
                    offer.IsApiProcessed = true;
                    processedCount++;

                    if (processedCount % batchSize == 0 || processedCount == offersToProcess.Count)
                    {
                        await _context.SaveChangesAsync();
                        _logger.LogInformation("Zapisano partię {BatchNum}/{TotalBatches} ofert dla sklepu {StoreName}.",
                            (processedCount / batchSize), (offersToProcess.Count / batchSize) + 1, store.StoreName);
                    }
                }

                _logger.LogInformation("Zakończono przetwarzanie i zapisano łącznie dane dla {Count} ofert dla sklepu {StoreName}.", offersToProcess.Count, store.StoreName);
                return (true, offersToProcess.Count, string.Empty);
            }
            catch (AllegroAuthException ex)
            {
                _logger.LogError(ex, "Błąd autoryzacji API Allegro dla sklepu {StoreName}. Token może być nieważny.", store.StoreName);

                foreach (var offer in offersToProcess)
                {
                    offer.IsApiProcessed = true;
                }
                await _context.SaveChangesAsync();
                return (false, 0, $"Token API dla sklepu '{store.StoreName}' jest nieważny.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił nieoczekiwany błąd podczas przetwarzania ofert dla sklepu {StoreName}", store.StoreName);
                return (false, 0, $"Nieoczekiwany błąd dla sklepu '{store.StoreName}': {ex.Message}");
            }
        }

        private async Task<AllegroApiSummary?> FetchApiDataForOffer(string accessToken, string offerId)
        {
            try
            {

                var offerDataNode = await GetOfferData(accessToken, offerId);
                if (offerDataNode == null) return null;

                var basePriceStr = offerDataNode["sellingMode"]?["price"]?["amount"]?.ToString();
                if (!decimal.TryParse(basePriceStr, CultureInfo.InvariantCulture, out var basePrice))
                {
                    _logger.LogWarning("Nie można sparsować ceny bazowej dla oferty {OfferId}", offerId);
                    return null;
                }

                string? ean = null;
                try
                {
                    var parameters = offerDataNode["productSet"]?[0]?["product"]?["parameters"]?.AsArray();
                    var eanValue = parameters?.FirstOrDefault(p => p?["id"]?.ToString() == "225693")?["values"]?[0]?.ToString();
                    if (!string.IsNullOrWhiteSpace(eanValue) && eanValue != "Brak") ean = eanValue;
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Nie udało się sparsować EAN dla {OfferId}", offerId); }

                var badgeCampaigns = await GetBadgesAsync(accessToken, offerId, offerDataNode);
                var alleDiscountCampaigns = await GetAlleDiscountsAsync(accessToken, offerId);
                var commission = await GetOfferCommission(accessToken, offerDataNode);

                decimal sellerEarns = basePrice;
                decimal customerPays = basePrice;
                string? invitationPriceStr = null;
                bool isAnyPromoActive = false;
                bool isSubsidyActive = false;
                bool isInvitationActive = false;

                var activeAlleDiscount = alleDiscountCampaigns.FirstOrDefault();
                var activeSubsidyBadge = badgeCampaigns.FirstOrDefault(b => b.Prices?["subsidy"] != null);
                var activeBargainBadge = badgeCampaigns.FirstOrDefault(b => b.Prices?["bargain"] != null && b.Prices?["subsidy"] == null);

                if (activeAlleDiscount != null)
                {
                    var proposedPriceStr = activeAlleDiscount.Prices?["proposedPrice"]?["amount"]?.ToString();
                    var customerPriceStr = activeAlleDiscount.Prices?["maximumSellingPrice"]?["amount"]?.ToString();

                    if (decimal.TryParse(proposedPriceStr, CultureInfo.InvariantCulture, out sellerEarns) &&
                        decimal.TryParse(customerPriceStr, CultureInfo.InvariantCulture, out customerPays))
                    {
                        isAnyPromoActive = true;
                        isSubsidyActive = true;
                    }
                }

                else if (activeSubsidyBadge != null)
                {
                    var targetPriceStr = activeSubsidyBadge.Prices?["subsidy"]?["targetPrice"]?["amount"]?.ToString();
                    var originalPriceStr = activeSubsidyBadge.Prices?["subsidy"]?["originalPrice"]?["amount"]?.ToString();
                    var marketPriceInBadgeStr = activeSubsidyBadge.Prices?["market"]?["amount"]?.ToString();

                    if (decimal.TryParse(targetPriceStr, CultureInfo.InvariantCulture, out var targetPrice))
                    {
                        bool isTrueInvitation = false;
                        if (decimal.TryParse(marketPriceInBadgeStr, CultureInfo.InvariantCulture, out var marketPriceInBadge))
                        {
                            if (basePrice == marketPriceInBadge && basePrice > targetPrice)
                            {
                                isTrueInvitation = true;
                            }
                        }

                        if (isTrueInvitation)
                        {

                            isInvitationActive = true;
                            invitationPriceStr = targetPriceStr;

                        }
                        else
                        {

                            isAnyPromoActive = true;
                            isSubsidyActive = true;
                            customerPays = targetPrice;

                            if (decimal.TryParse(originalPriceStr, CultureInfo.InvariantCulture, out var originalPrice))
                            {
                                sellerEarns = originalPrice;
                            }
                            else
                            {
                                sellerEarns = basePrice;
                            }
                        }
                    }
                }

                else if (activeBargainBadge != null)
                {
                    var bargainPriceStr = activeBargainBadge.Prices?["bargain"]?["amount"]?.ToString();
                    if (decimal.TryParse(bargainPriceStr, CultureInfo.InvariantCulture, out var bargainPrice))
                    {
                        isAnyPromoActive = true;
                        sellerEarns = bargainPrice;
                        customerPays = bargainPrice;
                    }
                }

                return new AllegroApiSummary(
                    customerPays,
                    sellerEarns,
                    commission,
                    ean,
                    isAnyPromoActive,
                    isSubsidyActive,
                    isInvitationActive,
                    decimal.TryParse(invitationPriceStr, CultureInfo.InvariantCulture, out var invPrice) ? invPrice : null
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas pobierania i analizy danych API dla oferty {OfferId}", offerId);
                return null;
            }
        }

        private async Task<List<BadgeData>> GetBadgesAsync(string accessToken, string offerId, JsonNode offerData)
        {
            var activeBadges = new List<BadgeData>();
            var apiUrl = $"https://api.allegro.pl/sale/badges?offer.id={offerId}&marketplace.id=allegro-pl";
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nie udało się pobrać danych (Badges) dla oferty {OfferId}. Status: {StatusCode}", offerId, response.StatusCode);
                return activeBadges;
            }

            var badgesNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var badgesArray = badgesNode?["badges"]?.AsArray();
            if (badgesArray == null || badgesArray.Count == 0) return activeBadges;

            foreach (var badge in badgesArray)
            {
                var processStatus = badge?["process"]?["status"]?.ToString();
                var campaignName = badge?["campaign"]?["name"]?.ToString();
                if (processStatus == "ACTIVE" && !string.IsNullOrEmpty(campaignName))
                {
                    activeBadges.Add(new BadgeData(campaignName, badge?["prices"]));
                }
            }
            return activeBadges;
        }

        private async Task<List<AlleDiscountData>> GetAlleDiscountsAsync(string accessToken, string offerId)
        {
            var activeDiscounts = new List<AlleDiscountData>();

            var campaignsApiUrl = "https://api.allegro.pl/sale/alle-discount/campaigns";
            var campaignsRequest = new HttpRequestMessage(HttpMethod.Get, campaignsApiUrl);
            campaignsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            campaignsRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            var campaignsResponse = await _httpClient.SendAsync(campaignsRequest);
            if (!campaignsResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nie udało się pobrać listy kampanii AlleObniżka. Status: {StatusCode}", campaignsResponse.StatusCode);
                return activeDiscounts;
            }

            var campaignsNode = JsonNode.Parse(await campaignsResponse.Content.ReadAsStringAsync());
            var campaignsArray = campaignsNode?["alleDiscountCampaigns"]?.AsArray();
            if (campaignsArray == null || campaignsArray.Count == 0) return activeDiscounts;

            foreach (var campaign in campaignsArray)
            {
                var campaignId = campaign?["id"]?.ToString();
                var campaignName = campaign?["name"]?.ToString();
                if (string.IsNullOrEmpty(campaignId) || string.IsNullOrEmpty(campaignName)) continue;

                var submittedApiUrl = $"https://api.allegro.pl/sale/alle-discount/{campaignId}/submitted-offers?offer.id={offerId}";
                var submittedRequest = new HttpRequestMessage(HttpMethod.Get, submittedApiUrl);
                submittedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                submittedRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

                var submittedResponse = await _httpClient.SendAsync(submittedRequest);
                if (!submittedResponse.IsSuccessStatusCode) continue;

                var submittedOffersNode = JsonNode.Parse(await submittedResponse.Content.ReadAsStringAsync());
                var submittedOffersArray = submittedOffersNode?["submittedOffers"]?.AsArray();

                if (submittedOffersArray != null)
                {
                    foreach (var submittedOffer in submittedOffersArray)
                    {
                        string? returnedOfferId = submittedOffer?["offer"]?["id"]?.ToString();
                        if (returnedOfferId != offerId) continue;

                        var processStatus = submittedOffer?["process"]?["status"]?.ToString();
                        if (processStatus == "ACTIVE")
                        {
                            activeDiscounts.Add(new AlleDiscountData(campaignName, submittedOffer?["prices"]));
                        }
                    }
                }
            }
            return activeDiscounts;
        }

        private async Task<JsonNode?> GetOfferData(string accessToken, string offerId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.allegro.pl/sale/product-offers/{offerId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new AllegroAuthException($"Błąd autoryzacji (401) podczas pobierania oferty {offerId}. Token jest prawdopodobnie nieważny.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nie udało się pobrać danych oferty {OfferId}. Status: {StatusCode}. Odpowiedź: {Response}", offerId, response.StatusCode, await response.Content.ReadAsStringAsync());
                return null;
            }
            return JsonNode.Parse(await response.Content.ReadAsStringAsync());
        }

        private async Task<decimal?> GetOfferCommission(string accessToken, JsonNode offerData)
        {
            var payload = new { offer = offerData };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.allegro.public.v1+json");
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.allegro.pl/pricing/offer-fee-preview") { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var feeNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var feeAmountString = feeNode?["commissions"]?[0]?["fee"]?["amount"]?.ToString();

            if (decimal.TryParse(feeAmountString, CultureInfo.InvariantCulture, out var feeDecimal))
            {
                return feeDecimal;
            }
            return null;
        }

        public class AllegroAuthException : Exception
        {
            public AllegroAuthException(string message) : base(message) { }
        }

        public class ApiProcessingResult
        {
            public bool Success { get; set; } = true;
            public int StoresProcessedCount { get; set; } = 0;
            public int TotalOffersProcessed { get; set; } = 0;
            public List<string> Messages { get; set; } = new List<string>();
        }
    }
}