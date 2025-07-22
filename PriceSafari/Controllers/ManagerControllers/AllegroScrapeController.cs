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
        private readonly AllegroProcessingService _processingService;

        public AllegroScrapeController(PriceSafariContext context,
                                       AllegroUrlGroupingService groupingService,
                                       IHubContext<ScrapingHub> hubContext,
                                       AllegroProcessingService processingService)
        {
            _context = context;
            _groupingService = groupingService;
            _hubContext = hubContext; // Przypisz
            _processingService = processingService;
        }

        // Wewnątrz klasy AllegroScrapeController

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var viewModel = new AllegroScrapeViewModel
            {
                // --- ZMIANA TUTAJ ---
                PreparedOffers = await _context.AllegroOffersToScrape
                    .OrderBy(o => o.Id) // Sortowanie rosnące po ID

                    .ToListAsync(),
                // --- KONIEC ZMIANY ---

                CurrentStatus = AllegroScrapeManager.CurrentStatus,
                ActiveScrapers = AllegroScrapeManager.ActiveScrapers.Values
            };
            return View("~/Views/ManagerPanel/AllegroScrape/Index.cshtml", viewModel);
        }

        // ... reszta kodu kontrolera bez zmian

        // Wewnątrz klasy AllegroScrapeController

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

            // --- NOWA, KLUCZOWA LOGIKA ---
            // 1. Znajdź wszystkie zadania, które mogły zostać "zawieszone" w stanie przetwarzania.
            var orphanedTasks = await _context.AllegroOffersToScrape
                .Where(o => o.IsProcessing)
                .ToListAsync();

            if (orphanedTasks.Any())
            {
                // 2. Zresetuj ich status, aby mogły być ponownie pobrane.
                foreach (var task in orphanedTasks)
                {
                    task.IsProcessing = false;
                }
                await _context.SaveChangesAsync();
                TempData["InfoMessage"] = $"Zresetowano stan dla {orphanedTasks.Count} zawieszonych zadań.";
            }
            // --- KONIEC NOWEJ LOGIKI ---

            // 3. Uruchom proces
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





        [HttpGet]
        public async Task<IActionResult> Process(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound();
            }

            // Policz, ile "surowych" ofert czeka na przetworzenie dla produktów tego sklepu
            var relevantProductIds = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId)
                .Select(p => p.AllegroProductId)
                .ToListAsync();

            // --- ZMIANA #1 ---
            // Pobieramy szerszy zbiór danych z bazy...
            var scrapedOffersFromDb = await _context.AllegroOffersToScrape
                .Where(o => o.IsScraped) // 1. Prosty filtr, który SQL potrafi przetłumaczyć
                .ToListAsync();         // 2. Pobieramy dane do pamięci aplikacji

            // ...i dopiero teraz wykonujemy skomplikowane filtrowanie w pamięci
            var offersToProcessCount = scrapedOffersFromDb
                .Count(o => o.AllegroProductIds.Any(pId => relevantProductIds.Contains(pId))); // 3. Złożony filtr na liście w C#

            // --- ZMIANA #2 ---
            // Ta sama logika dla drugiej tabeli
            var allScrapedOffersFromDb = await _context.AllegroScrapedOffers
                .Include(so => so.AllegroOfferToScrape)
                .Where(so => so.AllegroOfferToScrape.IsScraped) // 1. Prosty filtr w SQL
                .ToListAsync(); // 2. Pobierz dane do pamięci

            var scrapedOffersCount = allScrapedOffersFromDb
                .Count(so => so.AllegroOfferToScrape.AllegroProductIds.Any(pId => relevantProductIds.Contains(pId))); // 3. Złożony filtr w pamięci

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

            // Wracamy do widoku listy sklepów po przetworzeniu
            return RedirectToAction("Index", "Store");
        }
    }
}