//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Logging;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using System.Globalization;
//using System.Net;
//using System.Net.Http.Headers;
//using System.Text;
//using System.Text.Json;
//using System.Text.Json.Nodes;

//namespace PriceSafari.Services.AllegroServices
//{
//    public record AllegroApiSummary(
//        decimal? CustomerPrice,
//        decimal? SellerRevenue,
//        decimal? Commission,
//        string? Ean,
//        bool IsAnyPromoActive,
//        bool IsSubsidyActive
//    );

//    internal record BadgeData(string CampaignName, JsonNode? BadgeNode);
//    internal record AlleDiscountData(string CampaignName, JsonNode? Prices);

//    public class AllegroApiBotService
//    {
//        private readonly PriceSafariContext _context;
//        private readonly ILogger<AllegroApiBotService> _logger;
//        private readonly AllegroAuthTokenService _authTokenService;
//        private static readonly HttpClient _httpClient = new();

//        public AllegroApiBotService(
//            PriceSafariContext context,
//            ILogger<AllegroApiBotService> logger,
//            AllegroAuthTokenService authTokenService)
//        {
//            _context = context;
//            _logger = logger;
//            _authTokenService = authTokenService;
//        }

//        public async Task<ApiProcessingResult> ProcessOffersForActiveStoresAsync()
//        {
//            var result = new ApiProcessingResult();
//            _logger.LogInformation("Rozpoczynam proces pobierania dodatkowych danych z API Allegro...");

//            var activeStores = await _context.Stores
//                .Where(s => s.OnAllegro && s.FetchExtendedAllegroData && s.IsAllegroTokenActive && !string.IsNullOrEmpty(s.AllegroApiToken))
//                .AsNoTracking()
//                .ToListAsync();

//            if (!activeStores.Any())
//            {
//                result.Messages.Add("Brak aktywnych sklepów do przetworzenia.");
//                return result;
//            }

//            result.StoresProcessedCount = activeStores.Count;

//            foreach (var store in activeStores)
//            {
//                var storeResult = await ProcessOffersForSingleStore(store);
//                result.TotalOffersProcessed += storeResult.processedCount;

//                if (!storeResult.success)
//                {
//                    result.Success = false;
//                    result.Messages.Add(storeResult.message);
//                }
//                else if (storeResult.processedCount == 0 && !string.IsNullOrEmpty(storeResult.message))
//                {
//                    result.Messages.Add(storeResult.message);
//                }
//            }

//            _logger.LogInformation("Zakończono proces pobierania dodatkowych danych z API Allegro.");
//            return result;
//        }

//        private async Task<(bool success, int processedCount, string message)> ProcessOffersForSingleStore(StoreClass store)
//        {
//            _logger.LogInformation("Przetwarzam sklep: {StoreName} (ID: {StoreId})", store.StoreName, store.StoreId);

//            var offersToProcess = await _context.AllegroOffersToScrape
//                .Where(o => o.StoreId == store.StoreId && o.IsScraped && o.IsApiProcessed != true)
//                .ToListAsync();

//            if (!offersToProcess.Any()) return (true, 0, string.Empty);

//            // 1. Pobieramy token (może być "stary", jeśli data w bazie jest z przyszłości)
//            string? accessToken = await _authTokenService.GetValidAccessTokenAsync(store.StoreId);

//            if (string.IsNullOrEmpty(accessToken))
//            {
//                return (false, 0, $"Nie udało się pobrać tokena dla sklepu '{store.StoreName}'.");
//            }

//            // --- SEKCJA NAPRAWCZA: WERYFIKACJA I WYMUSZONE ODŚWIEŻENIE ---
//            try
//            {
//                // Bierzemy pierwszą ofertę na próbę, żeby sprawdzić czy token działa
//                var testOffer = offersToProcess.First();
//                // Próba pobrania danych. Jeśli token jest zły, rzuci AllegroAuthException (401)
//                await GetOfferData(accessToken, testOffer.AllegroOfferId.ToString());
//            }
//            catch (AllegroAuthException)
//            {
//                _logger.LogWarning("Wykryto nieważny token (401) mimo poprawnej daty w bazie. Próba wymuszonego odświeżenia...");

//                // Wymuszamy odświeżenie w serwisie
//                accessToken = await _authTokenService.ForceRefreshTokenAsync(store.StoreId);

//                if (string.IsNullOrEmpty(accessToken))
//                {
//                    return (false, 0, "Token wygasł i nie udało się go odświeżyć (Refresh Token może być nieważny).");
//                }
//                _logger.LogInformation("Token został pomyślnie odświeżony. Kontynuuję przetwarzanie.");
//            }
//            catch (Exception)
//            {
//                // Inne błędy na tym etapie ignorujemy, pętla główna sobie z nimi poradzi
//            }
//            // -------------------------------------------------------------

//            int successCounter = 0;
//            int failureCounter = 0;

//            try
//            {
//                var semaphore = new SemaphoreSlim(5);
//                var tasks = new List<Task>();

//                _logger.LogInformation("Rozpoczynam przetwarzanie {Count} ofert dla sklepu {StoreName}...", offersToProcess.Count, store.StoreName);

//                foreach (var offer in offersToProcess)
//                {
//                    tasks.Add(Task.Run(async () =>
//                    {
//                        await semaphore.WaitAsync();
//                        try
//                        {
//                            var apiData = await FetchApiDataForOffer(accessToken!, offer.AllegroOfferId.ToString());

//                            if (apiData != null && apiData.CustomerPrice > 0 && apiData.SellerRevenue > 0)
//                            {
//                                offer.ApiAllegroPriceFromUser = apiData.SellerRevenue;
//                                offer.ApiAllegroPrice = apiData.CustomerPrice;
//                                offer.ApiAllegroCommission = apiData.Commission;
//                                offer.AllegroEan = apiData.Ean;
//                                offer.AnyPromoActive = apiData.IsAnyPromoActive;
//                                offer.IsSubsidyActive = apiData.IsSubsidyActive;

//                                offer.IsApiProcessed = true;
//                                Interlocked.Increment(ref successCounter);
//                            }
//                            else
//                            {
//                                Interlocked.Increment(ref failureCounter);
//                                _logger.LogWarning("Otrzymano puste dane dla oferty {OfferId}.", offer.AllegroOfferId);
//                            }
//                        }
//                        catch (AllegroAuthException)
//                        {
//                            // Jeśli tutaj wpadnie 401, to znaczy że token padł w trakcie pętli (mało prawdopodobne po weryfikacji wyżej)
//                            throw;
//                        }
//                        catch (Exception ex)
//                        {
//                            Interlocked.Increment(ref failureCounter);
//                            _logger.LogError(ex, "Błąd oferty {OfferId}", offer.AllegroOfferId);
//                        }
//                        finally
//                        {
//                            semaphore.Release();
//                        }
//                    }));
//                }

//                await Task.WhenAll(tasks);
//                await _context.SaveChangesAsync();

//                string msg = failureCounter > 0 ? $"Ostrzeżenie: {failureCounter} błędów." : string.Empty;
//                if (successCounter == 0 && offersToProcess.Count > 0) return (false, 0, "Brak sukcesów.");

//                return (true, successCounter, msg);
//            }
//            catch (AllegroAuthException)
//            {
//                // To wyłapie sytuację, jeśli token padnie w trakcie (bardzo rzadkie)
//                return (false, 0, "Token utracił ważność w trakcie operacji.");
//            }
//            catch (Exception ex)
//            {
//                return (false, 0, $"Błąd krytyczny: {ex.Message}");
//            }
//        }

//        private async Task<AllegroApiSummary?> FetchApiDataForOffer(string accessToken, string offerId)
//        {
//            try
//            {
//                var offerDataTask = GetOfferData(accessToken, offerId);
//                var badgesTask = GetBadgesAsync(accessToken, offerId);

//                await Task.WhenAll(offerDataTask, badgesTask);

//                var offerDataNode = await offerDataTask;
//                if (offerDataNode == null) return null;

//                var badgeCampaigns = await badgesTask;

//                var commissionTask = GetOfferCommission(accessToken, offerDataNode);
//                var alleDiscountsTask = GetAlleDiscountsAsync(accessToken, offerId, badgeCampaigns);

//                await Task.WhenAll(commissionTask, alleDiscountsTask);

//                var commission = await commissionTask;
//                var alleDiscountCampaigns = await alleDiscountsTask;

//                var basePriceStr = offerDataNode["sellingMode"]?["price"]?["amount"]?.ToString();
//                if (!decimal.TryParse(basePriceStr, CultureInfo.InvariantCulture, out var basePrice))
//                {
//                    _logger.LogWarning("Nie można sparsować ceny bazowej dla oferty {OfferId}", offerId);
//                    return null;
//                }

//                string? ean = null;
//                try
//                {
//                    var parameters = offerDataNode["productSet"]?[0]?["product"]?["parameters"]?.AsArray();
//                    var eanValue = parameters?.FirstOrDefault(p => p?["id"]?.ToString() == "225693")?["values"]?[0]?.ToString();
//                    if (!string.IsNullOrWhiteSpace(eanValue) && eanValue != "Brak") ean = eanValue;
//                }
//                catch (Exception ex) { _logger.LogWarning(ex, "Nie udało się sparsować EAN dla {OfferId}", offerId); }

//                decimal sellerEarns = basePrice;
//                decimal customerPays = basePrice;
//                bool isAnyPromoActive = false;
//                bool isSubsidyActive = false;
//                // 5. ZMIANA: Usunięto zmienne Invitation

//                var activeAlleDiscount = alleDiscountCampaigns.FirstOrDefault();

//                var activeSubsidyBadge = badgeCampaigns.FirstOrDefault(b =>
//                    b.BadgeNode?["prices"]?["subsidy"] != null &&
//                    !(b.CampaignName.Contains("AlleObniżka") || b.CampaignName.Contains("AlleDiscount"))
//                );

//                var activeBargainBadge = badgeCampaigns.FirstOrDefault(b =>
//                    b.BadgeNode?["prices"]?["bargain"] != null &&
//                    b.BadgeNode?["prices"]?["subsidy"] == null
//                );

//                if (activeAlleDiscount != null)
//                {
//                    var proposedPriceStr = activeAlleDiscount.Prices?["proposedPrice"]?["amount"]?.ToString();
//                    var customerPriceStr = activeAlleDiscount.Prices?["maximumSellingPrice"]?["amount"]?.ToString();

//                    if (decimal.TryParse(proposedPriceStr, CultureInfo.InvariantCulture, out sellerEarns) &&
//                        decimal.TryParse(customerPriceStr, CultureInfo.InvariantCulture, out customerPays))
//                    {
//                        isAnyPromoActive = true;
//                        isSubsidyActive = true;
//                    }
//                }
//                else if (activeSubsidyBadge != null)
//                {
//                    var targetPriceStr = activeSubsidyBadge.BadgeNode?["prices"]?["subsidy"]?["targetPrice"]?["amount"]?.ToString();
//                    var originalPriceStr = activeSubsidyBadge.BadgeNode?["prices"]?["subsidy"]?["originalPrice"]?["amount"]?.ToString();

//                    if (decimal.TryParse(targetPriceStr, CultureInfo.InvariantCulture, out var targetPrice))
//                    {
//                        isAnyPromoActive = true;
//                        isSubsidyActive = true;
//                        customerPays = targetPrice;

//                        if (decimal.TryParse(originalPriceStr, CultureInfo.InvariantCulture, out var originalPrice))
//                        {
//                            sellerEarns = originalPrice;
//                        }
//                        else
//                        {
//                            sellerEarns = basePrice;
//                        }
//                    }
//                }
//                else if (activeBargainBadge != null)
//                {
//                    var bargainPriceStr = activeBargainBadge.BadgeNode?["prices"]?["bargain"]?["amount"]?.ToString();
//                    if (decimal.TryParse(bargainPriceStr, CultureInfo.InvariantCulture, out var bargainPrice))
//                    {
//                        isAnyPromoActive = true;
//                        sellerEarns = bargainPrice;
//                        customerPays = bargainPrice;
//                    }
//                }

//                if (customerPays <= 0 || sellerEarns <= 0)
//                {
//                    _logger.LogWarning("Oferta {OfferId} zwróciła niepoprawne ceny (<=0): CustomerPays={CP}, SellerEarns={SE}", offerId, customerPays, sellerEarns);
//                    return null;
//                }

//                return new AllegroApiSummary(
//                    customerPays,
//                    sellerEarns,
//                    commission,
//                    ean,
//                    isAnyPromoActive,
//                    isSubsidyActive
//                );
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "Błąd podczas pobierania i analizy danych API dla oferty {OfferId}", offerId);
//                return null;
//            }
//        }

//        private async Task<List<BadgeData>> GetBadgesAsync(string accessToken, string offerId)
//        {
//            var activeBadges = new List<BadgeData>();
//            var apiUrl = $"https://api.allegro.pl/sale/badges?offer.id={offerId}&marketplace.id=allegro-pl";
//            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
//            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
//            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

//            var response = await _httpClient.SendAsync(request);
//            if (!response.IsSuccessStatusCode)
//            {
//                _logger.LogWarning("Nie udało się pobrać danych (Badges) dla oferty {OfferId}. Status: {StatusCode}", offerId, response.StatusCode);
//                return activeBadges;
//            }

//            var badgesNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
//            var badgesArray = badgesNode?["badges"]?.AsArray();
//            if (badgesArray == null || badgesArray.Count == 0) return activeBadges;

//            foreach (var badge in badgesArray)
//            {
//                var processStatus = badge?["process"]?["status"]?.ToString();
//                var campaignName = badge?["campaign"]?["name"]?.ToString();
//                if (processStatus == "ACTIVE" && !string.IsNullOrEmpty(campaignName))
//                {

//                    activeBadges.Add(new BadgeData(campaignName, badge));
//                }
//            }
//            return activeBadges;
//        }

//        private async Task<List<AlleDiscountData>> GetAlleDiscountsAsync(string accessToken, string offerId, List<BadgeData> badges)
//        {
//            var activeDiscounts = new List<AlleDiscountData>();

//            var alleDiscountBadges = badges
//                .Where(b => b.CampaignName.Contains("AlleObniżka") || b.CampaignName.Contains("AlleDiscount"))
//                .ToList();

//            if (alleDiscountBadges.Count == 0)
//            {
//                _logger.LogInformation("Dla oferty {OfferId} nie znaleziono aktywnych oznaczeń (badges) AlleObniżka. Pomijam dalsze sprawdzanie.", offerId);
//                return activeDiscounts;
//            }

//            var tasks = new List<Task<AlleDiscountData?>>();

//            foreach (var badge in alleDiscountBadges)
//            {
//                var campaignId = badge.BadgeNode?["campaign"]?["id"]?.ToString();
//                if (string.IsNullOrEmpty(campaignId))
//                {
//                    _logger.LogWarning("Dla oferty {OfferId} znaleziono badge AlleObniżka ({CampaignName}), ale brak campaign.id. Pomijam.", offerId, badge.CampaignName);
//                    continue;
//                }
//                tasks.Add(CheckSingleCampaignAsync(accessToken, offerId, campaignId, badge.CampaignName));
//            }

//            var results = await Task.WhenAll(tasks);

//            foreach (var result in results)
//            {
//                if (result != null)
//                {
//                    activeDiscounts.Add(result);
//                }
//            }

//            return activeDiscounts;
//        }

//        private async Task<AlleDiscountData?> CheckSingleCampaignAsync(string accessToken, string offerId, string campaignId, string campaignName)
//        {
//            var submittedApiUrl = $"https://api.allegro.pl/sale/alle-discount/{campaignId}/submitted-offers?offer.id={offerId}";
//            var submittedRequest = new HttpRequestMessage(HttpMethod.Get, submittedApiUrl);
//            submittedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
//            submittedRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

//            var submittedResponse = await _httpClient.SendAsync(submittedRequest);
//            if (!submittedResponse.IsSuccessStatusCode)
//            {
//                _logger.LogWarning("Błąd pobierania szczegółów AlleObniżka dla oferty {OfferId} i kampanii {CampaignId}. Status: {StatusCode}", offerId, campaignId, submittedResponse.StatusCode);
//                return null;
//            }

//            var submittedOffersNode = JsonNode.Parse(await submittedResponse.Content.ReadAsStringAsync());
//            var submittedOffersArray = submittedOffersNode?["submittedOffers"]?.AsArray();

//            if (submittedOffersArray != null)
//            {
//                foreach (var submittedOffer in submittedOffersArray)
//                {
//                    string? returnedOfferId = submittedOffer?["offer"]?["id"]?.ToString();
//                    if (returnedOfferId != offerId) continue;

//                    var processStatus = submittedOffer?["process"]?["status"]?.ToString();
//                    if (processStatus == "ACTIVE")
//                    {
//                        return new AlleDiscountData(campaignName, submittedOffer?["prices"]);
//                    }
//                }
//            }
//            return null;
//        }

//        private async Task<JsonNode?> GetOfferData(string accessToken, string offerId)
//        {
//            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.allegro.pl/sale/product-offers/{offerId}");
//            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
//            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

//            var response = await _httpClient.SendAsync(request);

//            if (response.StatusCode == HttpStatusCode.Unauthorized)
//            {
//                throw new AllegroAuthException($"Błąd autoryzacji (401) podczas pobierania oferty {offerId}. Token jest prawdopodobnie nieważny.");
//            }

//            if (!response.IsSuccessStatusCode)
//            {
//                _logger.LogWarning("Nie udało się pobrać danych oferty {OfferId}. Status: {StatusCode}. Odpowiedź: {Response}", offerId, response.StatusCode, await response.Content.ReadAsStringAsync());
//                return null;
//            }
//            return JsonNode.Parse(await response.Content.ReadAsStringAsync());
//        }

//        private async Task<decimal?> GetOfferCommission(string accessToken, JsonNode offerData)
//        {
//            var payload = new { offer = offerData };
//            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.allegro.public.v1+json");
//            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.allegro.pl/pricing/offer-fee-preview") { Content = content };
//            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

//            var response = await _httpClient.SendAsync(request);
//            if (!response.IsSuccessStatusCode) return null;

//            var feeNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
//            var feeAmountString = feeNode?["commissions"]?[0]?["fee"]?["amount"]?.ToString();

//            if (decimal.TryParse(feeAmountString, CultureInfo.InvariantCulture, out var feeDecimal))
//            {
//                return feeDecimal;
//            }
//            return null;
//        }

//        public class AllegroAuthException : Exception
//        {
//            public AllegroAuthException(string message) : base(message) { }
//        }

//        public class ApiProcessingResult
//        {
//            public bool Success { get; set; } = true;
//            public int StoresProcessedCount { get; set; } = 0;
//            public int TotalOffersProcessed { get; set; } = 0;
//            public List<string> Messages { get; set; } = new List<string>();
//        }
//    }
//}








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
    // Rekordy pomocnicze
    public record AllegroApiSummary(
        decimal? CustomerPrice,
        decimal? SellerRevenue,
        decimal? Commission,
        string? Ean,
        bool IsAnyPromoActive,
        bool IsSubsidyActive
    );

    internal record BadgeData(string CampaignName, JsonNode? BadgeNode);
    internal record AlleDiscountData(string CampaignName, JsonNode? Prices);

    public class AllegroApiBotService
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<AllegroApiBotService> _logger;
        private readonly AllegroAuthTokenService _authTokenService;
        private static readonly HttpClient _httpClient = new();

        public AllegroApiBotService(
            PriceSafariContext context,
            ILogger<AllegroApiBotService> logger,
            AllegroAuthTokenService authTokenService)
        {
            _context = context;
            _logger = logger;
            _authTokenService = authTokenService;
        }

        // --- GŁÓWNA METODA ---
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
                result.Messages.Add("Brak aktywnych sklepów do przetworzenia.");
                return result;
            }

            result.StoresProcessedCount = activeStores.Count;

            foreach (var store in activeStores)
            {
                // Wywołujemy metodę dla pojedynczego sklepu i odbieramy szczegółowe statystyki
                var storeResult = await ProcessOffersForSingleStore(store);

                // Agregujemy wyniki do głównego raportu
                result.TotalOffersChecked += storeResult.checkedCount;
                result.TotalOffersSuccess += storeResult.successCount;
                result.TotalOffersFailed += storeResult.failureCount;

                if (!storeResult.success)
                {
                    result.Success = false;
                    result.Messages.Add($"Sklep {store.StoreName}: {storeResult.message}");
                }
                else if (storeResult.failureCount > 0)
                {
                    // Jeśli były błędy cząstkowe, dodajemy info, ale nie oznaczamy całego procesu jako fail
                    result.Messages.Add($"Sklep {store.StoreName}: Błędy przy {storeResult.failureCount} ofertach.");
                }
            }

            _logger.LogInformation("Zakończono proces. Sprawdzono: {Checked}, Sukces: {Success}, Błędy: {Failed}",
                result.TotalOffersChecked, result.TotalOffersSuccess, result.TotalOffersFailed);

            return result;
        }

        // --- PRZETWARZANIE JEDNEGO SKLEPU (TUTAJ BYŁ BŁĄD) ---
        private async Task<(bool success, int checkedCount, int successCount, int failureCount, string message)> ProcessOffersForSingleStore(StoreClass store)
        {
            _logger.LogInformation("Przetwarzam sklep: {StoreName} (ID: {StoreId})", store.StoreName, store.StoreId);

            var offersToProcess = await _context.AllegroOffersToScrape
                .Where(o => o.StoreId == store.StoreId && o.IsScraped && o.IsApiProcessed != true)
                .ToListAsync();

            int totalToCheck = offersToProcess.Count;
            if (totalToCheck == 0) return (true, 0, 0, 0, string.Empty);

            // 1. Pobieramy token
            string? accessToken = await _authTokenService.GetValidAccessTokenAsync(store.StoreId);

            if (string.IsNullOrEmpty(accessToken))
            {
                return (false, totalToCheck, 0, 0, $"Nie udało się pobrać tokena dla sklepu '{store.StoreName}'.");
            }

            // --- SEKCJA NAPRAWCZA: WERYFIKACJA I WYMUSZONE ODŚWIEŻENIE ---
            try
            {
                var testOffer = offersToProcess.First();
                await GetOfferData(accessToken, testOffer.AllegroOfferId.ToString());
            }
            catch (AllegroAuthException)
            {
                _logger.LogWarning("Wykryto nieważny token (401) mimo poprawnej daty w bazie. Próba wymuszonego odświeżenia...");
                accessToken = await _authTokenService.ForceRefreshTokenAsync(store.StoreId);

                if (string.IsNullOrEmpty(accessToken))
                {
                    return (false, totalToCheck, 0, 0, "Token wygasł i nie udało się go odświeżyć.");
                }
                _logger.LogInformation("Token został pomyślnie odświeżony. Kontynuuję przetwarzanie.");
            }
            catch (Exception)
            {
                // Ignorujemy inne błędy testowe
            }
            // -------------------------------------------------------------

            int successCounter = 0;
            int failureCounter = 0;

            try
            {
                var semaphore = new SemaphoreSlim(5);
                var tasks = new List<Task>();

                _logger.LogInformation("Rozpoczynam przetwarzanie {Count} ofert dla sklepu {StoreName}...", totalToCheck, store.StoreName);

                foreach (var offer in offersToProcess)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync();
                        try
                        {
                            var apiData = await FetchApiDataForOffer(accessToken!, offer.AllegroOfferId.ToString());

                            // Sprawdzamy czy mamy dane (zwłaszcza prowizję)
                            if (apiData != null && apiData.Commission.HasValue)
                            {
                                offer.ApiAllegroPriceFromUser = apiData.SellerRevenue;
                                offer.ApiAllegroPrice = apiData.CustomerPrice;
                                offer.ApiAllegroCommission = apiData.Commission;
                                offer.AllegroEan = apiData.Ean;
                                offer.AnyPromoActive = apiData.IsAnyPromoActive;
                                offer.IsSubsidyActive = apiData.IsSubsidyActive;

                                offer.IsApiProcessed = true;
                                Interlocked.Increment(ref successCounter);
                            }
                            else
                            {
                                Interlocked.Increment(ref failureCounter);
                                _logger.LogWarning("Otrzymano puste dane/brak prowizji dla oferty {OfferId}.", offer.AllegroOfferId);
                            }
                        }
                        catch (Exception ex)
                        {
                            // WAŻNE: Tutaj łapiemy błąd pojedynczej oferty i NIE robimy throw, 
                            // aby nie przerwać przetwarzania innych ofert i pozwolić na zapis tych udanych.
                            Interlocked.Increment(ref failureCounter);
                            _logger.LogError(ex, "Błąd przetwarzania oferty {OfferId}", offer.AllegroOfferId);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                // Czekamy na zakończenie wszystkich zadań (zarówno sukcesów jak i błędów)
                await Task.WhenAll(tasks);

                // Zapisujemy zmiany w bazie (te oferty, które się udały)
                await _context.SaveChangesAsync();

                string msg = failureCounter > 0 ? $"Ostrzeżenie: {failureCounter} błędów." : string.Empty;

                // Zwracamy pełną statystykę
                return (true, totalToCheck, successCounter, failureCounter, msg);
            }
            catch (Exception ex)
            {
                // Ten catch złapie błąd np. podczas SaveChangesAsync
                return (false, totalToCheck, successCounter, failureCounter, $"Błąd krytyczny zapisu/przetwarzania: {ex.Message}");
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
                bool isAnyPromoActive = false;
                bool isSubsidyActive = false;

                var activeAlleDiscount = alleDiscountCampaigns.FirstOrDefault();

                var activeSubsidyBadge = badgeCampaigns.FirstOrDefault(b =>
                    b.BadgeNode?["prices"]?["subsidy"] != null &&
                    !(b.CampaignName.Contains("AlleObniżka") || b.CampaignName.Contains("AlleDiscount"))
                );

                var activeBargainBadge = badgeCampaigns.FirstOrDefault(b =>
                    b.BadgeNode?["prices"]?["bargain"] != null &&
                    b.BadgeNode?["prices"]?["subsidy"] == null
                );

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
                    var targetPriceStr = activeSubsidyBadge.BadgeNode?["prices"]?["subsidy"]?["targetPrice"]?["amount"]?.ToString();
                    var originalPriceStr = activeSubsidyBadge.BadgeNode?["prices"]?["subsidy"]?["originalPrice"]?["amount"]?.ToString();

                    if (decimal.TryParse(targetPriceStr, CultureInfo.InvariantCulture, out var targetPrice))
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
                    isSubsidyActive
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
                return activeDiscounts;
            }

            var tasks = new List<Task<AlleDiscountData?>>();

            foreach (var badge in alleDiscountBadges)
            {
                var campaignId = badge.BadgeNode?["campaign"]?["id"]?.ToString();
                if (string.IsNullOrEmpty(campaignId))
                {
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
                _logger.LogWarning("Nie udało się pobrać danych oferty {OfferId}. Status: {StatusCode}", offerId, response.StatusCode);
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

        // Zaktualizowana klasa wyniku z nowymi polami
        public class ApiProcessingResult
        {
            public bool Success { get; set; } = true;
            public int StoresProcessedCount { get; set; } = 0;

            // Nowe pola do statystyk
            public int TotalOffersChecked { get; set; } = 0;
            public int TotalOffersSuccess { get; set; } = 0;
            public int TotalOffersFailed { get; set; } = 0;

            public List<string> Messages { get; set; } = new List<string>();
        }
    }
}