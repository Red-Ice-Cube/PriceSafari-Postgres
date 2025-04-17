using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models.ViewModels;
using System.Security.Claims;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Member")]
    public class DashboardPriceAnalysisController : Controller
    {
        private readonly PriceSafariContext _context;

        public DashboardPriceAnalysisController(PriceSafariContext context)
        {
            _context = context;
        }


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

        public async Task<IActionResult> Dashboard(int? storeId)
        {
            if (storeId is null)
                return NotFound("Store ID not provided.");

            /* 1. Nazwa sklepu ------------------------------------------------*/
            var storeName = await _context.Stores
                                          .Where(s => s.StoreId == storeId)
                                          .Select(s => s.StoreName)
                                          .FirstOrDefaultAsync();
            if (storeName is null)
                return NotFound("Sklep nie został znaleziony.");

            string myStoreName = storeName.ToLower();

            /* 2. Ostatnie 30 scrapów ----------------------------------------*/
            var scrapInfos = await _context.ScrapHistories
                                           .Where(sh => sh.StoreId == storeId)
                                           .OrderByDescending(sh => sh.Date)
                                           .Take(30)
                                           .Select(sh => new { sh.Id, sh.Date })
                                           .ToListAsync();

            scrapInfos = scrapInfos.OrderBy(sh => sh.Date).ToList();

            /* 3. Bufory ------------------------------------------------------*/
            var dayBuckets = new Dictionary<DateTime, DashboardDailyPriceChangeGroup>();
            var ambiguousByDate = new Dictionary<DateTime, List<int>>();   // lista ID‑ków produktów z wieloma cenami

            foreach (var s in scrapInfos)
            {
                DateTime d = s.Date.Date;
                if (!dayBuckets.ContainsKey(d))
                    dayBuckets[d] = new DashboardDailyPriceChangeGroup
                    {
                        Date = d,
                        RaisedDetails = new(),
                        LoweredDetails = new()
                    };

                if (!ambiguousByDate.ContainsKey(d))
                    ambiguousByDate[d] = new List<int>();
            }

            /* 4. Pobranie cen produktów -------------------------------------*/
            var priceRows = new List<PriceRow>();
            foreach (var s in scrapInfos)
            {
                var rows = await _context.PriceHistories
                                         .Where(ph => ph.ScrapHistoryId == s.Id &&
                                                      ph.StoreName.ToLower() == myStoreName)
                                         .Select(ph => new PriceRow
                                         {
                                             ProductId = ph.ProductId,
                                             Price = ph.Price,
                                             ScrapId = ph.ScrapHistoryId,
                                             ProductName = ph.Product.ProductName
                                         })
                                         .ToListAsync();
                priceRows.AddRange(rows);
            }

            /* 5. Rozszerzenie o datę scrapu --------------------------------*/
            var extended = priceRows.Join(scrapInfos,
                                          pr => pr.ScrapId,
                                          si => si.Id,
                                          (pr, si) => new ExtendedRow
                                          {
                                              ProductId = pr.ProductId,
                                              Price = pr.Price,
                                              Date = si.Date.Date,
                                              ProductName = pr.ProductName,
                                              ScrapId = pr.ScrapId
                                          })
                                    .ToList();

            /* 6. Analiza zmian cen -----------------------------------------*/
            var changeDetails = new List<PriceChangeDetail>();

            foreach (var productGroup in extended.GroupBy(r => r.ProductId))
            {
                var daily = productGroup
                            .GroupBy(r => r.Date)
                            .Select(g => new
                            {
                                Date = g.Key,
                                DistinctPrice = g.Select(r => r.Price).Distinct().OrderBy(p => p).ToList(),
                                Name = g.First().ProductName,
                                ScrapId = g.First().ScrapId
                            })
                            .OrderBy(x => x.Date)
                            .ToList();

                decimal? prevEffective = null;

                foreach (var day in daily)
                {
                    // *Ambiguous* – wiele cen tego samego dnia
                    if (day.DistinctPrice.Count > 1)
                    {
                        ambiguousByDate[day.Date].Add(productGroup.Key);
                        continue;                       // pomijamy zmianę
                    }

                    decimal now = day.DistinctPrice.First();

                    if (prevEffective.HasValue && now != prevEffective.Value)
                    {
                        changeDetails.Add(new PriceChangeDetail
                        {
                            Date = day.Date,
                            ProductId = productGroup.Key,
                            ProductName = day.Name,
                            OldPrice = prevEffective.Value,
                            NewPrice = now,
                            PriceDifference = now - prevEffective.Value,
                            ScrapId = day.ScrapId
                        });
                    }

                    prevEffective = now;
                }
            }

            /* 7. Zgrupowanie według dat ------------------------------------*/
            var byDate = changeDetails
                         .GroupBy(c => c.Date)
                         .ToDictionary(g => g.Key, g => new
                         {
                             Raised = g.Where(c => c.PriceDifference > 0).ToList(),
                             Lowered = g.Where(c => c.PriceDifference < 0).ToList()
                         });

            foreach (var date in dayBuckets.Keys.ToList())
            {
                if (byDate.TryGetValue(date, out var data))
                {
                    dayBuckets[date].PriceRaisedCount = data.Raised.Count;
                    dayBuckets[date].PriceLoweredCount = data.Lowered.Count;
                    dayBuckets[date].RaisedDetails = data.Raised;
                    dayBuckets[date].LoweredDetails = data.Lowered;
                }
                // UWAGA: ambiguousByDate nie przekazujemy do widoku
            }

            var model = dayBuckets.Values.OrderBy(d => d.Date).ToList();

            ViewBag.StoreName = storeName;
            ViewBag.StoreId = storeId;

            return View("~/Views/Panel/Dashboard/Dashboard.cshtml", model);
        }

       
        private class PriceRow
        {
            public int ProductId { get; set; }
            public decimal Price { get; set; }
            public int ScrapId { get; set; }
            public string ProductName { get; set; }
        }

     
        private sealed class ExtendedRow : PriceRow
        {
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

     
        public sealed class DashboardDailyPriceChangeGroup
        {
            public DateTime Date { get; set; }
            public int PriceRaisedCount { get; set; }
            public int PriceLoweredCount { get; set; }
            public List<PriceChangeDetail> RaisedDetails { get; set; }
            public List<PriceChangeDetail> LoweredDetails { get; set; }
        }
    }
}
