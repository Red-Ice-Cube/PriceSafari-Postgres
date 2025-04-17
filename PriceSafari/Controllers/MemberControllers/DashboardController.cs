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

        // GET /Dashboard/GetDashboardData?storeId=1&scraps=30
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

            // *ostatnie* N scrapów → będą widoczne w tabeli / wykresie
            var displayScraps = allScraps.Skip(1).ToList();       // opuszczamy najstarszy

            /* 3. wczytujemy PriceHistories dla wszystkich pobranych scrapów --- */
            var priceRows = new List<PriceRow>();

            foreach (var s in allScraps)
            {
                var rows = await _context.PriceHistories
                    .AsNoTracking()
                    .Where(ph => ph.ScrapHistoryId == s.Id &&
                                 ph.StoreName.ToLower() == storeNameLower)
                    .Select(ph => new PriceRow
                    {
                        ProductId = ph.ProductId,
                        ProductName = ph.Product.ProductName,
                        OldPrice = ph.Price,
                        ScrapId = ph.ScrapHistoryId,
                        Date = s.Date.Date
                    })
                    .ToListAsync();

                priceRows.AddRange(rows);
            }

            /* 4. bucket tylko dla dat, które mają być pokazane ---------------- */
            var buckets = displayScraps
                .Select(s => s.Date.Date)
                .Distinct()
                .ToDictionary(d => d, d => new DayBucket(d));

            /* 5. analiza zmian ------------------------------------------------ */
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
                        ScrapId = g.First().ScrapId
                    });

                decimal? prev = null;
                foreach (var day in byDay)
                {
                    if (day.Prices.Count != 1) { prev = null; continue; }   // ambiguous

                    var now = day.Prices.First();

                    if (prev.HasValue && now != prev.Value && buckets.ContainsKey(day.Date))
                    {
                        var det = new PriceChangeDetail
                        {
                            Date = day.Date,
                            ProductId = prodGrp.Key,
                            ProductName = day.ProductName,
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

            /* 6. JSON dla frontu ---------------------------------------------- */
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


        // ————— Pomocnicze klasy tylko w tym kontrolerze —————
        private sealed class PriceRow
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public decimal OldPrice { get; set; }
            public int ScrapId { get; set; }
            public DateTime Date { get; set; }
        }

        public sealed class PriceChangeDetail
        {
            public DateTime Date { get; set; }
            public int ProductId { get; set; }
            public string ProductName { get; set; }
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





//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models.ViewModels;
//using System.Security.Claims;

//namespace PriceSafari.Controllers.MemberControllers
//{
//    [Authorize(Roles = "Admin, Member")]
//    public class DashboardController : Controller
//    {
//        private readonly PriceSafariContext _context;

//        public DashboardController(PriceSafariContext context)
//        {
//            _context = context;
//        }


//        [HttpGet]
//        public async Task<IActionResult> Index()
//        {
//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

//            var userStores = await _context.UserStores
//                .Where(us => us.UserId == userId)
//                .Include(us => us.StoreClass)
//                .ThenInclude(s => s.ScrapHistories)



//                .ToListAsync();

//            var stores = userStores.Select(us => us.StoreClass).ToList();

//            var storeDetails = stores.Select(store => new ChanelViewModel
//            {
//                StoreId = store.StoreId,
//                StoreName = store.StoreName,
//                LogoUrl = store.StoreLogoUrl,
//                LastScrapeDate = store.ScrapHistories.OrderByDescending(sh => sh.Date).FirstOrDefault()?.Date,

//            }).ToList();

//            return View("~/Views/Panel/Dashboard/Index.cshtml", storeDetails);
//        }

//        public async Task<IActionResult> Dashboard(int? storeId)
//        {
//            if (storeId is null)
//                return NotFound("Store ID not provided.");

//            /* 1. Nazwa sklepu ------------------------------------------------*/
//            var storeName = await _context.Stores
//                                          .Where(s => s.StoreId == storeId)
//                                          .Select(s => s.StoreName)
//                                          .FirstOrDefaultAsync();
//            if (storeName is null)
//                return NotFound("Sklep nie został znaleziony.");

//            string myStoreName = storeName.ToLower();

//            /* 2. Ostatnie 30 scrapów ----------------------------------------*/
//            var scrapInfos = await _context.ScrapHistories
//                                           .Where(sh => sh.StoreId == storeId)
//                                           .OrderByDescending(sh => sh.Date)
//                                           .Take(30)
//                                           .Select(sh => new { sh.Id, sh.Date })
//                                           .ToListAsync();

//            scrapInfos = scrapInfos.OrderBy(sh => sh.Date).ToList();

//            /* 3. Bufory ------------------------------------------------------*/
//            var dayBuckets = new Dictionary<DateTime, DashboardDailyPriceChangeGroup>();
//            var ambiguousByDate = new Dictionary<DateTime, List<int>>();   // lista ID‑ków produktów z wieloma cenami

//            foreach (var s in scrapInfos)
//            {
//                DateTime d = s.Date.Date;
//                if (!dayBuckets.ContainsKey(d))
//                    dayBuckets[d] = new DashboardDailyPriceChangeGroup
//                    {
//                        Date = d,
//                        RaisedDetails = new(),
//                        LoweredDetails = new()
//                    };

//                if (!ambiguousByDate.ContainsKey(d))
//                    ambiguousByDate[d] = new List<int>();
//            }

//            /* 4. Pobranie cen produktów -------------------------------------*/
//            var priceRows = new List<PriceRow>();
//            foreach (var s in scrapInfos)
//            {
//                var rows = await _context.PriceHistories
//                                         .Where(ph => ph.ScrapHistoryId == s.Id &&
//                                                      ph.StoreName.ToLower() == myStoreName)
//                                         .Select(ph => new PriceRow
//                                         {
//                                             ProductId = ph.ProductId,
//                                             Price = ph.Price,
//                                             ScrapId = ph.ScrapHistoryId,
//                                             ProductName = ph.Product.ProductName
//                                         })
//                                         .ToListAsync();
//                priceRows.AddRange(rows);
//            }

//            /* 5. Rozszerzenie o datę scrapu --------------------------------*/
//            var extended = priceRows.Join(scrapInfos,
//                                          pr => pr.ScrapId,
//                                          si => si.Id,
//                                          (pr, si) => new ExtendedRow
//                                          {
//                                              ProductId = pr.ProductId,
//                                              Price = pr.Price,
//                                              Date = si.Date.Date,
//                                              ProductName = pr.ProductName,
//                                              ScrapId = pr.ScrapId
//                                          })
//                                    .ToList();

//            /* 6. Analiza zmian cen -----------------------------------------*/
//            var changeDetails = new List<PriceChangeDetail>();

//            foreach (var productGroup in extended.GroupBy(r => r.ProductId))
//            {
//                var daily = productGroup
//                            .GroupBy(r => r.Date)
//                            .Select(g => new
//                            {
//                                Date = g.Key,
//                                DistinctPrice = g.Select(r => r.Price).Distinct().OrderBy(p => p).ToList(),
//                                Name = g.First().ProductName,
//                                ScrapId = g.First().ScrapId
//                            })
//                            .OrderBy(x => x.Date)
//                            .ToList();

//                decimal? prevEffective = null;

//                foreach (var day in daily)
//                {
//                    // *Ambiguous* – wiele cen tego samego dnia
//                    if (day.DistinctPrice.Count > 1)
//                    {
//                        ambiguousByDate[day.Date].Add(productGroup.Key);
//                        continue;                       // pomijamy zmianę
//                    }

//                    decimal now = day.DistinctPrice.First();

//                    if (prevEffective.HasValue && now != prevEffective.Value)
//                    {
//                        changeDetails.Add(new PriceChangeDetail
//                        {
//                            Date = day.Date,
//                            ProductId = productGroup.Key,
//                            ProductName = day.Name,
//                            OldPrice = prevEffective.Value,
//                            NewPrice = now,
//                            PriceDifference = now - prevEffective.Value,
//                            ScrapId = day.ScrapId
//                        });
//                    }

//                    prevEffective = now;
//                }
//            }

//            /* 7. Zgrupowanie według dat ------------------------------------*/
//            var byDate = changeDetails
//                         .GroupBy(c => c.Date)
//                         .ToDictionary(g => g.Key, g => new
//                         {
//                             Raised = g.Where(c => c.PriceDifference > 0).ToList(),
//                             Lowered = g.Where(c => c.PriceDifference < 0).ToList()
//                         });

//            foreach (var date in dayBuckets.Keys.ToList())
//            {
//                if (byDate.TryGetValue(date, out var data))
//                {
//                    dayBuckets[date].PriceRaisedCount = data.Raised.Count;
//                    dayBuckets[date].PriceLoweredCount = data.Lowered.Count;
//                    dayBuckets[date].RaisedDetails = data.Raised;
//                    dayBuckets[date].LoweredDetails = data.Lowered;
//                }
//                // UWAGA: ambiguousByDate nie przekazujemy do widoku
//            }

//            var model = dayBuckets.Values.OrderBy(d => d.Date).ToList();

//            ViewBag.StoreName = storeName;
//            ViewBag.StoreId = storeId;

//            return View("~/Views/Panel/Dashboard/Dashboard.cshtml", model);
//        }


//        private class PriceRow
//        {
//            public int ProductId { get; set; }
//            public decimal Price { get; set; }
//            public int ScrapId { get; set; }
//            public string ProductName { get; set; }
//        }


//        private sealed class ExtendedRow : PriceRow
//        {
//            public DateTime Date { get; set; }
//        }


//        public sealed class PriceChangeDetail
//        {
//            public DateTime Date { get; set; }
//            public int ProductId { get; set; }
//            public string ProductName { get; set; }
//            public decimal OldPrice { get; set; }
//            public decimal NewPrice { get; set; }
//            public decimal PriceDifference { get; set; }
//            public int ScrapId { get; set; }
//        }


//        public sealed class DashboardDailyPriceChangeGroup
//        {
//            public DateTime Date { get; set; }
//            public int PriceRaisedCount { get; set; }
//            public int PriceLoweredCount { get; set; }
//            public List<PriceChangeDetail> RaisedDetails { get; set; }
//            public List<PriceChangeDetail> LoweredDetails { get; set; }
//        }
//    }
//}
