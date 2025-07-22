using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Services.AllegroServices;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class AllegroScrapeController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly AllegroUrlGroupingService _groupingService;

        public AllegroScrapeController(PriceSafariContext context, AllegroUrlGroupingService groupingService)
        {
            _context = context;
            _groupingService = groupingService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var preparedOffers = await _context.AllegroOffersToScrape
                .OrderByDescending(o => o.AddedDate)
                .ToListAsync();


            return View("~/Views/ManagerPanel/AllegroScrape/Index.cshtml", preparedOffers);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrepareUrls()
        {
            var (urlsPrepared, totalProducts) = await _groupingService.GroupAndSaveUrls();
            TempData["SuccessMessage"] = $"Przygotowano {urlsPrepared} unikalnych URL-i z {totalProducts} zebranych produktów.";
            return RedirectToAction(nameof(Index));
        }

        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var rowsAffected = await _context.AllegroOffersToScrape.ExecuteDeleteAsync();
            TempData["SuccessMessage"] = $"Usunięto {rowsAffected} przygotowanych ofert.";
            return RedirectToAction(nameof(Index));
        }
    }
}
