using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models.ManagerViewModels;
using PriceSafari.ScrapersControllers;
using PriceSafari.Services.AllegroServices;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class AllegroScrapeController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly AllegroUrlGroupingService _groupingService;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly AllegroProcessingService _processingService;

        public AllegroScrapeController(PriceSafariContext context,
                                       AllegroUrlGroupingService groupingService,
                                       IHubContext<ScrapingHub> hubContext,
                                       AllegroProcessingService processingService)
        {
            _context = context;
            _groupingService = groupingService;
            _hubContext = hubContext;
            _processingService = processingService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var viewModel = new AllegroScrapeViewModel
            {

                PreparedOffers = await _context.AllegroOffersToScrape
                    .OrderBy(o => o.Id)

                    .ToListAsync(),

                CurrentStatus = AllegroScrapeManager.CurrentStatus,
                ActiveScrapers = AllegroScrapeManager.ActiveScrapers.Values
            };
            return View("~/Views/ManagerPanel/AllegroScrape/Index.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartScraping()
        {
            var anyActiveScrapers = AllegroScrapeManager.ActiveScrapers.Values.Any(s => s.Status != ScraperLiveStatus.Offline);

            if (!anyActiveScrapers)
            {
                TempData["ErrorMessage"] = "Nie można uruchomić procesu. Żaden scraper nie jest aktywny (online).";
                return RedirectToAction(nameof(Index));
            }

            var orphanedTasks = await _context.AllegroOffersToScrape
                .Where(o => o.IsProcessing)
                .ToListAsync();

            if (orphanedTasks.Any())
            {

                foreach (var task in orphanedTasks)
                {
                    task.IsProcessing = false;
                }
                await _context.SaveChangesAsync();
                TempData["InfoMessage"] = $"Zresetowano stan dla {orphanedTasks.Count} zawieszonych zadań.";
            }

            AllegroScrapeManager.CurrentStatus = ScrapingProcessStatus.Running;
            TempData["SuccessMessage"] = "Proces scrapowania ofert został uruchomiony.";
            await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", new { status = "Running" });

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StopScraping()
        {
            AllegroScrapeManager.CurrentStatus = ScrapingProcessStatus.Idle;
            TempData["SuccessMessage"] = "Proces scrapowania ofert został zatrzymany.";
            await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", new { status = "Idle" });
            return RedirectToAction(nameof(Index));
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

        [HttpGet]
        public async Task<IActionResult> Process(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound();
            }

            var relevantProductIds = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId)
                .Select(p => p.AllegroProductId)
                .ToListAsync();

            var scrapedOffersFromDb = await _context.AllegroOffersToScrape
                .Where(o => o.IsScraped)
                .ToListAsync();

            var offersToProcessCount = scrapedOffersFromDb
                .Count(o => o.AllegroProductIds.Any(pId => relevantProductIds.Contains(pId)));

            var allScrapedOffersFromDb = await _context.AllegroScrapedOffers
                .Include(so => so.AllegroOfferToScrape)
                .Where(so => so.AllegroOfferToScrape.IsScraped)
                .ToListAsync();

            var scrapedOffersCount = allScrapedOffersFromDb
                .Count(so => so.AllegroOfferToScrape.AllegroProductIds.Any(pId => relevantProductIds.Contains(pId)));

            ViewBag.Store = store;
            ViewBag.OffersToProcessCount = offersToProcessCount;
            ViewBag.ScrapedOffersCount = scrapedOffersCount;

            return View("~/Views/ManagerPanel/AllegroScrape/Process.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessData(int storeId)
        {
            var (processedUrls, savedOffers) = await _processingService.ProcessScrapedDataForStoreAsync(storeId);

            TempData["SuccessMessage"] = $"Przetworzono dane dla sklepu '{_context.Stores.Find(storeId)?.StoreName}'. Zapisano {savedOffers} czystych ofert z {processedUrls} unikalnych URL-i.";

            return RedirectToAction("Index", "Store");
        }
    }
}