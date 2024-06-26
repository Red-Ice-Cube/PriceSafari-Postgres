using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models;
using Heat_Lead.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Heat_Lead.Controllers
{
    [Authorize(Roles = "Member")]
    public class PromoteController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public PromoteController(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> Index(List<int> selectedCategories)
        {
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            var stores = await _context.Store
                .Include(s => s.Category)
                .ToListAsync();

            var storesWithCategoriesList = stores.Select(store => new StoreWithCategories
            {
                Store = store,
                Categories = store.Category.Where(c => !c.IsDeleted).ToList()
            }).ToList();

            var activeProducts = await _context.Product
                .Include(p => p.Category)
                .ThenInclude(c => c.Store)
                .Where(p => p.IsActive && (!selectedCategories.Any() || selectedCategories.Contains(p.CategoryId.Value)))
                .ToListAsync();

            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                return NotFound("Nie znaleziono ustawień.");
            }

            int cookieLifeTimeDays = settings.TTL;
            var cartProductIds = HttpContext.Session.Get<List<int>>("CartProductIds") ?? new List<int>();

            var viewModel = new PromoteViewModel
            {
                StoresWithCategories = storesWithCategoriesList,
                Products = activeProducts,
                SelectedCategories = selectedCategories,
                CartProductIds = cartProductIds,
                CookieLifeTime = cookieLifeTimeDays
            };

            return View("~/Views/Panel/Promote/Index.cshtml", viewModel);
        }

        public IActionResult GetProductData(int productId)
        {
            var product = _context.Product
                                   .FirstOrDefault(p => p.ProductId == productId);

            if (product == null)
            {
                return NotFound();
            }

            var productData = new
            {
                ProductId = product.ProductId,
                ProductName = product.ProductName,
                ProductImage = product.ProductImage,
            };

            return Json(productData);
        }

        public IActionResult Generate(string ids)
        {
            var idList = new List<int>();
            if (!string.IsNullOrEmpty(ids))
            {
                idList = ids.Split(',').Select(int.Parse).ToList();
            }

            var cartProducts = new List<Product>();
            foreach (var id in idList)
            {
                var product = _context.Product
                                      .Include(p => p.Category)
                                      .FirstOrDefault(p => p.ProductId == id && p.IsActive);
                if (product != null)
                {
                    cartProducts.Add(product);
                }
            }

            var viewModel = new PromoteViewModel
            {
                Products = cartProducts,
                CartProductIds = idList
            };

            return View("~/Views/Panel/Promote/Generate.cshtml", viewModel);
        }
    }
}