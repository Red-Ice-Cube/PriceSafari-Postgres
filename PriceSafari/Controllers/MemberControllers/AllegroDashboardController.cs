using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.SignalIR;
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
        public async Task<IActionResult> GetDashboardData(
                int storeId,
                int scraps = 7,
                string connectionId = null)
        {
            int step = 0;
            int totalSteps = scraps + 5;

            async Task Progress(string msg)
            {
                if (string.IsNullOrEmpty(connectionId)) return;
                step++;
                int pct = step * 100 / totalSteps;
                if (pct > 100) pct = 100;
                await _hub.Clients.Client(connectionId).SendAsync("ReceiveProgress", msg, pct);
            }

            await Progress("Pobieram dane sklepu Allegro...");

            var storeInfo = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => new { s.StoreNameAllegro })
                .FirstOrDefaultAsync();

            if (storeInfo is null || string.IsNullOrEmpty(storeInfo.StoreNameAllegro))
            {
                return BadRequest("Sklep nie ma skonfigurowanej nazwy sprzedawcy Allegro.");
            }

            string mySellerName = storeInfo.StoreNameAllegro;

            await Progress($"Pobieram {scraps + 1} ostatnich analiz Allegro...");

            int take = scraps + 1;

            var allScraps = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(take)
                .Select(sh => new { sh.Id, sh.Date })
                .ToListAsync();

            allScraps = allScraps.OrderBy(s => s.Date).ToList();
            var displayScraps = allScraps.Skip(1).ToList();

            var priceRows = new List<PriceRow>();

            for (int i = 0; i < allScraps.Count; i++)
            {
                var s = allScraps[i];

                var rows = await _context.AllegroPriceHistories
                    .AsNoTracking()
                    .Where(ph => ph.AllegroScrapeHistoryId == s.Id
                              && ph.SellerName == mySellerName)
                    .Select(ph => new PriceRow
                    {
                        ProductId = ph.AllegroProductId,
                        ProductName = ph.AllegroProduct.AllegroProductName,

                        ProductImage = "",
                        OldPrice = ph.Price,
                        ScrapId = ph.AllegroScrapeHistoryId,
                        Date = s.Date.Date
                    })
                    .ToListAsync();

                priceRows.AddRange(rows);
                await Progress($"Analizuję Allegro scrap {i + 1}/{allScraps.Count}...");
            }

            await Progress("Agreguję wyniki Allegro...");

            var buckets = displayScraps
                .Select(s => s.Date.Date)
                .Distinct()
                .ToDictionary(d => d, d => new DayBucket(d));

            foreach (var prodGrp in priceRows.GroupBy(r => r.ProductId))
            {
                var byDay = prodGrp
                    .GroupBy(r => r.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new
                    {
                        Date = g.Key,

                        Prices = g.Select(x => x.OldPrice).Distinct().OrderBy(p => p).ToList(),
                        ProductName = g.First().ProductName,
                        ProductImage = g.First().ProductImage,
                        ScrapId = g.First().ScrapId
                    });

                decimal? prev = null;
                foreach (var day in byDay)
                {

                    var now = day.Prices.First();

                    if (prev.HasValue && now != prev.Value && buckets.ContainsKey(day.Date))
                    {
                        var det = new PriceChangeDetail
                        {
                            Date = day.Date,
                            ProductId = prodGrp.Key,
                            ProductName = day.ProductName,
                            ProductImage = day.ProductImage,
                            OldPrice = prev.Value,
                            NewPrice = now,
                            PriceDifference = now - prev.Value,
                            ScrapId = day.ScrapId
                        };

                        if (det.PriceDifference > 0)
                            buckets[day.Date].RaisedDetails.Add(det);
                        else
                            buckets[day.Date].LoweredDetails.Add(det);
                    }
                    prev = now;
                }
            }

            await Progress("Finalizuję odpowiedź...");

            var result = buckets.Values
                .OrderBy(b => b.Date)
                .Select(b => new
                {
                    date = b.Date.ToString("yyyy-MM-dd"),
                    day = b.Date.ToString("ddd"),
                    raised = b.RaisedDetails.Count,
                    lowered = b.LoweredDetails.Count,
                    raisedDetails = b.RaisedDetails,
                    loweredDetails = b.LoweredDetails
                });

            return Json(result);
        }

        private sealed class PriceRow
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string ProductImage { get; set; }
            public decimal OldPrice { get; set; }
            public int ScrapId { get; set; }
            public DateTime Date { get; set; }
        }

        public sealed class PriceChangeDetail
        {
            public DateTime Date { get; set; }
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string ProductImage { get; set; }
            public decimal OldPrice { get; set; }
            public decimal NewPrice { get; set; }
            public decimal PriceDifference { get; set; }
            public int ScrapId { get; set; }
        }

        private sealed class DayBucket
        {
            public DayBucket(DateTime d) => Date = d;
            public DateTime Date { get; }
            public List<PriceChangeDetail> RaisedDetails { get; } = new();
            public List<PriceChangeDetail> LoweredDetails { get; } = new();
        }
    }
}