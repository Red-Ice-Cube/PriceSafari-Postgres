// kod uruchamiajacy wewnetrzny scraper google w c#

//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Logging;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using PriceSafari.Models;
//using PriceSafari.Services.ControlNetwork;

//namespace PriceSafari.Services.ScheduleService
//{
//    public class GoogleScraperService
//    {
//        private readonly IServiceScopeFactory _scopeFactory;
//        private readonly IHubContext<ScrapingHub> _hubContext;
//        private readonly INetworkControlService _networkControlService;
//        private readonly ILogger<GoogleScraperService> _logger;

//        private const int MAX_NETWORK_RESETS = 3;

//        public GoogleScraperService(
//            IServiceScopeFactory scopeFactory,
//            IHubContext<ScrapingHub> hubContext,
//            INetworkControlService networkControlService,
//            ILogger<GoogleScraperService> logger)
//        {
//            _scopeFactory = scopeFactory;
//            _hubContext = hubContext;
//            _networkControlService = networkControlService;
//            _logger = logger;
//        }

//        // --- Helper Async FireAndForget ---
//        private void FireAndForget(string methodName, params object[] args)
//        {
//            Task.Run(async () =>
//            {
//                try { await _hubContext.Clients.All.SendCoreAsync(methodName, args); }
//                catch (Exception ex) { _logger.LogError(ex, $"Failed to send SignalR message: {methodName}"); }
//            });
//        }
//        // ----------------------------------

//        public enum GoogleScrapingResult
//        {
//            Success,
//            SettingsNotFound,
//            NoProductsToScrape,
//            Error,
//            PersistentBlock
//        }

//        public record GoogleScrapingDto(
//            GoogleScrapingResult Result,
//            int TotalScraped,
//            int TotalRejected,
//            int NetworkResets,
//            int TotalUrlsToScrape,
//            string? Message
//        );

//        public async Task<GoogleScrapingDto> StartScraping()
//        {
//            int totalScrapedOverall = 0;
//            int totalRejectedOverall = 0;
//            int networkResetCount = 0;

//            int totalUrlsToScrape = 0;
//            var stopwatch = Stopwatch.StartNew();

//            for (int attempt = 0; attempt <= MAX_NETWORK_RESETS; attempt++)
//            {

//                (bool success, int scrapedThisRun, int rejectedThisRun, int urlsInRun) result;

//                try
//                {
//                    result = await PerformSingleScrapingRun(stopwatch);
//                }
//                catch (Exception ex)
//                {
//                    _logger.LogError(ex, "[GoogleService] A critical error occurred during a scraping run.");

//                    // Ważne: W razie błędu krytycznego też czyścimy magazyn
//                    GlobalCookieWarehouse.StopAndClear();

//                    return new GoogleScrapingDto(GoogleScrapingResult.Error, totalScrapedOverall, totalRejectedOverall, networkResetCount, totalUrlsToScrape, $"A critical error occurred: {ex.Message}");
//                }

//                if (attempt == 0)
//                {
//                    totalUrlsToScrape = result.urlsInRun;
//                }

//                totalScrapedOverall += result.scrapedThisRun;
//                totalRejectedOverall += result.rejectedThisRun;

//                if (result.success)
//                {
//                    _logger.LogInformation($"[GoogleService] Scraping run successful. Overall scraped: {totalScrapedOverall}, rejected: {totalRejectedOverall}.");
//                    FireAndForget("ReceiveGeneralMessage", $"Google: Scrapowanie zakończone. Zebrano: {totalScrapedOverall}, odrzucono: {totalRejectedOverall}.");

//                    return new GoogleScrapingDto(GoogleScrapingResult.Success, totalScrapedOverall, totalRejectedOverall, networkResetCount, totalUrlsToScrape, "Scraping completed successfully.");
//                }

//                networkResetCount++;
//                _logger.LogWarning($"[GoogleService] Persistent block detected on attempt {attempt + 1}. Reset count: {networkResetCount}.");

//                if (attempt == MAX_NETWORK_RESETS)
//                {
//                    _logger.LogError($"[GoogleService] Persistent block remained after max ({networkResetCount}) resets.");
//                    FireAndForget("ReceiveGeneralMessage", $"Google: Blokada utrzymuje się po {networkResetCount} próbach resetu. Wymagana ręczna interwencja.");

//                    return new GoogleScrapingDto(GoogleScrapingResult.PersistentBlock, totalScrapedOverall, totalRejectedOverall, networkResetCount, totalUrlsToScrape, $"Persistent block after {networkResetCount} resets. Manual intervention required.");
//                }

//                FireAndForget("ReceiveGeneralMessage", $"Google: Wykryto blokadę (Reset {networkResetCount}/{MAX_NETWORK_RESETS}). Próba resetu sieci...");

//                try
//                {
//                    await _networkControlService.TriggerNetworkDisableAndResetAsync();
//                    FireAndForget("ReceiveGeneralMessage", "Google: Reset sieci udany. Ponawiam scrapowanie za 10 sekund...");
//                }
//                catch (Exception ex)
//                {
//                    _logger.LogError(ex, "[GoogleService] Error during network reset.");
//                    FireAndForget("ReceiveGeneralMessage", $"Google: Błąd podczas resetu sieci: {ex.Message}");
//                }

//                await Task.Delay(TimeSpan.FromSeconds(10));
//            }

//            return new GoogleScrapingDto(GoogleScrapingResult.Error, totalScrapedOverall, totalRejectedOverall, networkResetCount, totalUrlsToScrape, "Unexpected end of scraping process.");
//        }

//        private async Task<(bool success, int scraped, int rejected, int totalUrls)> PerformSingleScrapingRun(Stopwatch stopwatch)
//        {
//            using var scope = _scopeFactory.CreateScope();
//            var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//            var settings = await dbContext.Settings.FirstOrDefaultAsync();

//            if (settings == null)
//            {
//                _logger.LogError("[GoogleService] Settings not found in the database.");
//                throw new InvalidOperationException("Settings not found.");
//            }

//            // 1. [BATCHING] Inicjalizacja procesora wyników
//            ResultBatchProcessor.Initialize(_scopeFactory);

//            int generatorCount = settings.GoogleGeneratorsCount;
//            bool headlessMode = settings.HeadLessForGoogleGenerators;

//            GlobalCookieWarehouse.StartGenerators(generatorCount, headlessMode);

//            var coOfrsToScrape = await dbContext.CoOfrs
//               .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
//               .ToListAsync();

//            int totalUrls = coOfrsToScrape.Count;

//            if (!coOfrsToScrape.Any())
//            {
//                // Jeśli nie ma nic do roboty, czyścimy i wychodzimy
//                GlobalCookieWarehouse.StopAndClear();
//                return (success: true, scraped: 0, rejected: 0, totalUrls: 0);
//            }

//            _logger.LogInformation($"[GoogleService] Starting a new run with {totalUrls} products.");

//            // 3. [WARM-UP] Oczekiwanie na ciastka
//            // Czekamy, aż w magazynie pojawi się chociaż kilka ciastek, zanim ruszymy z HTTP.
//            int maxConcurrent = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
//            int minCookiesToStart = Math.Min(maxConcurrent, 3);

//            _logger.LogInformation($"[GoogleService] Waiting for cookies warm-up (Target: {minCookiesToStart})...");

//            // Timeout: max 60 sekund czekania na start
//            int waitSeconds = 0;
//            while (GlobalCookieWarehouse.AvailableCookies < minCookiesToStart && waitSeconds < 60)
//            {
//                await Task.Delay(1000);
//                waitSeconds++;
//            }
//            _logger.LogInformation($"[GoogleService] Warm-up finished or timed out. Available cookies: {GlobalCookieWarehouse.AvailableCookies}");

//            int scrapedThisRun = 0;
//            int rejectedThisRun = 0;
//            int persistentFailures = 0;

//            var semaphore = new SemaphoreSlim(maxConcurrent);
//            var tasks = new List<Task>();
//            var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

//            for (int i = 0; i < maxConcurrent; i++)
//            {
//                tasks.Add(Task.Run(async () =>
//                {
//                    await semaphore.WaitAsync();
//                    try
//                    {
//                        // Instancjonowanie Scrapera - to pobierze ciastko z Warehouse
//                        // (zablokuje wątek, jeśli Warehouse jest pusty)
//                        var scraper = new GoogleMainPriceScraper();

//                        while (true)
//                        {
//                            CoOfrClass item = null;
//                            lock (productQueue)
//                            {
//                                if (productQueue.Count > 0) item = productQueue.Dequeue();
//                            }
//                            if (item == null) break;

//                            try
//                            {
//                                using var productScope = _scopeFactory.CreateScope();
//                                var db = productScope.ServiceProvider.GetRequiredService<PriceSafariContext>();

//                                var prices = await scraper.ScrapePricesAsync(item);

//                                var trackedItem = await db.CoOfrs.FindAsync(item.Id);
//                                if (trackedItem == null) continue;

//                                if (prices != null && prices.Any())
//                                {
//                                    foreach (var p in prices)
//                                    {
//                                        p.CoOfrClassId = trackedItem.Id;
//                                        p.GoogleCid = trackedItem.GoogleCid;
//                                    }

//                                    // [BATCHING] Dodanie do kolejki
//                                    ResultBatchProcessor.Enqueue(prices);

//                                    trackedItem.GoogleIsScraped = true;
//                                    trackedItem.GoogleIsRejected = false;
//                                    trackedItem.GooglePricesCount = prices.Count;
//                                    Interlocked.Increment(ref scrapedThisRun);
//                                }
//                                else
//                                {
//                                    trackedItem.GoogleIsScraped = true;
//                                    trackedItem.GoogleIsRejected = true;
//                                    trackedItem.GooglePricesCount = 0;
//                                    Interlocked.Increment(ref rejectedThisRun);

//                                    if (prices == null) Interlocked.Increment(ref persistentFailures);
//                                }

//                                await db.SaveChangesAsync(); // Zapis statusu rodzica
//                            }
//                            catch (Exception ex)
//                            {
//                                _logger.LogError(ex, $"[GoogleService] Error scraping item {item?.Id}.");
//                                Interlocked.Increment(ref rejectedThisRun);
//                                Interlocked.Increment(ref persistentFailures);
//                            }
//                        }
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.LogError(ex, $"[GoogleService] Worker initialization error.");
//                    }
//                    finally
//                    {
//                        semaphore.Release();
//                    }
//                }));
//            }

//            await Task.WhenAll(tasks);

//            // [CLEANUP] Sprzątanie po zakończeniu
//            await ResultBatchProcessor.StopAndFlushAsync();
//            GlobalCookieWarehouse.StopAndClear(); // Zatrzymujemy generatory

//            _logger.LogInformation($"[GoogleService] Run finished. Scraped: {scrapedThisRun}, Rejected: {rejectedThisRun}, Failures: {persistentFailures}");

//            bool wasSuccess = persistentFailures <= (coOfrsToScrape.Count / 2);

//            return (success: wasSuccess, scraped: scrapedThisRun, rejected: rejectedThisRun, totalUrls: totalUrls);
//        }
//    }
//}





// wersja bez jazni


//using System;
//using System.Diagnostics;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Logging;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using PriceSafari.Services.GoogleScraping;

//namespace PriceSafari.Services.ScheduleService
//{
//    /// <summary>
//    /// Serwis do uruchamiania i monitorowania scrapowania Google przez zewnętrzny scraper Python.
//    /// Python scraper zarządza całą logiką (generatory, sesje, NUKE protocol, VPN).
//    /// Ten serwis tylko uruchamia, monitoruje i zwraca wyniki.
//    /// </summary>
//    public class GoogleScraperService
//    {
//        private readonly IServiceScopeFactory _scopeFactory;
//        private readonly IHubContext<ScrapingHub> _hubContext;
//        private readonly GoogleScrapingStateService _stateService;
//        private readonly ILogger<GoogleScraperService> _logger;

//        // Konfiguracja monitorowania
//        private const int POLLING_INTERVAL_SECONDS = 5;
//        private const int MAX_IDLE_MINUTES = 30;
//        private const int MAX_NO_SCRAPERS_MINUTES = 5;

//        public GoogleScraperService(
//            IServiceScopeFactory scopeFactory,
//            IHubContext<ScrapingHub> hubContext,
//            GoogleScrapingStateService stateService,
//            ILogger<GoogleScraperService> logger)
//        {
//            _scopeFactory = scopeFactory;
//            _hubContext = hubContext;
//            _stateService = stateService;
//            _logger = logger;
//        }

//        // Helper dla SignalR
//        private void FireAndForget(string methodName, params object[] args)
//        {
//            Task.Run(async () =>
//            {
//                try { await _hubContext.Clients.All.SendCoreAsync(methodName, args); }
//                catch (Exception ex) { _logger.LogError(ex, $"Failed to send SignalR message: {methodName}"); }
//            });
//        }

//        #region Enums & DTOs

//        public enum GoogleScrapingResult
//        {
//            Success,
//            SettingsNotFound,
//            NoProductsToScrape,
//            Error,
//            PersistentBlock,
//            Timeout,
//            AlreadyRunning
//        }

//        public record GoogleScrapingDto(
//            GoogleScrapingResult Result,
//            int TotalScraped,
//            int TotalRejected,
//            int NetworkResets, // Zawsze 0 - Python zarządza tym sam
//            int TotalUrlsToScrape,
//            string? Message
//        );

//        #endregion

//        /// <summary>
//        /// Główna metoda uruchamiająca scrapowanie.
//        /// Uruchamia proces, monitoruje postęp i czeka na zakończenie.
//        /// Scraper Python musi być uruchomiony osobno i odpytywać API.
//        /// </summary>
//        public async Task<GoogleScrapingDto> StartScraping(CancellationToken cancellationToken = default)
//        {
//            _logger.LogInformation("[GoogleService] Inicjalizacja scrapowania Google przez Python API...");

//            try
//            {
//                using var scope = _scopeFactory.CreateScope();
//                var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

//                // Sprawdź ustawienia
//                var settings = await dbContext.Settings.FirstOrDefaultAsync(cancellationToken);
//                if (settings == null)
//                {
//                    _logger.LogError("[GoogleService] Settings not found in the database.");
//                    return new GoogleScrapingDto(
//                        GoogleScrapingResult.SettingsNotFound,
//                        0, 0, 0, 0,
//                        "Settings not found in the database."
//                    );
//                }

//                // Sprawdź ile produktów do scrapowania
//                var totalToScrape = await dbContext.CoOfrs
//                    .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
//                                     && !c.GoogleIsScraped, cancellationToken);

//                var totalAll = await dbContext.CoOfrs
//                    .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer, cancellationToken);

//                if (totalToScrape == 0)
//                {
//                    _logger.LogInformation("[GoogleService] Brak produktów do scrapowania.");
//                    FireAndForget("ReceiveGeneralMessage", "Google: Brak produktów do scrapowania.");
//                    return new GoogleScrapingDto(
//                        GoogleScrapingResult.NoProductsToScrape,
//                        0, 0, 0, 0,
//                        "No products to scrape."
//                    );
//                }

//                // Spróbuj uruchomić scrapowanie
//                if (!_stateService.TryStartScraping(totalAll))
//                {
//                    _logger.LogWarning("[GoogleService] Scrapowanie już działa.");
//                    return new GoogleScrapingDto(
//                        GoogleScrapingResult.AlreadyRunning,
//                        0, 0, 0, totalToScrape,
//                        "Scraping is already running."
//                    );
//                }

//                _logger.LogInformation($"[GoogleService] Scrapowanie uruchomione. Total: {totalAll}, Do przetworzenia: {totalToScrape}");
//                FireAndForget("ReceiveGeneralMessage", $"Google: Scrapowanie uruchomione! {totalToScrape} produktów do przetworzenia. Oczekiwanie na scrapery Python...");
//                FireAndForget("ReceiveProgressUpdate", totalAll - totalToScrape, totalAll, 0, 0);

//                // Monitoruj postęp
//                var result = await MonitorScrapingProgress(totalAll, cancellationToken);

//                return result;
//            }
//            catch (OperationCanceledException)
//            {
//                _logger.LogWarning("[GoogleService] Scrapowanie anulowane.");
//                _stateService.StopScraping();
//                FireAndForget("ReceiveGeneralMessage", "Google: Scrapowanie anulowane.");

//                return new GoogleScrapingDto(
//                    GoogleScrapingResult.Error,
//                    _stateService.TotalProcessedInSession,
//                    _stateService.TotalRejectedInSession,
//                    0, 0,
//                    "Scraping was cancelled."
//                );
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "[GoogleService] Błąd krytyczny podczas scrapowania.");
//                _stateService.StopScraping();
//                FireAndForget("ReceiveGeneralMessage", $"Google: Błąd krytyczny: {ex.Message}");

//                return new GoogleScrapingDto(
//                    GoogleScrapingResult.Error,
//                    _stateService.TotalProcessedInSession,
//                    _stateService.TotalRejectedInSession,
//                    0, 0,
//                    $"Critical error: {ex.Message}"
//                );
//            }
//        }

//        /// <summary>
//        /// Monitoruje postęp scrapowania aż do zakończenia lub timeout.
//        /// </summary>
//        private async Task<GoogleScrapingDto> MonitorScrapingProgress(int totalTasks, CancellationToken cancellationToken)
//        {
//            var stopwatch = Stopwatch.StartNew();
//            int lastCompletedTasks = 0;
//            DateTime lastProgressTime = DateTime.UtcNow;
//            int consecutiveNoScrapers = 0;
//            int maxNoScrapersChecks = (MAX_NO_SCRAPERS_MINUTES * 60) / POLLING_INTERVAL_SECONDS;

//            _logger.LogInformation($"[GoogleService] Rozpoczynam monitorowanie. Total: {totalTasks}, Timeout idle: {MAX_IDLE_MINUTES}min, No-scrapers: {MAX_NO_SCRAPERS_MINUTES}min");

//            while (!cancellationToken.IsCancellationRequested)
//            {
//                await Task.Delay(TimeSpan.FromSeconds(POLLING_INTERVAL_SECONDS), cancellationToken);

//                // Sprawdź czy scrapowanie jest nadal włączone
//                if (!_stateService.IsScrapingEnabled)
//                {
//                    _logger.LogInformation("[GoogleService] Scrapowanie zakończone (IsEnabled = false).");

//                    var elapsed = _stateService.ElapsedTime;
//                    FireAndForget("ReceiveGeneralMessage",
//                        $"Google: Scrapowanie zakończone! Przetworzone: {_stateService.TotalProcessedInSession}, " +
//                        $"Odrzucone: {_stateService.TotalRejectedInSession}, " +
//                        $"Czas: {elapsed.TotalMinutes:F1} min");

//                    return new GoogleScrapingDto(
//                        GoogleScrapingResult.Success,
//                        _stateService.TotalProcessedInSession,
//                        _stateService.TotalRejectedInSession,
//                        0,
//                        totalTasks,
//                        $"Scraping completed. Processed: {_stateService.TotalProcessedInSession}, Rejected: {_stateService.TotalRejectedInSession}"
//                    );
//                }

//                // Pobierz aktualny stan z bazy
//                int completedTasks;
//                int remainingTasks;
//                using (var scope = _scopeFactory.CreateScope())
//                {
//                    var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//                    completedTasks = await dbContext.CoOfrs
//                        .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
//                                         && c.GoogleIsScraped, cancellationToken);
//                    remainingTasks = totalTasks - completedTasks;
//                }

//                // Sprawdź aktywne scrapery
//                _stateService.CleanupDeadScrapers();
//                var activeScrapersCount = _stateService.ActiveScrapers.Count;

//                if (activeScrapersCount == 0)
//                {
//                    consecutiveNoScrapers++;

//                    if (consecutiveNoScrapers % 12 == 1) // Log co minutę (12 * 5s = 60s)
//                    {
//                        _logger.LogWarning($"[GoogleService] Brak aktywnych scraperów! Check {consecutiveNoScrapers}/{maxNoScrapersChecks}");
//                        FireAndForget("ReceiveGeneralMessage", $"⚠️ Google: Brak aktywnych scraperów Python! Sprawdź czy scraper jest uruchomiony.");
//                    }

//                    if (consecutiveNoScrapers >= maxNoScrapersChecks)
//                    {
//                        _logger.LogError("[GoogleService] Timeout - brak scraperów przez zbyt długi czas.");
//                        _stateService.StopScraping();

//                        FireAndForget("ReceiveGeneralMessage",
//                            $"❌ Google: Timeout - brak scraperów Python przez {MAX_NO_SCRAPERS_MINUTES} minut!");

//                        return new GoogleScrapingDto(
//                            GoogleScrapingResult.Timeout,
//                            _stateService.TotalProcessedInSession,
//                            _stateService.TotalRejectedInSession,
//                            0,
//                            totalTasks,
//                            $"Timeout: No active scrapers for {MAX_NO_SCRAPERS_MINUTES} minutes."
//                        );
//                    }
//                }
//                else
//                {
//                    consecutiveNoScrapers = 0;
//                }

//                // Sprawdź postęp
//                if (completedTasks > lastCompletedTasks)
//                {
//                    lastCompletedTasks = completedTasks;
//                    lastProgressTime = DateTime.UtcNow;

//                    double progressPercent = totalTasks > 0 ? (double)completedTasks / totalTasks * 100 : 0;
//                    _logger.LogDebug($"[GoogleService] Postęp: {completedTasks}/{totalTasks} ({progressPercent:F1}%) - Scraperów: {activeScrapersCount}");
//                }

//                // Sprawdź stagnację
//                var idleTime = DateTime.UtcNow - lastProgressTime;
//                if (idleTime.TotalMinutes > MAX_IDLE_MINUTES)
//                {
//                    _logger.LogError($"[GoogleService] Timeout - brak postępu przez {MAX_IDLE_MINUTES} minut.");
//                    _stateService.StopScraping();

//                    FireAndForget("ReceiveGeneralMessage",
//                        $"❌ Google: Timeout - brak postępu przez {MAX_IDLE_MINUTES} minut!");

//                    return new GoogleScrapingDto(
//                        GoogleScrapingResult.Timeout,
//                        _stateService.TotalProcessedInSession,
//                        _stateService.TotalRejectedInSession,
//                        0,
//                        totalTasks,
//                        $"Timeout: No progress for {MAX_IDLE_MINUTES} minutes."
//                    );
//                }

//                // Sprawdź czy wszystko zrobione
//                if (remainingTasks == 0 && _stateService.TasksInProgressCount == 0)
//                {
//                    _logger.LogInformation("[GoogleService] Wszystkie zadania przetworzone!");

//                    // Poczekaj chwilę na finalizację
//                    await Task.Delay(2000, cancellationToken);

//                    _stateService.MarkAsCompleted();

//                    var elapsed = _stateService.ElapsedTime;
//                    FireAndForget("ReceiveGeneralMessage",
//                        $"✅ Google: Scrapowanie zakończone! Przetworzone: {_stateService.TotalProcessedInSession}, " +
//                        $"Odrzucone: {_stateService.TotalRejectedInSession}, " +
//                        $"Czas: {elapsed.TotalMinutes:F1} min");

//                    return new GoogleScrapingDto(
//                        GoogleScrapingResult.Success,
//                        _stateService.TotalProcessedInSession,
//                        _stateService.TotalRejectedInSession,
//                        0,
//                        totalTasks,
//                        $"Scraping completed successfully. Processed: {_stateService.TotalProcessedInSession}, Rejected: {_stateService.TotalRejectedInSession}"
//                    );
//                }

//                // Log co 2 minuty
//                if (stopwatch.Elapsed.TotalSeconds % 120 < POLLING_INTERVAL_SECONDS)
//                {
//                    double progressPercent = totalTasks > 0 ? (double)completedTasks / totalTasks * 100 : 0;
//                    _logger.LogInformation($"[GoogleService] Status: {completedTasks}/{totalTasks} ({progressPercent:F1}%), " +
//                                          $"In progress: {_stateService.TasksInProgressCount}, " +
//                                          $"Scrapers: {activeScrapersCount}, " +
//                                          $"Elapsed: {stopwatch.Elapsed.TotalMinutes:F1} min");
//                }
//            }

//            // Anulowano
//            _stateService.StopScraping();

//            return new GoogleScrapingDto(
//                GoogleScrapingResult.Error,
//                _stateService.TotalProcessedInSession,
//                _stateService.TotalRejectedInSession,
//                0,
//                totalTasks,
//                "Scraping was cancelled."
//            );
//        }

//        /// <summary>
//        /// Zatrzymuje scrapowanie (do wywołania z zewnątrz jeśli potrzeba)
//        /// </summary>
//        public void StopScraping()
//        {
//            _logger.LogInformation("[GoogleService] Ręczne zatrzymanie scrapowania...");
//            var (processed, rejected, elapsed) = _stateService.StopScraping();

//            FireAndForget("ReceiveGeneralMessage",
//                $"Google: Scrapowanie zatrzymane ręcznie. Przetworzone: {processed}, Odrzucone: {rejected}");
//        }

//        /// <summary>
//        /// Pobiera aktualny status scrapowania
//        /// </summary>
//        public async Task<ScrapingStatusDto> GetCurrentStatus()
//        {
//            using var scope = _scopeFactory.CreateScope();
//            var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

//            var totalTasks = await dbContext.CoOfrs
//                .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer);

//            var completedTasks = await dbContext.CoOfrs
//                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsScraped);

//            var rejectedTasks = await dbContext.CoOfrs
//                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsRejected);

//            return _stateService.GetFullStatus(totalTasks, completedTasks, rejectedTasks);
//        }

//        /// <summary>
//        /// Sprawdza czy scrapowanie jest aktualnie uruchomione
//        /// </summary>
//        public bool IsRunning => _stateService.IsScrapingEnabled;
//    }
//}



using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Services.GoogleScraping;

namespace PriceSafari.Services.ScheduleService
{
    /// <summary>
    /// Serwis zarządzający procesem scrapowania Google Shopping
    /// </summary>
    public class GoogleScraperService
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<GoogleScraperService> _logger;

        public GoogleScraperService(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            ILogger<GoogleScraperService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// Uruchamia proces scrapowania Google
        /// </summary>
        public async Task<(bool success, string message)> StartScrapingProcessAsync()
        {
            _logger.LogInformation("Uruchamianie procesu scrapowania Google...");

            if (GoogleScrapeManager.CurrentStatus == GoogleScrapingProcessStatus.Running)
                return (false, "Proces już działa.");

            // Sprawdź czy są aktywne scrapery
            var anyActiveScrapers = GoogleScrapeManager.ActiveScrapers.Values
                .Any(s => s.Status != GoogleScraperLiveStatus.Offline && s.Status != GoogleScraperLiveStatus.Stopped);

            if (!anyActiveScrapers)
                return (false, "Brak aktywnych scraperów. Uruchom skrypt Python.");

            // Sprawdź ile URLi do przetworzenia
            var urlsToScrape = await _context.CoOfrs
                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && !c.GoogleIsScraped);

            if (urlsToScrape == 0)
                return (true, "Brak URLi do scrapowania.");

            // Uruchom proces
            GoogleScrapeManager.ResetForNewProcess();

            // Powiadom front
            await _hubContext.Clients.All.SendAsync("GoogleScrapingStarted", new
            {
                startTime = GoogleScrapeManager.ScrapingStartTime,
                totalUrls = urlsToScrape
            });

            await BroadcastDashboard();

            _logger.LogInformation("Proces scrapowania Google uruchomiony. URLi: {Count}", urlsToScrape);
            return (true, $"Proces uruchomiony. {urlsToScrape} URLi do przetworzenia.");
        }

        /// <summary>
        /// Zatrzymuje proces scrapowania Google
        /// </summary>
        public async Task<(bool success, string message)> StopScrapingProcessAsync()
        {
            GoogleScrapeManager.CurrentStatus = GoogleScrapingProcessStatus.Idle;
            GoogleScrapeManager.ScrapingEndTime = DateTime.UtcNow;
            GoogleScrapeManager.AddSystemLog("WARNING", "Proces zatrzymany ręcznie");

            await _hubContext.Clients.All.SendAsync("GoogleScrapingStopped", new
            {
                endTime = GoogleScrapeManager.ScrapingEndTime
            });

            await BroadcastDashboard();

            _logger.LogInformation("Proces scrapowania Google zatrzymany.");
            return (true, "Proces zatrzymany.");
        }

        /// <summary>
        /// Zatrzymuje indywidualnego scrapera
        /// </summary>
        public async Task PauseScraperAsync(string scraperName)
        {
            GoogleScrapeManager.PauseScraper(scraperName);

            if (GoogleScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            await BroadcastLogs();
            _logger.LogInformation("Scraper {ScraperName} zatrzymany.", scraperName);
        }

        /// <summary>
        /// Wznawia indywidualnego scrapera
        /// </summary>
        public async Task ResumeScraperAsync(string scraperName)
        {
            GoogleScrapeManager.ResumeScraper(scraperName);

            if (GoogleScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            await BroadcastLogs();
            _logger.LogInformation("Scraper {ScraperName} wznowiony.", scraperName);
        }

        /// <summary>
        /// Sprawdza i obsługuje timeout paczek
        /// </summary>
        public async Task<int> CheckAndHandleTimeoutsAsync()
        {
            if (GoogleScrapeManager.CurrentStatus != GoogleScrapingProcessStatus.Running)
                return 0;

            var timedOutBatches = GoogleScrapeManager.FindAndMarkTimedOutBatches();

            if (!timedOutBatches.Any())
                return 0;

            _logger.LogWarning("Znaleziono {Count} paczek z timeoutem.", timedOutBatches.Count);

            // Zwróć URLe do puli (w tym przypadku po prostu zostaną pobrane ponownie,
            // bo GoogleIsScraped jest false)
            foreach (var (batchId, scraperName, taskIds) in timedOutBatches)
            {
                // Aktualizuj status scrapera
                if (GoogleScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                {
                    scraper.Status = GoogleScraperLiveStatus.Offline;
                    scraper.CurrentBatchId = null;
                    await BroadcastScraperStatus(scraper);
                }
            }

            await BroadcastLogs();
            await BroadcastDashboard();

            return timedOutBatches.Count;
        }

        /// <summary>
        /// Sprawdza i oznacza nieaktywne scrapery jako offline
        /// </summary>
        public async Task<int> CheckAndMarkOfflineScrapersAsync()
        {
            var markedOffline = GoogleScrapeManager.MarkInactiveScrapersAsOffline();

            foreach (var scraperName in markedOffline)
            {
                if (GoogleScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                    await BroadcastScraperStatus(scraper);
            }

            if (markedOffline.Any())
            {
                await BroadcastLogs();
                await BroadcastDashboard();
            }

            return markedOffline.Count;
        }

        /// <summary>
        /// Pobiera statystyki z bazy danych
        /// </summary>
        public async Task<GoogleDbStatsDto> GetDatabaseStatsAsync()
        {
            var stats = await _context.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
                .GroupBy(_ => 1)
                .Select(g => new GoogleDbStatsDto
                {
                    TotalUrls = g.Count(),
                    ScrapedUrls = g.Count(c => c.GoogleIsScraped),
                    RejectedUrls = g.Count(c => c.GoogleIsRejected),
                    TotalPrices = g.Sum(c => c.GooglePricesCount),
                    UrlsWithWRGA = g.Count(c => c.UseWRGA),
                    UrlsWithGPID = g.Count(c => c.UseGPID),
                    UrlsWithHidOffer = g.Count(c => c.UseGoogleHidOffer)
                })
                .FirstOrDefaultAsync() ?? new GoogleDbStatsDto();

            stats.PendingUrls = stats.TotalUrls - stats.ScrapedUrls;
            return stats;
        }

        /// <summary>
        /// Pobiera listę URLi do widoku
        /// </summary>
        public async Task<List<GoogleUrlDto>> GetUrlsAsync(int limit = 1000)
        {
            return await _context.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
                .OrderBy(c => c.GoogleIsScraped)
                .ThenBy(c => c.Id)
                .Take(limit)
                .Select(c => new GoogleUrlDto
                {
                    Id = c.Id,
                    GoogleOfferUrl = c.GoogleOfferUrl,
                    GoogleCid = c.GoogleCid,
                    GoogleGid = c.GoogleGid,
                    GoogleHid = c.GoogleHid,
                    UseGoogleHidOffer = c.UseGoogleHidOffer,
                    UseWRGA = c.UseWRGA,
                    UseGPID = c.UseGPID,
                    GoogleIsScraped = c.GoogleIsScraped,
                    GoogleIsRejected = c.GoogleIsRejected,
                    GooglePricesCount = c.GooglePricesCount,
                    ProductIdsGoogle = c.ProductIdsGoogle ?? new List<int>()
                })
                .ToListAsync();
        }

        /// <summary>
        /// Resetuje odrzucone URLe
        /// </summary>
        public async Task<int> ResetRejectedUrlsAsync()
        {
            var rejected = await _context.CoOfrs
                .Where(c => c.GoogleIsRejected)
                .ToListAsync();

            foreach (var offer in rejected)
            {
                offer.GoogleIsRejected = false;
                offer.GoogleIsScraped = false;
            }

            await _context.SaveChangesAsync();

            GoogleScrapeManager.AddSystemLog("INFO", $"Zresetowano {rejected.Count} odrzuconych URLi");
            await BroadcastLogs();

            return rejected.Count;
        }

        /// <summary>
        /// Czyści zebrane dane Google
        /// </summary>
        public async Task<int> ClearCollectedDataAsync()
        {
            var deletedPrices = await _context.CoOfrPriceHistories.CountAsync();
            _context.CoOfrPriceHistories.RemoveRange(_context.CoOfrPriceHistories);

            var offers = await _context.CoOfrs
                .Where(c => c.GoogleIsScraped || c.GoogleIsRejected)
                .ToListAsync();

            foreach (var offer in offers)
            {
                offer.GoogleIsScraped = false;
                offer.GoogleIsRejected = false;
                offer.GooglePricesCount = 0;
            }

            await _context.SaveChangesAsync();

            GoogleScrapeManager.AddSystemLog("WARNING", $"Wyczyszczono {deletedPrices} cen, zresetowano {offers.Count} URLi");
            await BroadcastLogs();

            return deletedPrices;
        }

        // ===== BROADCAST HELPERS =====

        private async Task BroadcastScraperStatus(GoogleScraperClient scraper)
        {
            GoogleScrapeManager.ScraperStatistics.TryGetValue(scraper.Name, out var stats);
            var activeBatch = GoogleScrapeManager.GetActiveScraperBatch(scraper.Name);

            await _hubContext.Clients.All.SendAsync("GoogleUpdateScraperStatus", new
            {
                name = scraper.Name,
                status = scraper.Status.ToString(),
                statusCode = (int)scraper.Status,
                lastCheckIn = scraper.LastCheckIn,
                currentBatchId = scraper.CurrentBatchId,
                currentIpAddress = scraper.CurrentIpAddress,
                isManuallyPaused = stats?.IsManuallyPaused ?? false,
                totalUrlsProcessed = stats?.TotalUrlsProcessed ?? 0,
                totalUrlsSuccess = stats?.TotalUrlsSuccess ?? 0,
                totalUrlsFailed = stats?.TotalUrlsFailed ?? 0,
                totalUrlsRejected = stats?.TotalUrlsRejected ?? 0,
                totalPricesCollected = stats?.TotalPricesCollected ?? 0,
                successRate = stats?.SuccessRate ?? 0,
                urlsPerMinute = stats?.UrlsPerMinute ?? 0,
                batchesCompleted = stats?.TotalBatchesCompleted ?? 0,
                batchesTimedOut = stats?.TotalBatchesTimedOut ?? 0,
                currentBatchNumber = stats?.CurrentBatchNumber ?? 0,
                nukeCount = stats?.NukeCount ?? 0,
                activeBatchTaskCount = activeBatch?.TaskIds.Count ?? 0
            });
        }

        private async Task BroadcastDashboard()
        {
            await _hubContext.Clients.All.SendAsync("GoogleUpdateDashboard",
                GoogleScrapeManager.GetDashboardSummary());
        }

        private async Task BroadcastLogs()
        {
            await _hubContext.Clients.All.SendAsync("GoogleUpdateLogs",
                GoogleScrapeManager.GetRecentLogs(20));
        }
    }

    // ===== DTOs =====

    public class GoogleDbStatsDto
    {
        public int TotalUrls { get; set; }
        public int ScrapedUrls { get; set; }
        public int RejectedUrls { get; set; }
        public int PendingUrls { get; set; }
        public int TotalPrices { get; set; }
        public int UrlsWithWRGA { get; set; }
        public int UrlsWithGPID { get; set; }
        public int UrlsWithHidOffer { get; set; }
    }

    public class GoogleUrlDto
    {
        public int Id { get; set; }
        public string? GoogleOfferUrl { get; set; }
        public string? GoogleCid { get; set; }
        public string? GoogleGid { get; set; }
        public string? GoogleHid { get; set; }
        public bool UseGoogleHidOffer { get; set; }
        public bool UseWRGA { get; set; }
        public bool UseGPID { get; set; }
        public bool GoogleIsScraped { get; set; }
        public bool GoogleIsRejected { get; set; }
        public int GooglePricesCount { get; set; }
        public List<int> ProductIdsGoogle { get; set; } = new();
    }

    // ===== BACKGROUND SERVICE =====

    /// <summary>
    /// BackgroundService monitorujący timeouty i status scraperów Google
    /// </summary>
    public class GoogleScrapingMonitorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GoogleScrapingMonitorService> _logger;
        private const int CheckIntervalSeconds = 20;

        public GoogleScrapingMonitorService(
            IServiceProvider serviceProvider,
            ILogger<GoogleScrapingMonitorService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("GoogleScrapingMonitorService uruchomiony.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scrapingService = scope.ServiceProvider.GetRequiredService<GoogleScraperService>();

                    // Sprawdź timeouty paczek
                    var timedOutCount = await scrapingService.CheckAndHandleTimeoutsAsync();
                    if (timedOutCount > 0)
                        _logger.LogWarning("Obsłużono {Count} paczek z timeoutem.", timedOutCount);

                    // Sprawdź nieaktywne scrapery
                    var offlineCount = await scrapingService.CheckAndMarkOfflineScrapersAsync();
                    if (offlineCount > 0)
                        _logger.LogInformation("Oznaczono {Count} scraperów jako offline.", offlineCount);

                    // Sprawdź czy proces się zakończył
                    if (GoogleScrapeManager.CurrentStatus == GoogleScrapingProcessStatus.Running)
                    {
                        var stats = await scrapingService.GetDatabaseStatsAsync();
                        if (stats.PendingUrls == 0 && !GoogleScrapeManager.HasActiveBatches())
                        {
                            _logger.LogInformation("Wszystkie URLe przetworzone. Kończę proces.");
                            GoogleScrapeManager.FinishProcess();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd w GoogleScrapingMonitorService.");
                }

                await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), stoppingToken);
            }

            _logger.LogInformation("GoogleScrapingMonitorService zatrzymany.");
        }
    }
}