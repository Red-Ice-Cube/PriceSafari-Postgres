using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models;
using Heat_Lead.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Heat_Lead.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class StoreController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public StoreController(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var stores = await _context.Store.ToListAsync();
            var storeViewModels = stores.Select(store => new StoreViewModel
            {
                Id = store.StoreId,
                Name = store.StoreName,
                Code = store.CodeSTO,
                Logo = store.LogoUrl
            }).ToList();

            return View("~/Views/ManagerPanel/Store/Index.cshtml", storeViewModels);
        }

        // GET: Store/Create
        public IActionResult Create()
        {
            return View("~/Views/ManagerPanel/Store/Create.cshtml", new CreateStoreViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("StoreName,CodeSTO,APIurl,APIkey,LogoUrl")] CreateStoreViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                Store store = new Store
                {
                    StoreName = viewModel.StoreName,
                    APIurl = viewModel.APIurl,
                    APIkey = viewModel.APIkey,
                    LogoUrl = viewModel.LogoUrl
                };

                _context.Add(store);
                await _context.SaveChangesAsync();

                return RedirectToAction("Index", "Store");
            }
            return View("~/Views/ManagerPanel/Store/Create.cshtml", viewModel);
        }

        // GET: Store/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var store = await _context.Store.FindAsync(id);
            if (store == null)
            {
                return NotFound();
            }

            EditStoreViewModel viewModel = new EditStoreViewModel
            {
                StoreId = store.StoreId,
                StoreName = store.StoreName,
                APIurl = store.APIurl,
                APIkey = store.APIkey,
                LogoUrl = store.LogoUrl
            };

            return View("~/Views/ManagerPanel/Store/Edit.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("StoreId,StoreName,APIurl,APIkey,LogoUrl")] EditStoreViewModel viewModel)
        {
            if (id != viewModel.StoreId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var store = await _context.Store.FindAsync(id);
                if (store == null)
                {
                    return NotFound();
                }

                store.StoreName = viewModel.StoreName;
                store.APIurl = viewModel.APIurl;
                store.APIkey = viewModel.APIkey;
                store.LogoUrl = viewModel.LogoUrl;

                _context.Update(store);
                await _context.SaveChangesAsync();
                return RedirectToAction("Index", "Store");
            }
            return View("~/Views/ManagerPanel/Store/Edit.cshtml", viewModel);
        }

        // POST: Store/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var store = await _context.Store.FindAsync(id);
            _context.Store.Remove(store);
            await _context.SaveChangesAsync();
            return RedirectToAction("Index", "Store");
        }
    }
}