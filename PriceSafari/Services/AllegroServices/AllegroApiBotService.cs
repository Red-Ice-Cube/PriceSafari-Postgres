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

    public record AllegroApiData(
        decimal? BasePrice,
        decimal? FinalPrice,
        decimal? Commission,
        bool HasActivePromo,
        string? Ean
    );

    public class AllegroApiBotService
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<AllegroApiBotService> _logger;
        private static readonly HttpClient _httpClient = new();

        //private const string CLIENT_ID = "9163c502a1cb4c348579c5ff54c75df1";
        //private const string CLIENT_SECRET = "ULlu7uecKMM1t1LpgrDe1D7MOTn0ABVVuHeKeQfGJ0Z80v9ojN4JyVqXOBLByQMZ";

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
                        offer.ApiAllegroPriceFromUser = apiData.BasePrice;
                        offer.ApiAllegroPrice = apiData.FinalPrice;
                        offer.ApiAllegroCommission = apiData.Commission;
                        offer.AnyPromoActive = apiData.HasActivePromo;
                        offer.AllegroEan = apiData.Ean;
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

        private async Task<AllegroApiData?> FetchApiDataForOffer(string accessToken, string offerId)
        {
            try
            {
                var offerDataNode = await GetOfferData(accessToken, offerId);
                if (offerDataNode == null) return null;

                var basePriceString = offerDataNode["sellingMode"]?["price"]?["amount"]?.ToString();
                decimal.TryParse(basePriceString, CultureInfo.InvariantCulture, out var basePrice);
                decimal? parsedBasePrice = basePrice > 0 ? basePrice : null;

                string? ean = null;
                try
                {
                    var parameters = offerDataNode["productSet"]?[0]?["product"]?["parameters"]?.AsArray();

                    var eanValue = parameters?.FirstOrDefault(p => p?["id"]?.ToString() == "225693")?["values"]?[0]?.ToString();

                    if (!string.IsNullOrWhiteSpace(eanValue) && eanValue != "Brak")
                    {
                        ean = eanValue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Nie udało się sparsować kodu EAN dla oferty {OfferId}. Dane JSON mogły mieć nietypową strukturę.", offerId);
                    ean = null;
                }

                var promoPrice = await GetActiveCampaignPrice(accessToken, offerId);

                decimal? finalPrice = promoPrice ?? parsedBasePrice;

                // --- POCZĄTEK MODYFIKACJI ---

                // 1. Logika podstawowa: czy API zgłosiło aktywną kampanię?
                bool hasActivePromo = promoPrice.HasValue;

                // 2. Logika dodatkowa (zabezpieczenie):
                // Sprawdzamy, czy cena bazowa (od użytkownika) jest różna od ceny końcowej (wyświetlanej).
                // Jeśli tak, to również traktujemy to jako aktywną promocję.
                if (!hasActivePromo && // Sprawdzamy tylko, jeśli nie zostało to już wykryte
                    parsedBasePrice.HasValue &&
                    finalPrice.HasValue &&
                    parsedBasePrice.Value != finalPrice.Value)
                {
                    _logger.LogWarning("Wykryto niespójność cen dla oferty {OfferId} (Baza: {BasePrice}, Końcowa: {FinalPrice}) mimo braku flagi 'badge'. Oznaczam jako Aktywna Promocja.",
                        offerId, parsedBasePrice.Value, finalPrice.Value);

                    hasActivePromo = true;
                }
                // --- KONIEC MODYFIKACJI ---

                var commission = await GetOfferCommission(accessToken, offerDataNode);

                return new AllegroApiData(parsedBasePrice, finalPrice, commission, hasActivePromo, ean);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas pobierania danych API dla oferty {OfferId}", offerId);
                return null;
            }
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

        private async Task<decimal?> GetActiveCampaignPrice(string accessToken, string offerId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.allegro.pl/sale/badges?offer.id={offerId}&marketplace.id=allegro-pl");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var badgesNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var badgesArray = badgesNode?["badges"]?.AsArray();
            if (badgesArray == null) return null;

            foreach (var badge in badgesArray)
            {
                if (badge?["process"]?["status"]?.ToString() == "ACTIVE")
                {
                    var targetPriceString = badge?["prices"]?["subsidy"]?["targetPrice"]?["amount"]?.ToString();
                    if (decimal.TryParse(targetPriceString, CultureInfo.InvariantCulture, out var targetPrice) && targetPrice > 0)
                    {
                        return targetPrice;
                    }
                }
            }

            return null;
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