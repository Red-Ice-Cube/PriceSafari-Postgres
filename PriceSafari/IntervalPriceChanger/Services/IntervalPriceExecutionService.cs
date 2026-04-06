using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.IntervalPriceChanger.Models;
using PriceSafari.Models;
using PriceSafari.Services.AllegroServices;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PriceSafari.IntervalPriceChanger.Services
{
    public class IntervalPriceExecutionService
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<IntervalPriceExecutionService> _logger;
        private readonly AllegroAuthTokenService _authTokenService;
        private readonly IHttpClientFactory _httpClientFactory;

        private static readonly HttpClient _allegroHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public IntervalPriceExecutionService(
            PriceSafariContext context,
            ILogger<IntervalPriceExecutionService> logger,
            AllegroAuthTokenService authTokenService,
            IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _logger = logger;
            _authTokenService = authTokenService;
            _httpClientFactory = httpClientFactory;
        }

        // ═══════════════════════════════════════════════════════
        // GŁÓWNA METODA — wywoływana co 10 minut
        // ═══════════════════════════════════════════════════════

        public async Task ExecutePendingIntervalsAsync(string deviceName, CancellationToken ct)
        {
            var now = DateTime.Now;
            int dayIndex = ((int)now.DayOfWeek + 6) % 7; // Pn=0
            int slotIndex = (now.Hour * 60 + now.Minute) / 10;

            _logger.LogInformation(
                "⏱️ [IntervalExec] Sprawdzam interwały. Dzień={Day}, Slot={Slot} ({Time})",
                dayIndex, slotIndex, now.ToString("HH:mm"));

            var allRules = await _context.IntervalPriceRules
            .Include(r => r.AutomationRule)
                .ThenInclude(ar => ar.Store)
            .Where(r => r.IsActive
                     && r.AutomationRule.IsActive
                     && r.AutomationRule.Store.RemainingDays > 0
                     && r.ScheduleJson != null)
            .ToListAsync(ct);

            // Filtruj do tych z aktywnym slotem TERAZ
            var qualifyingRules = allRules
                .Where(r => r.IsEffectivelyActive && r.IsSlotActive(dayIndex, slotIndex))
                .ToList();

            if (!qualifyingRules.Any())
            {
                _logger.LogInformation("⏱️ [IntervalExec] Brak interwałów do wykonania w tym slocie.");
                return;
            }

            _logger.LogInformation(
                "⏱️ [IntervalExec] Znaleziono {Count} interwałów do wykonania.",
                qualifyingRules.Count);

            // Grupuj po sklepie
            var byStore = qualifyingRules.GroupBy(r => r.AutomationRule.StoreId);

            foreach (var storeGroup in byStore)
            {
                if (ct.IsCancellationRequested) break;

                var storeId = storeGroup.Key;
                var store = storeGroup.First().AutomationRule.Store;

                // ═══ LOCK: Interwał NIGDY nie czeka — jeśli sklep zajęty, pomija ═══
                // Główny automat używa AcquireAsync() z timeoutem — ma pierwszeństwo.
                // Interwał używa TryAcquire() — natychmiast, bez kolejki.
                using var storeLock = StoreLockManager.TryAcquire(storeId);

                if (storeLock == null)
                {
                    _logger.LogWarning(
                        "⏱️ [IntervalExec] Sklep '{StoreName}' (ID={StoreId}) zajęty — główny automat pracuje. Pomijam.",
                        store.StoreName, storeId);

                    // Zapisz informację w batch logach dla każdego pominiętego interwału
                    foreach (var rule in storeGroup)
                    {
                        var skipBatch = new IntervalPriceExecutionBatch
                        {
                            IntervalPriceRuleId = rule.Id,
                            StoreId = storeId,
                            ExecutionDate = DateTime.Now,
                            EndDate = DateTime.Now,
                            SlotIndex = slotIndex,
                            DayIndex = dayIndex,
                            PriceStepApplied = rule.PriceStep,
                            IsPriceStepPercent = rule.IsPriceStepPercent,
                            DeviceName = deviceName,
                            Comment = "POMINIĘTO — sklep zajęty przez główny automat."
                        };
                        _context.Set<IntervalPriceExecutionBatch>().Add(skipBatch);
                    }
                    await _context.SaveChangesAsync(ct);
                    continue;
                }

                _logger.LogInformation(
                    "⏱️ [IntervalExec] Lock zdobyty. Przetwarzam sklep '{StoreName}' — {Count} interwałów.",
                    store.StoreName, storeGroup.Count());

                foreach (var rule in storeGroup)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        await ExecuteSingleIntervalAsync(rule, store, dayIndex, slotIndex, deviceName, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "❌ [IntervalExec] Błąd krytyczny interwału '{Name}' (ID={Id})",
                            rule.Name, rule.Id);
                    }
                }

                // Lock zwalniany automatycznie przez using/Dispose
            }

            _logger.LogInformation("⏱️ [IntervalExec] Zakończono przetwarzanie slotu {Slot}.", slotIndex);
        }

        // ═══════════════════════════════════════════════════════
        // WYKONANIE JEDNEGO INTERWAŁU
        // ═══════════════════════════════════════════════════════

        private async Task ExecuteSingleIntervalAsync(
            IntervalPriceRule rule,
            StoreClass store,
            int dayIndex,
            int slotIndex,
            string deviceName,
            CancellationToken ct)
        {
            var parent = rule.AutomationRule;
            bool isMarketplace = parent.SourceType == AutomationSourceType.Marketplace;

            _logger.LogInformation(
                "🔄 [Interval:{Name}] Start. Krok: {Step}{Unit}, Typ: {Type}",
                rule.Name,
                rule.PriceStep.ToString("F2"),
                rule.IsPriceStepPercent ? "%" : " PLN",
                isMarketplace ? "Allegro" : "Sklep");

            var batch = new IntervalPriceExecutionBatch
            {
                IntervalPriceRuleId = rule.Id,
                StoreId = store.StoreId,
                ExecutionDate = DateTime.Now,
                SlotIndex = slotIndex,
                DayIndex = dayIndex,
                PriceStepApplied = rule.PriceStep,
                IsPriceStepPercent = rule.IsPriceStepPercent,
                DeviceName = deviceName
            };

            _context.Set<IntervalPriceExecutionBatch>().Add(batch);
            await _context.SaveChangesAsync(ct);

            var sw = Stopwatch.StartNew();

            try
            {
                if (isMarketplace)
                    await ExecuteMarketplaceInterval(rule, parent, store, batch, ct);
                else
                    await ExecuteComparisonInterval(rule, parent, store, batch, ct);
            }
            catch (Exception ex)
            {
                batch.Comment = (batch.Comment ?? "") + $" | KRYTYCZNY BŁĄD: {ex.Message}";
                _logger.LogError(ex, "❌ [Interval:{Name}] Krytyczny błąd", rule.Name);
            }

            sw.Stop();
            batch.EndDate = DateTime.Now;
            batch.Comment = (batch.Comment ?? "") + $" | Czas: {sw.Elapsed.TotalSeconds:F1}s";

            await _context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "✅ [Interval:{Name}] Zakończono. Sukces={S}, Blokady={B}, Błędy={F}, Limity={L}",
                rule.Name, batch.SuccessCount, batch.BlockedCount,
                batch.FailedCount, batch.LimitReachedCount);
        }

        // ═══════════════════════════════════════════════════════
        // MARKETPLACE (Allegro)
        // ═══════════════════════════════════════════════════════

        private async Task ExecuteMarketplaceInterval(
            IntervalPriceRule rule,
            AutomationRule parent,
            StoreClass store,
            IntervalPriceExecutionBatch batch,
            CancellationToken ct)
        {
            var assignments = await _context.IntervalPriceProductAssignments
                .Where(a => a.IntervalPriceRuleId == rule.Id && a.AllegroProductId.HasValue)
                .Include(a => a.AllegroProduct)
                .ToListAsync(ct);

            batch.TotalProductsInInterval = assignments.Count;

            if (!assignments.Any())
            {
                batch.Comment = "Brak produktów w interwale.";
                return;
            }

            // Token
            string accessToken = await _authTokenService.GetValidAccessTokenAsync(store.StoreId);
            if (string.IsNullOrEmpty(accessToken))
            {
                batch.Comment = "BŁĄD: Brak tokena API Allegro.";
                batch.FailedCount = assignments.Count;
                return;
            }

            // Test tokena na pierwszej ofercie
            try
            {
                var testProduct = assignments.First().AllegroProduct;
                if (testProduct != null)
                    await GetAllegroOfferData(accessToken, testProduct.IdOnAllegro);
            }
            catch (AllegroAuthException)
            {
                _logger.LogWarning("[Interval:{Name}] Token 401. Próba odświeżenia...", rule.Name);
                accessToken = await _authTokenService.ForceRefreshTokenAsync(store.StoreId);
                if (string.IsNullOrEmpty(accessToken))
                {
                    batch.Comment = "BŁĄD: Token wygasł i nie udało się odświeżyć.";
                    batch.FailedCount = assignments.Count;
                    return;
                }
            }
            catch { /* Kontynuuj */ }

            int apiRequests = 0;

            foreach (var assignment in assignments)
            {
                if (ct.IsCancellationRequested) break;

                var product = assignment.AllegroProduct;
                if (product == null) continue;

                var item = new IntervalPriceExecutionItem
                {
                    BatchId = batch.Id,
                    AllegroProductId = product.AllegroProductId,
                    AllegroOfferId = product.IdOnAllegro,
                    PurchasePrice = product.AllegroMarginPrice,
                };

                // ── BLOKADY ──

                if (!product.AllegroMarginPrice.HasValue || product.AllegroMarginPrice <= 0)
                {
                    SetBlocked(item, batch, IntervalExecutionItemStatus.BlockedNoPurchasePrice, "Brak ceny zakupu");
                    continue;
                }

                // Limity Min/Max z rodzica
                decimal extraCost = 0;
                CalculateLimits(parent, product.AllegroMarginPrice.Value, null, out decimal? minLimit, out decimal? maxLimit, extraCost);
                item.MinPriceLimit = minLimit;
                item.MaxPriceLimit = maxLimit;

                if (!minLimit.HasValue)
                {
                    SetBlocked(item, batch, IntervalExecutionItemStatus.BlockedNoMinLimit, "Brak limitu Min");
                    continue;
                }

                if (minLimit.HasValue && maxLimit.HasValue && minLimit.Value > maxLimit.Value)
                {
                    SetBlocked(item, batch, IntervalExecutionItemStatus.BlockedMinMaxConflict, "Konflikt Min/Max");
                    continue;
                }

                // ── POBRANIE AKTUALNEJ CENY Z API ──
                try
                {
                    var offerData = await GetAllegroOfferData(accessToken, product.IdOnAllegro);
                    apiRequests++;

                    if (offerData == null)
                    {
                        SetBlocked(item, batch, IntervalExecutionItemStatus.BlockedNoPriceData, "Brak danych z API");
                        continue;
                    }

                    var priceStr = offerData["sellingMode"]?["price"]?["amount"]?.ToString();
                    if (!decimal.TryParse(priceStr, CultureInfo.InvariantCulture, out var currentPrice) || currentPrice <= 0)
                    {
                        SetBlocked(item, batch, IntervalExecutionItemStatus.BlockedNoPriceData, "Nieprawidłowa cena w API");
                        continue;
                    }

                    item.PriceBefore = currentPrice;

                    // Prowizja
                    decimal? commission = await GetAllegroCommission(accessToken, offerData);
                    apiRequests++;
                    item.CommissionBefore = commission;

                    // Przelicz limity z prowizją jeśli rodzic tego wymaga
                    if (parent.MarketplaceIncludeCommission && commission.HasValue)
                    {
                        extraCost = commission.Value;
                        CalculateLimits(parent, product.AllegroMarginPrice.Value, commission.Value, out minLimit, out maxLimit, extraCost);
                        item.MinPriceLimit = minLimit;
                        item.MaxPriceLimit = maxLimit;
                    }

                    // Kampanie / Dopłaty — na razie false, docelowo rozbudujemy
                    item.IsInCampaign = false;
                    item.IsSubsidyActive = false;
                    item.CustomerVisiblePrice = currentPrice;

                    // ── KALKULACJA NOWEJ CENY ──
                    decimal step = rule.IsPriceStepPercent
                        ? currentPrice * (rule.PriceStep / 100m)
                        : rule.PriceStep;

                    decimal targetPrice = Math.Round(currentPrice + step, 2);
                    bool limitedMin = false, limitedMax = false;

                    // Sprawdź limit Min
                    if (minLimit.HasValue && targetPrice < minLimit.Value)
                    {
                        if (Math.Abs(currentPrice - minLimit.Value) < 0.01m)
                        {
                            item.PriceAfterTarget = currentPrice;
                            item.PriceChange = 0;
                            item.Status = IntervalExecutionItemStatus.BlockedLimitReached;
                            item.StatusReason = "Cena na Min";
                            batch.LimitReachedCount++;
                            _context.Set<IntervalPriceExecutionItem>().Add(item);
                            continue;
                        }
                        targetPrice = minLimit.Value;
                        limitedMin = true;
                    }

                    // Sprawdź limit Max
                    if (maxLimit.HasValue && targetPrice > maxLimit.Value)
                    {
                        if (Math.Abs(currentPrice - maxLimit.Value) < 0.01m)
                        {
                            item.PriceAfterTarget = currentPrice;
                            item.PriceChange = 0;
                            item.Status = IntervalExecutionItemStatus.BlockedLimitReached;
                            item.StatusReason = "Cena na Max";
                            batch.LimitReachedCount++;
                            _context.Set<IntervalPriceExecutionItem>().Add(item);
                            continue;
                        }
                        targetPrice = maxLimit.Value;
                        limitedMax = true;
                    }

                    decimal priceChange = Math.Round(targetPrice - currentPrice, 2);
                    if (priceChange == 0)
                    {
                        item.PriceAfterTarget = currentPrice;
                        item.PriceChange = 0;
                        item.Status = IntervalExecutionItemStatus.NoChangeNeeded;
                        item.StatusReason = "Krok=0 po zaokrągleniu";
                        _context.Set<IntervalPriceExecutionItem>().Add(item);
                        continue;
                    }

                    item.PriceAfterTarget = targetPrice;
                    item.PriceChange = priceChange;
                    item.WasLimitedByMin = limitedMin;
                    item.WasLimitedByMax = limitedMax;

                    // ── WGRANIE NOWEJ CENY ──
                    var (success, errorMsg) = await SetAllegroPrice(accessToken, product.IdOnAllegro, targetPrice);
                    apiRequests++;

                    if (!success)
                    {
                        if (errorMsg.Contains("401") || errorMsg.Contains("Unauthorized"))
                        {
                            item.Status = IntervalExecutionItemStatus.FailedAuth;
                            item.StatusReason = "Token wygasł";
                            batch.FailedCount++;
                            batch.Comment = (batch.Comment ?? "") + " | TOKEN WYGASŁ — przerywam.";
                            _context.Set<IntervalPriceExecutionItem>().Add(item);
                            break; // Przerywamy cały interwał
                        }

                        item.Status = IntervalExecutionItemStatus.FailedApi;
                        item.StatusReason = errorMsg.Length > 200 ? errorMsg[..200] : errorMsg;
                        batch.FailedCount++;
                        _context.Set<IntervalPriceExecutionItem>().Add(item);
                        continue;
                    }

                    // ── WERYFIKACJA ──
                    await Task.Delay(1500, ct);

                    var verifyData = await GetAllegroOfferData(accessToken, product.IdOnAllegro);
                    apiRequests++;
                    if (verifyData != null)
                    {
                        var verifiedPriceStr = verifyData["sellingMode"]?["price"]?["amount"]?.ToString();
                        if (decimal.TryParse(verifiedPriceStr, CultureInfo.InvariantCulture, out var vp))
                            item.PriceAfterVerified = vp;

                        item.CommissionAfterVerified = await GetAllegroCommission(accessToken, verifyData);
                        apiRequests++;
                    }

                    item.Success = true;
                    item.Status = limitedMin
                        ? IntervalExecutionItemStatus.SuccessLimitedMin
                        : limitedMax
                            ? IntervalExecutionItemStatus.SuccessLimitedMax
                            : IntervalExecutionItemStatus.Success;
                    batch.SuccessCount++;
                }
                catch (AllegroAuthException)
                {
                    item.Status = IntervalExecutionItemStatus.FailedAuth;
                    item.StatusReason = "Token 401";
                    batch.FailedCount++;
                    batch.Comment = (batch.Comment ?? "") + " | TOKEN 401 — przerywam.";
                    _context.Set<IntervalPriceExecutionItem>().Add(item);
                    break;
                }
                catch (Exception ex)
                {
                    item.Status = IntervalExecutionItemStatus.FailedApi;
                    item.StatusReason = $"Wyjątek: {(ex.Message.Length > 180 ? ex.Message[..180] : ex.Message)}";
                    batch.FailedCount++;
                    _logger.LogError(ex, "❌ [Interval] Błąd produktu {ProductId}", product.AllegroProductId);
                }

                _context.Set<IntervalPriceExecutionItem>().Add(item);
            }

            batch.Comment = (batch.Comment ?? "") + $" | API requests: {apiRequests}";
            await _context.SaveChangesAsync(ct);
        }

        // ═══════════════════════════════════════════════════════
        // COMPARISON (Sklep / Ceneo / Google)
        // ═══════════════════════════════════════════════════════

        private async Task ExecuteComparisonInterval(
            IntervalPriceRule rule,
            AutomationRule parent,
            StoreClass store,
            IntervalPriceExecutionBatch batch,
            CancellationToken ct)
        {
            var assignments = await _context.IntervalPriceProductAssignments
                .Where(a => a.IntervalPriceRuleId == rule.Id && a.ProductId.HasValue)
                .Include(a => a.Product)
                .ToListAsync(ct);

            batch.TotalProductsInInterval = assignments.Count;

            if (!assignments.Any())
            {
                batch.Comment = "Brak produktów w interwale.";
                return;
            }

            if (!store.IsStorePriceBridgeActive || string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
            {
                batch.Comment = "BŁĄD: Sklep nie ma aktywnej integracji API.";
                batch.FailedCount = assignments.Count;
                return;
            }

            var client = _httpClientFactory.CreateClient();
            var authToken = Encoding.ASCII.GetBytes($"{store.StoreApiKey}:");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            foreach (var assignment in assignments)
            {
                if (ct.IsCancellationRequested) break;

                var product = assignment.Product;
                if (product == null) continue;

                var item = new IntervalPriceExecutionItem
                {
                    BatchId = batch.Id,
                    ProductId = product.ProductId,
                    PurchasePrice = product.MarginPrice,
                };

                // Blokady
                if (!product.MarginPrice.HasValue || product.MarginPrice <= 0)
                {
                    SetBlocked(item, batch, IntervalExecutionItemStatus.BlockedNoPurchasePrice, "Brak ceny zakupu");
                    continue;
                }

                CalculateLimits(parent, product.MarginPrice.Value, null, out decimal? minLimit, out decimal? maxLimit, 0);
                item.MinPriceLimit = minLimit;
                item.MaxPriceLimit = maxLimit;

                if (!minLimit.HasValue)
                {
                    SetBlocked(item, batch, IntervalExecutionItemStatus.BlockedNoMinLimit, "Brak limitu Min");
                    continue;
                }

                if (minLimit.HasValue && maxLimit.HasValue && minLimit.Value > maxLimit.Value)
                {
                    SetBlocked(item, batch, IntervalExecutionItemStatus.BlockedMinMaxConflict, "Konflikt Min/Max");
                    continue;
                }

                if (product.ExternalId == null)
                {
                    SetBlocked(item, batch, IntervalExecutionItemStatus.BlockedNoPriceData, "Brak ExternalId");
                    continue;
                }

                try
                {
                    decimal? currentPrice = await GetStoreProductPrice(client, store, product.ExternalId.ToString());

                    if (!currentPrice.HasValue || currentPrice <= 0)
                    {
                        SetBlocked(item, batch, IntervalExecutionItemStatus.BlockedNoPriceData, "Brak ceny ze sklepu");
                        continue;
                    }

                    item.PriceBefore = currentPrice.Value;

                    // Kalkulacja
                    decimal step = rule.IsPriceStepPercent
                        ? currentPrice.Value * (rule.PriceStep / 100m)
                        : rule.PriceStep;

                    decimal targetPrice = Math.Round(currentPrice.Value + step, 2);
                    bool limitedMin = false, limitedMax = false;

                    if (minLimit.HasValue && targetPrice < minLimit.Value)
                    {
                        if (Math.Abs(currentPrice.Value - minLimit.Value) < 0.01m)
                        {
                            item.PriceAfterTarget = currentPrice.Value;
                            item.PriceChange = 0;
                            item.Status = IntervalExecutionItemStatus.BlockedLimitReached;
                            item.StatusReason = "Cena na Min";
                            batch.LimitReachedCount++;
                            _context.Set<IntervalPriceExecutionItem>().Add(item);
                            continue;
                        }
                        targetPrice = minLimit.Value;
                        limitedMin = true;
                    }

                    if (maxLimit.HasValue && targetPrice > maxLimit.Value)
                    {
                        if (Math.Abs(currentPrice.Value - maxLimit.Value) < 0.01m)
                        {
                            item.PriceAfterTarget = currentPrice.Value;
                            item.PriceChange = 0;
                            item.Status = IntervalExecutionItemStatus.BlockedLimitReached;
                            item.StatusReason = "Cena na Max";
                            batch.LimitReachedCount++;
                            _context.Set<IntervalPriceExecutionItem>().Add(item);
                            continue;
                        }
                        targetPrice = maxLimit.Value;
                        limitedMax = true;
                    }

                    decimal priceChange = Math.Round(targetPrice - currentPrice.Value, 2);
                    if (priceChange == 0)
                    {
                        item.PriceAfterTarget = currentPrice.Value;
                        item.PriceChange = 0;
                        item.Status = IntervalExecutionItemStatus.NoChangeNeeded;
                        _context.Set<IntervalPriceExecutionItem>().Add(item);
                        continue;
                    }

                    item.PriceAfterTarget = targetPrice;
                    item.PriceChange = priceChange;
                    item.WasLimitedByMin = limitedMin;
                    item.WasLimitedByMax = limitedMax;

                    var (success, errorMsg) = await SetStoreProductPrice(client, store, product.ExternalId.ToString(), targetPrice);

                    if (!success)
                    {
                        item.Status = IntervalExecutionItemStatus.FailedApi;
                        item.StatusReason = errorMsg.Length > 200 ? errorMsg[..200] : errorMsg;
                        batch.FailedCount++;
                        _context.Set<IntervalPriceExecutionItem>().Add(item);
                        continue;
                    }

                    // Weryfikacja
                    await Task.Delay(1500, ct);
                    item.PriceAfterVerified = await GetStoreProductPrice(client, store, product.ExternalId.ToString());

                    item.Success = true;
                    item.Status = limitedMin
                        ? IntervalExecutionItemStatus.SuccessLimitedMin
                        : limitedMax
                            ? IntervalExecutionItemStatus.SuccessLimitedMax
                            : IntervalExecutionItemStatus.Success;
                    batch.SuccessCount++;
                }
                catch (Exception ex)
                {
                    item.Status = IntervalExecutionItemStatus.FailedApi;
                    item.StatusReason = $"Wyjątek: {(ex.Message.Length > 180 ? ex.Message[..180] : ex.Message)}";
                    batch.FailedCount++;
                    _logger.LogError(ex, "❌ [Interval] Błąd produktu {ProductId}", product.ProductId);
                }

                _context.Set<IntervalPriceExecutionItem>().Add(item);
            }

            await _context.SaveChangesAsync(ct);
        }

        // ═══════════════════════════════════════════════════════
        // HELPER — Blokada produktu (skrót)
        // ═══════════════════════════════════════════════════════

        private void SetBlocked(IntervalPriceExecutionItem item, IntervalPriceExecutionBatch batch,
            IntervalExecutionItemStatus status, string reason)
        {
            item.Status = status;
            item.StatusReason = reason;
            batch.BlockedCount++;
            _context.Set<IntervalPriceExecutionItem>().Add(item);
        }

        // ═══════════════════════════════════════════════════════
        // HELPER — Limity Min/Max
        // ═══════════════════════════════════════════════════════

        private void CalculateLimits(
            AutomationRule parent, decimal purchasePrice, decimal? commission,
            out decimal? minLimit, out decimal? maxLimit, decimal extraCost)
        {
            minLimit = null;
            maxLimit = null;

            if (parent.EnforceMinimalMarkup && purchasePrice > 0)
            {
                decimal min = parent.IsMinimalMarkupPercent
                    ? purchasePrice + (purchasePrice * (parent.MinimalMarkupValue / 100)) + extraCost
                    : purchasePrice + parent.MinimalMarkupValue + extraCost;
                minLimit = Math.Round(min, 2);
            }

            if (parent.EnforceMaxMarkup && purchasePrice > 0)
            {
                decimal max = parent.IsMaxMarkupPercent
                    ? purchasePrice + (purchasePrice * (parent.MaxMarkupValue / 100)) + extraCost
                    : purchasePrice + parent.MaxMarkupValue + extraCost;
                maxLimit = Math.Round(max, 2);
            }
        }

        // ═══════════════════════════════════════════════════════
        // ALLEGRO API
        // ═══════════════════════════════════════════════════════

        private async Task<JsonNode> GetAllegroOfferData(string token, string offerId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.allegro.pl/sale/product-offers/{offerId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            var response = await _allegroHttpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new AllegroAuthException($"401 dla oferty {offerId}");

            if (!response.IsSuccessStatusCode) return null;
            return JsonNode.Parse(await response.Content.ReadAsStringAsync());
        }

        private async Task<decimal?> GetAllegroCommission(string token, JsonNode offerData)
        {
            var payload = new { offer = offerData };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.allegro.public.v1+json");
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.allegro.pl/pricing/offer-fee-preview") { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _allegroHttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var feeNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var feeStr = feeNode?["commissions"]?[0]?["fee"]?["amount"]?.ToString();
            return decimal.TryParse(feeStr, CultureInfo.InvariantCulture, out var fee) ? fee : null;
        }

        private async Task<(bool Success, string Error)> SetAllegroPrice(string token, string offerId, decimal newPrice)
        {
            var payload = new
            {
                sellingMode = new
                {
                    price = new
                    {
                        amount = newPrice.ToString(CultureInfo.InvariantCulture),
                        currency = "PLN"
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.allegro.public.v1+json");
            var request = new HttpRequestMessage(HttpMethod.Patch, $"https://api.allegro.pl/sale/product-offers/{offerId}") { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            var response = await _allegroHttpClient.SendAsync(request);
            if (response.IsSuccessStatusCode) return (true, "");

            var errorBody = await response.Content.ReadAsStringAsync();
            return (false, $"HTTP {response.StatusCode}: {errorBody}");
        }

        // ═══════════════════════════════════════════════════════
        // STORE API (PrestaShop)
        // ═══════════════════════════════════════════════════════

        private async Task<decimal?> GetStoreProductPrice(HttpClient client, StoreClass store, string externalId)
        {
            string url = $"{store.StoreApiUrl.TrimEnd('/')}/products/{externalId}?display=[price]&output_format=JSON";
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var root = JsonNode.Parse(json);
            var priceStr = root?["products"]?[0]?["price"]?.ToString()
                ?? root?["product"]?["price"]?.ToString();

            if (decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var netPrice))
                return Math.Round(netPrice * 1.23m, 2);

            return null;
        }

        private async Task<(bool Success, string Error)> SetStoreProductPrice(
            HttpClient client, StoreClass store, string externalId, decimal newPriceBrutto)
        {
            decimal priceNet = Math.Round(newPriceBrutto / 1.23m, 6);
            string priceNetStr = priceNet.ToString(CultureInfo.InvariantCulture);
            string apiUrl = $"{store.StoreApiUrl.TrimEnd('/')}/products/{externalId}";

            string xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<prestashop xmlns:xlink=""http://www.w3.org/1999/xlink"">
    <product><id>{externalId}</id><price>{priceNetStr}</price></product>
</prestashop>";

            var request = new HttpRequestMessage(new HttpMethod("PATCH"), apiUrl)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml")
            };

            var response = await client.SendAsync(request);
            if (response.IsSuccessStatusCode) return (true, "");

            var error = await response.Content.ReadAsStringAsync();
            return (false, $"PATCH {response.StatusCode}: {error}");
        }

        // ═══════════════════════════════════════════════════════
        // WYJĄTEK AUTH
        // ═══════════════════════════════════════════════════════

        private class AllegroAuthException : Exception
        {
            public AllegroAuthException(string message) : base(message) { }
        }
    }
}