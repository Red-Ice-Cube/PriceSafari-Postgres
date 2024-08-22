using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using HtmlAgilityPack;
using System;
using Microsoft.AspNetCore.Authorization;
using System.Net;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class CategoryScraperController : Controller
    {
        private readonly PriceSafariContext _context;

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

            // Wybór XPath na podstawie głębokości
            var categoryNodes = doc.DocumentNode.SelectNodes(depth == 0 ? "//span[@class='cat-nav__title']/a" : "//div[@class='cat-nav__card']/a");

            if (categoryNodes != null && categoryNodes.Count > 0)
            {
                Console.WriteLine("Found " + categoryNodes.Count + " category nodes.");

                foreach (var node in categoryNodes)
                {
                    // Namierzanie nazwy kategorii na podstawie głębokości
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

                        // Kontynuacja skrapowania dla głębokości < 3
                        if (depth < 3)
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


    }
}
