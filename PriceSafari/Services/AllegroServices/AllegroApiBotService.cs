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

    internal record BadgeData(string CampaignName, JsonNode? BadgeNode);
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

                // Dodajemy tylko te przetworzone z sukcesem
                result.TotalOffersProcessed += storeResult.processedCount;

                if (!storeResult.success)
                {
                    result.Success = false;
                    result.Messages.Add(storeResult.message);
                }
                else if (storeResult.processedCount == 0 && !string.IsNullOrEmpty(storeResult.message))
                {
                    // Ostrzeżenie, gdy sukces = true, ale nic nie zaktualizowano (np. same błędy 0.00 zł)
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

            int successCounter = 0;
            int failureCounter = 0;

            try
            {

                var semaphore = new SemaphoreSlim(5);
                var tasks = new List<Task>();

                _logger.LogInformation("Rozpoczynam zrównoleglone przetwarzanie {Count} ofert (limit {SemaphoreCount}) dla sklepu {StoreName}...", offersToProcess.Count, 5, store.StoreName);

                foreach (var offer in offersToProcess)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var apiData = await FetchApiDataForOffer(accessToken, offer.AllegroOfferId.ToString());

                            // --- WALIDACJA: Sprawdzamy czy dane są sensowne (nie null, cena > 0) ---
                            if (apiData != null && apiData.CustomerPrice > 0 && apiData.SellerRevenue > 0)
                            {
                                offer.ApiAllegroPriceFromUser = apiData.SellerRevenue;
                                offer.ApiAllegroPrice = apiData.CustomerPrice;
                                offer.ApiAllegroCommission = apiData.Commission;
                                offer.AllegroEan = apiData.Ean;
                                offer.AnyPromoActive = apiData.IsAnyPromoActive;
                                offer.IsSubsidyActive = apiData.IsSubsidyActive;
                        

                                // SUKCES: Oznaczamy jako przetworzone tylko gdy mamy poprawne dane
                                offer.IsApiProcessed = true;
                                Interlocked.Increment(ref successCounter);
                            }
                            else
                            {
                                // BŁĄD LOGICZNY/DANYCH: Nie aktualizujemy IsApiProcessed, aby spróbować ponownie później
                                Interlocked.Increment(ref failureCounter);
                                _logger.LogWarning("Otrzymano puste lub zerowe dane dla oferty {OfferId}. Pomijam aktualizację.", offer.AllegroOfferId);
                            }
                        }
                        catch (AllegroAuthException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failureCounter);
                            _logger.LogError(ex, "Błąd podczas przetwarzania oferty {OfferId} dla sklepu {StoreName}", offer.AllegroOfferId, store.StoreName);
                        }
                        finally
                        {
                            // UWAGA: Usunięto stąd "offer.IsApiProcessed = true", aby nie "zaliczać" błędnych ofert.
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Zakończono przetwarzanie dla sklepu {StoreName}. Sukces: {Success}, Porażka/Puste: {Fail}.",
                    store.StoreName, successCounter, failureCounter);

                if (successCounter == 0 && offersToProcess.Count > 0)
                {
                    return (false, 0, $"Sklep {store.StoreName}: Próbowano przetworzyć {offersToProcess.Count} ofert, ale żadna nie zwróciła poprawnych danych (0 sukcesów).");
                }

                string msg = failureCounter > 0 ? $"Ostrzeżenie: {failureCounter} ofert nie udało się pobrać poprawnie." : string.Empty;
                return (true, successCounter, msg);
            }
            catch (AllegroAuthException ex)
            {
                _logger.LogError(ex, "Błąd autoryzacji API Allegro dla sklepu {StoreName}. Token może być nieważny.", store.StoreName);

                // Tutaj decydujemy czy oznaczać je jako przetworzone, czy nie. 
                // W oryginale oznaczaliśmy, żeby nie zapętlać błędu auth.
                foreach (var offer in offersToProcess.Where(o => o.IsApiProcessed != true))
                {
                    // W przypadku błędu Auth można oznaczyć, lub zostawić do ponowienia po odświeżeniu tokena.
                    // Zgodnie z oryginalną logiką oznaczamy, ale w poprawnej implementacji lepiej byłoby nie oznaczać.
                    // Zostawiam tak jak było w bloku catch Auth, ale dodaję komentarz.
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
                var offerDataTask = GetOfferData(accessToken, offerId);
                var badgesTask = GetBadgesAsync(accessToken, offerId);

                await Task.WhenAll(offerDataTask, badgesTask);

                var offerDataNode = await offerDataTask;
                if (offerDataNode == null) return null;

                var badgeCampaigns = await badgesTask;

                var commissionTask = GetOfferCommission(accessToken, offerDataNode);
                var alleDiscountsTask = GetAlleDiscountsAsync(accessToken, offerId, badgeCampaigns);

                await Task.WhenAll(commissionTask, alleDiscountsTask);

                var commission = await commissionTask;
                var alleDiscountCampaigns = await alleDiscountsTask;

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

                decimal sellerEarns = basePrice;
                decimal customerPays = basePrice;
                string? invitationPriceStr = null;
                bool isAnyPromoActive = false;
                bool isSubsidyActive = false;
                bool isInvitationActive = false;

                // 1. Priorytet: AlleObniżka (AlleDiscount)
                var activeAlleDiscount = alleDiscountCampaigns.FirstOrDefault();

                // 2. Badges typu Subsidy (Dopłaty, np. Allegro Prices), ale bez AlleObniżki (żeby nie dublować)
                var activeSubsidyBadge = badgeCampaigns.FirstOrDefault(b =>
                    b.BadgeNode?["prices"]?["subsidy"] != null &&
                    !(b.CampaignName.Contains("AlleObniżka") || b.CampaignName.Contains("AlleDiscount"))
                );

                // 3. Badges typu Bargain (Zwykłe przeceny)
                var activeBargainBadge = badgeCampaigns.FirstOrDefault(b =>
                    b.BadgeNode?["prices"]?["bargain"] != null &&
                    b.BadgeNode?["prices"]?["subsidy"] == null
                );

                // --- ZMIENIONA LOGIKA PONIŻEJ ---

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
                    // TUTAJ NASTĄPIŁA POPRAWKA PRODUKCYJNA
                    // Usunięto logikę "if (basePrice == marketPrice ...)", która błędnie wykrywała zaproszenia.

                    var targetPriceStr = activeSubsidyBadge.BadgeNode?["prices"]?["subsidy"]?["targetPrice"]?["amount"]?.ToString();
                    var originalPriceStr = activeSubsidyBadge.BadgeNode?["prices"]?["subsidy"]?["originalPrice"]?["amount"]?.ToString();

                    if (decimal.TryParse(targetPriceStr, CultureInfo.InvariantCulture, out var targetPrice))
                    {
                        // Skoro badge jest ACTIVE i ma subsidy -> to jest dopłata.
                        isAnyPromoActive = true;
                        isSubsidyActive = true;
                        customerPays = targetPrice;

                        // Jeśli Allegro podaje originalPrice, to tyle zarabiasz.
                        // Jeśli jest null, zarabiasz swoją basePrice.
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
                else if (activeBargainBadge != null)
                {
                    var bargainPriceStr = activeBargainBadge.BadgeNode?["prices"]?["bargain"]?["amount"]?.ToString();
                    if (decimal.TryParse(bargainPriceStr, CultureInfo.InvariantCulture, out var bargainPrice))
                    {
                        isAnyPromoActive = true;
                        sellerEarns = bargainPrice;
                        customerPays = bargainPrice;
                    }
                }

                // --- OSTATECZNA WALIDACJA CEN ---
                if (customerPays <= 0 || sellerEarns <= 0)
                {
                    _logger.LogWarning("Oferta {OfferId} zwróciła niepoprawne ceny (<=0): CustomerPays={CP}, SellerEarns={SE}", offerId, customerPays, sellerEarns);
                    return null;
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

        private async Task<List<BadgeData>> GetBadgesAsync(string accessToken, string offerId)
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

                    activeBadges.Add(new BadgeData(campaignName, badge));
                }
            }
            return activeBadges;
        }

        private async Task<List<AlleDiscountData>> GetAlleDiscountsAsync(string accessToken, string offerId, List<BadgeData> badges)
        {
            var activeDiscounts = new List<AlleDiscountData>();

            var alleDiscountBadges = badges
                .Where(b => b.CampaignName.Contains("AlleObniżka") || b.CampaignName.Contains("AlleDiscount"))
                .ToList();

            if (alleDiscountBadges.Count == 0)
            {

                _logger.LogInformation("Dla oferty {OfferId} nie znaleziono aktywnych oznaczeń (badges) AlleObniżka. Pomijam dalsze sprawdzanie.", offerId);
                return activeDiscounts;
            }

            var tasks = new List<Task<AlleDiscountData?>>();

            foreach (var badge in alleDiscountBadges)
            {

                var campaignId = badge.BadgeNode?["campaign"]?["id"]?.ToString();
                if (string.IsNullOrEmpty(campaignId))
                {
                    _logger.LogWarning("Dla oferty {OfferId} znaleziono badge AlleObniżka ({CampaignName}), ale brak campaign.id. Pomijam.", offerId, badge.CampaignName);
                    continue;
                }

                tasks.Add(CheckSingleCampaignAsync(accessToken, offerId, campaignId, badge.CampaignName));
            }

            var results = await Task.WhenAll(tasks);

            foreach (var result in results)
            {
                if (result != null)
                {
                    activeDiscounts.Add(result);
                }
            }

            return activeDiscounts;
        }

        private async Task<AlleDiscountData?> CheckSingleCampaignAsync(string accessToken, string offerId, string campaignId, string campaignName)
        {
            var submittedApiUrl = $"https://api.allegro.pl/sale/alle-discount/{campaignId}/submitted-offers?offer.id={offerId}";
            var submittedRequest = new HttpRequestMessage(HttpMethod.Get, submittedApiUrl);
            submittedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            submittedRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            var submittedResponse = await _httpClient.SendAsync(submittedRequest);
            if (!submittedResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Błąd pobierania szczegółów AlleObniżka dla oferty {OfferId} i kampanii {CampaignId}. Status: {StatusCode}", offerId, campaignId, submittedResponse.StatusCode);
                return null;
            }

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
                        return new AlleDiscountData(campaignName, submittedOffer?["prices"]);
                    }
                }
            }

            return null;
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