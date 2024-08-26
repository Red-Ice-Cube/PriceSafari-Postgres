using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using HtmlAgilityPack;
using PriceSafari.Hubs;
using PriceSafari.Models;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using Microsoft.AspNetCore.Authorization;
using System.Net;

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
        public async Task<IActionResult> Index()
        {
            var stores = await _context.Stores.ToListAsync();

            var lastScrapDates = await _context.ScrapHistories
                .GroupBy(sh => sh.StoreId)
                .Select(g => new { StoreId = g.Key, LastScrapDate = g.Max(sh => sh.Date) })
                .ToDictionaryAsync(x => x.StoreId, x => (DateTime?)x.LastScrapDate);

            ViewBag.LastScrapDates = lastScrapDates;

            return View("~/Views/ManagerPanel/Store/Index.cshtml", stores);
        }

        [HttpGet]
        public async Task<IActionResult> ScrapeProducts(int storeId, int depth)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            var categories = await _context.Categories.Where(c => c.StoreId == storeId && c.Depth == depth).ToListAsync();

            foreach (var category in categories)
            {
                var baseUrlTemplate = $"https://www.ceneo.pl/{category.CategoryUrl};0192;{store.StoreProfile}-0v;0020-15-0-0-{{0}}.htm";
                await ScrapeCategoryProducts(storeId, category.CategoryName, baseUrlTemplate);
            }

            return RedirectToAction("ProductList", new { storeId });
        }

        private async Task ScrapeCategoryProducts(int storeId, string categoryName, string baseUrlTemplate)
        {
            var web = new HtmlWeb();
            HtmlDocument doc;
            int page = 0;
            int pageCount = 1;
            bool hasMorePages = true;
            HashSet<string> existingProductUrls = _context.Products.Where(p => p.StoreId == storeId).Select(p => p.OfferUrl).ToHashSet();
            HashSet<string> newProductUrls = new HashSet<string>();

            while (hasMorePages)
            {
                var url = string.Format(baseUrlTemplate, page);
                Console.WriteLine($"Processing page: {url}");

                try
                {
                    doc = web.Load(url);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading page {page}: {ex.Message}");
                    break;
                }

                var pageCountNode = doc.DocumentNode.SelectSingleNode("//input[@id='page-counter']");
                if (page == 0 && pageCountNode != null)
                {
                    int.TryParse(pageCountNode.GetAttributeValue("data-pagecount", "1"), out pageCount);
                }

                var products = doc.DocumentNode.SelectNodes("//div[contains(@class, 'cat-prod-box')]");
                if (products != null && products.Count > 0)
                {
                    foreach (var product in products)
                    {
                        try
                        {
                            var nameNode = product.SelectSingleNode(".//strong[contains(@class, 'cat-prod-box__name')]//a");
                            var pid = product.GetAttributeValue("data-pid", "");
                            var gaCategoryName = product.GetAttributeValue("data-gacategoryname", "");

                            if (nameNode != null && !string.IsNullOrEmpty(pid) && !string.IsNullOrEmpty(gaCategoryName))
                            {
                                var name = WebUtility.HtmlDecode(nameNode.InnerText.Trim());
                                var offerUrl = "https://www.ceneo.pl/" + pid;

                                if (existingProductUrls.Contains(offerUrl) || newProductUrls.Contains(offerUrl))
                                {
                                    continue;
                                }

                                var trimmedCategoryName = gaCategoryName.Split('/').LastOrDefault()?.Trim() ?? categoryName;

                                var productEntity = new ProductClass
                                {
                                    StoreId = storeId,
                                    ProductName = name,
                                    Category = trimmedCategoryName,
                                    OfferUrl = offerUrl
                                };

                                _context.Products.Add(productEntity);
                                newProductUrls.Add(offerUrl);

                                Console.WriteLine($"Scraped Product - Name: {name}, Category: {trimmedCategoryName}, URL: {offerUrl}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing product on page {page}: {ex.Message}");
                            continue;
                        }
                    }

                    await _context.SaveChangesAsync();
                }
                else
                {
                    hasMorePages = false;
                }

                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", newProductUrls.Count, existingProductUrls.Count + newProductUrls.Count, page + 1, pageCount, storeId);

                page++;
                if (page >= pageCount)
                {
                    hasMorePages = false;
                }
            }
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
        public async Task<IActionResult> UpdateProductsToScrap(int storeId, int productsToScrap)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound();
            }

            store.ProductsToScrap = productsToScrap;
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
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