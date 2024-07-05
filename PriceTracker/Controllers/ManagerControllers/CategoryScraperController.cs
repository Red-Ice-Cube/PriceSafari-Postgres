using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using HtmlAgilityPack;
using System;
using Microsoft.AspNetCore.Authorization;
using System.Net;

namespace PriceTracker.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class CategoryScraperController : Controller
    {
        private readonly PriceTrackerContext _context;

        public CategoryScraperController(PriceTrackerContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var stores = await _context.Stores.ToListAsync();

            return View("~/Views/ManagerPanel/CategoryScraper/Index.cshtml", stores);
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

        private async Task ScrapeCategories(int storeId, string categoryUrl, int depth)
        {
            Console.WriteLine("Scraping categories from URL: " + categoryUrl + " at depth: " + depth);

            var web = new HtmlWeb();
            HtmlDocument doc;

            try
            {
                doc = web.Load(categoryUrl);
                Console.WriteLine("Page loaded successfully: " + categoryUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading page {categoryUrl}: {ex.Message}");
                return;
            }

            var categoryNodes = doc.DocumentNode.SelectNodes(depth == 0 ? "//span[@class='cat-nav__title']/a" : "//div[@class='cat-nav__card']/a");
            if (categoryNodes != null && categoryNodes.Count > 0)
            {
                Console.WriteLine("Found " + categoryNodes.Count + " category nodes.");

                foreach (var node in categoryNodes)
                {
                    var categoryNameNode = depth == 0 ? node : node.SelectSingleNode(".//div[@class='nav-item__name']");
                    if (categoryNameNode != null)
                    {
                        var categoryName = WebUtility.HtmlDecode(categoryNameNode.InnerText.Trim());
                        var categoryUrlSegment = node.GetAttributeValue("href", "").Split(';')[0].Trim('/');

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

                        if (depth == 0)
                        {
                            var subCategoryUrl = "https://www.ceneo.pl" + node.GetAttributeValue("href", "");
                            await ScrapeCategories(storeId, subCategoryUrl, depth + 1);
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("No category nodes found at URL: " + categoryUrl);
            }
        }

        [HttpPost]
        public async Task<IActionResult> ScrapeSubcategories(int storeId)
        {
            Console.WriteLine("ScrapeSubcategories started for storeId: " + storeId);

            var categories = await _context.Categories.Where(c => c.StoreId == storeId && c.Depth == 0).ToListAsync();
            foreach (var category in categories)
            {
                var subCategoryUrl = "https://www.ceneo.pl" + category.CategoryUrl + ";0-0.htm";
                await ScrapeCategories(storeId, subCategoryUrl, 1);
            }

            return RedirectToAction("CategoryList", new { storeId });
        }

        public async Task<IActionResult> CategoryList(int storeId)
        {
            var categories = await _context.Categories.Where(c => c.StoreId == storeId).ToListAsync();
            
            return View("~/Views/ManagerPanel/CategoryScraper/CategoryList.cshtml", categories);
        }
    }
}
