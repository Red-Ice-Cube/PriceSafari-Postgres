//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.DependencyInjection;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using PriceSafari.Models;
//using PriceSafari.Services.ControlNetwork;

//namespace PriceSafari.Services.ScheduleService
//{
//    public class GoogleScraperService
//    {
//        private readonly PriceSafariContext _context;
//        private readonly IHubContext<ScrapingHub> _hubContext;
//        private readonly IServiceScopeFactory _scopeFactory;
//        private readonly INetworkControlService _networkControlService;

//        // do synchronizacji i limitowania automatycznych resetów
//        private const int MAX_CONSECUTIVE_CAPTCHA_RESETS = 10;

//        public GoogleScraperService(
//            PriceSafariContext context,
//            IHubContext<ScrapingHub> hubContext,
//            IServiceScopeFactory scopeFactory,
//            INetworkControlService networkControlService)
//        {
//            _context = context;
//            _hubContext = hubContext;
//            _scopeFactory = scopeFactory;
//            _networkControlService = networkControlService;
//        }

//        public enum GoogleScrapingResult
//        {
//            Success,
//            SettingsNotFound,
//            NoProductsToScrape,
//            Error,
//            CaptchaDetected
//        }

//        public record GoogleScrapingDto(
//            GoogleScrapingResult Result,
//            int TotalScraped,
//            int TotalRejected,
//            int CaptchaResets,
//            string? Message
//        );


//        // W PriceSafari.Services.ScheduleService.GoogleScraperService.cs

//        public async Task<GoogleScrapingDto> StartScraping()
//        {
//            var settings = await _context.Settings.FirstOrDefaultAsync(); // Rozważ użycie _scopeFactory, jeśli _context ma krótki cykl życia
//            if (settings == null)
//            {
//                Console.WriteLine("Settings not found in the database."); // Użyj ILogger
//                return new GoogleScrapingDto(
//                    GoogleScrapingResult.SettingsNotFound,
//                    0, 0, 0,
//                    "Settings not found."
//                );
//            }

//            int totalScrapedOverall = 0;
//            int totalRejectedOverall = 0;
//            int captchaResetCount = 0;
//            var stopwatch = Stopwatch.StartNew();

//            for (int attempt = 0; attempt <= MAX_CONSECUTIVE_CAPTCHA_RESETS; attempt++)
//            {
//                List<CoOfrClass> coOfrsToScrapeThisAttempt;
//                using (var scope = _scopeFactory.CreateScope())
//                {
//                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//                    coOfrsToScrapeThisAttempt = await scopedContext.CoOfrs
//                        .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
//                        .ToListAsync();
//                }

//                if (!coOfrsToScrapeThisAttempt.Any())
//                {
//                    // Użyj ILogger zamiast Console.WriteLine
//                    Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: No products left to scrape for Google.");
//                    if (attempt == 0)
//                    {
//                        return new GoogleScrapingDto(
//                            GoogleScrapingResult.NoProductsToScrape,
//                            totalScrapedOverall, totalRejectedOverall, captchaResetCount,
//                            "No Google products to scrape initially."
//                        );
//                    }
//                    else
//                    {
//                        return new GoogleScrapingDto(
//                            GoogleScrapingResult.Success,
//                            totalScrapedOverall, totalRejectedOverall, captchaResetCount,
//                            "All Google products scraped successfully after retries."
//                        );
//                    }
//                }

//                // Użyj ILogger
//                Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: Found {coOfrsToScrapeThisAttempt.Count} products to scrape.");
//                // Rozważ, czy ten ProgressUpdate jest potrzebny tutaj, czy wystarczą te z PerformScrapingLogicInternalAsyncWithCaptchaFlag
//                // oraz ReceiveGeneralMessage
//                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Rozpoczynam próbę {attempt + 1} z {coOfrsToScrapeThisAttempt.Count} produktami.", CancellationToken.None);


//                // ----- POCZĄTEK POPRAWKI -----
//                // Usunięto pierwsze, nadmiarowe wywołanie. Pozostaje tylko jedno, poprawne wywołanie:
//                bool captchaDetectedActual = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(
//                    coOfrsToScrapeThisAttempt,
//                    settings,
//                    stopwatch,
//                    (deltaScraped, deltaRejected) => // Callback aktualizujący liczniki globalne
//                    {
//                        Interlocked.Add(ref totalScrapedOverall, deltaScraped);
//                        Interlocked.Add(ref totalRejectedOverall, deltaRejected);
//                    }
//                );
//                // ----- KONIEC POPRAWKI -----

//                if (!captchaDetectedActual)
//                {
//                    // Użyj ILogger
//                    Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: No CAPTCHA. Overall scraped: {totalScrapedOverall}, rejected: {totalRejectedOverall}.");
//                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Scrapowanie zakończone pomyślnie w próbie {attempt + 1}. Całkowicie zebrano: {totalScrapedOverall}, odrzucono: {totalRejectedOverall}.", CancellationToken.None);
//                    return new GoogleScrapingDto(
//                        GoogleScrapingResult.Success,
//                        totalScrapedOverall,
//                        totalRejectedOverall,
//                        captchaResetCount, // Liczba resetów, które wystąpiły PRZED tym udanym przebiegiem
//                        $"Google: Scrapowanie zakończone. Zebrano: {totalScrapedOverall}, odrzucono: {totalRejectedOverall}. Resetów CAPTCHA: {captchaResetCount}."
//                    );
//                }

//                // Wykryto CAPTCHA w tym przebiegu
//                captchaResetCount++;
//                // Użyj ILogger
//                Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: CAPTCHA detected. Reset count: {captchaResetCount}.");

//                if (attempt == MAX_CONSECUTIVE_CAPTCHA_RESETS) // Sprawdź, czy to była ostatnia dozwolona próba
//                {
//                    // Użyj ILogger
//                    Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: CAPTCHA persisted after max ({captchaResetCount}) resets.");
//                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: CAPTCHA utrzymuje się po {captchaResetCount} próbach resetu (maksimum). Wymagana ręczna interwencja. Zebrano: {totalScrapedOverall}, odrzucono: {totalRejectedOverall}.", CancellationToken.None);
//                    return new GoogleScrapingDto(
//                        GoogleScrapingResult.CaptchaDetected,
//                        totalScrapedOverall,
//                        totalRejectedOverall,
//                        captchaResetCount,
//                        $"Google: CAPTCHA utrzymuje się po {captchaResetCount} próbach resetu. Wymagana ręczna interwencja. W tej sesji zebrano: {totalScrapedOverall}, Odrzucono: {totalRejectedOverall}."
//                    );
//                }

//                // Logika resetu sieci, jeśli wykryto CAPTCHA i dozwolone są kolejne próby
//                // Użyj ILogger
//                Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: Attempting network reset ({captchaResetCount}/{MAX_CONSECUTIVE_CAPTCHA_RESETS}).");
//                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Wykryto CAPTCHA (Próba {attempt + 1}, Reset {captchaResetCount}/{MAX_CONSECUTIVE_CAPTCHA_RESETS}). Próba resetu sieci...", CancellationToken.None);
//                bool resetOk = false;
//                try
//                {
//                    resetOk = await _networkControlService.TriggerNetworkDisableAndResetAsync();
//                }
//                catch (Exception ex)
//                {
//                    // Użyj ILogger
//                    Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: Error during network reset ({captchaResetCount}): {ex.Message}");
//                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Błąd podczas resetu sieci (Próba {attempt + 1}, Reset {captchaResetCount}): {ex.Message}", CancellationToken.None);
//                }

//                await _hubContext.Clients.All.SendAsync(
//                    "ReceiveGeneralMessage",
//                    resetOk
//                        ? $"Google: Reset sieci udany (Próba {attempt + 1}, Reset {captchaResetCount}). Ponawiam scrapowanie (następna próba: {attempt + 2})..."
//                        : $"Google: Reset sieci NIEUDANY (Próba {attempt + 1}, Reset {captchaResetCount}). Mimo to ponawiam scrapowanie (następna próba: {attempt + 2})...",
//                    CancellationToken.None);

//                await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None); // Rozważ przekazanie CancellationToken z `RunGoogleAsync` jeśli scrapowanie ma być anulowalne
//            }

//            // Ten punkt jest osiągany, jeśli pętla zakończy wszystkie iteracje (MAX_CONSECUTIVE_CAPTCHA_RESETS + 1)
//            // Oznacza to, że CAPTCHA wystąpiła w ostatniej dozwolonej próbie,
//            // a warunek `if (attempt == MAX_CONSECUTIVE_CAPTCHA_RESETS)` wewnątrz pętli już obsłużył ten przypadek i zwrócił wynik.
//            // Ten return jest więc awaryjny.
//            // Użyj ILogger
//            Console.WriteLine($"[GoogleService] Exited loop unexpectedly after {captchaResetCount} resets. Overall scraped: {totalScrapedOverall}, rejected: {totalRejectedOverall}.");
//            return new GoogleScrapingDto(
//                GoogleScrapingResult.Error, // Lub CaptchaDetected, jeśli captchaResetCount > 0
//                totalScrapedOverall,
//                totalRejectedOverall,
//                captchaResetCount,
//                captchaResetCount > 0 ? $"Google: Scrapowanie zatrzymane po {captchaResetCount} resetach CAPTCHA i wyczerpaniu prób." : "Google: Nieoczekiwane zakończenie pętli scrapowania."
//            );
//        }

//        private async Task<bool> PerformScrapingLogicInternalAsyncWithCaptchaFlag(
//            List<CoOfrClass> coOfrsForThisRun, // Zmieniono nazwę dla jasności
//            Settings settings,
//            Stopwatch overallStopwatch,
//            Action<int, int> accumulateProgressCallback) // Nazwa callbacku zmieniona dla jasności
//        {
//            bool captchaDetectedInThisRun = false;

//            int processedInThisSpecificRun = 0;
//            int rejectedInThisSpecificRun = 0;

//            int maxConcurrent = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
//            var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
//            var tasks = new List<Task>();
//            // Użyj przekazanej listy dla tego uruchomienia
//            var queue = new Queue<CoOfrClass>(coOfrsForThisRun);


//            for (int i = 0; i < maxConcurrent; i++)
//            {
//                tasks.Add(Task.Run(async () =>
//                {
//                    await semaphore.WaitAsync();
//                    var scraper = new GoogleMainPriceScraper();
//                    await scraper.InitializeAsync(settings);
//                    try
//                    {
//                        while (true)
//                        {
//                            if (captchaDetectedInThisRun) break;

//                            CoOfrClass item = null;
//                            lock (queue)
//                            {
//                                if (queue.Count > 0)
//                                    item = queue.Dequeue();
//                            }
//                            if (item == null) break;

//                            try
//                            {
//                                using var scope = _scopeFactory.CreateScope();
//                                var db = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

//                                var prices = await scraper.ScrapePricesAsync(item.GoogleOfferUrl);

//                                var trackedItem = await db.CoOfrs.FindAsync(item.Id);
//                                if (trackedItem == null)
//                                {
//                                    Interlocked.Increment(ref processedInThisSpecificRun);
//                                    // Log error or decide how to handle
//                                    continue;
//                                }

//                                if (prices.Any())
//                                {
//                                    foreach (var p in prices) p.CoOfrClassId = trackedItem.Id;
//                                    db.CoOfrPriceHistories.AddRange(prices);
//                                    trackedItem.GoogleIsScraped = true;
//                                    trackedItem.GoogleIsRejected = false;
//                                    trackedItem.GooglePricesCount = prices.Count;
//                                    accumulateProgressCallback(1, 0); // Aktualizuj liczniki globalne
//                                }
//                                else
//                                {
//                                    trackedItem.GoogleIsScraped = true;
//                                    trackedItem.GoogleIsRejected = true;
//                                    trackedItem.GooglePricesCount = 0;
//                                    accumulateProgressCallback(0, 1); // Aktualizuj liczniki globalne
//                                    Interlocked.Increment(ref rejectedInThisSpecificRun);
//                                }
//                                // db.CoOfrs.Update(trackedItem); // Niepotrzebne jeśli FindAsync śledzi
//                                await db.SaveChangesAsync();
//                                Interlocked.Increment(ref processedInThisSpecificRun);


//                                double elapsed = overallStopwatch.Elapsed.TotalSeconds;

//                                await _hubContext.Clients.All.SendAsync(
//                                    "ReceiveProgressUpdate",
//                                    processedInThisSpecificRun,
//                                    coOfrsForThisRun.Count,
//                                    elapsed,
//                                    rejectedInThisSpecificRun // Odrzucone tylko w tym przebiegu
//                                );

//                            }
//                            catch (CaptchaDetectedException)
//                            {
//                                captchaDetectedInThisRun = true;
//                                break;
//                            }
//                            catch (Exception ex)
//                            {
//                                Console.WriteLine($"Google: Error scraping item {item?.Id} in service: {ex.Message}"); // Lub ILogger

//                            }
//                        }
//                    }
//                    finally
//                    {
//                        await scraper.CloseAsync();
//                        semaphore.Release();
//                    }
//                }));
//            }

//            await Task.WhenAll(tasks);
//            return captchaDetectedInThisRun;
//        }
//    }
//}





using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging; // Import ILogger
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Services.ControlNetwork;

namespace PriceSafari.Services.ScheduleService
{
    public class GoogleScraperService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly INetworkControlService _networkControlService;
        private readonly ILogger<GoogleScraperService> _logger; // Dodajemy logger

        private const int MAX_NETWORK_RESETS = 3; // Zmieniono nazwę dla jasności

        public GoogleScraperService(
            IServiceScopeFactory scopeFactory,
            IHubContext<ScrapingHub> hubContext,
            INetworkControlService networkControlService,
            ILogger<GoogleScraperService> logger) // Wstrzykujemy logger
        {
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
            _networkControlService = networkControlService;
            _logger = logger;
        }

        public enum GoogleScrapingResult
        {
            Success,
            SettingsNotFound,
            NoProductsToScrape,
            Error,
            PersistentBlock // Zastępuje CaptchaDetected
        }

        public record GoogleScrapingDto(
            GoogleScrapingResult Result,
            int TotalScraped,
            int TotalRejected,
            int NetworkResets,
            string? Message
        );

        // ZMIANA: Całkowicie nowa, uproszczona implementacja
        public async Task<GoogleScrapingDto> StartScraping()
        {
            int totalScrapedOverall = 0;
            int totalRejectedOverall = 0;
            int networkResetCount = 0;
            var stopwatch = Stopwatch.StartNew();

            for (int attempt = 0; attempt <= MAX_NETWORK_RESETS; attempt++)
            {
                (bool success, int scrapedThisRun, int rejectedThisRun) result;

                // Zamykamy logikę scrapowania w bloku try-catch, aby obsłużyć błędy krytyczne
                try
                {
                    result = await PerformSingleScrapingRun(stopwatch);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GoogleService] A critical error occurred during a scraping run.");
                    return new GoogleScrapingDto(GoogleScrapingResult.Error, totalScrapedOverall, totalRejectedOverall, networkResetCount, $"A critical error occurred: {ex.Message}");
                }

                totalScrapedOverall += result.scrapedThisRun;
                totalRejectedOverall += result.rejectedThisRun;

                // Jeśli operacja się powiodła (nie było uporczywego bloku)
                if (result.success)
                {
                    _logger.LogInformation($"[GoogleService] Scraping run successful. Overall scraped: {totalScrapedOverall}, rejected: {totalRejectedOverall}.");
                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Scrapowanie zakończone. Zebrano: {totalScrapedOverall}, odrzucono: {totalRejectedOverall}.", CancellationToken.None);
                    return new GoogleScrapingDto(GoogleScrapingResult.Success, totalScrapedOverall, totalRejectedOverall, networkResetCount, "Scraping completed successfully.");
                }

                // Jeśli wystąpił błąd (uporczywa blokada)
                networkResetCount++;
                _logger.LogWarning($"[GoogleService] Persistent block detected on attempt {attempt + 1}. Reset count: {networkResetCount}.");

                // Jeśli to była ostatnia dozwolona próba
                if (attempt == MAX_NETWORK_RESETS)
                {
                    _logger.LogError($"[GoogleService] Persistent block remained after max ({networkResetCount}) resets.");
                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Blokada utrzymuje się po {networkResetCount} próbach resetu. Wymagana ręczna interwencja.", CancellationToken.None);
                    return new GoogleScrapingDto(GoogleScrapingResult.PersistentBlock, totalScrapedOverall, totalRejectedOverall, networkResetCount, $"Persistent block after {networkResetCount} resets. Manual intervention required.");
                }

                // Próba resetu sieci
                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Wykryto blokadę (Reset {networkResetCount}/{MAX_NETWORK_RESETS}). Próba resetu sieci...", CancellationToken.None);

                try
                {
                    await _networkControlService.TriggerNetworkDisableAndResetAsync();
                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google: Reset sieci udany. Ponawiam scrapowanie za 10 sekund...", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GoogleService] Error during network reset.");
                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Błąd podczas resetu sieci: {ex.Message}", CancellationToken.None);
                }

                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            // Ten kod nie powinien być osiągnięty, ale jest zabezpieczeniem
            return new GoogleScrapingDto(GoogleScrapingResult.Error, totalScrapedOverall, totalRejectedOverall, networkResetCount, "Unexpected end of scraping process.");
        }

        // ZMIANA: Nowa, uproszczona metoda do wykonania pojedynczego przebiegu scrapowania
        private async Task<(bool success, int scraped, int rejected)> PerformSingleScrapingRun(Stopwatch stopwatch)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
            var settings = await dbContext.Settings.FirstOrDefaultAsync();

            if (settings == null)
            {
                _logger.LogError("[GoogleService] Settings not found in the database.");
                throw new InvalidOperationException("Settings not found.");
            }

            var coOfrsToScrape = await dbContext.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
                .ToListAsync();

            if (!coOfrsToScrape.Any())
            {
                return (success: true, scraped: 0, rejected: 0); // Sukces, bo nie ma nic do zrobienia
            }

            _logger.LogInformation($"[GoogleService] Starting a new run with {coOfrsToScrape.Count} products.");

            int scrapedThisRun = 0;
            int rejectedThisRun = 0;
            int persistentFailures = 0; // Licznik produktów, których nie udało się pobrać w tym przebiegu

            int maxConcurrent = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
            var semaphore = new SemaphoreSlim(maxConcurrent);
            var tasks = new List<Task>();
            var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

            for (int i = 0; i < maxConcurrent; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var scraper = new GoogleMainPriceScraper(); // Lekka instancja, bez Initialize/Close

                        while (true)
                        {
                            CoOfrClass item = null;
                            lock (productQueue)
                            {
                                if (productQueue.Count > 0) item = productQueue.Dequeue();
                            }
                            if (item == null) break;

                            try
                            {
                                using var productScope = _scopeFactory.CreateScope();
                                var db = productScope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                var prices = await scraper.ScrapePricesAsync(item);

                                var trackedItem = await db.CoOfrs.FindAsync(item.Id);
                                if (trackedItem == null) continue;

                                if (prices != null && prices.Any())
                                {
                                    foreach (var p in prices) p.CoOfrClassId = trackedItem.Id;
                                    db.CoOfrPriceHistories.AddRange(prices);
                                    trackedItem.GoogleIsScraped = true;
                                    trackedItem.GoogleIsRejected = false;
                                    trackedItem.GooglePricesCount = prices.Count;
                                    Interlocked.Increment(ref scrapedThisRun);
                                }
                                else
                                {
                                    trackedItem.GoogleIsScraped = true;
                                    trackedItem.GoogleIsRejected = true;
                                    trackedItem.GooglePricesCount = 0;
                                    Interlocked.Increment(ref rejectedThisRun);

                                    // Jeśli scraper zwrócił pustą listę, może to być sygnał błędu/bloku
                                    if (prices == null) Interlocked.Increment(ref persistentFailures);
                                }

                                await db.SaveChangesAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"[GoogleService] Error scraping item {item?.Id}.");
                                Interlocked.Increment(ref rejectedThisRun);
                                Interlocked.Increment(ref persistentFailures);
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            _logger.LogInformation($"[GoogleService] Run finished. Scraped: {scrapedThisRun}, Rejected: {rejectedThisRun}, Failures: {persistentFailures}");

            // Uznajemy przebieg za nieudany (blokada), jeśli ponad 50% prób zakończyło się błędem
            bool wasSuccess = persistentFailures <= (coOfrsToScrape.Count / 2);
            return (success: wasSuccess, scraped: scrapedThisRun, rejected: rejectedThisRun);
        }
    }
}





