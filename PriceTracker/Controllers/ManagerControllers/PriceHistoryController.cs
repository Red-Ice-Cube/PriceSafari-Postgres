using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using PriceTracker.ViewModels;

namespace PriceTracker.Controllers.ManagerControllers
{
    public class PriceHistoryController : Controller
    {
        private readonly PriceTrackerContext _context;

        public PriceHistoryController(PriceTrackerContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int? storeId)
        {
            if (storeId == null)
            {
                return NotFound("Store ID not provided.");
            }

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return View(new List<FlagsClass>());
            }

            var storeName = await _context.Stores
                .Where(sn => sn.StoreId == storeId)
                .Select(sn => sn.StoreName)
                .FirstOrDefaultAsync();

            var categories = await _context.Products
                .Where(p => p.StoreId == storeId)
                .Select(p => p.Category)
                .Distinct()
                .ToListAsync();

            var flags = await _context.Flags
                .Where(f => f.StoreId == storeId)
                .ToListAsync();

            ViewBag.LatestScrap = latestScrap;
            ViewBag.StoreId = storeId;
            ViewBag.StoreName = storeName;
            ViewBag.Categories = categories;
            ViewBag.Flags = flags; 

            return View("~/Views/ManagerPanel/PriceHistory/Index.cshtml");
        }


        [HttpGet]
        public async Task<IActionResult> GetPrices(int? storeId)
        {
            if (storeId == null)
            {
                return Json(new { productCount = 0, priceCount = 0, myStoreName = "", prices = new List<dynamic>(), setPrice1 = 2.00m, setPrice2 = 2.00m });
            }

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return Json(new { productCount = 0, priceCount = 0, myStoreName = "", prices = new List<dynamic>(), setPrice1 = 2.00m, setPrice2 = 2.00m });
            }

            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == storeId)
                .Select(pv => new { pv.SetPrice1, pv.SetPrice2 })
                .FirstOrDefaultAsync();

            if (priceValues == null)
            {
                priceValues = new { SetPrice1 = 2.00m, SetPrice2 = 2.00m };
            }

            var prices = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id)
                .Include(ph => ph.Product)
                .Select(ph => new
                {
                    ph.ProductId,
                    ph.Product.ProductName,
                    ph.Product.Category,
                    ph.Price,
                    ph.StoreName,
                    ph.ScrapHistoryId
                })
                .ToListAsync();

            var productFlags = await _context.ProductFlags
                .Where(pf => prices.Select(p => p.ProductId).Contains(pf.ProductId))
                .GroupBy(pf => pf.ProductId)
                .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            var allPrices = prices
                .GroupBy(p => p.ProductId)
                .Select(g =>
                {
                    var bestPriceEntry = g.OrderBy(p => p.Price).First();
                    var myPriceEntry = g.FirstOrDefault(p => p.StoreName.ToLower() == storeName.ToLower());
                    var isSharedBestPrice = g.Count(p => p.Price == bestPriceEntry.Price) > 1;
                    var isMyBestPrice = myPriceEntry != null && myPriceEntry.Price == bestPriceEntry.Price;
                    var secondBestPrice = g.Where(p => p.Price > bestPriceEntry.Price).OrderBy(p => p.Price).FirstOrDefault()?.Price ?? 0;

                    var bestPrice = bestPriceEntry.Price;
                    var myPrice = myPriceEntry != null ? myPriceEntry.Price : bestPrice;

                    productFlags.TryGetValue(bestPriceEntry.ProductId, out var flagIds);
                    flagIds = flagIds ?? new List<int>();

                    return new
                    {
                        bestPriceEntry.ProductId,
                        bestPriceEntry.ProductName,
                        bestPriceEntry.Category,
                        LowestPrice = bestPrice,
                        bestPriceEntry.StoreName,
                        MyPrice = myPrice,
                        ScrapId = bestPriceEntry.ScrapHistoryId,
                        PriceDifference = Math.Round(myPrice - bestPrice, 2),
                        PercentageDifference = Math.Round((myPrice - bestPrice) / bestPrice * 100, 2),
                        Savings = isMyBestPrice && !isSharedBestPrice ? Math.Round(secondBestPrice - bestPrice, 2) : (decimal?)null,
                        IsSharedBestPrice = isMyBestPrice && isSharedBestPrice,
                        IsUniqueBestPrice = isMyBestPrice && !isSharedBestPrice,
                        FlagIds = flagIds
                    };
                })
                .ToList();

            var uniqueAllPrices = allPrices.GroupBy(p => p.ProductId).Select(g => g.First()).ToList();

            return Json(new
            {
                productCount = uniqueAllPrices.Count,
                priceCount = prices.Count,
                myStoreName = storeName,
                prices = uniqueAllPrices,
                setPrice1 = priceValues.SetPrice1,
                setPrice2 = priceValues.SetPrice2
            });
        }





        [HttpGet]
        public async Task<IActionResult> GetStores(int? storeId)
        {
            if (storeId == null)
            {
                return Json(new List<string>());
            }

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return Json(new List<string>());
            }

            var stores = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id)
                .Select(ph => ph.StoreName)
                .Distinct()
                .ToListAsync();

            return Json(stores);
        }

        [HttpPost]
        public async Task<IActionResult> SavePriceValues([FromBody] PriceValuesViewModel model)
        {
            if (model == null || model.StoreId <= 0)
            {
                return BadRequest("Invalid store ID or price values.");
            }

            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == model.StoreId)
                .FirstOrDefaultAsync();

            if (priceValues == null)
            {
                priceValues = new PriceValueClass
                {
                    StoreId = model.StoreId,
                    SetPrice1 = model.SetPrice1,
                    SetPrice2 = model.SetPrice2
                };
                _context.PriceValues.Add(priceValues);
            }
            else
            {
                priceValues.SetPrice1 = model.SetPrice1;
                priceValues.SetPrice2 = model.SetPrice2;
                _context.PriceValues.Update(priceValues);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Price values updated successfully." });
        }

        public async Task<IActionResult> Details(int scrapId, int productId)
        {
            var scrapHistory = await _context.ScrapHistories.FindAsync(scrapId);
            if (scrapHistory == null)
            {
                return NotFound();
            }

            var prices = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == scrapId && ph.ProductId == productId)
                .Include(ph => ph.Product)
                .ToListAsync();

            var product = await _context.Products.FindAsync(productId);
            if (product == null)
            {
                return NotFound();
            }

            ViewBag.ScrapHistory = scrapHistory;
            ViewBag.ProductName = product.ProductName;
            ViewBag.StoreName = (await _context.Stores.FindAsync(scrapHistory.StoreId))?.StoreName;

            return View("~/Views/ManagerPanel/PriceHistory/Details.cshtml", prices);
        }

        


    }
}
