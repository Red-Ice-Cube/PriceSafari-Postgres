using HtmlAgilityPack;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ManagerViewModels;
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
        public async Task<IActionResult> Index(int? storeId)
        {
            var viewModel = new CategoryManagementViewModel
            {
                AllStores = await _context.Stores.ToListAsync(),
                CategoriesForSelectedStore = new List<CategoryClass>()
            };

            if (storeId.HasValue)
            {
                viewModel.SelectedStoreId = storeId.Value;
                var selectedStore = await _context.Stores.FindAsync(storeId.Value);
                if (selectedStore != null)
                {
                    viewModel.SelectedStoreName = selectedStore.StoreName;
                    viewModel.CategoriesForSelectedStore = await _context.Categories
                        .Where(c => c.StoreId == storeId.Value)
                        .OrderBy(c => c.Depth)
                        .ThenBy(c => c.CategoryName)
                        .ToListAsync();
                }
            }

            return View("~/Views/ManagerPanel/CategoryScraper/Index.cshtml", viewModel);
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
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound();
            }

            var storeProfile = store.StoreProfile;
            var baseUrl = $"https://www.ceneo.pl/;{storeProfile}-0v.htm";

            await ScrapeCategories(storeId, baseUrl, 0);

            return RedirectToAction("Index", new { storeId = storeId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] CreateCategoryDto dto)
        {

            if (!ModelState.IsValid)
            {

                return BadRequest(ModelState);
            }

            var newCategory = new CategoryClass
            {
                StoreId = dto.StoreId.Value,
                CategoryName = dto.CategoryName,
                CategoryUrl = dto.CategoryUrl,
                Depth = dto.Depth
            };

            _context.Categories.Add(newCategory);
            await _context.SaveChangesAsync();

            return Ok(newCategory);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var category = await _context.Categories.FindAsync(id);
            if (category == null)
            {
                return NotFound();
            }
            return View("~/Views/ManagerPanel/CategoryScraper/Edit.cshtml", category);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("CategoryId,StoreId,CategoryName,CategoryUrl,Depth")] CategoryClass formData)
        {

            if (id != formData.CategoryId)
            {
                return NotFound("Błąd: Niezgodność identyfikatorów.");
            }

            if (ModelState.IsValid)
            {
                try
                {

                    var categoryToUpdate = await _context.Categories.FindAsync(id);

                    if (categoryToUpdate == null)
                    {
                        return NotFound("Błąd: Kategoria nie istnieje w bazie danych.");
                    }

                    categoryToUpdate.CategoryName = formData.CategoryName;
                    categoryToUpdate.CategoryUrl = formData.CategoryUrl;
                    categoryToUpdate.Depth = formData.Depth;

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {

                    if (!_context.Categories.Any(e => e.CategoryId == formData.CategoryId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }

                return RedirectToAction(nameof(Index), new { storeId = formData.StoreId });
            }

            return View("~/Views/ManagerPanel/CategoryScraper/Edit.cshtml", formData);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _context.Categories.FindAsync(id);
            if (category == null)
            {
                return NotFound();
            }

            var storeId = category.StoreId;
            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { storeId = storeId });
        }

    }
}