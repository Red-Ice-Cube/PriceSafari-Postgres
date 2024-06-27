using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using HtmlAgilityPack;

namespace PriceTracker.Controllers
{
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
            return View(stores);
        }

        [HttpPost]
        public async Task<IActionResult> ScrapeCategories(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            var storeProfile = store.StoreProfile;
            var baseUrl = $"https://www.ceneo.pl/;0192;{storeProfile}-0v.htm";

            await ScrapeSubcategories(storeId, baseUrl);

            return RedirectToAction("CategoryList", new { storeId = storeId });
        }

        private async Task ScrapeSubcategories(int storeId, string categoryUrl)
        {
            var web = new HtmlWeb();
            HtmlDocument doc;

            try
            {
                doc = web.Load(categoryUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading page {categoryUrl}: {ex.Message}");
                return;
            }

            var subcategoryNodes = doc.DocumentNode.SelectNodes("//div[@class='cat-nav__card']//a[@class='nav-item js_categories-link']");
            if (subcategoryNodes != null && subcategoryNodes.Count > 0)
            {
                foreach (var node in subcategoryNodes)
                {
                    var subcategoryNameNode = node.SelectSingleNode(".//div[@class='nav-item__name']");
                    if (subcategoryNameNode != null)
                    {
                        var subcategoryName = subcategoryNameNode.InnerText.Trim();
                        var subcategoryUrl = node.GetAttributeValue("href", "").Split(';')[0];

                        var category = new CategoryClass
                        {
                            StoreId = storeId,
                            CategoryName = subcategoryName,
                            CategoryUrl = subcategoryUrl
                        };

                        _context.Categories.Add(category);
                        Console.WriteLine($"Scraped Subcategory - Name: {subcategoryName}, URL: {subcategoryUrl}");

                        
                        await ScrapeSubcategories(storeId, "https://www.ceneo.pl" + node.GetAttributeValue("href", ""));
                    }
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task<IActionResult> CategoryList(int storeId)
        {
            var categories = await _context.Categories.Where(c => c.StoreId == storeId).ToListAsync();
            return View(categories);
        }
    }
}
