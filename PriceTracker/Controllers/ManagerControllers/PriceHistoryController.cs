using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;

namespace PriceTracker.Controllers.ManagerControllers
{
    public class PriceHistoryController : Controller
    {
        private readonly PriceTrackerContext _context;

        public PriceHistoryController(PriceTrackerContext context)
        {
            _context = context;
        }

        // GET: PriceHistory
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
                return View(new List<dynamic>());
            }

            var storeName = await _context.Stores
                .Where(sn => sn.StoreId == storeId)
                .Select(sn => sn.StoreName)
                .FirstOrDefaultAsync();

            ViewBag.LatestScrap = latestScrap;
            ViewBag.StoreId = storeId;
            ViewBag.StoreName = storeName;

            var categories = await _context.Products
                .Where(p => p.StoreId == storeId)
                .Select(p => p.Category)
                .Distinct()
                .ToListAsync();

            ViewBag.Categories = categories;


            return View("~/Views/ManagerPanel/PriceHistory/Index.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetPrices(int? storeId)
        {
            if (storeId == null)
            {
                return Json(new { productCount = 0, priceCount = 0, prices = new List<dynamic>() });
            }

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return Json(new { productCount = 0, priceCount = 0, prices = new List<dynamic>() });
            }

            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            var prices = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id)
                .Include(ph => ph.Product)
                .ToListAsync();

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

                    return new
                    {
                        bestPriceEntry.ProductId,
                        bestPriceEntry.Product.ProductName,
                        bestPriceEntry.Product.Category,
                        LowestPrice = bestPrice,
                        bestPriceEntry.StoreName,
                        MyPrice = myPrice,
                        ScrapId = bestPriceEntry.ScrapHistoryId,
                        PriceDifference = Math.Round(myPrice - bestPrice, 2),
                        PercentageDifference = Math.Round((myPrice - bestPrice) / bestPrice * 100, 2),
                        Savings = isMyBestPrice && !isSharedBestPrice ? Math.Round(secondBestPrice - bestPrice, 2) : (decimal?)null,
                        IsSharedBestPrice = isMyBestPrice && isSharedBestPrice,
                        IsUniqueBestPrice = isMyBestPrice && !isSharedBestPrice
                    };
                })
                .ToList();

            // Eliminujemy powielone produkty na podstawie ProductId
            var uniqueAllPrices = allPrices.GroupBy(p => p.ProductId).Select(g => g.First()).ToList();

            var remainingProductCount = uniqueAllPrices.Count;
            var remainingPriceCount = prices.Count;

            return Json(new { productCount = remainingProductCount, priceCount = remainingPriceCount, prices = uniqueAllPrices });
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
