// usingi bez zmian
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR; // Dodaj ten using
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs; // Dodaj ten using
using PriceSafari.Models.ManagerViewModels;
using PriceSafari.ScrapersControllers;
using PriceSafari.Services.AllegroServices; // Dodaj ten using

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class AllegroScrapeController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly AllegroUrlGroupingService _groupingService;
        private readonly IHubContext<ScrapingHub> _hubContext; // Wstrzyknij HubContext

        public AllegroScrapeController(PriceSafariContext context, AllegroUrlGroupingService groupingService, IHubContext<ScrapingHub> hubContext)
        {
            _context = context;
            _groupingService = groupingService;
            _hubContext = hubContext; // Przypisz
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var viewModel = new AllegroScrapeViewModel
            {
                PreparedOffers = await _context.AllegroOffersToScrape.OrderByDescending(o => o.AddedDate).ToListAsync(),
                CurrentStatus = AllegroScrapeManager.CurrentStatus,
                ActiveScrapers = AllegroScrapeManager.ActiveScrapers.Values
            };
            return View("~/Views/ManagerPanel/AllegroScrape/Index.cshtml", viewModel);
        }

        // NOWA AKCJA - Start
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartScraping()
        {
            AllegroScrapeManager.CurrentStatus = ScrapingProcessStatus.Running;
            TempData["SuccessMessage"] = "Proces scrapowania ofert został uruchomiony.";
            await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", "Running");
            return RedirectToAction(nameof(Index));
        }

        // NOWA AKCJA - Stop
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StopScraping()
        {
            AllegroScrapeManager.CurrentStatus = ScrapingProcessStatus.Idle;
            TempData["SuccessMessage"] = "Proces scrapowania ofert został zatrzymany.";
            await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", "Idle");
            return RedirectToAction(nameof(Index));
        }

        // Istniejące akcje PrepareUrls i DeleteAll bez zmian...
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