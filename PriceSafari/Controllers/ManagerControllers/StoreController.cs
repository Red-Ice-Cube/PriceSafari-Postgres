using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using HtmlAgilityPack;
using PriceSafari.Hubs;
using PriceSafari.Models;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using Microsoft.AspNetCore.Authorization;
using PriceSafari.Scrapers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class StoreController : Controller
    {

        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;

        public StoreController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet]
        public IActionResult CreateStore()
        {
            return View("~/Views/ManagerPanel/Store/CreateStore.cshtml");
        }

        [HttpPost]
        public async Task<IActionResult> CreateStore(string storeName, string storeProfile, string? apiUrl, string? apiKey, string? logoUrl, int? productPack)
        {
            if (string.IsNullOrEmpty(storeName) || string.IsNullOrEmpty(storeProfile))
            {
                return BadRequest("Store name and profile are required.");
            }

            var store = new StoreClass
            {
                StoreName = storeName,
                StoreProfile = storeProfile,
                StoreApiUrl = apiUrl,
                StoreApiKey = apiKey,
                StoreLogoUrl = logoUrl,
                ProductsToScrap = productPack
            };

            _context.Stores.Add(store);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> EditStore(int storeId)
        {
            var store = await _context.Stores.Include(s => s.Plan).FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null)
            {
                return NotFound();
            }

            var plans = await _context.Plans.ToListAsync();
            ViewBag.Plans = new SelectList(plans, "PlanId", "PlanName", store.PlanId);

            return View("~/Views/ManagerPanel/Store/EditStore.cshtml", store);
        }

        [HttpPost]
        public async Task<IActionResult> EditStore(StoreClass store)
        {

            if (!ModelState.IsValid)
            {

                var plans = await _context.Plans.ToListAsync();
                ViewBag.Plans = new SelectList(plans, "PlanId", "PlanName", store.PlanId);
                return View("~/Views/ManagerPanel/Store/EditStore.cshtml", store);
            }

            var existingStore = await _context.Stores
                .Include(s => s.Plan)
                .FirstOrDefaultAsync(s => s.StoreId == store.StoreId);

            if (existingStore == null)
                return NotFound();

            existingStore.StoreName = store.StoreName;
            existingStore.StoreProfile = store.StoreProfile;
            existingStore.StoreApiUrl = store.StoreApiUrl;
            existingStore.StoreApiKey = store.StoreApiKey;
            existingStore.StoreLogoUrl = store.StoreLogoUrl;
            existingStore.ProductMapXmlUrl = store.ProductMapXmlUrl;
            existingStore.ProductMapXmlUrlGoogle = store.ProductMapXmlUrlGoogle;
            existingStore.GoogleMiG = store.GoogleMiG;
            existingStore.DiscountPercentage = store.DiscountPercentage ?? 0;
            existingStore.RemainingScrapes = store.RemainingScrapes;
            existingStore.ProductsToScrap = store.ProductsToScrap;
            existingStore.StoreNameAllegro = store.StoreNameAllegro;

            existingStore.StoreNameGoogle = store.StoreNameGoogle;
            existingStore.StoreNameCeneo = store.StoreNameCeneo;
            existingStore.UseGoogleXMLFeedPrice = store.UseGoogleXMLFeedPrice;

            if (existingStore.PlanId != store.PlanId)
            {
                existingStore.PlanId = store.PlanId;
                var newPlan = await _context.Plans.FindAsync(store.PlanId);

                if (newPlan != null)
                {

                    existingStore.ProductsToScrap = newPlan.ProductsToScrap;

                    if (newPlan.NetPrice == 0 || newPlan.IsTestPlan)
                    {
                        existingStore.RemainingScrapes = newPlan.ScrapesPerInvoice;

                        var unpaidInvoices = await _context.Invoices
                            .Where(i => i.StoreId == store.StoreId && !i.IsPaid)
                            .ToListAsync();

                        foreach (var invoice in unpaidInvoices)
                        {
                            invoice.IsPaid = true;
                        }
                    }
                    else
                    {

                        existingStore.RemainingScrapes = 0;
                    }
                }
                else
                {
                    existingStore.ProductsToScrap = null;
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var stores = await _context.Stores
                .Include(s => s.Plan)
                .Include(s => s.Invoices)
                .Include(s => s.ScrapHistories)
                .ToListAsync();

            var lastScrapDates = stores
                .Select(s => new
                {
                    StoreId = s.StoreId,
                    LastScrapDate = s.ScrapHistories.OrderByDescending(sh => sh.Date).FirstOrDefault()?.Date
                })
                .ToDictionary(x => x.StoreId, x => (DateTime?)x.LastScrapDate);

            ViewBag.LastScrapDates = lastScrapDates;

            return View("~/Views/ManagerPanel/Store/Index.cshtml", stores);
        }

        [HttpGet]
        public async Task<IActionResult> ScrapeProducts(int storeId, int depth)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            var categories = await _context.Categories
                .Where(c => c.StoreId == storeId && c.Depth == depth)
                .ToListAsync();

            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {

                settings = new Settings
                {
                    HeadLess = true,
                    JavaScript = true,
                    Styles = false,
                    WarmUpTime = 5
                };
            }

            using (var productScraper = new ProductScraper(_context, _hubContext, settings))
            {

                await productScraper.NavigateToCaptchaAsync();

                await productScraper.WaitForCaptchaSolutionAsync();

                foreach (var category in categories)
                {
                    var baseUrlTemplate = $"https://www.ceneo.pl/{category.CategoryUrl};0192;{store.StoreProfile}-0v;0020-15-0-0-{{0}}.htm";
                    await productScraper.ScrapeCategoryProducts(storeId, category.CategoryName, baseUrlTemplate);
                }
            }

            return RedirectToAction("ProductList", new { storeId });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteStore(int storeId)
        {

            bool storeExists = await _context.Stores.AnyAsync(s => s.StoreId == storeId);
            if (!storeExists)
            {
                Console.WriteLine($"Próba usunięcia Store o ID={storeId}, ale nie znaleziono go w bazie.");
                return NotFound();
            }

            _context.Database.SetCommandTimeout(300);

            Console.WriteLine($"Rozpoczynam usuwanie Store o ID={storeId}...");

            using var transaction = await _context.Database.BeginTransactionAsync();

            int chunkSize = 100;

            await DeleteInChunksAsync("Products", "StoreId", storeId, chunkSize);
            await DeleteInChunksAsync("Categories", "StoreId", storeId, chunkSize);
            await DeleteInChunksAsync("ScrapHistories", "StoreId", storeId, chunkSize);
            await DeleteInChunksAsync("PriceValues", "StoreId", storeId, chunkSize);
            await DeleteInChunksAsync("Flags", "StoreClassStoreId", storeId, chunkSize);
            await DeleteInChunksAsync("UserStores", "StoreId", storeId, chunkSize);
            await DeleteInChunksAsync("PriceSafariReports", "StoreId", storeId, chunkSize);
            await DeleteInChunksAsync("Invoices", "StoreId", storeId, chunkSize);

            int deletedStores = await _context.Database.ExecuteSqlRawAsync(
                "DELETE FROM [Stores] WHERE StoreId = {0}",
                storeId
            );
            Console.WriteLine($"Usunięto {deletedStores} rekord(ów) z tabeli [Stores].");

            await transaction.CommitAsync();

            Console.WriteLine($"Zakończono usuwanie Store o ID={storeId} wraz z powiązanymi danymi.");

            return RedirectToAction("Index");
        }

        private async Task<int> DeleteInChunksAsync(string tableName, string whereColumn, int storeId, int chunkSize)
        {
            int totalDeleted = 0;
            while (true)
            {

                int deleted = await _context.Database.ExecuteSqlRawAsync($@"
            DELETE TOP({chunkSize})
            FROM [{tableName}]
            WHERE {whereColumn} = @storeId",
                    new SqlParameter("@storeId", storeId)
                );

                totalDeleted += deleted;
                Console.WriteLine($"[{tableName}] - usunięto {deleted} rekordów (łącznie {totalDeleted}).");

                if (deleted == 0)
                    break;
            }
            return totalDeleted;
        }

        [HttpGet]
        public async Task<IActionResult> ProductList(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.IsScrapable)
                .ToListAsync();

            var allProducts = await _context.Products
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            var rejectedProducts = allProducts
                .Where(p => p.IsRejected && p.IsScrapable)
                .ToList();

            var categories = products.Select(p => p.Category).Distinct().ToList();

            ViewBag.StoreName = store.StoreName;
            ViewBag.Categories = categories;
            ViewBag.StoreId = storeId;
            ViewBag.AllProducts = allProducts;
            ViewBag.ScrapableProducts = products;
            ViewBag.RejectedProductsCount = rejectedProducts.Count;

            return View("~/Views/ManagerPanel/Store/ProductList.cshtml", products);
        }

        [HttpPost]
        public async Task<IActionResult> ClearRejectedProducts(int storeId)
        {

            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.IsRejected && p.IsScrapable)
                .ToListAsync();

            foreach (var product in products)
            {
                product.IsRejected = false;
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("ProductList", new { storeId });
        }

    }
}