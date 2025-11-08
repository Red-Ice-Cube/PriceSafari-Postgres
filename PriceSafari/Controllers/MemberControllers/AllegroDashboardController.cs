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

        public async Task<IActionResult> Dashboard(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId)
                            || User.IsInRole("Admin") || User.IsInRole("Manager");

            if (!hasAccess) return Forbid();

            var store = await _context.Stores.FindAsync(storeId);
            if (store is null) return NotFound();

            ViewBag.StoreId = storeId;
            ViewBag.StoreName = store.StoreName;
            ViewBag.AllegroSellerName = store.StoreNameAllegro;

            return View("~/Views/Panel/AllegroDashboard/Dashboard.cshtml");
        }

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
                    .Where(s => s.StoreId == storeId)
                    .Select(s => new { s.StoreNameAllegro })
                    .FirstOrDefaultAsync();

                if (string.IsNullOrEmpty(storeInfo?.StoreNameAllegro))
                {
                    return BadRequest("Brak skonfigurowanej nazwy sprzedawcy Allegro.");
                }
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

                var productUrlMap = await _context.AllegroProducts
                    .AsNoTracking()
                    .Where(p => p.StoreId == storeId)
                    .Select(p => new { p.AllegroProductId, p.AllegroOfferUrl })
                    .ToListAsync();

                var mainOfferIds = new Dictionary<int, long>();
                foreach (var prod in productUrlMap)
                {
                    if (!string.IsNullOrEmpty(prod.AllegroOfferUrl))
                    {
                        var parts = prod.AllegroOfferUrl.Split('-');
                        if (parts.Length > 0 && long.TryParse(parts.Last(), out long offerId))
                        {
                            mainOfferIds[prod.AllegroProductId] = offerId;
                        }
                    }
                }

                var previousPrices = new Dictionary<int, (decimal Price, string Name)>();

                if (referenceScrap != null)
                {
                    await ReportProgress($"Ładuję scrap referencyjny z {referenceScrap.Date:dd.MM HH:mm}...", 10);

                    var allRefPrices = await _context.AllegroPriceHistories
                        .AsNoTracking()
                        .Where(ph => ph.AllegroScrapeHistoryId == referenceScrap.Id && ph.SellerName == mySellerName)
                        .Select(ph => new { ph.AllegroProductId, ph.IdAllegro, ph.Price, ph.AllegroProduct.AllegroProductName })
                        .ToListAsync();

                    foreach (var p in allRefPrices)
                    {
                        if (mainOfferIds.TryGetValue(p.AllegroProductId, out long mainId) && p.IdAllegro == mainId)
                        {
                            previousPrices[p.AllegroProductId] = (p.Price, p.AllegroProductName ?? "Nazwa niedostępna");
                        }
                    }
                }

                var dailySummaries = new Dictionary<DateTime, DailySummary>();

                int startIndex = referenceScrap != null ? 1 : 0;
                int totalIterations = allScrapsToProcess.Count - startIndex;
                int iterationsDone = 0;

                int startPct = 15;
                int availablePctRange = 80;

                for (int i = startIndex; i < allScrapsToProcess.Count; i++)
                {
                    var currentScrap = allScrapsToProcess[i];

                    int currentPct = startPct + (int)((double)iterationsDone / totalIterations * availablePctRange);
                    await ReportProgress($"Analizuję scrap {iterationsDone + 1}/{totalIterations}: {currentScrap.Date:dd.MM HH:mm}...", currentPct);

                    var allCurrentPricesList = await _context.AllegroPriceHistories
                        .AsNoTracking()
                        .Where(ph => ph.AllegroScrapeHistoryId == currentScrap.Id && ph.SellerName == mySellerName)
                        .Select(ph => new { ph.AllegroProductId, ph.IdAllegro, ph.Price, ph.AllegroProduct.AllegroProductName })
                        .ToListAsync();

                    var currentPrices = new Dictionary<int, (decimal Price, string Name)>();
                    var scrapSummary = new ScrapSummary
                    {
                        ScrapId = currentScrap.Id,
                        Date = currentScrap.Date
                    };

                    foreach (var item in allCurrentPricesList)
                    {
                        if (mainOfferIds.TryGetValue(item.AllegroProductId, out long mainId) && item.IdAllegro == mainId)
                        {
                            string productName = item.AllegroProductName ?? "Nazwa niedostępna";
                            currentPrices[item.AllegroProductId] = (item.Price, productName);

                            if (previousPrices.TryGetValue(item.AllegroProductId, out var prev))
                            {
                                if (item.Price != prev.Price)
                                {
                                    var change = new PriceChangeDetail
                                    {
                                        ProductId = item.AllegroProductId,
                                        ProductName = productName,
                                        OldPrice = prev.Price,
                                        NewPrice = item.Price,
                                        PriceDifference = item.Price - prev.Price
                                    };

                                    if (change.PriceDifference > 0)
                                        scrapSummary.RaisedDetails.Add(change);
                                    else
                                        scrapSummary.LoweredDetails.Add(change);
                                }
                            }
                        }
                    }

                    if (scrapSummary.RaisedDetails.Any() || scrapSummary.LoweredDetails.Any())
                    {
                        var dayDate = currentScrap.Date.Date;
                        if (!dailySummaries.ContainsKey(dayDate))
                        {
                            dailySummaries[dayDate] = new DailySummary { Date = dayDate };
                        }
                        dailySummaries[dayDate].Scraps.Add(scrapSummary);
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
                        totalRaised = d.Scraps.Sum(s => s.RaisedDetails.Count),
                        totalLowered = d.Scraps.Sum(s => s.LoweredDetails.Count),
                        scraps = d.Scraps.OrderBy(s => s.Date).Select(s => new
                        {
                            scrapId = s.ScrapId,
                            fullDate = s.Date,
                            time = s.Date.ToString("HH:mm"),
                            raised = s.RaisedDetails.Count,
                            lowered = s.LoweredDetails.Count,
                            raisedDetails = s.RaisedDetails.OrderByDescending(x => x.PriceDifference).ToList(),
                            loweredDetails = s.LoweredDetails.OrderBy(x => x.PriceDifference).ToList()
                        }).ToList()
                    })
                    .ToList();

                if (!string.IsNullOrEmpty(connectionId))
                {
                    await _hub.Clients.Client(connectionId).SendAsync("ReceiveProgress", "Gotowe!", 100);
                }

                return Json(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AllegroDashboard ERROR] {ex}");
                return StatusCode(500, $"Błąd serwera: {ex.Message}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPriceBridgeHistory(int storeId, int days = 30)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId)
                            || User.IsInRole("Admin") || User.IsInRole("Manager");

            if (!hasAccess) return Forbid();

            var sinceDate = DateTime.UtcNow.AddDays(-days).Date;

            var batches = await _context.AllegroPriceBridgeBatches
                .AsNoTracking()
                .Where(b => b.StoreId == storeId && b.ExecutionDate >= sinceDate)
                .Include(b => b.User)
                .Include(b => b.BridgeItems)
                    .ThenInclude(i => i.AllegroProduct)
                .OrderByDescending(b => b.ExecutionDate)
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
                        executionTime = b.ExecutionDate.ToString("HH:mm"),
                        userName = b.User?.UserName ?? "Nieznany",
                        successfulCount = b.SuccessfulCount,
                        failedCount = b.FailedCount,

                        items = b.BridgeItems.Select(i => new
                        {
                            productName = i.AllegroProduct?.AllegroProductName ?? "Produkt usunięty",
                            productId = i.AllegroProduct?.AllegroProductId,
                            offerId = i.AllegroOfferId,
                            priceBefore = i.PriceBefore,
                            priceAfter = i.PriceAfter_Verified ?? i.PriceAfter_Simulated,
                            priceDiff = (i.PriceAfter_Verified ?? i.PriceAfter_Simulated) - i.PriceBefore,
                            success = i.Success,
                            errorMessage = i.ErrorMessage,

                            commissionBefore = i.CommissionBefore,
                            commissionAfter = i.CommissionAfter_Verified,
                            marginPrice = i.MarginPrice,
                            includeCommissionInMargin = i.IncludeCommissionInMargin

                        }).OrderBy(i => i.productName).ToList()
                    }).OrderByDescending(b => b.executionTime).ToList()
                })
                .ToList();

            return Json(groupedByDay);
        }

        private class ScrapInfo
        {
            public int Id { get; set; }
            public DateTime Date { get; set; }
        }

        public sealed class PriceChangeDetail
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public decimal OldPrice { get; set; }
            public decimal NewPrice { get; set; }
            public decimal PriceDifference { get; set; }
        }

        public sealed class ScrapSummary
        {
            public int ScrapId { get; set; }
            public DateTime Date { get; set; }
            public List<PriceChangeDetail> RaisedDetails { get; } = new();
            public List<PriceChangeDetail> LoweredDetails { get; } = new();
        }

        public sealed class DailySummary
        {
            public DateTime Date { get; set; }
            public List<ScrapSummary> Scraps { get; } = new();
        }
    }
}