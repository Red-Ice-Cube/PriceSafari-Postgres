using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.IRepo.Interface;
using Heat_Lead.Models.ManagerViewModels;
using Heat_Lead.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using static Heat_Lead.Models.ManagerViewModels.ManagerCategoryViewModel;

namespace Heat_Lead.Controllers.ManagerControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class CategoryController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public CategoryController(Heat_LeadContext context, UserManager<Heat_LeadUser> userManager, XmlGeneratorService xmlGeneratorService, IApiService apiService)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var categories = await _context.Category
                .Include(c => c.Product)
                .Where(c => !c.IsDeleted)
                .ToListAsync();

            var managerCategoryDetailsViewModel = categories.Select(c => new ManagerCategoryDetailsViewModel
            {
                CategoryId = c.CategoryId,
                CategoryName = c.CategoryName,
                Validation = c.Validation,
                CodeTracking = c.CodeTracking,
                NumberOfProducts = c.Product.Count(p => p.IsActive),
                CommissionPercentage = c.CommissionPercentage
            }).ToList();

            var model = new ManagerCategoryViewModel
            {
                CategoryDetails = managerCategoryDetailsViewModel
            };

            return View("~/Views/ManagerPanel/Category/Index.cshtml", model);
        }

        // GET: Category/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || _context.Category == null)
            {
                return NotFound();
            }

            var category = await _context.Category
                .Include(c => c.Store)
                .Include(c => c.Product)
                .FirstOrDefaultAsync(c => c.CategoryId == id && !c.IsDeleted);

            if (category == null)
            {
                return NotFound();
            }

            var editViewModel = new ManagerCategoryEditViewModel
            {
                CategoryId = category.CategoryId,
                CategoryName = category.CategoryName,
                Validation = category.Validation,
                CodeTracking = category.CodeTracking,
                StoreId = category.StoreId,
                CommissionPercentage = category.CommissionPercentage,
                NumberOfProducts = category.Product.Count(p => p.IsActive)
            };

            ViewBag.Store = new SelectList(await _context.Store.ToListAsync(), "StoreId", "StoreName", category.StoreId);

            return View("~/Views/ManagerPanel/Category/Edit.cshtml", editViewModel);
        }

        // POST: Category/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("CategoryId,CategoryName,Validation,CodeTracking,StoreId,CommissionPercentage")] ManagerCategoryEditViewModel editModel, bool UpdateProductCommissions)
        {
            if (id != editModel.CategoryId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var categoryToUpdate = await _context.Category.Include(c => c.Product).FirstOrDefaultAsync(c => c.CategoryId == id && !c.IsDeleted);
                if (categoryToUpdate == null)
                {
                    return NotFound();
                }

                categoryToUpdate.CategoryName = editModel.CategoryName;
                categoryToUpdate.Validation = editModel.Validation;
                categoryToUpdate.CodeTracking = editModel.CodeTracking;
                categoryToUpdate.StoreId = editModel.StoreId;
                categoryToUpdate.CommissionPercentage = editModel.CommissionPercentage;

                if (UpdateProductCommissions && editModel.CommissionPercentage.HasValue)
                {
                    foreach (var product in categoryToUpdate.Product.Where(p => p.IsActive))
                    {
                        product.AffiliateCommission = product.ProductPrice * (editModel.CommissionPercentage.Value / 100);
                    }
                }

                try
                {
                    _context.Update(categoryToUpdate);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Category.Any(e => e.CategoryId == id && !e.IsDeleted))
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
            ViewBag.Store = new SelectList(await _context.Store.ToListAsync(), "StoreId", "StoreName", editModel.StoreId);
            return View("~/Views/ManagerPanel/Category/Edit.cshtml", editModel);
        }

        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var category = await _context.Category.FindAsync(id);
            if (category != null)
            {
                category.IsDeleted = true;
                _context.Update(category);

                var defaultCategory = await _context.Category.FirstOrDefaultAsync(c => c.CategoryName == "Inne produkty");
                if (defaultCategory != null)
                {
                    var productsToUpdate = _context.Product.Where(p => p.CategoryId == id && p.IsActive).ToList();
                    foreach (var product in productsToUpdate)
                    {
                        product.CategoryId = defaultCategory.CategoryId;
                    }
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
