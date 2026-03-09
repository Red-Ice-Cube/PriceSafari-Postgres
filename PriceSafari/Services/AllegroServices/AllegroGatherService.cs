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
        // REZULTATY
        // =====================================================================

        public class StoreGatherStats
        {
            public string StoreName { get; set; } = "";
            public string AllegroName { get; set; } = "";
            public int ProductsBefore { get; set; }
            public int ProductsAfter { get; set; }
            public int NewProductsFound => ProductsAfter - ProductsBefore;
            public int ActiveBefore { get; set; }
            public int ActiveAfter { get; set; }
            public int AutoActivated => ActiveAfter - ActiveBefore;
            public int Limit { get; set; }
            public bool Completed { get; set; }
            public bool WasCancelled { get; set; }
        }

        public class GatherResult
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public int StoresQueued { get; set; }
            public int StoresCompleted { get; set; }
            public int StoresCancelled { get; set; }
            public List<StoreGatherStats> StoreStats { get; set; } = new();
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
        // ZLECANIE + OCZEKIWANIE + AUTO-AKTYWACJA (scheduler)
        // =====================================================================

        /// <summary>
        /// Zleca zbieranie ofert, czeka na zakończenie, a następnie automatycznie
        /// aktywuje nowe produkty (IsScrapable = true) do limitu sklepu.
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

            // 2. SNAPSHOT PRZED — zliczamy produkty i aktywne produkty przed rozpoczęciem
            var snapshotBefore = new Dictionary<int, (int TotalProducts, int ActiveProducts, int Limit, string StoreName, string AllegroName)>();

            foreach (var store in stores)
            {
                var totalProducts = await _context.AllegroProducts.CountAsync(p => p.StoreId == store.StoreId, ct);
                var activeProducts = await _context.AllegroProducts.CountAsync(p => p.StoreId == store.StoreId && p.IsScrapable, ct);
                var limit = store.ProductsToScrapAllegro ?? int.MaxValue;

                snapshotBefore[store.StoreId] = (totalProducts, activeProducts, limit, store.StoreName, store.StoreNameAllegro);
            }

            // 3. Zlecaj zadania
            var queuedAllegroNames = new List<string>();
            var storeIdByAllegroName = new Dictionary<string, int>();

            foreach (var store in stores)
            {
                var newTask = new ScrapingTaskState { Status = ScrapingStatus.Pending };
                if (AllegroGatherManager.ActiveTasks.TryAdd(store.StoreNameAllegro, newTask))
                {
                    queuedAllegroNames.Add(store.StoreNameAllegro);
                    storeIdByAllegroName[store.StoreNameAllegro] = store.StoreId;
                    await _hubContext.Clients.All.SendAsync("UpdateTaskProgress", store.StoreNameAllegro, newTask, ct);
                    _logger.LogInformation("[GATHER] Zlecono: {AllegroName}", store.StoreNameAllegro);
                }
                else
                {
                    _logger.LogWarning("[GATHER] Zadanie już istnieje dla: {AllegroName}. Pomijam.", store.StoreNameAllegro);
                }
            }

            result.StoresQueued = queuedAllegroNames.Count;

            if (queuedAllegroNames.Count == 0)
            {
                result.Message = "Wszystkie sklepy mają już aktywne zadania.";
                return result;
            }

            // 4. Czekaj na zakończenie
            _logger.LogInformation("[GATHER] Oczekiwanie na zakończenie {Count} zadań...", queuedAllegroNames.Count);

            while (!ct.IsCancellationRequested)
            {
                int stillActive = 0;

                foreach (var allegroName in queuedAllegroNames)
                {
                    if (AllegroGatherManager.ActiveTasks.ContainsKey(allegroName))
                    {
                        stillActive++;
                    }
                }

                if (stillActive == 0)
                    break;

                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }

            // 5. SNAPSHOT PO + AUTO-AKTYWACJA + STATYSTYKI
            foreach (var allegroName in queuedAllegroNames)
            {
                var storeId = storeIdByAllegroName[allegroName];
                var before = snapshotBefore[storeId];

                var stats = new StoreGatherStats
                {
                    StoreName = before.StoreName,
                    AllegroName = before.AllegroName,
                    ProductsBefore = before.TotalProducts,
                    ActiveBefore = before.ActiveProducts,
                    Limit = before.Limit
                };

                // Sprawdź czy zadanie się zakończyło (klucz usunięty) czy anulowano
                bool wasCancelled = AllegroGatherManager.ActiveTasks.ContainsKey(allegroName);
                stats.WasCancelled = wasCancelled;
                stats.Completed = !wasCancelled;

                if (wasCancelled)
                {
                    result.StoresCancelled++;
                }
                else
                {
                    result.StoresCompleted++;
                }

                // Policz produkty po zakończeniu
                stats.ProductsAfter = await _context.AllegroProducts.CountAsync(p => p.StoreId == storeId, ct);

                // AUTO-AKTYWACJA: aktywuj nowe produkty do limitu
                if (stats.NewProductsFound > 0 && stats.Completed)
                {
                    var currentActive = await _context.AllegroProducts.CountAsync(p => p.StoreId == storeId && p.IsScrapable, ct);
                    var remainingSlots = Math.Max(0, before.Limit - currentActive);

                    if (remainingSlots > 0)
                    {
                        // Pobieramy nowe, nieaktywne, nieodrzucone produkty (najnowsze pierwsze)
                        var productsToActivate = await _context.AllegroProducts
                            .Where(p => p.StoreId == storeId && !p.IsScrapable && !p.IsRejected)
                            .OrderByDescending(p => p.AddedDate)
                            .Take(remainingSlots)
                            .ToListAsync(ct);

                        foreach (var product in productsToActivate)
                        {
                            product.IsScrapable = true;
                        }

                        if (productsToActivate.Any())
                        {
                            await _context.SaveChangesAsync(ct);
                            _logger.LogInformation("[GATHER] Auto-aktywowano {Count} produktów dla sklepu '{Store}' ({Active}/{Limit}).",
                                productsToActivate.Count, before.StoreName, currentActive + productsToActivate.Count, before.Limit);
                        }
                    }
                }

                // Finalne zliczenie aktywnych
                stats.ActiveAfter = await _context.AllegroProducts.CountAsync(p => p.StoreId == storeId && p.IsScrapable, ct);

                result.StoreStats.Add(stats);
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