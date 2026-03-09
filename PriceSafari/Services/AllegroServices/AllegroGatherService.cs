using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.ScrapersControllers;

namespace PriceSafari.Services.AllegroServices
{
    public class AllegroGatherService
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<AllegroGatherService> _logger;

        public AllegroGatherService(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            ILogger<AllegroGatherService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        // =====================================================================
        // REZULTAT DLA SCHEDULERA
        // =====================================================================

        public class GatherResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public int StoresQueued { get; set; }
            public int StoresCompleted { get; set; }
            public int StoresCancelled { get; set; }
            public List<string> Details { get; set; } = new();
        }

        // =====================================================================
        // ZLECANIE SCRAPOWANIA DLA JEDNEGO SKLEPU (kontroler)
        // =====================================================================

        public async Task<(bool Success, string Message)> StartScrapingForStoreAsync(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null || string.IsNullOrEmpty(store.StoreNameAllegro))
            {
                return (false, "Sklep nie istnieje lub nie ma przypisanej nazwy Allegro.");
            }

            var newTask = new ScrapingTaskState { Status = ScrapingStatus.Pending };
            if (AllegroGatherManager.ActiveTasks.TryAdd(store.StoreNameAllegro, newTask))
            {
                await _hubContext.Clients.All.SendAsync("UpdateTaskProgress", store.StoreNameAllegro, newTask);
                _logger.LogInformation("Zlecono zadanie zbierania ofert dla sklepu '{StoreName}' (Allegro: {AllegroName}).",
                    store.StoreName, store.StoreNameAllegro);
                return (true, $"Zlecono zadanie dla sklepu: {store.StoreName}");
            }

            return (false, "Zadanie dla tego sklepu jest już w trakcie lub oczekuje na wykonanie.");
        }

        // =====================================================================
        // ZLECANIE SCRAPOWANIA DLA WIELU SKLEPÓW + OCZEKIWANIE (scheduler)
        // =====================================================================

        /// <summary>
        /// Zleca zbieranie ofert dla wielu sklepów i czeka na zakończenie wszystkich.
        /// Zadanie jest "zakończone" gdy scraper wywoła finish-task (co usuwa klucz z ActiveTasks)
        /// lub gdy status zmieni się na Cancelled (i scraper potwierdzi acknowledge-cancel → też TryRemove).
        /// </summary>
        public async Task<GatherResult> StartAndWaitForStoresAsync(List<int> storeIds, CancellationToken ct)
        {
            var result = new GatherResult();

            // 1. Pobierz sklepy z bazy
            var stores = await _context.Stores
                .Where(s => storeIds.Contains(s.StoreId) && !string.IsNullOrEmpty(s.StoreNameAllegro))
                .ToListAsync(ct);

            if (!stores.Any())
            {
                result.Message = "Brak sklepów z przypisaną nazwą Allegro.";
                return result;
            }

            // 2. Zlecaj zadania
            var queuedAllegroNames = new List<string>();

            foreach (var store in stores)
            {
                var newTask = new ScrapingTaskState { Status = ScrapingStatus.Pending };
                if (AllegroGatherManager.ActiveTasks.TryAdd(store.StoreNameAllegro, newTask))
                {
                    queuedAllegroNames.Add(store.StoreNameAllegro);
                    await _hubContext.Clients.All.SendAsync("UpdateTaskProgress", store.StoreNameAllegro, newTask, ct);
                    _logger.LogInformation("[GATHER] Zlecono: {AllegroName}", store.StoreNameAllegro);
                }
                else
                {
                    _logger.LogWarning("[GATHER] Zadanie już istnieje dla: {AllegroName}. Pomijam.", store.StoreNameAllegro);
                    result.Details.Add($"{store.StoreNameAllegro}: już w kolejce/trakcie");
                }
            }

            result.StoresQueued = queuedAllegroNames.Count;

            if (queuedAllegroNames.Count == 0)
            {
                result.Message = "Wszystkie sklepy mają już aktywne zadania.";
                return result;
            }

            // 3. Czekaj na zakończenie wszystkich zleconych zadań
            // Scraper po zakończeniu wywołuje finish-task → TryRemove z ActiveTasks
            // Więc zadanie jest "gotowe" gdy klucz ZNIKNIE z dictionary
            _logger.LogInformation("[GATHER] Oczekiwanie na zakończenie {Count} zadań...", queuedAllegroNames.Count);

            while (!ct.IsCancellationRequested)
            {
                int stillActive = 0;

                foreach (var allegroName in queuedAllegroNames)
                {
                    if (AllegroGatherManager.ActiveTasks.TryGetValue(allegroName, out var taskState))
                    {
                        // Klucz wciąż istnieje
                        if (taskState.Status == ScrapingStatus.Pending ||
                            taskState.Status == ScrapingStatus.Running)
                        {
                            stillActive++;
                        }
                        // Cancelled — scraper niedługo wywołuje acknowledge-cancel → usunie klucz
                        // Ale liczymy jako jeszcze aktywne, bo klucz istnieje
                        else if (taskState.Status == ScrapingStatus.Cancelled)
                        {
                            stillActive++; // Czekamy aż scraper potwierdzi i usunie
                        }
                    }
                    // Klucz nie istnieje → finish-task lub acknowledge-cancel → zakończone
                }

                if (stillActive == 0)
                    break;

                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }

            // 4. Zbierz statystyki — w tym momencie wszystkie klucze powinny być usunięte
            foreach (var allegroName in queuedAllegroNames)
            {
                if (AllegroGatherManager.ActiveTasks.TryGetValue(allegroName, out var finalState))
                {
                    // Klucz wciąż istnieje — coś poszło nie tak (timeout/preempcja)
                    result.StoresCancelled++;
                    result.Details.Add($"{allegroName}: nie zakończono (status={finalState.Status}, zebrano {finalState.CollectedOffersCount} ofert)");
                }
                else
                {
                    // Klucz usunięty = zakończone (finish-task lub acknowledge-cancel)
                    result.StoresCompleted++;
                    result.Details.Add($"{allegroName}: zakończono");
                }
            }

            result.Success = result.StoresCancelled == 0;
            result.Message = $"Zlecono: {result.StoresQueued}, Zakończono: {result.StoresCompleted}, Anulowano: {result.StoresCancelled}";

            return result;
        }

        // =====================================================================
        // ANULOWANIE
        // =====================================================================

        public async Task<(bool Success, string Message)> CancelScrapingForStoreAsync(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null || string.IsNullOrEmpty(store.StoreNameAllegro))
            {
                return (false, "Sklep nie istnieje lub nie ma przypisanej nazwy Allegro.");
            }

            if (AllegroGatherManager.ActiveTasks.TryGetValue(store.StoreNameAllegro, out var taskState))
            {
                taskState.Status = ScrapingStatus.Cancelled;
                taskState.LastProgressMessage = "Anulowane przez użytkownika.";

                await _hubContext.Clients.All.SendAsync("UpdateTaskProgress", store.StoreNameAllegro, taskState);
                _logger.LogInformation("Wysłano sygnał anulowania dla '{StoreName}'.", store.StoreName);
                return (true, $"Wysłano sygnał przerwania do zadania dla sklepu: {store.StoreName}");
            }

            return (false, "Nie znaleziono aktywnego zadania dla tego sklepu.");
        }

        // =====================================================================
        // USUWANIE WSZYSTKICH PRODUKTÓW
        // =====================================================================

        public async Task<(bool Success, string Message)> DeleteAllProductsAsync()
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var priceHistoryRows = await _context.AllegroPriceHistories.ExecuteDeleteAsync();
                var productRows = await _context.AllegroProducts.ExecuteDeleteAsync();

                await transaction.CommitAsync();

                var msg = $"Usunięto {productRows} produktów oraz {priceHistoryRows} powiązanych wpisów historii cen.";
                _logger.LogInformation("[GATHER] {Message}", msg);
                return (true, msg);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Błąd podczas usuwania produktów Allegro.");
                return (false, $"Wystąpił błąd podczas usuwania danych: {ex.Message}");
            }
        }
    }
}