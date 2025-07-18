using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models.ManagerViewModels;
using PriceSafari.ScrapersControllers;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class AllegroGatherController : Controller
    {
        private readonly PriceSafariContext _context;

        public AllegroGatherController(PriceSafariContext context)
        {
            _context = context;
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

            // Przekazujemy cały słownik aktywnych zadań do widoku
            var viewModel = new AllegroGatherViewModel
            {
                ScrapableStores = scrapableStores,
                ScrapedProducts = scrapedProducts,
                // Przekazujemy aktualny stan zadań
                ActiveTasks = AllegroTaskManager.ActiveTasks
            };

            return View("~/Views/ManagerPanel/Allegro/Index.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartScraping(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store != null && !string.IsNullOrEmpty(store.StoreNameAllegro))
            {
                // TryAdd zadziała tylko, jeśli zadanie dla tego sklepu jeszcze nie istnieje.
                // To jest nasza "blokada" przed podwójnym uruchomieniem.
                bool addedSuccessfully = AllegroTaskManager.ActiveTasks
                    .TryAdd(store.StoreNameAllegro, ScrapingStatus.Pending);

                if (addedSuccessfully)
                {
                    TempData["SuccessMessage"] = $"Zlecono zadanie dla sklepu: {store.StoreName}";
                }
                else
                {
                    TempData["ErrorMessage"] = "Zadanie dla tego sklepu jest już w trakcie lub oczekuje na wykonanie.";
                }
            }
            return RedirectToAction(nameof(Index));
        }

        // NOWA AKCJA: Przerwanie zadania
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelScraping(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store != null && !string.IsNullOrEmpty(store.StoreNameAllegro))
            {
                // Jeśli zadanie istnieje, próbujemy zaktualizować jego stan na "Anulowane"
                if (AllegroTaskManager.ActiveTasks.ContainsKey(store.StoreNameAllegro))
                {
                    AllegroTaskManager.ActiveTasks[store.StoreNameAllegro] = ScrapingStatus.Cancelled;
                    TempData["SuccessMessage"] = $"Wysłano sygnał przerwania do zadania dla sklepu: {store.StoreName}";
                }
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAllProducts()
        {
            var rowsAffected = await _context.AllegroProducts.ExecuteDeleteAsync();
            TempData["SuccessMessage"] = $"Usunięto {rowsAffected} produktów.";
            return RedirectToAction(nameof(Index));
        }
    }
}
