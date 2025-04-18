using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models.ViewModels;
using System.Security.Claims;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Member")]
    public class DashboardController : Controller
    {
        private readonly PriceSafariContext _context;
        public DashboardController(PriceSafariContext context) => _context = context;


        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStores = await _context.UserStores
                .Where(us => us.UserId == userId)
                .Include(us => us.StoreClass)
                .ThenInclude(s => s.ScrapHistories)



                .ToListAsync();

            var stores = userStores.Select(us => us.StoreClass).ToList();

            var storeDetails = stores.Select(store => new ChanelViewModel
            {
                StoreId = store.StoreId,
                StoreName = store.StoreName,
                LogoUrl = store.StoreLogoUrl,
                LastScrapeDate = store.ScrapHistories.OrderByDescending(sh => sh.Date).FirstOrDefault()?.Date,

            }).ToList();

            return View("~/Views/Panel/Dashboard/Index.cshtml", storeDetails);
        }

        // 2) Pusty HTML Dashboard
        public async Task<IActionResult> Dashboard(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store is null) return NotFound();

            ViewBag.StoreId = storeId;
            ViewBag.StoreName = store.StoreName;
            return View("~/Views/Panel/Dashboard/Dashboard.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetDashboardData(int storeId, int scraps = 30)
        {
            /* 1. sklep -------------------------------------------------------- */
            var store = await _context.Stores
                                      .Where(s => s.StoreId == storeId)
                                      .Select(s => new { s.StoreName })
                                      .FirstOrDefaultAsync();
            if (store is null) return NotFound();
            string storeNameLower = store.StoreName.ToLower();

            /* 2. pobieramy N+1 scrapów (potrzebny jest 1 wcześniejszy) -------- */
            int take = scraps + 1;
            var allScraps = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(take)
                .Select(sh => new { sh.Id, sh.Date })
                .ToListAsync();

            // rosnąco po dacie
            allScraps = allScraps.OrderBy(s => s.Date).ToList();
            var displayScraps = allScraps.Skip(1).ToList();

            /* 3. zbieramy wszystkie wiersze cen wraz z URL obrazka --------------- */
            var priceRows = new List<PriceRow>();
            foreach (var s in allScraps)
            {
                var rows = await _context.PriceHistories
                    .AsNoTracking()
                    .Where(ph => ph.ScrapHistoryId == s.Id
                              && ph.StoreName.ToLower() == storeNameLower)
                    .Select(ph => new PriceRow
                    {
                        ProductId = ph.ProductId,
                        ProductName = ph.Product.ProductName,
                        ProductImage = ph.Product.MainUrl,   // ← tu pobieramy URL
                        OldPrice = ph.Price,
                        ScrapId = ph.ScrapHistoryId,
                        Date = s.Date.Date
                    })
                    .ToListAsync();

                priceRows.AddRange(rows);
            }

            /* 4. przygotowujemy kubełki na dni, które będziemy pokazywać -------- */
            var buckets = displayScraps
                .Select(s => s.Date.Date)
                .Distinct()
                .ToDictionary(d => d, d => new DayBucket(d));

            /* 5. analizujemy zmiany cen i od razu przypisujemy URL ------------- */
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
                        ProductImage = g.First().ProductImage,  // ← URL z PriceRow
                        ScrapId = g.First().ScrapId
                    });

                decimal? prev = null;
                foreach (var day in byDay)
                {
                    if (day.Prices.Count != 1) { prev = null; continue; }

                    var now = day.Prices.First();
                    if (prev.HasValue && now != prev.Value && buckets.ContainsKey(day.Date))
                    {
                        var det = new PriceChangeDetail
                        {
                            Date = day.Date,
                            ProductId = prodGrp.Key,
                            ProductName = day.ProductName,
                            ProductImage = day.ProductImage,   // ← tu trafia do detalu
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

            /* 6. serializujemy wynik do JSON ---------------------------------- */
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


        // ————— Pomocnicze klasy użyte w kontrolerze —————
        private sealed class PriceRow
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string ProductImage { get; set; }   // ← dodane
            public decimal OldPrice { get; set; }
            public int ScrapId { get; set; }
            public DateTime Date { get; set; }
        }

        public sealed class PriceChangeDetail
        {
            public DateTime Date { get; set; }
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string ProductImage { get; set; }   // ← dodane
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




