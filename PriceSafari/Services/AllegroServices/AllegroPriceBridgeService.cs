


using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Controllers.MemberControllers;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.DTOs;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly AllegroAuthTokenService _authTokenService;
        private static readonly HttpClient _httpClient = new();

        // ═══ Inline request tracking ═══
        private int _apiRequestCount = 0;
        public string? LastApiStats { get; private set; }
        private Stopwatch _sessionStopwatch = new();
        private readonly Dictionary<int, int> _requestsPerMinute = new();
        private readonly object _statsLock = new();

        private const int RATE_LIMIT_PER_MINUTE = 9000;
        private const int RATE_LIMIT_SAFETY = 8500;

        public AllegroPriceBridgeService(
            PriceSafariContext context,
            ILogger<AllegroPriceBridgeService> logger,
            AllegroAuthTokenService authTokenService)
        {
            _context = context;
            _logger = logger;
            _authTokenService = authTokenService;
        }

        private async Task RecordApiRequestAsync(int count = 1)
        {
            Interlocked.Add(ref _apiRequestCount, count);

            int minuteKey = (int)_sessionStopwatch.Elapsed.TotalMinutes;
            int currentMinuteRequests;
            lock (_statsLock)
            {
                _requestsPerMinute.TryGetValue(minuteKey, out int current);
                _requestsPerMinute[minuteKey] = current + count;
                currentMinuteRequests = _requestsPerMinute[minuteKey];
            }

            if (currentMinuteRequests >= RATE_LIMIT_SAFETY)
            {
                var elapsedInMinute = _sessionStopwatch.Elapsed.TotalSeconds % 60;
                var waitSeconds = 61 - elapsedInMinute;
                _logger.LogWarning("⏳ Rate limit throttle ({Current}/{Limit}/min): czekam {Wait:F0}s do nowej minuty...",
                    currentMinuteRequests, RATE_LIMIT_PER_MINUTE, waitSeconds);
                await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
            }
        }

        private string BuildApiStatsString()
        {
            var sb = new StringBuilder();
            sb.Append($"API requests: {_apiRequestCount} w {_sessionStopwatch.Elapsed:mm\\:ss}");

            lock (_statsLock)
            {
                if (_requestsPerMinute.Count > 0)
                {
                    var peak = _requestsPerMinute.Values.Max();
                    var avg = _sessionStopwatch.Elapsed.TotalMinutes > 0
                        ? (int)(_apiRequestCount / _sessionStopwatch.Elapsed.TotalMinutes)
                        : _apiRequestCount;

                    sb.Append($", śr: {avg}/min, szczyt: {peak}/min (limit: {RATE_LIMIT_PER_MINUTE}/min)");
                    sb.Append(", per minuta: [");
                    sb.Append(string.Join(", ",
                        _requestsPerMinute.OrderBy(x => x.Key).Select(x => $"min{x.Key}:{x.Value}")));
                    sb.Append(']');
                }
            }

            return sb.ToString();
        }

        public async Task<PriceBridgeResult> ExecutePriceChangesAsync(
            int storeId,
            int allegroScrapeHistoryId,
            string userId,
            bool includeCommissionInMargin,
            List<AllegroPriceBridgeItemRequest> itemsToBridge,
            bool isAutomation = false,

            int? automationRuleId = null,
            int? targetMetCount = null,
            int? targetUnmetCount = null,
            int? priceIncreasedCount = null,
            int? priceDecreasedCount = null,
            int? priceMaintainedCount = null,
            int? totalProductsInRule = null)
        {
            var result = new PriceBridgeResult();

            // Reset tracking
            _apiRequestCount = 0;
            lock (_statsLock) { _requestsPerMinute.Clear(); }
            _sessionStopwatch = Stopwatch.StartNew();

            var store = await _context.Stores.FindAsync(storeId);

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

            string? accessToken = await _authTokenService.GetValidAccessTokenAsync(storeId);


            if (string.IsNullOrEmpty(accessToken))
            {
                string tokenDiag = _authTokenService.LastTokenDiagnostics ?? "brak diagnostyki";

                result.FailedCount = itemsToBridge.Count;
                result.Errors.Add(new PriceBridgeError
                {
                    OfferId = "Wszystkie",
                    Message = $"Nie udało się uzyskać tokena. Diagnostyka: {tokenDiag}"
                });
                _logger.LogWarning("Przerwano PriceBridge dla sklepu {StoreId}. Token: {TokenDiag}", storeId, tokenDiag);
                return result;
            }


            if (itemsToBridge.Any())
            {
                try
                {
                    var testItem = itemsToBridge.First();
                    if (!string.IsNullOrEmpty(testItem.OfferId))
                    {
                        await GetOfferData(accessToken, testItem.OfferId);
                    }
                }
                catch (AllegroAuthException)
                {
                    _logger.LogWarning("Wykryto 'Zombie Token' (401) w PriceBridge mimo poprawnej daty. Próba wymuszonego odświeżenia...");

                    accessToken = await _authTokenService.ForceRefreshTokenAsync(storeId);

                    if (string.IsNullOrEmpty(accessToken))
                    {
                        result.FailedCount = itemsToBridge.Count;
                        result.Errors.Add(new PriceBridgeError { OfferId = "Wszystkie", Message = "Token wygasł i nie udało się go odświeżyć." });
                        return result;
                    }
                    _logger.LogInformation("Token został pomyślnie odświeżony w PriceBridge. Kontynuuję.");
                }
                catch (Exception)
                {
                }
            }

            var newBatch = new AllegroPriceBridgeBatch
            {
                ExecutionDate = DateTime.Now,
                StoreId = storeId,
                AllegroScrapeHistoryId = allegroScrapeHistoryId,
                UserId = userId,
                SuccessfulCount = 0,
                FailedCount = 0,

                IsAutomation = isAutomation,
                AutomationRuleId = automationRuleId,

                TotalProductsCount = totalProductsInRule.HasValue ? totalProductsInRule.Value : itemsToBridge.Count,

                TargetMetCount = targetMetCount,
                TargetUnmetCount = targetUnmetCount,
                PriceIncreasedCount = priceIncreasedCount,
                PriceDecreasedCount = priceDecreasedCount,
                PriceMaintainedCount = priceMaintainedCount
            };
            _context.AllegroPriceBridgeBatches.Add(newBatch);

            var itemsToVerify = new List<AllegroPriceBridgeItem>();

            _logger.LogInformation("Rozpoczynam PĘTLĘ 1: Wysyłanie {Count} zmian cen... (Automat: {IsAuto})", itemsToBridge.Count, isAutomation);

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

                    Mode = item.Mode,
                    PriceIndexTarget = item.PriceIndexTarget,
                    StepPriceApplied = item.StepPriceApplied,

                    MinPriceLimit = item.MinPriceLimit,
                    MaxPriceLimit = item.MaxPriceLimit,
                    WasLimitedByMin = item.WasLimitedByMin,
                    WasLimitedByMax = item.WasLimitedByMax
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
                        _logger.LogWarning("Token API dla sklepu {StoreId} wygasł w trakcie przetwarzania batcha (Loop). Przerywam.", storeId);
                        store.IsAllegroTokenActive = false;

                        int remainingChanges = itemsToBridge.Count - result.SuccessfulCount - result.FailedCount;
                        if (remainingChanges > 0)
                        {
                            result.FailedCount += remainingChanges;
                            result.Errors.Add(new PriceBridgeError { OfferId = "Pozostałe", Message = "Token API wygasł w trakcie operacji. Przerwano." });
                        }

                        _context.AllegroPriceBridgeItems.Add(bridgeItem);
                        break;
                    }
                }

                _context.AllegroPriceBridgeItems.Add(bridgeItem);
            }

            _logger.LogInformation("PĘTLA 1 zakończona. Sukces: {Success}, Błędy: {Failed}. {ApiStats}",
                result.SuccessfulCount, result.FailedCount, BuildApiStatsString());

            if (!store.IsAllegroTokenActive)
            {
                await _context.SaveChangesAsync();
            }

            if (itemsToVerify.Any())
            {
                _logger.LogInformation("Oczekuję 3 sekundy na przetworzenie przez Allegro...");
                await Task.Delay(3000);
            }

            _logger.LogInformation("Rozpoczynam PĘTLĘ 2: Weryfikacja {Count} zmian...", itemsToVerify.Count);

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
                    _logger.LogError(authEx, "Błąd autoryzacji podczas weryfikacji.");
                    bridgeItem.Success = false;
                    bridgeItem.ErrorMessage = $"Wgrano, ale weryfikacja nieudana (Auth): {authEx.Message}";

                    result.SuccessfulCount--;
                    result.FailedCount++;

                    store.IsAllegroTokenActive = false;
                    await _context.SaveChangesAsync();
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd weryfikacji.");
                    bridgeItem.ErrorMessage = $"Wgrano, ale weryfikacja nieudana: {ex.Message}";
                }
            }

            newBatch.SuccessfulCount = result.SuccessfulCount;
            newBatch.FailedCount = result.FailedCount;

            _sessionStopwatch.Stop();

            LastApiStats = BuildApiStatsString();

            _logger.LogInformation(
                "PriceBridge zakończony. Sukces: {Success}, Błędy: {Failed}. {ApiStats}",
                result.SuccessfulCount, result.FailedCount, LastApiStats);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "Błąd zapisu logów batcha.");
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
                await RecordApiRequestAsync();

                if (response.IsSuccessStatusCode)
                {
                    return (true, string.Empty);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    string errorDetails = string.Empty;
                    try { errorDetails = await response.Content.ReadAsStringAsync(); } catch { errorDetails = "Brak treści błędu"; }
                    _logger.LogError("!!! CRITICAL 401 !!! Błąd autoryzacji dla oferty {OfferId}. Allegro Body: {ErrorDetails}", offerId, errorDetails);
                    return (false, $"Błąd autoryzacji (401). Response: {errorDetails}");
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("!!! BŁĄD API ALLEGRO !!! Nie udało się zmienić ceny oferty {OfferId}. Status: {StatusCode}. Body: {ErrorContent}", offerId, response.StatusCode, errorContent);

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
                _logger.LogError(ex, "!!! WYJĄTEK KRYTYCZNY !!! Błąd podczas wysyłania zmiany ceny dla oferty {OfferId}", offerId);
                return (false, $"Wyjątek aplikacji: {ex.Message}");
            }
        }

        private async Task<JsonNode?> GetOfferData(string accessToken, string offerId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.allegro.pl/sale/product-offers/{offerId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            var response = await _httpClient.SendAsync(request);
            await RecordApiRequestAsync();

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                string errorDetails = string.Empty;
                try { errorDetails = await response.Content.ReadAsStringAsync(); } catch { }
                throw new AllegroAuthException($"Błąd autoryzacji (401) podczas pobierania oferty {offerId}. Response: {errorDetails}");
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
            await RecordApiRequestAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nie udało się pobrać prowizji. Status: {StatusCode}.", response.StatusCode);
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