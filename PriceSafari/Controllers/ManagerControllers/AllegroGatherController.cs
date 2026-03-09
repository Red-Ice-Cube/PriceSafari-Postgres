using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models.ManagerViewModels;
using PriceSafari.ScrapersControllers;
using PriceSafari.Services.AllegroServices;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class AllegroGatherController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly AllegroGatherService _gatherService;

        public AllegroGatherController(PriceSafariContext context, AllegroGatherService gatherService)
        {
            _context = context;
            _gatherService = gatherService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var scrapableStores = await _context.Stores
                .Where(s => !string.IsNullOrEmpty(s.StoreNameAllegro))
                .OrderBy(s => s.StoreName)
                .ToListAsync();

            var scrapedProducts = await _context.AllegroProducts
                .Include(p => p.Store)
                .OrderByDescending(p => p.AddedDate)
                .ToListAsync();

            var viewModel = new AllegroGatherViewModel
            {
                ScrapableStores = scrapableStores,
                ScrapedProducts = scrapedProducts,
                ActiveTasks = AllegroGatherManager.ActiveTasks,
                ActiveScrapers = AllegroGatherManager.ActiveScrapers
            };

            return View("~/Views/ManagerPanel/Allegro/Index.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartScraping(int storeId)
        {
            var (success, message) = await _gatherService.StartScrapingForStoreAsync(storeId);

            if (success)
                TempData["SuccessMessage"] = message;
            else
                TempData["ErrorMessage"] = message;

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelScraping(int storeId)
        {
            var (success, message) = await _gatherService.CancelScrapingForStoreAsync(storeId);

            if (success)
                TempData["SuccessMessage"] = message;
            else
                TempData["ErrorMessage"] = message;

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAllProducts()
        {
            var (success, message) = await _gatherService.DeleteAllProductsAsync();

            if (success)
                TempData["SuccessMessage"] = message;
            else
                TempData["ErrorMessage"] = message;

            return RedirectToAction(nameof(Index));
        }
    }
}