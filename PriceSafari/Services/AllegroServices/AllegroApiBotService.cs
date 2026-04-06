


using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Diagnostics;
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
        int? VisitsCount,
        string? Brand,
        string? ProducerCode
    );

    internal record BadgeData(string CampaignName, JsonNode? BadgeNode);
    internal record AlleDiscountData(string CampaignName, JsonNode? Prices);

    public enum FailureReason
    {
        None,
        Unknown,
        RateLimited,
        Timeout,
        NetworkError,
        Unauthorized,
        NotFound,
        NullCommission,
        InvalidPrice,
        ApiError
    }

    public class ProcessingCounters
    {
        private int _success = 0;
        private int _failure = 0;

        public int Success => _success;
        public int Failure => _failure;

        public void IncrementSuccess() => Interlocked.Increment(ref _success);
        public void IncrementFailure() => Interlocked.Increment(ref _failure);
    }

    public class AllegroApiBotService
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<AllegroApiBotService> _logger;
        private readonly AllegroAuthTokenService _authTokenService;

        private const int MAX_RETRIES = 3;
        private const int INITIAL_DELAY_MS = 500;
        private const int CONCURRENT_REQUESTS = 4;
        private const int BULK_OFFERS_PAGE_SIZE = 1000;
        private const int RATE_LIMIT_PER_MINUTE = 9000;
        private const int RATE_LIMIT_SAFETY = 8500;

        private readonly Dictionary<FailureReason, int> _failureStats = new();
        private readonly object _statsLock = new();

        // ═══ Inline request tracking ═══
        private int _apiRequestCount = 0;
        private Stopwatch _sessionStopwatch = new();
        private readonly Dictionary<int, int> _requestsPerMinute = new();

        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public AllegroApiBotService(
            PriceSafariContext context,
            ILogger<AllegroApiBotService> logger,
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

        public async Task<ApiProcessingResult> ProcessOffersForActiveStoresAsync()
        {
            var result = new ApiProcessingResult();

            _apiRequestCount = 0;
            lock (_statsLock) { _requestsPerMinute.Clear(); }
            _sessionStopwatch = Stopwatch.StartNew();

            _logger.LogInformation("Rozpoczynam proces pobierania dodatkowych danych z API Allegro...");

            var activeStores = await _context.Stores
                .Where(s =>
                    s.OnAllegro
                    && s.FetchExtendedAllegroData
                    && s.IsAllegroTokenActive
                    && (!string.IsNullOrEmpty(s.AllegroApiToken) || !string.IsNullOrEmpty(s.AllegroRefreshToken))
                    && _context.AllegroOffersToScrape.Any(o =>
                        o.StoreId == s.StoreId
                        && o.IsScraped == true
                        && o.IsApiProcessed != true
                    )
                )
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
                lock (_statsLock) { _failureStats.Clear(); }

                var storeResult = await ProcessOffersForSingleStore(store);

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
                    var statsMessage = BuildFailureStatsMessage(store.StoreName, storeResult.failureCount);
                    result.Messages.Add(statsMessage);
                }
            }

            _sessionStopwatch.Stop();

            var apiStats = BuildApiStatsString();

            _logger.LogInformation(
                "Zakończono proces. Sprawdzono: {Checked}, Sukces: {Success}, Błędy: {Failed}. {ApiStats}",
                result.TotalOffersChecked, result.TotalOffersSuccess, result.TotalOffersFailed,
                apiStats);

            result.Messages.Add(apiStats);

            return result;
        }

        private string BuildFailureStatsMessage(string storeName, int totalFailures)
        {
            var sb = new StringBuilder($"Sklep {storeName}: Błędy przy {totalFailures} ofertach.");

            lock (_statsLock)
            {
                if (_failureStats.Count > 0)
                {
                    sb.Append(" Powody: ");
                    var reasons = _failureStats
                        .OrderByDescending(x => x.Value)
                        .Select(x => $"{x.Key}={x.Value}");
                    sb.Append(string.Join(", ", reasons));
                }
            }

            return sb.ToString();
        }

        private void RecordFailure(FailureReason reason)
        {
            lock (_statsLock)
            {
                _failureStats.TryGetValue(reason, out int count);
                _failureStats[reason] = count + 1;
            }
        }

        private async Task<(bool success, int checkedCount, int successCount, int failureCount, string message)> ProcessOffersForSingleStore(StoreClass store)
        {
            _logger.LogInformation("Przetwarzam sklep: {StoreName} (ID: {StoreId})", store.StoreName, store.StoreId);

            var offersToProcess = await _context.AllegroOffersToScrape
                .Where(o => o.StoreId == store.StoreId && o.IsScraped && o.IsApiProcessed != true)
                .ToListAsync();

            int totalToCheck = offersToProcess.Count;
            if (totalToCheck == 0) return (true, 0, 0, 0, string.Empty);

            string? accessToken = await _authTokenService.GetValidAccessTokenAsync(store.StoreId);

            if (string.IsNullOrEmpty(accessToken))
            {
                return (false, totalToCheck, 0, 0, $"Nie udało się pobrać tokena dla sklepu '{store.StoreName}'.");
            }

            // Test tokena
            try
            {
                var testOffer = offersToProcess.First();
                await GetOfferDataWithRetry(accessToken, testOffer.AllegroOfferId.ToString());
            }
            catch (AllegroAuthException)
            {
                _logger.LogWarning("Wykryto nieważny token (401). Próba wymuszonego odświeżenia...");
                accessToken = await _authTokenService.ForceRefreshTokenAsync(store.StoreId);

                if (string.IsNullOrEmpty(accessToken))
                {
                    return (false, totalToCheck, 0, 0, "Token wygasł i nie udało się go odświeżyć.");
                }
                _logger.LogInformation("Token został pomyślnie odświeżony.");
            }
            catch (Exception) { /* Kontynuuj */ }

            // ═══ BULK FAZA: Pobierz wizyty i badge'e hurtowo ═══
            _logger.LogInformation("📦 Bulk: Pobieram wizyty i badge'e hurtowo dla {Count} ofert...", totalToCheck);

            var offerIds = offersToProcess.Select(o => o.AllegroOfferId.ToString()).ToHashSet();

            var bulkVisitsTask = BulkFetchVisitsAsync(accessToken, offerIds);
            var bulkBadgesTask = BulkFetchBadgesAsync(accessToken, offerIds);
            await Task.WhenAll(bulkVisitsTask, bulkBadgesTask);

            var visitsMap = await bulkVisitsTask;
            var badgesMap = await bulkBadgesTask;

            _logger.LogInformation(
                "📦 Bulk gotowy. Wizyty: {VisitsCount}/{Total} ofert, Badge'e: {BadgesCount} ofert z aktywnymi badge'ami. {ApiStats}",
                visitsMap.Count, totalToCheck, badgesMap.Count, BuildApiStatsString());

            // ═══ PER-OFFER FAZA ═══
            var counters = new ProcessingCounters();
            var failedOffersLock = new object();
            var failedOffers = new List<AllegroOfferToScrape>();

            try
            {
                var semaphore = new SemaphoreSlim(CONCURRENT_REQUESTS);

                _logger.LogInformation("Rozpoczynam przetwarzanie {Count} ofert dla sklepu {StoreName}...",
                    totalToCheck, store.StoreName);

                var tasks = offersToProcess.Select(offer => ProcessSingleOfferAsync(
                    offer, accessToken!, semaphore, counters, failedOffers, failedOffersLock,
                    visitsMap, badgesMap, isRetry: false
                )).ToList();

                await Task.WhenAll(tasks);

                if (failedOffers.Count > 0)
                {
                    _logger.LogInformation("🔄 Retry: Ponawiam {Count} nieudanych ofert po 3 sekundach...",
                        failedOffers.Count);

                    await Task.Delay(3000);

                    var retryList = failedOffers.ToList();
                    failedOffers.Clear();

                    var retrySemaphore = new SemaphoreSlim(2);

                    var retryTasks = retryList.Select(offer => ProcessSingleOfferAsync(
                        offer, accessToken!, retrySemaphore, counters, failedOffers, failedOffersLock,
                        visitsMap, badgesMap, isRetry: true
                    )).ToList();

                    await Task.WhenAll(retryTasks);

                    _logger.LogInformation("🔄 Retry zakończony. Naprawiono: {Fixed}, Nadal błędnych: {StillFailed}",
                        retryList.Count - failedOffers.Count, failedOffers.Count);
                }

                await _context.SaveChangesAsync();

                string msg = counters.Failure > 0 ? $"Ostrzeżenie: {counters.Failure} błędów." : string.Empty;
                return (true, totalToCheck, counters.Success, counters.Failure, msg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd krytyczny przetwarzania");
                return (false, totalToCheck, counters.Success, counters.Failure, $"Błąd krytyczny: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // BULK: Wizyty — GET /sale/offers z paginacją (limit=1000)
        // Zamiast N x GET /sale/offers?offer.id=X → ceil(N/1000) requestów
        // ═══════════════════════════════════════════════════════════════
        private async Task<Dictionary<string, int?>> BulkFetchVisitsAsync(string accessToken, HashSet<string> offerIds)
        {
            var result = new Dictionary<string, int?>();
            int offset = 0;
            bool hasMore = true;

            try
            {
                while (hasMore)
                {
                    var apiUrl = $"https://api.allegro.pl/sale/offers?limit={BULK_OFFERS_PAGE_SIZE}&offset={offset}&publication.status=ACTIVE";
                    var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

                    var response = await _httpClient.SendAsync(request);
                    await RecordApiRequestAsync();

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("⚠️ Rate limit hit podczas bulk fetch visits (offset {Offset}). Czekam 5s...", offset);
                        await Task.Delay(5000);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Bulk visits: błąd {StatusCode} na offset {Offset}", response.StatusCode, offset);
                        break;
                    }

                    var rootNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                    var offersArray = rootNode?["offers"]?.AsArray();

                    if (offersArray == null || offersArray.Count == 0)
                        break;

                    foreach (var offer in offersArray)
                    {
                        var id = offer?["id"]?.ToString();
                        if (id != null && offerIds.Contains(id))
                        {
                            var visitsStr = offer?["stats"]?["visitsCount"]?.ToString();
                            result[id] = int.TryParse(visitsStr, out var v) ? v : null;
                        }
                    }

                    int totalCount = rootNode?["totalCount"]?.GetValue<int>() ?? 0;
                    offset += BULK_OFFERS_PAGE_SIZE;

                    if (offset >= totalCount || offersArray.Count < BULK_OFFERS_PAGE_SIZE)
                        hasMore = false;

                    // Mamy wizyty dla wszystkich szukanych ofert — koniec
                    if (offerIds.All(id => result.ContainsKey(id)))
                        hasMore = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bulk visits: wyjątek. Częściowe dane: {Count} ofert.", result.Count);
            }

            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        // BULK: Badge'e — GET /sale/badges?marketplace.id=allegro-pl
        // Zamiast N x GET /sale/badges?offer.id=X → 1 (lub kilka stron)
        // ═══════════════════════════════════════════════════════════════
        private async Task<Dictionary<string, List<BadgeData>>> BulkFetchBadgesAsync(string accessToken, HashSet<string> offerIds)
        {
            var result = new Dictionary<string, List<BadgeData>>();
            int offset = 0;
            bool hasMore = true;
            int limit = 1000;

            try
            {
                while (hasMore)
                {
                    var apiUrl = $"https://api.allegro.pl/sale/badges?marketplace.id=allegro-pl&limit={limit}&offset={offset}";
                    var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

                    var response = await _httpClient.SendAsync(request);
                    await RecordApiRequestAsync();

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        _logger.LogWarning("⚠️ Rate limit hit podczas bulk fetch badges. Czekam 5s...");
                        await Task.Delay(5000);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Bulk badges: błąd {StatusCode}", response.StatusCode);
                        break;
                    }

                    var rootNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                    var badgesArray = rootNode?["badges"]?.AsArray();

                    if (badgesArray == null || badgesArray.Count == 0)
                        break;

                    foreach (var badge in badgesArray)
                    {
                        var offerId = badge?["offer"]?["id"]?.ToString();
                        var processStatus = badge?["process"]?["status"]?.ToString();
                        var campaignName = badge?["campaign"]?["name"]?.ToString();

                        if (offerId != null && offerIds.Contains(offerId)
                            && processStatus == "ACTIVE" && !string.IsNullOrEmpty(campaignName))
                        {
                            if (!result.ContainsKey(offerId))
                                result[offerId] = new List<BadgeData>();

                            result[offerId].Add(new BadgeData(campaignName, badge));
                        }
                    }

                    if (badgesArray.Count < limit)
                        hasMore = false;
                    else
                        offset += limit;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bulk badges: wyjątek. Częściowe dane: {Count} ofert.", result.Count);
            }

            return result;
        }

        private async Task ProcessSingleOfferAsync(
            AllegroOfferToScrape offer,
            string accessToken,
            SemaphoreSlim semaphore,
            ProcessingCounters counters,
            List<AllegroOfferToScrape> failedOffers,
            object failedOffersLock,
            Dictionary<string, int?> visitsMap,
            Dictionary<string, List<BadgeData>> badgesMap,
            bool isRetry)
        {
            await semaphore.WaitAsync();
            try
            {
                var offerId = offer.AllegroOfferId.ToString();

                // Wizyty i badges z bulk — 0 dodatkowych requestów
                int? visitsCount = visitsMap.TryGetValue(offerId, out var v) ? v : null;
                var badgeCampaigns = badgesMap.TryGetValue(offerId, out var b) ? b : new List<BadgeData>();

                var (apiData, failureReason) = await FetchApiDataForOfferWithDiagnostics(
                    accessToken, offerId, visitsCount, badgeCampaigns);

                if (apiData != null && apiData.Commission.HasValue)
                {
                    offer.ApiAllegroPriceFromUser = apiData.SellerRevenue;
                    offer.ApiAllegroPrice = apiData.CustomerPrice;
                    offer.ApiAllegroCommission = apiData.Commission;
                    offer.AllegroEan = apiData.Ean;
                    offer.AnyPromoActive = apiData.IsAnyPromoActive;
                    offer.IsSubsidyActive = apiData.IsSubsidyActive;
                    offer.AllegroVisitsCount = apiData.VisitsCount;
                    offer.AllegroBrand = apiData.Brand;
                    offer.AllegroProducerCode = apiData.ProducerCode;
                    offer.IsApiProcessed = true;

                    counters.IncrementSuccess();
                }
                else
                {
                    RecordFailure(failureReason);

                    if (!isRetry && ShouldRetry(failureReason))
                    {
                        lock (failedOffersLock) { failedOffers.Add(offer); }
                    }
                    else
                    {
                        counters.IncrementFailure();
                        _logger.LogWarning("❌ Oferta {OfferId}: {Reason}", offer.AllegroOfferId, failureReason);
                    }
                }
            }
            catch (Exception ex)
            {
                RecordFailure(FailureReason.Unknown);

                if (!isRetry)
                {
                    lock (failedOffersLock) { failedOffers.Add(offer); }
                }
                else
                {
                    counters.IncrementFailure();
                    _logger.LogError(ex, "❌ Błąd oferty {OfferId}", offer.AllegroOfferId);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }

        private bool ShouldRetry(FailureReason reason)
        {
            return reason switch
            {
                FailureReason.RateLimited => true,
                FailureReason.Timeout => true,
                FailureReason.NetworkError => true,
                FailureReason.Unknown => true,
                FailureReason.ApiError => true,
                FailureReason.Unauthorized => false,
                FailureReason.NotFound => false,
                FailureReason.NullCommission => false,
                FailureReason.InvalidPrice => false,
                _ => false
            };
        }

        /// <summary>
        /// Teraz 2 req/ofertę (offer data + commission) + ew. AlleDiscount.
        /// Visits i badges przychodzą z bulk (0 req).
        /// Wcześniej: 4-5 req/ofertę.
        /// </summary>
        private async Task<(AllegroApiSummary? Data, FailureReason Reason)> FetchApiDataForOfferWithDiagnostics(
            string accessToken, string offerId,
            int? preloadedVisitsCount,
            List<BadgeData> preloadedBadges)
        {
            try
            {
                var offerDataNode = await GetOfferDataWithRetry(accessToken, offerId);
                if (offerDataNode == null)
                    return (null, FailureReason.ApiError);

                var commissionTask = GetOfferCommissionWithRetry(accessToken, offerDataNode);
                var alleDiscountsTask = GetAlleDiscountsAsync(accessToken, offerId, preloadedBadges);

                await Task.WhenAll(commissionTask, alleDiscountsTask);

                var commission = await commissionTask;
                var alleDiscountCampaigns = await alleDiscountsTask;

                if (!commission.HasValue)
                    return (null, FailureReason.NullCommission);

                var basePriceStr = offerDataNode["sellingMode"]?["price"]?["amount"]?.ToString();
                if (!decimal.TryParse(basePriceStr, CultureInfo.InvariantCulture, out var basePrice))
                    return (null, FailureReason.InvalidPrice);

                // Parsowanie EAN
                string? ean = null;
                try
                {
                    var parameters = offerDataNode["productSet"]?[0]?["product"]?["parameters"]?.AsArray();
                    var eanValue = parameters?.FirstOrDefault(p => p?["id"]?.ToString() == "225693")?["values"]?[0]?.ToString();
                    if (!string.IsNullOrWhiteSpace(eanValue) && eanValue != "Brak") ean = eanValue;
                }
                catch { }

                // Parsowanie Brand
                string? brand = null;
                try
                {
                    var parameters = offerDataNode["productSet"]?[0]?["product"]?["parameters"]?.AsArray();
                    if (parameters != null)
                    {
                        var brandParam = parameters.FirstOrDefault(p =>
                            p?["id"]?.ToString() == "11323" ||
                            p?["name"]?.ToString()?.Equals("Marka", StringComparison.OrdinalIgnoreCase) == true
                        );
                        if (brandParam != null)
                        {
                            var labelsArray = brandParam["valuesLabels"]?.AsArray();
                            if (labelsArray != null && labelsArray.Count > 0)
                                brand = labelsArray[0]?.ToString();
                            else
                            {
                                var valuesArray = brandParam["values"]?.AsArray();
                                if (valuesArray != null && valuesArray.Count > 0)
                                    brand = valuesArray[0]?.ToString();
                            }
                        }
                    }
                }
                catch { }

                // ProducerCode
                string? producerCode = null;
                try { producerCode = offerDataNode["external"]?["id"]?.ToString(); }
                catch { }

                decimal sellerEarns = basePrice;
                decimal customerPays = basePrice;
                bool isAnyPromoActive = false;
                bool isSubsidyActive = false;

                var activeAlleDiscount = alleDiscountCampaigns.FirstOrDefault();

                var activeSubsidyBadge = preloadedBadges.FirstOrDefault(b =>
                    b.BadgeNode?["prices"]?["subsidy"] != null &&
                    b.BadgeNode?["prices"]?["subsidy"].ToString() != "null"
                );

                var activeBargainBadge = preloadedBadges.FirstOrDefault(b =>
                    b.BadgeNode?["prices"]?["bargain"] != null &&
                    (b.BadgeNode?["prices"]?["subsidy"] == null ||
                     b.BadgeNode?["prices"]?["subsidy"].ToString() == "null")
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
                        sellerEarns = decimal.TryParse(originalPriceStr, CultureInfo.InvariantCulture, out var op) ? op : basePrice;
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
                    return (null, FailureReason.InvalidPrice);

                return (new AllegroApiSummary(
                    customerPays, sellerEarns, commission, ean,
                    isAnyPromoActive, isSubsidyActive,
                    preloadedVisitsCount, brand, producerCode),
                FailureReason.None);
            }
            catch (AllegroAuthException) { return (null, FailureReason.Unauthorized); }
            catch (TaskCanceledException) { return (null, FailureReason.Timeout); }
            catch (HttpRequestException) { return (null, FailureReason.NetworkError); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nieoczekiwany błąd dla oferty {OfferId}", offerId);
                return (null, FailureReason.Unknown);
            }
        }

        private async Task<JsonNode?> GetOfferDataWithRetry(string accessToken, string offerId)
        {
            int attempt = 0;
            int delayMs = INITIAL_DELAY_MS;

            while (attempt < MAX_RETRIES)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.allegro.pl/sale/product-offers/{offerId}");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

                    var response = await _httpClient.SendAsync(request);
                    await RecordApiRequestAsync();

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new AllegroAuthException($"Błąd 401 dla oferty {offerId}");

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        attempt++;
                        if (attempt >= MAX_RETRIES) return null;
                        await Task.Delay(delayMs * 2);
                        delayMs *= 2;
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                        return null;

                    if (!response.IsSuccessStatusCode)
                    {
                        attempt++;
                        if (attempt >= MAX_RETRIES) return null;
                        await Task.Delay(delayMs);
                        delayMs *= 2;
                        continue;
                    }

                    return JsonNode.Parse(await response.Content.ReadAsStringAsync());
                }
                catch (AllegroAuthException) { throw; }
                catch (TaskCanceledException)
                {
                    attempt++;
                    if (attempt >= MAX_RETRIES) return null;
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                }
                catch (HttpRequestException)
                {
                    attempt++;
                    if (attempt >= MAX_RETRIES) return null;
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                }
            }
            return null;
        }

        private async Task<decimal?> GetOfferCommissionWithRetry(string accessToken, JsonNode offerData)
        {
            int attempt = 0;
            int delayMs = INITIAL_DELAY_MS;

            while (attempt < MAX_RETRIES)
            {
                try
                {
                    var payload = new { offer = offerData };
                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.allegro.public.v1+json");
                    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.allegro.pl/pricing/offer-fee-preview") { Content = content };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                    var response = await _httpClient.SendAsync(request);
                    await RecordApiRequestAsync();

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        attempt++;
                        if (attempt >= MAX_RETRIES) return null;
                        await Task.Delay(delayMs * 2);
                        delayMs *= 2;
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        attempt++;
                        if (attempt >= MAX_RETRIES) return null;
                        await Task.Delay(delayMs);
                        delayMs *= 2;
                        continue;
                    }

                    var feeNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
                    var feeAmountString = feeNode?["commissions"]?[0]?["fee"]?["amount"]?.ToString();

                    if (decimal.TryParse(feeAmountString, CultureInfo.InvariantCulture, out var feeDecimal))
                        return feeDecimal;

                    return null;
                }
                catch (TaskCanceledException)
                {
                    attempt++;
                    if (attempt >= MAX_RETRIES) return null;
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                }
                catch (HttpRequestException)
                {
                    attempt++;
                    if (attempt >= MAX_RETRIES) return null;
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                }
            }
            return null;
        }

        private async Task<List<AlleDiscountData>> GetAlleDiscountsAsync(string accessToken, string offerId, List<BadgeData> badges)
        {
            var activeDiscounts = new List<AlleDiscountData>();

            var alleDiscountBadges = badges
                .Where(b => b.CampaignName.Contains("AlleObniżka") || b.CampaignName.Contains("AlleDiscount"))
                .ToList();

            if (alleDiscountBadges.Count == 0) return activeDiscounts;

            var tasks = new List<Task<AlleDiscountData?>>();

            foreach (var badge in alleDiscountBadges)
            {
                var campaignId = badge.BadgeNode?["campaign"]?["id"]?.ToString();
                if (string.IsNullOrEmpty(campaignId)) continue;
                tasks.Add(CheckSingleCampaignAsync(accessToken, offerId, campaignId, badge.CampaignName));
            }

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
            {
                if (result != null) activeDiscounts.Add(result);
            }

            return activeDiscounts;
        }

        private async Task<AlleDiscountData?> CheckSingleCampaignAsync(string accessToken, string offerId, string campaignId, string campaignName)
        {
            int attempt = 0;
            int delayMs = INITIAL_DELAY_MS;

            while (attempt < MAX_RETRIES)
            {
                try
                {
                    var submittedApiUrl = $"https://api.allegro.pl/sale/alle-discount/{campaignId}/submitted-offers?offer.id={offerId}";
                    var submittedRequest = new HttpRequestMessage(HttpMethod.Get, submittedApiUrl);
                    submittedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                    submittedRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

                    var submittedResponse = await _httpClient.SendAsync(submittedRequest);
                    await RecordApiRequestAsync();

                    if (submittedResponse.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        attempt++;
                        if (attempt >= MAX_RETRIES) return null;
                        await Task.Delay(delayMs * 2);
                        delayMs *= 2;
                        continue;
                    }

                    if (!submittedResponse.IsSuccessStatusCode) return null;

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
                catch (TaskCanceledException)
                {
                    attempt++;
                    if (attempt >= MAX_RETRIES) return null;
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                }
                catch (HttpRequestException)
                {
                    attempt++;
                    if (attempt >= MAX_RETRIES) return null;
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                }
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
            public int TotalOffersChecked { get; set; } = 0;
            public int TotalOffersSuccess { get; set; } = 0;
            public int TotalOffersFailed { get; set; } = 0;
            public List<string> Messages { get; set; } = new List<string>();
        }
    }
}