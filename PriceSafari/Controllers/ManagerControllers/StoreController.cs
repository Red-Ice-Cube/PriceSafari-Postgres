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
            if (ModelState.IsValid)
            {
                var existingStore = await _context.Stores.Include(s => s.Plan).FirstOrDefaultAsync(s => s.StoreId == store.StoreId);
                if (existingStore == null)
                {
                    return NotFound();
                }

                existingStore.StoreName = store.StoreName;
                existingStore.StoreProfile = store.StoreProfile;
                existingStore.StoreApiUrl = store.StoreApiUrl;
                existingStore.StoreApiKey = store.StoreApiKey;
                existingStore.StoreLogoUrl = store.StoreLogoUrl;
                existingStore.ProductMapXmlUrl = store.ProductMapXmlUrl;
                existingStore.ProductMapXmlUrlGoogle = store.ProductMapXmlUrlGoogle;
                existingStore.AutoMatching = store.AutoMatching;
                existingStore.DiscountPercentage = store.DiscountPercentage;
                existingStore.GoogleMiG = store.GoogleMiG;

                // Check if the plan has changed
                if (existingStore.PlanId != store.PlanId)
                {
                    existingStore.PlanId = store.PlanId;
                    var newPlan = await _context.Plans.FindAsync(store.PlanId);
                    if (newPlan != null)
                    {
                        existingStore.ProductsToScrap = newPlan.ProductsToScrap;

                        // Check if the new plan is free or test plan
                        if (newPlan.NetPrice == 0 || newPlan.IsTestPlan)
                        {
                            // Set RemainingScrapes based on the plan's ScrapesPerInvoice
                            existingStore.RemainingScrapes = newPlan.ScrapesPerInvoice; // Assuming ScrapesPerInvoice represents the number of days or scrapes

                            // Optionally, mark any unpaid invoices as paid (if applicable)
                            var unpaidInvoices = await _context.Invoices.Where(i => i.StoreId == store.StoreId && !i.IsPaid).ToListAsync();
                            foreach (var invoice in unpaidInvoices)
                            {
                                invoice.IsPaid = true;
                            }
                        }
                        else
                        {
                            // For paid plans, RemainingScrapes remains 0 until the invoice is paid
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

            var plans = await _context.Plans.ToListAsync();
            ViewBag.Plans = new SelectList(plans, "PlanId", "PlanName", store.PlanId);
            return View("~/Views/ManagerPanel/Store/EditStore.cshtml", store);
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
                // Tworzymy domyślne ustawienia, jeśli brak w bazie
                settings = new Settings
                {
                    HeadLess = true,
                    JavaScript = true,
                    Styles = false,
                    WarmUpTime = 5 // sekundy
                };
            }

            using (var productScraper = new ProductScraper(_context, _hubContext, settings))
            {
                // Najpierw przechodzimy intencjonalnie do strony z Captchą
                await productScraper.NavigateToCaptchaAsync();

                // Czekamy aż użytkownik rozwiąże Captchę
                await productScraper.WaitForCaptchaSolutionAsync();

                // Teraz, gdy Captcha jest rozwiązana, możemy kontynuować scrapowanie kategorii
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
            var store = await _context.Stores.Include(s => s.Products)
                                             .Include(s => s.Categories)
                                             .Include(s => s.ScrapHistories)
                                             .Include(s => s.PriceValues)
                                             .Include(s => s.Flags)
                                             .Include(s => s.UserStores)
                                             .FirstOrDefaultAsync(s => s.StoreId == storeId);

            if (store == null)
            {
                return NotFound();
            }

            _context.Stores.Remove(store);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
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
            // Pobieramy wszystkie produkty dla danego sklepu, które są odrzucone i mogą być zeskrobane
            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.IsRejected && p.IsScrapable)
                .ToListAsync();

            // Ustawiamy IsRejected na false tylko dla produktów spełniających warunki
            foreach (var product in products)
            {
                product.IsRejected = false;
            }

            // Zapisujemy zmiany do bazy danych
            await _context.SaveChangesAsync();

            // Przekierowanie do listy produktów po zakończeniu operacji
            return RedirectToAction("ProductList", new { storeId });
        }


    }
}