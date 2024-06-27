using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using HtmlAgilityPack;
using PriceTracker.Hubs;
using PriceTracker.Models;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;

namespace PriceTracker.Controllers
{
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
            return View();
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
            return View(stores);
        }

        [HttpGet]
        public async Task<IActionResult> ScrapeProducts(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            var storeProfile = store.StoreProfile; // Zakładamy, że to będzie np. "14337"
            var baseUrlTemplate = "https://www.ceneo.pl/;0192;{0}-0v;0020-15-0-0-{1}.htm";
            var web = new HtmlWeb();
            HtmlDocument doc;

            try
            {
                var initialUrl = string.Format(baseUrlTemplate, storeProfile, 0);
                doc = web.Load(initialUrl);
            }
            catch (Exception ex)
            {
                // Log the error (ex.Message) or handle it as needed
                return StatusCode(500, "Error loading the webpage");
            }

            // Pobieramy liczbę stron
            var pageCountNode = doc.DocumentNode.SelectSingleNode("//input[@id='page-counter']");
            int pageCount = 1;
            if (pageCountNode != null)
            {
                int.TryParse(pageCountNode.GetAttributeValue("data-pagecount", "1"), out pageCount);
            }

            var existingProductUrls = _context.Products.Where(p => p.StoreId == storeId).Select(p => p.OfferUrl).ToHashSet();
            int totalScraped = 0;
            int totalProducts = 0;
            HashSet<string> newProductUrls = new HashSet<string>();

            for (int page = 0; page < pageCount; page++)
            {
                var url = string.Format(baseUrlTemplate, storeProfile, page);
                Console.WriteLine($"Processing page: {url}"); // Log the current page URL being processed

                try
                {
                    doc = web.Load(url);
                }
                catch (Exception ex)
                {
                    // Log the error (ex.Message) or handle it as needed
                    Console.WriteLine($"Error loading page {page}: {ex.Message}");
                    continue; // Skip to the next page if there's an error loading the current page
                }

                var products = doc.DocumentNode.SelectNodes("//div[contains(@class, 'cat-prod-box')]");
                if (products != null && products.Count > 0)
                {
                    foreach (var product in products)
                    {
                        try
                        {
                            var nameNode = product.SelectSingleNode(".//strong[contains(@class, 'cat-prod-box__name')]//a");
                            var category = product.GetAttributeValue("data-gacategoryname", "");
                            var pid = product.GetAttributeValue("data-pid", "");

                            if (nameNode != null && !string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(pid))
                            {
                                var name = nameNode.InnerText.Trim();
                                var offerUrl = "https://www.ceneo.pl/" + pid;

                                if (existingProductUrls.Contains(offerUrl) || newProductUrls.Contains(offerUrl))
                                {
                                    continue;
                                }

                                var productEntity = new ProductClass
                                {
                                    StoreId = storeId,
                                    ProductName = name,
                                    Category = category,
                                    OfferUrl = offerUrl
                                };

                                _context.Products.Add(productEntity);
                                newProductUrls.Add(offerUrl);

                                // Log the product details for debugging
                                Console.WriteLine($"Scraped Product - Name: {name}, Category: {category}, URL: {offerUrl}");
                            }
                            totalScraped++;
                            await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", totalScraped, newProductUrls.Count, page + 1, pageCount, storeId);
                        }
                        catch (Exception ex)
                        {
                            // Log the error (ex.Message) or handle it as needed
                            Console.WriteLine($"Error processing product on page {page}: {ex.Message}");
                            continue; // Skip the current product if there's an error processing it
                        }
                    }

                    await _context.SaveChangesAsync();
                }
                else
                {
                    Console.WriteLine($"No products found on page {page}.");
                }
            }

            Console.WriteLine($"Total pages processed: {pageCount}, Total products scraped: {totalScraped}");

            return RedirectToAction("ProductList", new { storeId = storeId });
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

            return View(products);
        }
    }
}
