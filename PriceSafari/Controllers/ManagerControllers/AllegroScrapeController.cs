//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using PriceSafari.Models.ManagerViewModels;
//using PriceSafari.ScrapersControllers;
//using PriceSafari.Services.AllegroServices;

//namespace PriceSafari.Controllers.ManagerControllers
//{
//    [Authorize(Roles = "Admin")]
//    public class AllegroScrapeController : Controller
//    {
//        private readonly PriceSafariContext _context;
//        private readonly AllegroUrlGroupingService _groupingService;
//        private readonly IHubContext<ScrapingHub> _hubContext;
//        private readonly AllegroProcessingService _processingService;
//        private readonly AllegroApiBotService _apiBotService;
//        public AllegroScrapeController(PriceSafariContext context,
//                                 AllegroUrlGroupingService groupingService,
//                                 IHubContext<ScrapingHub> hubContext,
//                                 AllegroProcessingService processingService,
//                                 AllegroApiBotService apiBotService)

//        {
//            _context = context;
//            _groupingService = groupingService;
//            _hubContext = hubContext;
//            _processingService = processingService;
//            _apiBotService = apiBotService;

//        }

//        [HttpGet]
//        public async Task<IActionResult> Index()
//        {
//            var preparedOffers = await _context.AllegroOffersToScrape.ToListAsync();

//            var stats = new ScrapingStatsViewModel
//            {
//                TotalUrls = preparedOffers.Count,
//                ScrapedUrls = preparedOffers.Count(o => o.IsScraped),
//                RejectedUrls = preparedOffers.Count(o => o.IsRejected),
//                TotalPricesCollected = preparedOffers.Sum(o => o.CollectedPricesCount)
//            };

//            var viewModel = new AllegroScrapeViewModel
//            {
//                PreparedOffers = preparedOffers.OrderBy(o => o.Id).ToList(),
//                CurrentStatus = AllegroScrapeManager.CurrentStatus,
//                ActiveScrapers = AllegroScrapeManager.ActiveScrapers.Values,
//                Stats = stats
//            };
//            return View("~/Views/ManagerPanel/AllegroScrape/Index.cshtml", viewModel);
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> StartScraping()
//        {
//            var anyActiveScrapers = AllegroScrapeManager.ActiveScrapers.Values.Any(s => s.Status != ScraperLiveStatus.Offline);

//            if (!anyActiveScrapers)
//            {
//                TempData["ErrorMessage"] = "Nie można uruchomić procesu. Żaden scraper nie jest aktywny (online).";
//                return RedirectToAction(nameof(Index));
//            }

//            var orphanedTasks = await _context.AllegroOffersToScrape
//                .Where(o => o.IsProcessing)
//                .ToListAsync();

//            if (orphanedTasks.Any())
//            {

//                foreach (var task in orphanedTasks)
//                {
//                    task.IsProcessing = false;
//                }
//                await _context.SaveChangesAsync();
//                TempData["InfoMessage"] = $"Zresetowano stan dla {orphanedTasks.Count} zawieszonych zadań.";
//            }

//            AllegroScrapeManager.CurrentStatus = ScrapingProcessStatus.Running;
//            AllegroScrapeManager.ScrapingStartTime = DateTime.UtcNow;
//            TempData["SuccessMessage"] = "Proces scrapowania ofert został zatrzymany.";
//            await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", new
//            {
//                status = "Idle",
//                startTime = AllegroScrapeManager.ScrapingStartTime,
//                endTime = AllegroScrapeManager.ScrapingEndTime
//            });
//            return RedirectToAction(nameof(Index));
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> StopScraping()
//        {
//            AllegroScrapeManager.CurrentStatus = ScrapingProcessStatus.Idle;
//            AllegroScrapeManager.ScrapingStartTime = null;

//            TempData["SuccessMessage"] = "Proces scrapowania ofert został zatrzymany.";
//            await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", new { status = "Idle" });
//            return RedirectToAction(nameof(Index));
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> TriggerApiProcessing()
//        {

//            await _apiBotService.ProcessOffersForActiveStoresAsync();

//            TempData["SuccessMessage"] = "Uruchomiono proces pobierania danych z API Allegro dla aktywnych sklepów. Wyniki sprawdź w logach.";

//            return RedirectToAction(nameof(Index));
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> ClearApiData()
//        {

//            var offers = await _context.AllegroOffersToScrape
//                .Where(o => o.IsApiProcessed == true || o.ApiAllegroPrice != null)
//                .ToListAsync();

//            foreach (var offer in offers)
//            {
//                offer.IsApiProcessed = null;
//                offer.ApiAllegroPrice = null;
//                offer.ApiAllegroPriceFromUser = null;
//                offer.ApiAllegroCommission = null;
//                offer.AnyPromoActive = null;
//                offer.IsSubsidyActive = null;
//                offer.AllegroEan = null;

//            }

//            await _context.SaveChangesAsync();
//            return RedirectToAction(nameof(Index));
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> PrepareUrls()
//        {

//            var activeStoreIds = await _context.Stores
//                .Where(s => s.OnAllegro && s.RemainingDays > 0)
//                .Select(s => s.StoreId)
//                .ToListAsync();

//            if (!activeStoreIds.Any())
//            {
//                TempData["ErrorMessage"] = "Nie znaleziono żadnych aktywnych sklepów z włączoną integracją Allegro.";
//                return RedirectToAction(nameof(Index));
//            }

//            var (urlsPrepared, totalProducts, processedStores) = await _groupingService.GroupAndSaveUrls(activeStoreIds);

//            if (urlsPrepared > 0)
//            {
//                var storeNames = string.Join(", ", processedStores);
//                TempData["SuccessMessage"] = $"Sukces! Przygotowano {urlsPrepared} unikalnych URL-i (z {totalProducts} produktów) dla sklepów: {storeNames}.";
//            }
//            else
//            {
//                TempData["InfoMessage"] = "Operacja zakończona, ale nie znaleziono nowych URL-i do przygotowania.";
//            }

//            return RedirectToAction(nameof(Index));
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> DeleteAll()
//        {
//            var rowsAffected = await _context.AllegroOffersToScrape.ExecuteDeleteAsync();
//            TempData["SuccessMessage"] = $"Usunięto {rowsAffected} przygotowanych ofert.";
//            return RedirectToAction(nameof(Index));
//        }

//        [HttpGet]
//        public async Task<IActionResult> Process(int storeId)
//        {
//            var store = await _context.Stores.FindAsync(storeId);
//            if (store == null)
//            {
//                return NotFound();
//            }

//            var relevantProductIds = await _context.AllegroProducts
//                .Where(p => p.StoreId == storeId)
//                .Select(p => p.AllegroProductId)
//                .ToListAsync();

//            var scrapedOffersFromDb = await _context.AllegroOffersToScrape
//                .Where(o => o.IsScraped)
//                .ToListAsync();

//            var offersToProcessCount = scrapedOffersFromDb
//                .Count(o => o.AllegroProductIds.Any(pId => relevantProductIds.Contains(pId)));

//            var allScrapedOffersFromDb = await _context.AllegroScrapedOffers
//                .Include(so => so.AllegroOfferToScrape)
//                .Where(so => so.AllegroOfferToScrape.IsScraped)
//                .ToListAsync();

//            var scrapedOffersCount = allScrapedOffersFromDb
//                .Count(so => so.AllegroOfferToScrape.AllegroProductIds.Any(pId => relevantProductIds.Contains(pId)));

//            ViewBag.Store = store;
//            ViewBag.OffersToProcessCount = offersToProcessCount;
//            ViewBag.ScrapedOffersCount = scrapedOffersCount;

//            return View("~/Views/ManagerPanel/AllegroScrape/Process.cshtml");
//        }

//        [HttpPost]
//        [ValidateAntiForgeryToken]
//        public async Task<IActionResult> ProcessData(int storeId)
//        {
//            var (processedUrls, savedOffers) = await _processingService.ProcessScrapedDataForStoreAsync(storeId);

//            TempData["SuccessMessage"] = $"Przetworzono dane dla sklepu '{_context.Stores.Find(storeId)?.StoreName}'. Zapisano {savedOffers} czystych ofert z {processedUrls} unikalnych URL-i.";

//            return RedirectToAction("Index", "Store");
//        }

//        public class ScrapingStatsViewModel
//        {
//            public int TotalUrls { get; set; }
//            public int ScrapedUrls { get; set; }
//            public int RejectedUrls { get; set; }
//            public int TotalPricesCollected { get; set; }
//        }
//    }
//}




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
        private readonly AllegroApiBotService _apiBotService;
        private readonly AllegroScrapingService _scrapingService;

        public AllegroScrapeController(
            PriceSafariContext context,
            AllegroUrlGroupingService groupingService,
            IHubContext<ScrapingHub> hubContext,
            AllegroProcessingService processingService,
            AllegroApiBotService apiBotService,
            AllegroScrapingService scrapingService)
        {
            _context = context;
            _groupingService = groupingService;
            _hubContext = hubContext;
            _processingService = processingService;
            _apiBotService = apiBotService;
            _scrapingService = scrapingService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var preparedOffers = await _context.AllegroOffersToScrape.ToListAsync();

            // Pobierz statystyki przez serwis
            var stats = await _scrapingService.GetCurrentStatsAsync();

            // Pobierz szczegóły scraperów
            var scrapersDetails = _scrapingService.GetScrapersDetails();

            // Pobierz ostatnie logi
            var recentLogs = AllegroScrapeManager.GetRecentLogs(50);

            var viewModel = new AllegroScrapeViewModel
            {
                PreparedOffers = preparedOffers.OrderBy(o => o.Id).ToList(),
                CurrentStatus = AllegroScrapeManager.CurrentStatus,
                ActiveScrapers = AllegroScrapeManager.ActiveScrapers.Values,
                ScrapersDetails = scrapersDetails,
                RecentLogs = recentLogs,
                Stats = new ScrapingStatsViewModel
                {
                    TotalUrls = stats.TotalUrls,
                    ScrapedUrls = stats.ScrapedUrls,
                    RejectedUrls = stats.RejectedUrls,
                    ProcessingUrls = stats.ProcessingUrls,
                    TotalPricesCollected = stats.TotalPricesCollected
                },
                DashboardSummary = AllegroScrapeManager.GetDashboardSummary()
            };

            return View("~/Views/ManagerPanel/AllegroScrape/Index.cshtml", viewModel);
        }

        // ===== KONTROLA PROCESU =====

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartScraping()
        {
            var (success, message, totalUrls) = await _scrapingService.StartScrapingProcessAsync();

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StopScraping()
        {
            var (success, message) = await _scrapingService.StopScrapingProcessAsync();

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Index));
        }

        // ===== KONTROLA INDYWIDUALNYCH SCRAPERÓW =====

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PauseScraper(string scraperName)
        {
            var (success, message) = await _scrapingService.PauseScraperAsync(scraperName);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResumeScraper(string scraperName)
        {
            var (success, message) = await _scrapingService.ResumeScraperAsync(scraperName);

            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction(nameof(Index));
        }

        // ===== AJAX ENDPOINTS =====

        [HttpGet]
        public IActionResult GetScrapersStatus()
        {
            var scrapersDetails = _scrapingService.GetScrapersDetails();
            return Json(scrapersDetails);
        }

        [HttpGet]
        public IActionResult GetRecentLogs(int count = 50)
        {
            var logs = AllegroScrapeManager.GetRecentLogs(count);
            return Json(logs);
        }

        [HttpGet]
        public IActionResult GetDashboardSummary()
        {
            return Json(AllegroScrapeManager.GetDashboardSummary());
        }

        [HttpGet]
        public async Task<IActionResult> GetStats()
        {
            var stats = await _scrapingService.GetCurrentStatsAsync();
            return Json(stats);
        }

        // ===== POZOSTAŁE AKCJE (bez zmian) =====

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TriggerApiProcessing()
        {
            await _apiBotService.ProcessOffersForActiveStoresAsync();
            TempData["SuccessMessage"] = "Uruchomiono proces pobierania danych z API Allegro dla aktywnych sklepów.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearApiData()
        {
            var offers = await _context.AllegroOffersToScrape
                .Where(o => o.IsApiProcessed == true || o.ApiAllegroPrice != null)
                .ToListAsync();

            foreach (var offer in offers)
            {
                offer.IsApiProcessed = null;
                offer.ApiAllegroPrice = null;
                offer.ApiAllegroPriceFromUser = null;
                offer.ApiAllegroCommission = null;
                offer.AnyPromoActive = null;
                offer.IsSubsidyActive = null;
                offer.AllegroEan = null;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Wyczyszczono dane API dla {offers.Count} ofert.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PrepareUrls()
        {
            var activeStoreIds = await _context.Stores
                .Where(s => s.OnAllegro && s.RemainingDays > 0)
                .Select(s => s.StoreId)
                .ToListAsync();

            if (!activeStoreIds.Any())
            {
                TempData["ErrorMessage"] = "Nie znaleziono żadnych aktywnych sklepów z włączoną integracją Allegro.";
                return RedirectToAction(nameof(Index));
            }

            var (urlsPrepared, totalProducts, processedStores) = await _groupingService.GroupAndSaveUrls(activeStoreIds);

            if (urlsPrepared > 0)
            {
                var storeNames = string.Join(", ", processedStores);
                TempData["SuccessMessage"] = $"Sukces! Przygotowano {urlsPrepared} unikalnych URL-i (z {totalProducts} produktów) dla sklepów: {storeNames}.";
            }
            else
            {
                TempData["InfoMessage"] = "Operacja zakończona, ale nie znaleziono nowych URL-i do przygotowania.";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAll()
        {
            var rowsAffected = await _context.AllegroOffersToScrape.ExecuteDeleteAsync();

            // Wyczyść też stan managera
            AllegroScrapeManager.AssignedBatches.Clear();

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

    // ===== ViewModels =====

    public class ScrapingStatsViewModel
    {
        public int TotalUrls { get; set; }
        public int ScrapedUrls { get; set; }
        public int RejectedUrls { get; set; }
        public int ProcessingUrls { get; set; }
        public int TotalPricesCollected { get; set; }
    }
}
