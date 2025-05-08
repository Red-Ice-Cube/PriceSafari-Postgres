using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection; // Dla IServiceScopeFactory
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Services.ControlNetwork;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class GoogleMainPriceScraperController : Controller
{
    // Usunięto _context jako pole klasy, będziemy pobierać z zakresu
    private readonly IHubContext<ScrapingHub> _hubContext;
    private readonly INetworkControlService _networkControlService;
    private readonly ILogger<GoogleMainPriceScraperController> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory; // Wstrzyknięta fabryka zakresów

    private static CancellationTokenSource _googleCancellationTokenSource = new CancellationTokenSource();
    private static readonly object _cancellationTokenLock = new object();
    private static volatile bool _captchaGlobalSignal = false;

    private static volatile bool _isNetworkResetInProgress = false;
    private static readonly object _networkResetProcessLock = new object();
    private static int _consecutiveCaptchaResets = 0;
    private const int MAX_CONSECUTIVE_CAPTCHA_RESETS = 3;

    public GoogleMainPriceScraperController(
        IHubContext<ScrapingHub> hubContext, // Usunięto PriceSafariContext z konstruktora
        INetworkControlService networkControlService,
        ILogger<GoogleMainPriceScraperController> logger,
        IServiceScopeFactory serviceScopeFactory) // Dodano IServiceScopeFactory
    {
        _hubContext = hubContext;
        _networkControlService = networkControlService;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory; // Przypisanie
    }


    private void PrepareForNewScrapingSession(bool triggeredByCaptcha = false)
    {
        lock (_cancellationTokenLock)
        {
            if (_googleCancellationTokenSource != null && !_googleCancellationTokenSource.IsCancellationRequested)
            {
                _logger.LogInformation("Previous CancellationToken is being cancelled.");
                _googleCancellationTokenSource.Cancel();
            }
            _googleCancellationTokenSource?.Dispose(); // Utylizuj stary
            _googleCancellationTokenSource = new CancellationTokenSource();
            _logger.LogInformation("New CancellationTokenSource created for Google scraping.");

            if (!triggeredByCaptcha)
            {
                _captchaGlobalSignal = false;
                _logger.LogInformation("CAPTCHA signal flag reset for a new manual session.");
            }
        }
    }

    private void SignalCaptchaAndCancelTasks()
    {
        lock (_cancellationTokenLock)
        {
            if (!_captchaGlobalSignal)
            {
                _logger.LogWarning("CAPTCHA DETECTED GLOBALLY! Initiating shutdown of current Google scraping tasks.");
                _captchaGlobalSignal = true;
            }
            if (_googleCancellationTokenSource != null && !_googleCancellationTokenSource.IsCancellationRequested)
            {
                _googleCancellationTokenSource.Cancel();
                _logger.LogInformation("CancellationToken cancelled due to CAPTCHA detection.");
            }
        }
    }


    [HttpPost]
    public IActionResult StopScrapingGoogle()
    {
        _logger.LogInformation("StopScrapingGoogle action called by user.");
        lock (_networkResetProcessLock)
        {
            PrepareForNewScrapingSession(triggeredByCaptcha: false);
            _isNetworkResetInProgress = false;
            _consecutiveCaptchaResets = 0;
        }
        _logger.LogInformation("Google scraping stop requested. Tasks will be cancelled.");
        return Ok(new { Message = "Scraping stopped for Google. All tasks will be cancelled upon checking token." });
    }

    [HttpPost]
    public async Task<IActionResult> StartScraping() // Akcja HTTP
    {
        _logger.LogInformation("Google StartScraping HTTP action called.");
        CancellationToken cancellationToken;
        lock (_networkResetProcessLock)
        {
            if (_isNetworkResetInProgress)
            {
                _logger.LogWarning("Manual scraping start requested, but network reset is already in progress. Aborting.");
                return Conflict(new { Message = "Network reset is currently in progress. Please wait." });
            }
            PrepareForNewScrapingSession(triggeredByCaptcha: false);
            _consecutiveCaptchaResets = 0;
            cancellationToken = _googleCancellationTokenSource.Token;
        }

        // PerformScrapingLogicInternalAsyncWithCaptchaFlag będzie teraz używać _serviceScopeFactory
        bool captchaWasDetectedInRun = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(cancellationToken);

        if (captchaWasDetectedInRun)
        {
            _logger.LogWarning("CAPTCHA was detected by HTTP action's run. Initiating network reset procedure.");
            _ = Task.Run(() => HandleCaptchaNetworkResetAndRestartAsync()); // Uruchom w tle
            return Ok(new { Message = "CAPTCHA detected. Network reset and automatic restart process initiated." });
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Scraping process was cancelled (manual stop likely). Not redirecting.");
            return Ok(new { Message = "Google scraping process was cancelled." });
        }
        else
        {
            _logger.LogInformation("Google scraping completed successfully via HTTP action. Redirecting...");
            return RedirectToAction("GetUniqueScrapingUrls", "PriceScraping");
        }
    }

    // Ta metoda będzie używać IServiceScopeFactory do uzyskania DbContext
    private async Task<bool> PerformScrapingLogicInternalAsyncWithCaptchaFlag(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Google PerformScrapingLogicInternalAsyncWithCaptchaFlag started. Consecutive CAPTCHA resets: {_consecutiveCaptchaResets}");
        bool captchaDetectedInThisRun = false;

        // Utwórz nowy zakres dla tej operacji
        using (var scope = _serviceScopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>(); // Pobierz świeży DbContext
            var settings = await dbContext.Settings.FirstOrDefaultAsync(CancellationToken.None); // Użyj nowego dbContext

            if (settings == null)
            {
                _logger.LogError("Settings not found in the database.");
                return captchaDetectedInThisRun; // captchaDetectedInThisRun jest false
            }

            var coOfrsToScrape = await dbContext.CoOfrs // Użyj nowego dbContext
                .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
                .ToListAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Scraping (internal) cancelled before processing products.");
                return captchaDetectedInThisRun; // captchaDetectedInThisRun jest false
            }

            if (!coOfrsToScrape.Any())
            {
                _logger.LogInformation("No Google products found to scrape (internal).");
                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "No Google products found to scrape.", CancellationToken.None);
                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
                return captchaDetectedInThisRun; // captchaDetectedInThisRun jest false
            }

            _logger.LogInformation($"Found {coOfrsToScrape.Count} Google products to scrape (internal).");
            await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", 0, coOfrsToScrape.Count, 0, 0, CancellationToken.None);

            int totalScrapedCount = 0;
            int totalRejectedCount = 0;
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            int maxConcurrentScrapers = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
            var semaphore = new SemaphoreSlim(maxConcurrentScrapers, maxConcurrentScrapers);
            var tasks = new List<Task>();
            var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

            for (int i = 0; i < maxConcurrentScrapers; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    GoogleMainPriceScraper scraper = null;
                    try
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        if (cancellationToken.IsCancellationRequested) return;

                        scraper = new GoogleMainPriceScraper();
                        await scraper.InitializeAsync(settings); // settings są z zakresu nadrzędnego, to OK
                        if (cancellationToken.IsCancellationRequested) return;

                        while (true)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            CoOfrClass coOfr = null;
                            lock (productQueue) { if (productQueue.Count > 0) coOfr = productQueue.Dequeue(); }
                            if (coOfr == null) break;

                            try
                            {
                                // Każde zadanie scrapujące produkt tworzy własny zakres dla operacji DB
                                using (var productTaskScope = _serviceScopeFactory.CreateScope())
                                {
                                    var productDbContext = productTaskScope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                    _logger.LogDebug($"Task {Task.CurrentId}: Scraping {coOfr.GoogleOfferUrl}");
                                    var scrapedPrices = await scraper.ScrapePricesAsync(coOfr.GoogleOfferUrl);
                                    if (cancellationToken.IsCancellationRequested) break;

                                    // Pobierz śledzoną encję z productDbContext
                                    var coOfrTracked = await productDbContext.CoOfrs.FindAsync(coOfr.Id);
                                    if (coOfrTracked == null)
                                    {
                                        _logger.LogWarning($"Product with ID {coOfr.Id} not found in DB within product task scope.");
                                        continue; // Przejdź do następnego produktu w kolejce
                                    }

                                    if (scrapedPrices.Any())
                                    {
                                        foreach (var ph in scrapedPrices) ph.CoOfrClassId = coOfrTracked.Id;
                                        productDbContext.CoOfrPriceHistories.AddRange(scrapedPrices);
                                        coOfrTracked.GoogleIsScraped = true;
                                        coOfrTracked.GooglePricesCount = scrapedPrices.Count;
                                        coOfrTracked.GoogleIsRejected = false;

                                    }
                                    else
                                    {
                                        coOfrTracked.GoogleIsScraped = true; // Nawet jeśli brak cen, uznajemy za przetworzony
                                        coOfrTracked.GoogleIsRejected = true;
                                        coOfrTracked.GooglePricesCount = 0;
                                        Interlocked.Increment(ref totalRejectedCount);
                                    }
                                    // productDbContext.CoOfrs.Update(coOfrTracked); // Niepotrzebne jeśli encja jest śledzona przez FindAsync
                                    await productDbContext.SaveChangesAsync(CancellationToken.None); // Zapisz zmiany
                                    await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfrTracked.Id, coOfrTracked.GoogleIsScraped, coOfrTracked.GoogleIsRejected, coOfrTracked.GooglePricesCount, "Google", CancellationToken.None);
                                } // productTaskScope jest tutaj utylizowany, wraz z productDbContext
                 
                                Interlocked.Increment(ref totalScrapedCount);
                                // Logika ReceiveProgressUpdate z liczbą odrzuconych produktów
                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                                    // Pobierz aktualną wartość totalRejectedCount bezpiecznie (choć Interlocked już to robi)
                                    int currentTotalRejected = Volatile.Read(ref totalRejectedCount);

                                    await _hubContext.Clients.All.SendAsync(
                                        "ReceiveProgressUpdate",
                                        totalScrapedCount,      // Aktualnie przetworzonych
                                        coOfrsToScrape.Count,   // Wszystkich do przetworzenia
                                        elapsedSeconds,         // Czas, który upłynął
                                        currentTotalRejected,   // <<< LICZBA ODRZUCONYCH PRODUKTÓW
                                        CancellationToken.None
                                    );
                                }
                            }
                            catch (CaptchaDetectedException ex)
                            {
                                _logger.LogWarning(ex, $"Task {Task.CurrentId}: CAPTCHA DETECTED by scraper for product {coOfr?.Id}.");
                                captchaDetectedInThisRun = true; // Ustaw flagę, która zostanie zwrócona
                                SignalCaptchaAndCancelTasks(); // Anuluj token dla innych zadań
                                break; // Wyjdź z pętli while dla tego zadania
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                _logger.LogInformation($"Task {Task.CurrentId}: Operation cancelled for product {coOfr?.Id}.");
                                break;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Task {Task.CurrentId}: Error scraping product {coOfr?.Id}.");
                                if (coOfr != null) { /* ... opcjonalne oznaczanie produktu jako odrzuconego w nowym zakresie ... */ }
                            }
                        } // Koniec pętli while
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation($"Task {Task.CurrentId} cancelled during setup.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Task {Task.CurrentId}: Critical error in task execution.");
                    }
                    finally
                    {
                        if (scraper != null) await scraper.CloseAsync();
                        semaphore.Release();
                        _logger.LogDebug($"Task {Task.CurrentId} finished, semaphore released.");
                    }
                })); // Koniec Task.Run
            } // Koniec pętli for

            await Task.WhenAll(tasks);
            _logger.LogInformation("All Google scraping tasks have completed or been cancelled for this internal run.");
            stopwatch.Stop();

            // Logika powiadomień po zakończeniu tego przebiegu
            if (!captchaDetectedInThisRun && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Internal scraping run completed successfully without CAPTCHA.");
                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google scraping run completed successfully.", CancellationToken.None);
                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; } // Reset licznika CAPTCHA po sukcesie
            }
            else if (!captchaDetectedInThisRun && cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Internal scraping run was cancelled (likely manual stop).");
                // Komunikat o ręcznym zatrzymaniu jest wysyłany z akcji HTTP StartScraping lub StopScrapingGoogle
            }
            // Jeśli captchaDetectedInThisRun, akcja HTTP (StartScraping) obsłuży powiadomienie i reset sieci.

        } // Koniec using (var scope = _serviceScopeFactory.CreateScope())
        return captchaDetectedInThisRun;
    }


    private async Task HandleCaptchaNetworkResetAndRestartAsync()
    {
        bool canAttemptReset;
        lock (_networkResetProcessLock)
        {
            canAttemptReset = !_isNetworkResetInProgress;
            if (canAttemptReset) _isNetworkResetInProgress = true;
        }

        if (!canAttemptReset)
        {
            _logger.LogInformation("Network reset is already in progress. Skipping new attempt for this CAPTCHA event.");
            return;
        }

        try
        {
            if (_consecutiveCaptchaResets >= MAX_CONSECUTIVE_CAPTCHA_RESETS)
            {
                _logger.LogError($"Max consecutive CAPTCHA resets ({MAX_CONSECUTIVE_CAPTCHA_RESETS}) reached. Stopping automatic restarts for Google.");
                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google: Max network reset attempts after CAPTCHA reached. Manual intervention required.", CancellationToken.None);
                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; } // Zresetuj licznik dla przyszłych ręcznych startów
                return;
            }

            _consecutiveCaptchaResets++; // Inkrementuj tutaj, bo rozpoczynamy próbę
            _logger.LogInformation($"Attempting network reset for Google due to CAPTCHA. Attempt {_consecutiveCaptchaResets}/{MAX_CONSECUTIVE_CAPTCHA_RESETS}.");
            await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: CAPTCHA. Attempting network reset (attempt {_consecutiveCaptchaResets}/{MAX_CONSECUTIVE_CAPTCHA_RESETS})...", CancellationToken.None);

            bool resetSuccess = false;
            try
            {
                resetSuccess = await _networkControlService.TriggerNetworkDisableAndResetAsync();
            }
            catch (Exception netEx)
            {
                _logger.LogError(netEx, "Exception during NetworkControlService.TriggerNetworkDisableAndResetAsync.");
            }

            if (resetSuccess)
            {
                _logger.LogInformation("Network reset successful for Google. Preparing to restart scraping.");
                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google: Network reset successful. Restarting scraping in a moment...", CancellationToken.None);
                await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None); // Czas na stabilizację

                CancellationToken newCancellationTokenForRestart;
                // Przygotuj nowy token i zresetuj flagę CAPTCHA przed restartem
                lock (_cancellationTokenLock)
                {
                    // Anuluj i zutylizuj stary token, jeśli istnieje
                    if (_googleCancellationTokenSource != null && !_googleCancellationTokenSource.IsCancellationRequested)
                    {
                        _googleCancellationTokenSource.Cancel();
                    }
                    _googleCancellationTokenSource?.Dispose();
                    _googleCancellationTokenSource = new CancellationTokenSource(); // Nowy token
                    _captchaGlobalSignal = false; // Zresetuj flagę CAPTCHA
                    newCancellationTokenForRestart = _googleCancellationTokenSource.Token;
                    _logger.LogInformation("Token and CAPTCHA signal reset for automatic restart.");
                }

                _logger.LogInformation("Restarting Google scraping logic automatically after network reset.");
                // Wywołaj logikę wewnętrzną z nowym tokenem. Ona sama zarządza swoim zakresem DbContext.
                bool captchaDetectedAgain = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(newCancellationTokenForRestart);

                if (captchaDetectedAgain)
                {
                    _logger.LogWarning("CAPTCHA detected AGAIN immediately after network reset and restart. Will attempt another reset if limit not reached (via new Task.Run).");
                    // Jeśli CAPTCHA wystąpiła ponownie, HandleCaptchaNetworkResetAndRestartAsync zostanie wywołane
                    // ponownie przez `StartScraping` (jeśli to było pierwotne wywołanie)
                    // lub jeśli to było rekurencyjne, licznik już jest zwiększony.
                    // Rekurencyjne wywołanie z Task.Run, aby uniknąć przepełnienia stosu i pozwolić na kontynuację.
                    _ = Task.Run(() => HandleCaptchaNetworkResetAndRestartAsync());
                }
                // Jeśli nie było CAPTCHA, pomyślnie zrestartowano, a PerformScrapingLogicInternalAsyncWithCaptchaFlag obsłużyło powiadomienia.
                // _consecutiveCaptchaResets zostanie zresetowane w PerformScrapingLogicInternalAsyncWithCaptchaFlag po udanym przebiegu.
            }
            else
            {
                _logger.LogError("Network reset failed for Google. Automatic restart will not occur for this CAPTCHA event.");
                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google: Network reset failed. Scraping will not restart automatically this time.", CancellationToken.None);
                // Rozważ zresetowanie _consecutiveCaptchaResets tutaj, jeśli chcesz, aby następny ręczny start miał nową pulę prób.
                // lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in HandleCaptchaNetworkResetAndRestartAsync for Google.");
            await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google: Critical error during network reset and restart process.", CancellationToken.None);
        }
        finally
        {
            lock (_networkResetProcessLock)
            {
                _isNetworkResetInProgress = false;
                _logger.LogInformation("Network reset and restart process concluded. _isNetworkResetInProgress set to false.");
            }
        }
    }
}


//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using System.Diagnostics;


//public class GoogleMainPriceScraperController : Controller
//{
//    private readonly PriceSafariContext _context;
//    private readonly IHubContext<ScrapingHub> _hubContext;
//    private static CancellationTokenSource _googleCancellationTokenSource = new CancellationTokenSource();

//    public GoogleMainPriceScraperController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
//    {
//        _context = context;
//        _hubContext = hubContext;
//    }


//    private void ResetCancellationToken()
//    {
//        if (_googleCancellationTokenSource != null)
//        {
//            _googleCancellationTokenSource.Cancel();
//            _googleCancellationTokenSource.Dispose();
//        }
//        _googleCancellationTokenSource = new CancellationTokenSource();
//    }


//    [HttpPost]
//    public IActionResult StopScrapingGoogle()
//    {

//        ResetCancellationToken();
//        return Ok(new { Message = "Scraping stopped for Google." });
//    }

//    [HttpPost]
//    public async Task<IActionResult> StartScraping()
//    {
//        // Na starcie ustawiamy nowy token
//        ResetCancellationToken();
//        var cancellationToken = _googleCancellationTokenSource.Token;

//        // Get settings from the database
//        var settings = await _context.Settings.FirstOrDefaultAsync();
//        if (settings == null)
//        {
//            Console.WriteLine("Settings not found in the database.");
//            return BadRequest("Settings not found.");
//        }

//        // Get all CoOfrClass entries with a non-empty GoogleOfferUrl that haven't been scraped yet
//        var coOfrsToScrape = await _context.CoOfrs
//            .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
//            .ToListAsync();

//        if (!coOfrsToScrape.Any())
//        {
//            Console.WriteLine("No products found to scrape.");
//            return NotFound("No products found to scrape.");
//        }

//        Console.WriteLine($"Found {coOfrsToScrape.Count} products to scrape.");

//        if (_hubContext != null)
//        {
//            await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", 0, coOfrsToScrape.Count, 0, 0);
//        }
//        else
//        {
//            Console.WriteLine("Hub context is null.");
//        }

//        // Variables to track progress
//        int totalScraped = 0;
//        var stopwatch = new Stopwatch();
//        stopwatch.Start();

//        var serviceScopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();

//        // Get semaphore value from settings
//        int maxConcurrentScrapers = settings.SemophoreGoogle;
//        var semaphore = new SemaphoreSlim(maxConcurrentScrapers);
//        var tasks = new List<Task>();

//        var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

//        for (int i = 0; i < maxConcurrentScrapers; i++)
//        {
//            tasks.Add(Task.Run(async () =>
//            {
//                await semaphore.WaitAsync(cancellationToken);

//                var scraper = new GoogleMainPriceScraper();
//                if (scraper == null)
//                {
//                    Console.WriteLine("Scraper object is null.");
//                    semaphore.Release();
//                    return;
//                }

//                await scraper.InitializeAsync(settings);

//                try
//                {
//                    while (true)
//                    {
//                        // Sprawdzamy, czy nie nastąpiło anulowanie z zewnątrz:
//                        if (cancellationToken.IsCancellationRequested)
//                        {
//                            Console.WriteLine("Scraping (Google) was canceled by user request.");
//                            break;
//                        }

//                        CoOfrClass coOfr = null;

//                        lock (productQueue)
//                        {
//                            if (productQueue.Count > 0)
//                            {
//                                coOfr = productQueue.Dequeue();
//                            }
//                        }

//                        if (coOfr == null)
//                        {
//                            break;
//                        }

//                        try
//                        {
//                            using (var scope = serviceScopeFactory.CreateScope())
//                            {
//                                var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

//                                Console.WriteLine($"Starting scraping for URL: {coOfr.GoogleOfferUrl}");

//                                // Scrape
//                                var scrapedPrices = await scraper.ScrapePricesAsync(coOfr.GoogleOfferUrl);

//                                if (scrapedPrices.Any())
//                                {
//                                    // Set CoOfrClassId for each entry
//                                    foreach (var priceHistory in scrapedPrices)
//                                    {
//                                        // Zakładamy, że "scrapedPrices" jest listą obiektów
//                                        // CoOfrPriceHistoryClass - tak, jak w Twoim kodzie.
//                                        priceHistory.CoOfrClassId = coOfr.Id;
//                                    }

//                                    // Save all offers at once after processing the URL
//                                    scopedContext.CoOfrPriceHistories.AddRange(scrapedPrices);
//                                    await scopedContext.SaveChangesAsync();
//                                    Console.WriteLine($"Saved {scrapedPrices.Count} offers to the database for product {coOfr.GoogleOfferUrl}.");

//                                    // Update the product status after saving its offers
//                                    coOfr.GoogleIsScraped = true;
//                                    coOfr.GooglePricesCount = scrapedPrices.Count;

//                                    scopedContext.CoOfrs.Update(coOfr);
//                                    await scopedContext.SaveChangesAsync();
//                                    Console.WriteLine($"Updated status and offer count for product {coOfr.Id}: {coOfr.GooglePricesCount}.");

//                                    // Send update via SignalR
//                                    await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate",
//                                        coOfr.Id, coOfr.GoogleIsScraped, coOfr.GoogleIsRejected, coOfr.GooglePricesCount, "Google");
//                                }
//                                else
//                                {
//                                    // No prices scraped, mark as rejected
//                                    coOfr.GoogleIsScraped = true;
//                                    coOfr.GoogleIsRejected = true;
//                                    coOfr.GooglePricesCount = 0;

//                                    scopedContext.CoOfrs.Update(coOfr);
//                                    await scopedContext.SaveChangesAsync();

//                                    await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate",
//                                        coOfr.Id, coOfr.GoogleIsScraped, coOfr.GoogleIsRejected, coOfr.GooglePricesCount, "Google");
//                                }

//Interlocked.Increment(ref totalScraped);
//double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
//await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate",
//    totalScraped, coOfrsToScrape.Count, elapsedSeconds, 0);
//                            }
//                        }
//                        catch (Exception ex)
//                        {
//                            Console.WriteLine($"Error during scraping product {coOfr.Id}: {ex.Message}");
//                        }
//                    }
//                }
//                finally
//                {
//                    await scraper.CloseAsync();
//                    semaphore.Release();
//                }
//            }, cancellationToken));
//        }

//        await Task.WhenAll(tasks);
//        Console.WriteLine("All tasks completed.");

//        stopwatch.Stop();

//        return RedirectToAction("GetUniqueScrapingUrls", "PriceScraping");
//    }
//}
