using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.SignalIR;
using System.Globalization;
using System.Security.Claims;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    public class AllegroDashboardController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<DashboardProgressHub> _hub;

        public AllegroDashboardController(PriceSafariContext ctx, IHubContext<DashboardProgressHub> hub)
        {
            _context = ctx;
            _hub = hub;
        }

        private async Task<bool> UserHasAccessToStore(int storeId)
        {
            if (User.IsInRole("Admin") || User.IsInRole("Manager")) return true;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return await _context.UserStores.AsNoTracking().AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
        }

        public async Task<IActionResult> Dashboard(int storeId)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store is null) return NotFound();

            ViewBag.StoreId = storeId;
            ViewBag.StoreName = store.StoreName;
            ViewBag.AllegroSellerName = store.StoreNameAllegro;

            return View("~/Views/Panel/AllegroDashboard/Dashboard.cshtml");
        }

        // ══════════════════════════════════════════════════════════════
        // GetDashboardData — TYLKO podsumowania (liczniki)
        // ══════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetDashboardData(int storeId, int days = 7, string connectionId = null)
        {
            try
            {
                async Task ReportProgress(string msg, int pct)
                {
                    if (string.IsNullOrEmpty(connectionId)) return;
                    pct = Math.Max(0, Math.Min(99, pct));
                    await _hub.Clients.Client(connectionId).SendAsync("ReceiveProgress", msg, pct);
                }

                await ReportProgress("Pobieram konfigurację sklepu...", 1);

                var storeInfo = await _context.Stores
                    .AsNoTracking()
                    .Where(s => s.StoreId == storeId)
                    .Select(s => new { s.StoreNameAllegro })
                    .FirstOrDefaultAsync();

                if (string.IsNullOrEmpty(storeInfo?.StoreNameAllegro))
                    return BadRequest("Brak skonfigurowanej nazwy sprzedawcy Allegro.");

                string mySellerName = storeInfo.StoreNameAllegro;
                var sinceDate = DateTime.UtcNow.AddDays(-days).Date;

                var relevantScraps = await _context.AllegroScrapeHistories
                    .AsNoTracking()
                    .Where(sh => sh.StoreId == storeId && sh.Date >= sinceDate)
                    .OrderBy(sh => sh.Date)
                    .Select(sh => new ScrapInfo { Id = sh.Id, Date = sh.Date })
                    .ToListAsync();

                if (!relevantScraps.Any()) return Json(new List<object>());

                var firstRelevantDate = relevantScraps.First().Date;
                var referenceScrap = await _context.AllegroScrapeHistories
                    .AsNoTracking()
                    .Where(sh => sh.StoreId == storeId && sh.Date < firstRelevantDate)
                    .OrderByDescending(sh => sh.Date)
                    .Select(sh => new ScrapInfo { Id = sh.Id, Date = sh.Date })
                    .FirstOrDefaultAsync();

                var allScrapsToProcess = new List<ScrapInfo>();
                if (referenceScrap != null) allScrapsToProcess.Add(referenceScrap);
                allScrapsToProcess.AddRange(relevantScraps);

                await ReportProgress("Identyfikuję główne oferty produktów...", 5);

                var productInfoMap = await _context.AllegroProducts
                    .AsNoTracking()
                    .Where(p => p.StoreId == storeId)
                    .Select(p => new { p.AllegroProductId, p.AllegroOfferUrl })
                    .ToListAsync();

                var mainOfferIds = new Dictionary<int, long>();
                foreach (var prod in productInfoMap)
                {
                    if (!string.IsNullOrEmpty(prod.AllegroOfferUrl))
                    {
                        var parts = prod.AllegroOfferUrl.Split('-');
                        if (parts.Length > 0 && long.TryParse(parts.Last(), out long offerId))
                            mainOfferIds[prod.AllegroProductId] = offerId;
                    }
                }

                await ReportProgress("Ładuję wszystkie dane cenowe...", 10);

                var allScrapIds = allScrapsToProcess.Select(s => s.Id).ToList();

                var allPriceRecords = await _context.AllegroPriceHistories
                    .AsNoTracking()
                    .Where(ph => allScrapIds.Contains(ph.AllegroScrapeHistoryId)
                              && ph.SellerName == mySellerName)
                    .Select(ph => new { ph.AllegroScrapeHistoryId, ph.AllegroProductId, ph.IdAllegro, ph.Price })
                    .ToListAsync();

                var pricesByScrapId = allPriceRecords
                    .GroupBy(ph => ph.AllegroScrapeHistoryId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                await ReportProgress("Analizuję zmiany cen...", 30);

                var previousPrices = new Dictionary<int, decimal>();

                if (referenceScrap != null && pricesByScrapId.TryGetValue(referenceScrap.Id, out var refRecords))
                {
                    foreach (var p in refRecords)
                    {
                        if (mainOfferIds.TryGetValue(p.AllegroProductId, out long mainId) && p.IdAllegro == mainId)
                            previousPrices[p.AllegroProductId] = p.Price;
                    }
                }

                var dailySummaries = new Dictionary<DateTime, DailySummary>();
                int startIndex = referenceScrap != null ? 1 : 0;
                int totalIterations = allScrapsToProcess.Count - startIndex;
                int iterationsDone = 0;

                for (int i = startIndex; i < allScrapsToProcess.Count; i++)
                {
                    var currentScrap = allScrapsToProcess[i];
                    int currentPct = 35 + (int)((double)iterationsDone / totalIterations * 60);
                    await ReportProgress($"Przetwarzam scrap {iterationsDone + 1}/{totalIterations}: {currentScrap.Date:dd.MM HH:mm}...", currentPct);

                    var currentPrices = new Dictionary<int, decimal>();
                    int raisedCount = 0, loweredCount = 0;

                    if (pricesByScrapId.TryGetValue(currentScrap.Id, out var currentRecords))
                    {
                        foreach (var item in currentRecords)
                        {
                            if (mainOfferIds.TryGetValue(item.AllegroProductId, out long mainId) && item.IdAllegro == mainId)
                            {
                                currentPrices[item.AllegroProductId] = item.Price;
                                if (previousPrices.TryGetValue(item.AllegroProductId, out var prevPrice) && item.Price != prevPrice)
                                {
                                    if (item.Price > prevPrice) raisedCount++;
                                    else loweredCount++;
                                }
                            }
                        }
                    }

                    if (raisedCount > 0 || loweredCount > 0)
                    {
                        var dayDate = currentScrap.Date.Date;
                        if (!dailySummaries.ContainsKey(dayDate))
                            dailySummaries[dayDate] = new DailySummary { Date = dayDate };

                        dailySummaries[dayDate].Scraps.Add(new ScrapSummaryLite
                        {
                            ScrapId = currentScrap.Id,
                            Date = currentScrap.Date,
                            RaisedCount = raisedCount,
                            LoweredCount = loweredCount
                        });
                    }

                    previousPrices = currentPrices;
                    iterationsDone++;
                }

                await ReportProgress("Finalizuję dane...", 98);

                var result = dailySummaries.Values
                    .OrderBy(d => d.Date)
                    .Select(d => new
                    {
                        date = d.Date.ToString("yyyy-MM-dd"),
                        dayShort = d.Date.ToString("ddd", new CultureInfo("pl-PL")),
                        totalRaised = d.Scraps.Sum(s => s.RaisedCount),
                        totalLowered = d.Scraps.Sum(s => s.LoweredCount),
                        scraps = d.Scraps.OrderBy(s => s.Date).Select(s => new
                        {
                            scrapId = s.ScrapId,
                            fullDate = s.Date,
                            time = s.Date.ToString("HH:mm"),
                            raised = s.RaisedCount,
                            lowered = s.LoweredCount
                        }).ToList()
                    })
                    .ToList();

                if (!string.IsNullOrEmpty(connectionId))
                    await _hub.Clients.Client(connectionId).SendAsync("ReceiveProgress", "Gotowe!", 100);

                return Json(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AllegroDashboard ERROR] {ex}");
                return StatusCode(500, $"Błąd serwera: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════
        // GetScrapChangeDetails — lazy load szczegółów jednego scrapa
        // ══════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetScrapChangeDetails(int storeId, int scrapId)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var storeInfo = await _context.Stores
                .AsNoTracking()
                .Where(s => s.StoreId == storeId)
                .Select(s => new { s.StoreNameAllegro })
                .FirstOrDefaultAsync();
            if (storeInfo == null) return NotFound();

            string mySellerName = storeInfo.StoreNameAllegro;

            var currentScrap = await _context.AllegroScrapeHistories
                .AsNoTracking()
                .Where(sh => sh.Id == scrapId && sh.StoreId == storeId)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();
            if (currentScrap == null) return NotFound();

            var previousScrapId = await _context.AllegroScrapeHistories
                .AsNoTracking()
                .Where(sh => sh.StoreId == storeId && sh.Date < currentScrap.Date)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            var productInfoMap = await _context.AllegroProducts
                .AsNoTracking()
                .Where(p => p.StoreId == storeId)
                .Select(p => new { p.AllegroProductId, p.AllegroOfferUrl, p.AllegroProductName })
                .ToListAsync();

            var mainOfferIds = new Dictionary<int, long>();
            var productNames = new Dictionary<int, string>();
            foreach (var prod in productInfoMap)
            {
                productNames[prod.AllegroProductId] = prod.AllegroProductName ?? "Nazwa niedostępna";
                if (!string.IsNullOrEmpty(prod.AllegroOfferUrl))
                {
                    var parts = prod.AllegroOfferUrl.Split('-');
                    if (parts.Length > 0 && long.TryParse(parts.Last(), out long offerId))
                        mainOfferIds[prod.AllegroProductId] = offerId;
                }
            }

            var scrapIds = new List<int> { scrapId };
            if (previousScrapId > 0) scrapIds.Add(previousScrapId);

            var priceRecords = await _context.AllegroPriceHistories
                .AsNoTracking()
                .Where(ph => scrapIds.Contains(ph.AllegroScrapeHistoryId) && ph.SellerName == mySellerName)
                .Select(ph => new { ph.AllegroScrapeHistoryId, ph.AllegroProductId, ph.IdAllegro, ph.Price })
                .ToListAsync();

            var previousPrices = new Dictionary<int, decimal>();
            var currentPrices = new Dictionary<int, decimal>();

            foreach (var rec in priceRecords)
            {
                if (!mainOfferIds.TryGetValue(rec.AllegroProductId, out long mainId) || rec.IdAllegro != mainId)
                    continue;
                if (rec.AllegroScrapeHistoryId == previousScrapId)
                    previousPrices[rec.AllegroProductId] = rec.Price;
                else if (rec.AllegroScrapeHistoryId == scrapId)
                    currentPrices[rec.AllegroProductId] = rec.Price;
            }

            var raisedDetails = new List<object>();
            var loweredDetails = new List<object>();

            foreach (var kvp in currentPrices)
            {
                if (previousPrices.TryGetValue(kvp.Key, out var prevPrice) && kvp.Value != prevPrice)
                {
                    var detail = new
                    {
                        productId = kvp.Key,
                        productName = productNames.GetValueOrDefault(kvp.Key, "Nazwa niedostępna"),
                        oldPrice = prevPrice,
                        newPrice = kvp.Value,
                        priceDifference = kvp.Value - prevPrice
                    };
                    if (detail.priceDifference > 0) raisedDetails.Add(detail);
                    else loweredDetails.Add(detail);
                }
            }

            return Json(new
            {
                raisedDetails = raisedDetails.OrderByDescending(d => ((dynamic)d).priceDifference).ToList(),
                loweredDetails = loweredDetails.OrderBy(d => ((dynamic)d).priceDifference).ToList()
            });
        }

        // ══════════════════════════════════════════════════════════════
        // GetPriceBridgeHistory — TYLKO podsumowania (bez items)
        // ══════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetPriceBridgeHistory(int storeId, int days = 7)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var sinceDate = DateTime.UtcNow.AddDays(-days).Date;

            var batches = await _context.AllegroPriceBridgeBatches
                .AsNoTracking()
                .Where(b => b.StoreId == storeId && b.ExecutionDate >= sinceDate)
                .Include(b => b.User)
                .OrderByDescending(b => b.ExecutionDate)
                .Select(b => new
                {
                    b.Id,
                    b.ExecutionDate,
                    UserName = b.User != null ? b.User.UserName : "Nieznany",
                    b.SuccessfulCount,
                    b.FailedCount,
                    b.IsAutomation,
                    AutomationRuleName = b.AutomationRule != null ? b.AutomationRule.Name : null,
                    AutomationRuleColor = b.AutomationRule != null ? b.AutomationRule.ColorHex : null
                })
                .ToListAsync();

            var groupedByDay = batches
                .GroupBy(b => b.ExecutionDate.Date)
                .OrderByDescending(g => g.Key)
                .Select(g => new
                {
                    date = g.Key.ToString("yyyy-MM-dd"),
                    dayShort = g.Key.ToString("ddd", new CultureInfo("pl-PL")),
                    totalBatches = g.Count(),
                    totalItemsChanged = g.Sum(b => b.SuccessfulCount),
                    totalErrors = g.Sum(b => b.FailedCount),
                    batches = g.Select(b => new
                    {
                        batchId = b.Id,
                        executionTime = b.ExecutionDate.ToString("HH:mm"),
                        userName = b.IsAutomation ? "Automat Cenowy" : b.UserName,
                        successfulCount = b.SuccessfulCount,
                        failedCount = b.FailedCount,
                        automationRuleName = b.AutomationRuleName,
                        automationRuleColor = b.AutomationRuleColor
                    }).OrderByDescending(b => b.executionTime).ToList()
                })
                .ToList();

            return Json(groupedByDay);
        }

        // ══════════════════════════════════════════════════════════════
        // GetBatchDetails — lazy load szczegółów jednego batcha
        // ══════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetBatchDetails(int storeId, int batchId)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            // ── Słownik nazw produktów ──
            var productNameMap = await _context.AllegroProducts
                .AsNoTracking()
                .Where(p => p.StoreId == storeId)
                .Select(p => new { p.AllegroProductId, p.AllegroProductName })
                .ToDictionaryAsync(p => p.AllegroProductId, p => p.AllegroProductName);

            var items = await _context.AllegroPriceBridgeItems
                .AsNoTracking()
                .Where(i => i.AllegroPriceBridgeBatchId == batchId)
                .Select(i => new
                {
                    i.AllegroProductId,
                    i.AllegroOfferId,
                    i.PriceBefore,
                    PriceAfter = i.PriceAfter_Verified ?? i.PriceAfter_Simulated,
                    i.Success,
                    i.ErrorMessage,
                    i.CommissionBefore,
                    i.CommissionAfter_Verified,
                    i.MarginPrice,
                    i.IncludeCommissionInMargin
                })
                .ToListAsync();

            var result = items.Select(i => new
            {
                productName = productNameMap.GetValueOrDefault(i.AllegroProductId, "Produkt usunięty"),
                productId = (int?)i.AllegroProductId,
                offerId = i.AllegroOfferId,
                priceBefore = i.PriceBefore,
                priceAfter = i.PriceAfter,
                priceDiff = i.PriceAfter - i.PriceBefore,
                success = i.Success,
                errorMessage = i.ErrorMessage,
                commissionBefore = i.CommissionBefore,
                commissionAfter = i.CommissionAfter_Verified,
                marginPrice = i.MarginPrice,
                includeCommissionInMargin = i.IncludeCommissionInMargin
            })
            .OrderBy(i => i.productName)
            .ToList();

            return Json(result);
        }

        // ══════════════════════════════════════════════════════════════
        // Klasy pomocnicze
        // ══════════════════════════════════════════════════════════════
        private class ScrapInfo
        {
            public int Id { get; set; }
            public DateTime Date { get; set; }
        }

        public sealed class ScrapSummaryLite
        {
            public int ScrapId { get; set; }
            public DateTime Date { get; set; }
            public int RaisedCount { get; set; }
            public int LoweredCount { get; set; }
        }

        public sealed class DailySummary
        {
            public DateTime Date { get; set; }
            public List<ScrapSummaryLite> Scraps { get; } = new();
        }
    }
}