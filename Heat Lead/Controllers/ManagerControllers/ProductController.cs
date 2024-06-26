using Heat_Lead.Data;
using Heat_Lead.Models;
using Heat_Lead.Models.ManagerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using static Heat_Lead.Models.ManagerViewModels.ManagerProductViewModel;

namespace Heat_Lead.Controllers.ManagerControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class ProductController : Controller
    {
        private readonly Heat_LeadContext _context;

        public ProductController(Heat_LeadContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var products = await _context.Product
                .Include(p => p.Category)
                .Include(p => p.StoreProductIds)
                .Where(p => p.IsActive)
                .ToListAsync();

            var managerProductDetailsViewModel = products.Select(p => new ManagerProductDetailsViewModel
            {
                ProductId = p.ProductId,
                ProductName = p.ProductName,
                ProductIdStores = p.StoreProductIds.Select(s => s.StoreProductId).ToList(), 
                ProductPrice = p.ProductPrice,
                AffiliateCommission = p.AffiliateCommission,
                ProductURL = p.ProductURL,
                ProductImage = p.ProductImage,
                ProductCategory = p.Category?.CategoryName ?? "Kategoria nieustalona"
            }).ToList();

            var model = new ManagerProductViewModel
            {
                ProductDetails = managerProductDetailsViewModel
            };

            return View("~/Views/ManagerPanel/Product/Index.cshtml", model);
        }

        // GET: Product/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null || _context.Product == null)
            {
                return NotFound();
            }

            var product = await _context.Product
                .Include(p => p.Category)
                .Include(p => p.StoreProductIds)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null)
            {
                return NotFound();
            }

            var managerProductDetailsViewModel = new ManagerProductDetailsViewModel
            {
                ProductId = product.ProductId,
                ProductName = product.ProductName,
                ProductIdStores = product.StoreProductIds.Select(s => s.StoreProductId).ToList(),
                ProductPrice = product.ProductPrice,
                AffiliateCommission = product.AffiliateCommission,
                ProductURL = product.ProductURL,
                ProductImage = product.ProductImage,
                ProductCategory = product.Category.CategoryName
            };

            return View("~/Views/ManagerPanel/Product/Details.cshtml", managerProductDetailsViewModel);
        }

        // GET: Product/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Product == null)
            {
                return NotFound();
            }

            var product = await _context.Product
                .Include(p => p.Category)
                .Include(p => p.StoreProductIds)
                .FirstOrDefaultAsync(p => p.ProductId == id);

            if (product == null)
            {
                return NotFound();
            }

            var editViewModel = new ManagerProductEditViewModel
            {
                ProductId = product.ProductId,
                ProductName = product.ProductName,
                ProductIdStores = product.StoreProductIds.Select(s => s.StoreProductId).ToList(),
                ProductPrice = product.ProductPrice,
                AffiliateCommission = product.AffiliateCommission,
                ProductURL = product.ProductURL,
                ProductImage = product.ProductImage,
                CategoryId = product.CategoryId,
            };

            ViewBag.Categories = new SelectList(await _context.Category.ToListAsync(), "CategoryId", "CategoryName", product.CategoryId);

            return View("~/Views/ManagerPanel/Product/Edit.cshtml", editViewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ProductId,ProductName,ProductPrice,AffiliateCommission,ProductURL,ProductImage,CategoryId,ProductIdStores")] ManagerProductEditViewModel editModel)
        {
            if (id != editModel.ProductId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var productToUpdate = await _context.Product
                    .Include(p => p.StoreProductIds)
                    .FirstOrDefaultAsync(p => p.ProductId == id);

                if (productToUpdate == null)
                {
                    return NotFound();
                }

                productToUpdate.ProductName = editModel.ProductName;
                productToUpdate.ProductPrice = editModel.ProductPrice;
                productToUpdate.AffiliateCommission = editModel.AffiliateCommission;
                productToUpdate.ProductURL = editModel.ProductURL;
                productToUpdate.ProductImage = editModel.ProductImage;
                productToUpdate.CategoryId = editModel.CategoryId;

                _context.ProductIdStores.RemoveRange(productToUpdate.StoreProductIds);
                foreach (var storeId in editModel.ProductIdStores)
                {
                    productToUpdate.StoreProductIds.Add(new ProductIdStore { StoreProductId = storeId, ProductId = id });
                }

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Product.Any(e => e.ProductId == id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }

                return RedirectToAction("Index");
            }

            ViewBag.Categories = new SelectList(await _context.Category.ToListAsync(), "CategoryId", "CategoryName", editModel.CategoryId);
            return View("~/Views/ManagerPanel/Product/Edit.cshtml", editModel);
        }

        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var product = await _context.Product.FindAsync(id);
            if (product != null)
            {
                product.IsActive = false;

                var associatedProductIdStores = await _context.ProductIdStores
                    .Where(pis => pis.ProductId == id)
                    .ToListAsync();

                if (associatedProductIdStores.Any())
                {
                    _context.ProductIdStores.RemoveRange(associatedProductIdStores);
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}