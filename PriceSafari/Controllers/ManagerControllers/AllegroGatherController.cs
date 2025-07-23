using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models.ManagerViewModels;
using PriceSafari.ScrapersControllers;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class AllegroGatherController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;

        public AllegroGatherController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
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
                ActiveTasks = AllegroTaskManager.ActiveTasks,
                ActiveScrapers = AllegroTaskManager.ActiveScrapers
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
                var newTask = new ScrapingTaskState { Status = ScrapingStatus.Pending };
                if (AllegroTaskManager.ActiveTasks.TryAdd(store.StoreNameAllegro, newTask))
                {
                    TempData["SuccessMessage"] = $"Zlecono zadanie dla sklepu: {store.StoreName}";

                    await _hubContext.Clients.All.SendAsync("UpdateTaskProgress", store.StoreNameAllegro, newTask);
                }
                else
                {
                    TempData["ErrorMessage"] = "Zadanie dla tego sklepu jest już w trakcie lub oczekuje na wykonanie.";
                }
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelScraping(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store != null && !string.IsNullOrEmpty(store.StoreNameAllegro))
            {
                if (AllegroTaskManager.ActiveTasks.TryGetValue(store.StoreNameAllegro, out var taskState))
                {
                    taskState.Status = ScrapingStatus.Cancelled;
                    taskState.LastProgressMessage = "Anulowane przez użytkownika.";
                    TempData["SuccessMessage"] = $"Wysłano sygnał przerwania do zadania dla sklepu: {store.StoreName}";

                    await _hubContext.Clients.All.SendAsync("UpdateTaskProgress", store.StoreNameAllegro, taskState);
                }
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAllProducts()
        {
            // Używamy transakcji, aby obie operacje masowe wykonały się razem albo żadna
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Krok 1: Usuń wszystkie rekordy podrzędne (historię cen)
                // To polecenie usunie wszystkie wiersze z tabeli AllegroPriceHistories
                var priceHistoryRows = await _context.AllegroPriceHistories.ExecuteDeleteAsync();

                // Krok 2: Usuń wszystkie rekordy nadrzędne (produkty)
                // Teraz, gdy nie ma już powiązań, to polecenie zadziała
                var productRows = await _context.AllegroProducts.ExecuteDeleteAsync();

                // Zatwierdź transakcję, jeśli wszystko poszło dobrze
                await transaction.CommitAsync();

                TempData["SuccessMessage"] = $"Usunięto {productRows} produktów oraz {priceHistoryRows} powiązanych wpisów historii cen.";
            }
            catch (Exception ex)
            {
                // W razie błędu cofnij wszystkie zmiany
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = $"Wystąpił błąd podczas usuwania danych: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}