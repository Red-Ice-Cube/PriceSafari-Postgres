using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PuppeteerSharp;
using System.Net;
using Microsoft.AspNetCore.Authorization;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class CategoryScraperController : Controller
    {
        private readonly PriceSafariContext _context;
        private Browser _browser;
        private IPage _page;

        public CategoryScraperController(PriceSafariContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var stores = await _context.Stores.ToListAsync();
            return View("~/Views/ManagerPanel/CategoryScraper/Index.cshtml", stores);
        }

        private async Task InitializeBrowserAsync()
        {
            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();
            _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = false, // Headless mode
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-gpu",
                    "--disable-blink-features=AutomationControlled",
                    "--disable-extensions",
                    "--disable-dev-shm-usage"
                }
            });

            _page = await _browser.NewPageAsync();
            Console.WriteLine("Browser initialized and page created.");

            // Wait for 30 seconds only once when browser is initialized
            Console.WriteLine("Waiting for 30 seconds before starting the scraping...");
            await Task.Delay(30000); // Delay for 30 seconds
        }

        private async Task ScrapeCategories(int storeId, string categoryUrl, int depth)
        {
            Console.WriteLine("Scraping categories from URL: " + categoryUrl + " at depth: " + depth);

            // If the browser is not initialized, initialize it
            if (_browser == null || _page == null)
            {
                await InitializeBrowserAsync();
            }

            try
            {
                await _page.GoToAsync(categoryUrl);
                Console.WriteLine("Page loaded successfully: " + categoryUrl);

                // Scraping category nodes
                var categoryNodes = await _page.QuerySelectorAllAsync(depth == 0 ? "a.js_categories-link.cat-nav__title__name" : "div.nav-item__name");

                if (categoryNodes.Length > 0)
                {
                    Console.WriteLine("Found " + categoryNodes.Length + " category nodes.");

                    foreach (var node in categoryNodes)
                    {
                        string categoryName;
                        string categoryUrlSegment = string.Empty;

                        if (depth == 0)
                        {
                            categoryName = WebUtility.HtmlDecode(await node.EvaluateFunctionAsync<string>("el => el.textContent.trim()"));
                            categoryUrlSegment = await node.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
                        }
                        else
                        {
                            var categoryNameNode = await node.QuerySelectorAsync("div.nav-item__name");
                            if (categoryNameNode != null)
                            {
                                categoryName = WebUtility.HtmlDecode(await categoryNameNode.EvaluateFunctionAsync<string>("el => el.textContent.trim()"));
                            }
                            else
                            {
                                continue;
                            }
                        }

                        // Ensure the URL is clean and in correct format
                        categoryUrlSegment = categoryUrlSegment.Split(';')[0].Trim('/');
                        if (!categoryUrlSegment.StartsWith("https://"))
                        {
                            categoryUrlSegment = "https://www.ceneo.pl/" + categoryUrlSegment;
                        }

                        // Check if category exists in the database
                        var existingCategory = await _context.Categories
                            .FirstOrDefaultAsync(c => c.StoreId == storeId && c.CategoryName == categoryName && c.Depth == depth);

                        if (existingCategory == null)
                        {
                            var category = new CategoryClass
                            {
                                StoreId = storeId,
                                CategoryName = categoryName,
                                CategoryUrl = categoryUrlSegment,
                                Depth = depth
                            };

                            _context.Categories.Add(category);
                            Console.WriteLine($"Scraped Category - Name: {categoryName}, URL: {categoryUrlSegment}, Depth: {depth}");

                            await _context.SaveChangesAsync();
                        }
                        else
                        {
                            Console.WriteLine("Category already exists: " + categoryName + " at depth: " + depth);
                        }

                        // Recursively scrape subcategories if depth < 3
                        if (depth < 3)
                        {
                            await ScrapeCategories(storeId, categoryUrlSegment, depth + 1);
                        }
                    }
                }
                else
                {
                    Console.WriteLine("No category nodes found at URL: " + categoryUrl);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading page {categoryUrl}: {ex.Message}");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ScrapeCategories(int storeId)
        {
            Console.WriteLine("ScrapeCategories started for storeId: " + storeId);

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                Console.WriteLine("Store not found for storeId: " + storeId);
                return NotFound();
            }

            var storeProfile = store.StoreProfile;
            var baseUrl = $"https://www.ceneo.pl/;{storeProfile}-0v.htm";
            Console.WriteLine("Base URL: " + baseUrl);

            await ScrapeCategories(storeId, baseUrl, 0);

            return RedirectToAction("CategoryList", new { storeId });
        }

        public async Task<IActionResult> CategoryList(int storeId)
        {
            var categories = await _context.Categories.Where(c => c.StoreId == storeId).ToListAsync();
            return View("~/Views/ManagerPanel/CategoryScraper/CategoryList.cshtml", categories);
        }

        // Clean up the browser when the application shuts down
        public async Task<IActionResult> Shutdown()
        {
            if (_browser != null)
            {
                await _browser.CloseAsync();
                _browser = null;
                Console.WriteLine("Browser closed.");
            }
            return RedirectToAction("Index");
        }
    }
}




//KOD DO SCRAPOWANIA HTTP REQ    

//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using HtmlAgilityPack;
//using Microsoft.AspNetCore.Authorization;
//using System.Net;

//namespace PriceSafari.Controllers.ManagerControllers
//{
//    [Authorize(Roles = "Admin")]
//    public class CategoryScraperController : Controller
//    {
//        private readonly PriceSafariContext _context;

//        public CategoryScraperController(PriceSafariContext context)
//        {
//            _context = context;
//        }

//        [HttpGet]
//        public async Task<IActionResult> Index()
//        {
//            var stores = await _context.Stores.ToListAsync();

//            return View("~/Views/ManagerPanel/CategoryScraper/Index.cshtml", stores);
//        }

//        private async Task ScrapeCategories(int storeId, string categoryUrl, int depth)
//        {
//            Console.WriteLine("Scraping categories from URL: " + categoryUrl + " at depth: " + depth);

//            var web = new HtmlWeb();
//            HtmlDocument doc;

//            try
//            {
//                doc = web.Load(categoryUrl);
//                Console.WriteLine("Page loaded successfully: " + categoryUrl);
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Error loading page {categoryUrl}: {ex.Message}");
//                return;
//            }

//            // Wybór XPath na podstawie głębokości
//            var categoryNodes = doc.DocumentNode.SelectNodes(depth == 0 ? "//span[@class='cat-nav__title']/a" : "//div[@class='cat-nav__card']/a");

//            if (categoryNodes != null && categoryNodes.Count > 0)
//            {
//                Console.WriteLine("Found " + categoryNodes.Count + " category nodes.");

//                foreach (var node in categoryNodes)
//                {
//                    // Namierzanie nazwy kategorii na podstawie głębokości
//                    var categoryNameNode = depth == 0 ? node : node.SelectSingleNode(".//div[@class='nav-item__name']");
//                    if (categoryNameNode != null)
//                    {
//                        var categoryName = WebUtility.HtmlDecode(categoryNameNode.InnerText.Trim());
//                        var categoryUrlSegment = node.GetAttributeValue("href", "").Split(';')[0].Trim('/');

//                        var existingCategory = await _context.Categories
//                            .FirstOrDefaultAsync(c => c.StoreId == storeId && c.CategoryName == categoryName && c.Depth == depth);
//                        if (existingCategory == null)
//                        {
//                            var category = new CategoryClass
//                            {
//                                StoreId = storeId,
//                                CategoryName = categoryName,
//                                CategoryUrl = categoryUrlSegment,
//                                Depth = depth
//                            };

//                            _context.Categories.Add(category);
//                            Console.WriteLine($"Scraped Category - Name: {categoryName}, URL: {categoryUrlSegment}, Depth: {depth}");

//                            await _context.SaveChangesAsync();
//                        }
//                        else
//                        {
//                            Console.WriteLine("Category already exists: " + categoryName + " at depth: " + depth);
//                        }

//                        // Kontynuacja skrapowania dla głębokości < 3
//                        if (depth < 3)
//                        {
//                            var subCategoryUrl = "https://www.ceneo.pl" + node.GetAttributeValue("href", "");
//                            await ScrapeCategories(storeId, subCategoryUrl, depth + 1);
//                        }
//                    }
//                }
//            }
//            else
//            {
//                Console.WriteLine("No category nodes found at URL: " + categoryUrl);
//            }
//        }

//        [HttpPost]
//        public async Task<IActionResult> ScrapeCategories(int storeId)
//        {
//            Console.WriteLine("ScrapeCategories started for storeId: " + storeId);

//            var store = await _context.Stores.FindAsync(storeId);
//            if (store == null)
//            {
//                Console.WriteLine("Store not found for storeId: " + storeId);
//                return NotFound();
//            }

//            var storeProfile = store.StoreProfile;
//            var baseUrl = $"https://www.ceneo.pl/;{storeProfile}-0v.htm";
//            Console.WriteLine("Base URL: " + baseUrl);

//            await ScrapeCategories(storeId, baseUrl, 0);

//            return RedirectToAction("CategoryList", new { storeId });
//        }

//        public async Task<IActionResult> CategoryList(int storeId)
//        {
//            var categories = await _context.Categories.Where(c => c.StoreId == storeId).ToListAsync();

//            return View("~/Views/ManagerPanel/CategoryScraper/CategoryList.cshtml", categories);
//        }


//    }
//}
