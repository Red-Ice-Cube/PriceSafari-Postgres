using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PriceTracker.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Heat_Lead.Data;

namespace PriceTracker.Controllers
{
    public class PriceHistoryController : Controller
    {
        private readonly Heat_LeadContext _context;

        public PriceHistoryController(Heat_LeadContext context)
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

          
            return View("~/Views/Panel/PriceHistory/Index.cshtml");
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

            var filteredPrices = prices.GroupBy(p => p.ProductId)
                .Where(g => g.Any(p => p.StoreName.ToLower() == storeName.ToLower()) && g.Count() > 1)
                .Select(g =>
                {
                    var bestPriceEntry = g.OrderBy(p => p.Price).First();
                    var myPriceEntry = g.FirstOrDefault(p => p.StoreName.ToLower() == storeName.ToLower());

                    var bestPrice = bestPriceEntry.Price;
                    var myPrice = myPriceEntry != null ? myPriceEntry.Price : bestPrice;

                    return new
                    {
                        ProductId = bestPriceEntry.ProductId,
                        ProductName = bestPriceEntry.Product.ProductName,
                        Category = bestPriceEntry.Product.Category,
                        LowestPrice = bestPrice,
                        StoreName = bestPriceEntry.StoreName,
                        MyPrice = myPrice,
                        ScrapId = bestPriceEntry.ScrapHistoryId,
                        OfferUrl = bestPriceEntry.OfferUrl,
                        PriceDifference = myPrice != 0 ? Math.Round(myPrice - bestPrice, 2) : (decimal?)null,
                        PercentageDifference = myPrice != 0 ? Math.Round(((myPrice - bestPrice) / bestPrice) * 100, 2) : (decimal?)null
                    };
                })
                .ToList();

            // Usuwanie produktów i powiązanych historii cen, które nie spełniają warunków
            var excludedProductIds = prices.GroupBy(p => p.ProductId)
                .Where(g => !g.Any(p => p.StoreName.ToLower() == storeName.ToLower()) || g.Count() <= 1)
                .Select(g => g.Key)
                .ToList();

            var productsToRemove = await _context.Products
                .Where(p => excludedProductIds.Contains(p.ProductId))
                .ToListAsync();

            _context.Products.RemoveRange(productsToRemove);

            var priceHistoriesToRemove = await _context.PriceHistories
                .Where(ph => excludedProductIds.Contains(ph.ProductId))
                .ToListAsync();

            _context.PriceHistories.RemoveRange(priceHistoriesToRemove);

            await _context.SaveChangesAsync();

            // Aktualna liczba produktów po usunięciu
            var remainingProductCount = await _context.Products
                .Where(p => p.StoreId == storeId)
                .CountAsync();

            // Aktualna liczba cen do wyświetlenia
            var remainingPriceCount = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id)
                .CountAsync();

            return Json(new { productCount = remainingProductCount, priceCount = remainingPriceCount, prices = filteredPrices });
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

        // GET: PriceHistory/Details/5
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

            return View(prices);
        }
    }
}
