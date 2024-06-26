using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using static Heat_Lead.Models.ManagerViewModels.ManagerProductViewModel;

namespace Heat_Lead.Controllers.ManagerControllers
{
    [Authorize(Roles = "Manager, Admin")]
    [Route("ProductAssignment")]
    public class ProductAssignmentController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public ProductAssignmentController(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var stores = _context.Store.ToList();
            ViewBag.Stores = new SelectList(stores, "StoreId", "StoreName");
            ViewBag.Categories = new SelectList(Enumerable.Empty<SelectListItem>(), "CategoryId", "CategoryName");
            return View("~/Views/ManagerPanel/ProductAssignment/Index.cshtml");
        }

        [HttpGet("GetCategories/{storeId}")]
        public IActionResult GetCategoriesByStore(int storeId)
        {
            var categories = _context.Category
                .Where(c => c.StoreId == storeId && !c.IsDeleted)
                .Select(c => new
                {
                    categoryId = c.CategoryId,
                    categoryName = c.CategoryName,
                    isActive = !c.IsDeleted
                })
                .ToList();

            return Json(categories);
        }

        [HttpGet("AddProduct")]
        public IActionResult AddProduct(int storeId, int categoryId)
        {
            ViewBag.StoreId = storeId;
            ViewBag.CategoryId = categoryId;

            var categories = _context.Category.Where(c => c.StoreId == storeId).ToList();
            ViewBag.Categories = new SelectList(categories, "CategoryId", "CategoryName");

            var model = new ManagerProductEditViewModel
            {
                ProductIdStores = new List<string>()
            };

            return View("~/Views/ManagerPanel/ProductAssignment/AddProduct.cshtml", model);
        }

        [HttpPost("AddProduct")]
        public async Task<IActionResult> AddProduct(ManagerProductEditViewModel model)
        {
            if (ModelState.IsValid)
            {
                decimal affiliateCommission = model.AffiliateCommission;

                
                if (affiliateCommission == 0 && model.CategoryId.HasValue)
                {
                    var category = await _context.Category.FindAsync(model.CategoryId.Value);
                    if (category != null && category.CommissionPercentage.HasValue)
                    {
                        affiliateCommission = model.ProductPrice * (category.CommissionPercentage.Value / 100);
                    }
                }

                var product = new Product
                {
                    ProductName = model.ProductName,
                    ProductPrice = model.ProductPrice,
                    AffiliateCommission = affiliateCommission,
                    ProductURL = model.ProductURL,
                    ProductImage = model.ProductImage,
                    CategoryId = model.CategoryId
                };

                _context.Add(product);
                await _context.SaveChangesAsync();

                foreach (var storeId in model.ProductIdStores)
                {
                    var productIdStore = new ProductIdStore
                    {
                        StoreProductId = storeId,
                        ProductId = product.ProductId
                    };
                    _context.ProductIdStores.Add(productIdStore);
                }
                await _context.SaveChangesAsync();

                return RedirectToAction("Index");
            }

            var stores = _context.Store.ToList();
            ViewBag.Stores = new SelectList(stores, "StoreId", "StoreName");
            var categories = _context.Category.Where(c => c.StoreId == model.CategoryId).ToList();
            ViewBag.Categories = new SelectList(categories, "CategoryId", "CategoryName");
            return View("~/Views/ManagerPanel/ProductAssignment/AddProduct.cshtml", model);
        }

        [HttpGet("GetCategoryCommission/{categoryId}")]
        public IActionResult GetCategoryCommission(int categoryId)
        {
            var category = _context.Category
                .Where(c => c.CategoryId == categoryId)
                .Select(c => new
                {
                    commissionPercentage = c.CommissionPercentage
                })
                .FirstOrDefault();

            if (category == null)
            {
                return NotFound();
            }

            return Json(category);
        }



        [HttpGet("AddCategory")]
        public IActionResult AddCategory()
        {
            var stores = _context.Store.ToList();
            ViewBag.Stores = new SelectList(stores, "StoreId", "StoreName");
            return View("~/Views/ManagerPanel/ProductAssignment/AddCategory.cshtml");
        }

        [HttpPost("AddCategory")]
        public async Task<IActionResult> AddCategory(Category category)
        {
            if (ModelState.IsValid)
            {
                _context.Add(category);
                await _context.SaveChangesAsync();
                return RedirectToAction("Index");
            }

            var stores = _context.Store.ToList();
            ViewBag.Stores = new SelectList(stores, "StoreId", "StoreName");
            return View("~/Views/ManagerPanel/ProductAssignment/AddCategory.cshtml", category);
        }
    }
}
