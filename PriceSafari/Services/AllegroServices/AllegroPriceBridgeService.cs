using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Controllers.MemberControllers;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace PriceSafari.Services.AllegroServices
{

    using static PriceSafari.Controllers.MemberControllers.AllegroPriceHistoryController;

    public class AllegroPriceBridgeService
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<AllegroPriceBridgeService> _logger;
        private static readonly HttpClient _httpClient = new();

        public AllegroPriceBridgeService(PriceSafariContext context, ILogger<AllegroPriceBridgeService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<PriceBridgeResult> ExecutePriceChangesAsync(
         int storeId,
         int allegroScrapeHistoryId,
         string userId,
         bool includeCommissionInMargin,
         List<AllegroPriceBridgeItemRequest> itemsToBridge)
        {
            var result = new PriceBridgeResult();
            var store = await _context.Stores.FindAsync(storeId);

            // --- Walidacja wstępna (bez zmian) ---
            if (store == null)
            {
                result.FailedCount = itemsToBridge.Count;
                result.Errors.Add(new PriceBridgeError { OfferId = "Wszystkie", Message = "Nie znaleziono sklepu." });
                return result;
            }
            if (!store.IsAllegroPriceBridgeActive)
            {
                result.FailedCount = itemsToBridge.Count;
                result.Errors.Add(new PriceBridgeError { OfferId = "Wszystkie", Message = "Wgrywanie cen po API jest wyłączone dla tego sklepu." });
                return result;
            }
            if (!store.IsAllegroTokenActive || string.IsNullOrEmpty(store.AllegroApiToken))
            {
                result.FailedCount = itemsToBridge.Count;
                result.Errors.Add(new PriceBridgeError { OfferId = "Wszystkie", Message = "Brak aktywnego tokenu API Allegro dla tego sklepu." });
                return result;
            }

            string accessToken = store.AllegroApiToken;

            var newBatch = new AllegroPriceBridgeBatch
            {
                ExecutionDate = DateTime.UtcNow,
                StoreId = storeId,
                AllegroScrapeHistoryId = allegroScrapeHistoryId,
                UserId = userId,
                SuccessfulCount = 0,
                FailedCount = 0
            };
            _context.AllegroPriceBridgeBatches.Add(newBatch);

            var itemsToVerify = new List<AllegroPriceBridgeItem>();

            _logger.LogInformation("Rozpoczynam PĘTLĘ 1: Wysyłanie {Count} zmian cen...", itemsToBridge.Count);

            foreach (var item in itemsToBridge)
            {
                if (string.IsNullOrEmpty(item.OfferId))
                {
                    result.FailedCount++;
                    result.Errors.Add(new PriceBridgeError { OfferId = item.OfferId ?? "Brak ID", Message = "Brakujące ID oferty." });
                    continue;
                }

                var bridgeItem = new AllegroPriceBridgeItem
                {
                    PriceBridgeBatch = newBatch,
                    AllegroProductId = item.ProductId,
                    AllegroOfferId = item.OfferId,
                    MarginPrice = item.MarginPrice,
                    IncludeCommissionInMargin = includeCommissionInMargin,
                    PriceBefore = item.PriceBefore,
                    CommissionBefore = item.CommissionBefore,
                    RankingBefore = item.RankingBefore,
                    PriceAfter_Simulated = item.PriceAfter_Simulated,
                    RankingAfter_Simulated = item.RankingAfter_Simulated,

                    // --- NOWE POLA (DODANE) ---
                    Mode = item.Mode,
                    PriceIndexTarget = item.PriceIndexTarget,
                    StepPriceApplied = item.StepPriceApplied
                    // ---------------------------
                };

                string newPriceString = item.PriceAfter_Simulated.ToString(CultureInfo.InvariantCulture);

                var (success, errorMessage) = await SetNewOfferPriceAsync(accessToken, item.OfferId, newPriceString);

                bridgeItem.Success = success;
                bridgeItem.ErrorMessage = success ? string.Empty : errorMessage;

                if (success)
                {
                    result.SuccessfulCount++;
                    itemsToVerify.Add(bridgeItem);
                }
                else
                {
                    result.FailedCount++;
                    result.Errors.Add(new PriceBridgeError { OfferId = item.OfferId, Message = errorMessage });

                    if (errorMessage.Contains("401") || errorMessage.Contains("Unauthorized"))
                    {
                        _logger.LogWarning("Token API dla sklepu {StoreId} wygasł (podczas wysyłania). Przerywam wgrywanie cen.", storeId);
                        store.IsAllegroTokenActive = false;

                        int remainingChanges = itemsToBridge.Count - result.SuccessfulCount - result.FailedCount;
                        if (remainingChanges > 0)
                        {
                            result.FailedCount += remainingChanges;
                            result.Errors.Add(new PriceBridgeError { OfferId = "Pozostałe", Message = "Token API wygasł. Przerwano." });
                        }

                        // Zapisujemy nowe pola także dla błędnego elementu (ważne dla logów)
                        bridgeItem.Mode = item.Mode;
                        bridgeItem.PriceIndexTarget = item.PriceIndexTarget;
                        bridgeItem.StepPriceApplied = item.StepPriceApplied;

                        _context.AllegroPriceBridgeItems.Add(bridgeItem);
                        break;
                    }
                }

                _context.AllegroPriceBridgeItems.Add(bridgeItem);
            }

            if (!store.IsAllegroTokenActive)
            {
                await _context.SaveChangesAsync();
            }

            if (itemsToVerify.Any())
            {
                _logger.LogInformation("Zakończono wysyłanie. Oczekuję 3 sekundy na przetworzenie przez Allegro...");
                await Task.Delay(3000);
            }

            // --- PĘTLA 2: WERYFIKACJA (bez zmian w logice, tylko kontekst) ---
            _logger.LogInformation("Rozpoczynam PĘTLĘ 2: Weryfikacja {Count} wysłanych zmian...", itemsToVerify.Count);

            foreach (var bridgeItem in itemsToVerify)
            {
                decimal? fetchedNewPrice = null;
                decimal? fetchedNewCommission = null;

                try
                {
                    var offerDataNode = await GetOfferData(accessToken, bridgeItem.AllegroOfferId);

                    if (offerDataNode != null)
                    {
                        var newPriceStringApi = offerDataNode["sellingMode"]?["price"]?["amount"]?.ToString();
                        if (decimal.TryParse(newPriceStringApi, CultureInfo.InvariantCulture, out var price) && price > 0)
                        {
                            fetchedNewPrice = price;
                        }
                        fetchedNewCommission = await GetOfferCommission(accessToken, offerDataNode);

                        bridgeItem.PriceAfter_Verified = fetchedNewPrice;
                        bridgeItem.CommissionAfter_Verified = fetchedNewCommission;
                    }
                    else
                    {
                        _logger.LogWarning("Wgrano zmianę dla {OfferId}, ale weryfikacja danych (GetOfferData) nie powiodła się.", bridgeItem.AllegroOfferId);
                        bridgeItem.ErrorMessage = "Wgrano, ale weryfikacja GetOfferData nie powiodła się.";
                        result.Errors.Add(new PriceBridgeError { OfferId = bridgeItem.AllegroOfferId, Message = bridgeItem.ErrorMessage });
                    }

                    result.SuccessfulChangesDetails.Add(new PriceBridgeSuccessDetail
                    {
                        OfferId = bridgeItem.AllegroOfferId,
                        FetchedNewPrice = bridgeItem.PriceAfter_Verified,
                        FetchedNewCommission = bridgeItem.CommissionAfter_Verified
                    });
                }
                catch (AllegroAuthException authEx)
                {
                    _logger.LogError(authEx, "Błąd autoryzacji podczas weryfikacji zmiany ceny dla oferty {OfferId}", bridgeItem.AllegroOfferId);
                    bridgeItem.Success = false;
                    bridgeItem.ErrorMessage = $"Wgrano, ale weryfikacja nieudana: {authEx.Message}";
                    result.SuccessfulCount--;
                    result.FailedCount++;
                    result.Errors.Add(new PriceBridgeError { OfferId = bridgeItem.AllegroOfferId, Message = bridgeItem.ErrorMessage });

                    store.IsAllegroTokenActive = false;
                    await _context.SaveChangesAsync();

                    int remainingToVerify = itemsToVerify.Count - result.SuccessfulChangesDetails.Count;
                    if (remainingToVerify > 0)
                    {
                        result.Errors.Add(new PriceBridgeError { OfferId = "Pozostałe", Message = "Token API wygasł podczas weryfikacji." });
                    }
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd podczas weryfikacji zmiany ceny dla oferty {OfferId}", bridgeItem.AllegroOfferId);
                    bridgeItem.ErrorMessage = $"Wgrano, ale weryfikacja nieudana: {ex.Message}";
                    result.Errors.Add(new PriceBridgeError { OfferId = bridgeItem.AllegroOfferId, Message = bridgeItem.ErrorMessage });
                }
            }

            newBatch.SuccessfulCount = result.SuccessfulCount;
            newBatch.FailedCount = result.FailedCount;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Błąd zapisu logów AllegroPriceBridgeBatch do bazy danych.");
                result.Errors.Add(new PriceBridgeError { OfferId = "Baza Danych", Message = "Błąd zapisu logów operacji." });
            }

            return result;
        }

        private async Task<(bool Success, string ErrorMessage)> SetNewOfferPriceAsync(string accessToken, string offerId, string newPrice)
        {
            var apiUrl = $"https://api.allegro.pl/sale/product-offers/{offerId}";

            var formattedPrice = decimal.Parse(newPrice, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

            var payload = new
            {
                sellingMode = new
                {
                    price = new
                    {
                        amount = formattedPrice,
                        currency = "PLN"
                    }
                }
            };

            var httpContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.allegro.public.v1+json");

            using var request = new HttpRequestMessage(HttpMethod.Patch, apiUrl) { Content = httpContent };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            try
            {
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    return (true, string.Empty);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return (false, "Błąd autoryzacji (401). Token może być nieważny.");
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Błąd API Allegro podczas zmiany ceny oferty {OfferId}. Status: {StatusCode}. Odpowiedź: {ErrorContent}", offerId, response.StatusCode, errorContent);

                try
                {
                    var errorNode = JsonNode.Parse(errorContent);
                    var errors = errorNode?["errors"]?.AsArray();
                    if (errors != null && errors.Count > 0)
                    {
                        return (false, errors[0]?["message"]?.ToString() ?? errorContent);
                    }
                }
                catch { }

                return (false, $"Błąd API ({response.StatusCode}). {errorContent}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Krytyczny błąd podczas wysyłania zmiany ceny dla oferty {OfferId}", offerId);
                return (false, $"Wyjątek aplikacji: {ex.Message}");
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
                throw new AllegroAuthException($"Błąd autoryzacji (401) podczas pobierania oferty {offerId} (weryfikacja). Token jest prawdopodobnie nieważny.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nie udało się pobrać danych oferty {OfferId} (weryfikacja). Status: {StatusCode}. Odpowiedź: {Response}", offerId, response.StatusCode, await response.Content.ReadAsStringAsync());
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
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nie udało się pobrać prowizji (weryfikacja). Status: {StatusCode}. Odpowiedź: {Response}", response.StatusCode, await response.Content.ReadAsStringAsync());
                return null;
            }

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
    }
}