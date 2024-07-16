using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using HtmlAgilityPack;
using PriceTracker.Hubs;
using PriceTracker.Models;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using System.Net;

namespace PriceTracker.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class StoreController : Controller
    {
        private readonly PriceTrackerContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;

        public StoreController(PriceTrackerContext context, IHubContext<ScrapingHub> hubContext)
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
        public async Task<IActionResult> CreateStore(string storeName, string storeProfile)
        {
            if (string.IsNullOrEmpty(storeName) || string.IsNullOrEmpty(storeProfile))
            {
                return BadRequest("Store name and profile are required.");
            }

            var store = new StoreClass
            {
                StoreName = storeName,
                StoreProfile = storeProfile
            };

            _context.Stores.Add(store);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var stores = await _context.Stores.ToListAsync();

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

                            if (nameNode != null && !string.IsNullOrEmpty(pid))
                            {
                                var name = WebUtility.HtmlDecode(nameNode.InnerText.Trim());
                                var offerUrl = "https://www.ceneo.pl/" + pid;

                                if (existingProductUrls.Contains(offerUrl) || newProductUrls.Contains(offerUrl))
                                {
                                    continue;
                                }

                                var productEntity = new ProductClass
                                {
                                    StoreId = storeId,
                                    ProductName = name,
                                    Category = categoryName,
                                    OfferUrl = offerUrl
                                };

                                _context.Products.Add(productEntity);
                                newProductUrls.Add(offerUrl);

                                Console.WriteLine($"Scraped Product - Name: {name}, Category: {categoryName}, URL: {offerUrl}");
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

            var products = await _context.Products.Where(p => p.StoreId == storeId).ToListAsync();
            var categories = products.Select(p => p.Category).Distinct().ToList();

            ViewBag.StoreName = store.StoreName;
            ViewBag.Categories = categories;
            ViewBag.StoreId = storeId;

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
    }
}
