//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.DependencyInjection;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using PriceSafari.Models;
//using PriceSafari.Services.ControlNetwork;
//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;
//using PriceSafari.Services;

//public class GoogleMainPriceScraperController : Controller
//{

//    private readonly IHubContext<ScrapingHub> _hubContext;
//    private readonly INetworkControlService _networkControlService;
//    private readonly ILogger<GoogleMainPriceScraperController> _logger;
//    private readonly IServiceScopeFactory _serviceScopeFactory;

//    private static CancellationTokenSource _googleCancellationTokenSource = new CancellationTokenSource();
//    private static readonly object _cancellationTokenLock = new object();
//    private static volatile bool _captchaGlobalSignal = false;

//    private static volatile bool _isNetworkResetInProgress = false;
//    private static readonly object _networkResetProcessLock = new object();
//    private static int _consecutiveCaptchaResets = 0;
//    private const int MAX_CONSECUTIVE_CAPTCHA_RESETS = 3;

//    public GoogleMainPriceScraperController(
//        IHubContext<ScrapingHub> hubContext,
//        INetworkControlService networkControlService,
//        ILogger<GoogleMainPriceScraperController> logger,
//        IServiceScopeFactory serviceScopeFactory)
//    {
//        _hubContext = hubContext;
//        _networkControlService = networkControlService;
//        _logger = logger;
//        _serviceScopeFactory = serviceScopeFactory;
//    }

//    private void PrepareForNewScrapingSession(bool triggeredByCaptcha = false)
//    {
//        lock (_cancellationTokenLock)
//        {
//            if (_googleCancellationTokenSource != null && !_googleCancellationTokenSource.IsCancellationRequested)
//            {
//                _logger.LogInformation("Previous CancellationToken is being cancelled.");
//                _googleCancellationTokenSource.Cancel();
//            }
//            _googleCancellationTokenSource?.Dispose();
//            _googleCancellationTokenSource = new CancellationTokenSource();
//            _logger.LogInformation("New CancellationTokenSource created for Google scraping.");

//            if (!triggeredByCaptcha)
//            {
//                _captchaGlobalSignal = false;
//                _logger.LogInformation("CAPTCHA signal flag reset for a new manual session.");
//            }
//        }
//    }

//    private void SignalCaptchaAndCancelTasks()
//    {
//        lock (_cancellationTokenLock)
//        {
//            if (!_captchaGlobalSignal)
//            {
//                _logger.LogWarning("CAPTCHA DETECTED GLOBALLY! Initiating shutdown of current Google scraping tasks.");
//                _captchaGlobalSignal = true;
//            }
//            if (_googleCancellationTokenSource != null && !_googleCancellationTokenSource.IsCancellationRequested)
//            {
//                _googleCancellationTokenSource.Cancel();
//                _logger.LogInformation("CancellationToken cancelled due to CAPTCHA detection.");
//            }
//        }
//    }

//    [HttpPost]
//    public IActionResult StopScrapingGoogle()
//    {
//        _logger.LogInformation("StopScrapingGoogle action called by user.");

//        // --- [NOWOŚĆ] ---
//        // To zatrzymuje generatory ciasteczek (Selenium) i czyści magazyn
//        GoogleMainPriceScraper.StopAndCleanUp();
//        // ----------------

//        lock (_networkResetProcessLock)
//        {
//            PrepareForNewScrapingSession(triggeredByCaptcha: false);
//            _isNetworkResetInProgress = false;
//            _consecutiveCaptchaResets = 0;
//        }

//        _logger.LogInformation("Google scraping stop requested. Tasks will be cancelled.");
//        return Ok(new { Message = "Scraping stopped for Google. All tasks will be cancelled upon checking token." });
//    }

//    [HttpPost]
//    public async Task<IActionResult> StartScraping()
//    {
//        _logger.LogInformation("Google StartScraping HTTP action called.");
//        CancellationToken cancellationToken;
//        lock (_networkResetProcessLock)
//        {
//            if (_isNetworkResetInProgress)
//            {
//                _logger.LogWarning("Manual scraping start requested, but network reset is already in progress. Aborting.");
//                return Conflict(new { Message = "Network reset is currently in progress. Please wait." });
//            }
//            PrepareForNewScrapingSession(triggeredByCaptcha: false);
//            _consecutiveCaptchaResets = 0;
//            cancellationToken = _googleCancellationTokenSource.Token;
//        }

//        bool captchaWasDetectedInRun = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(cancellationToken);

//        if (captchaWasDetectedInRun)
//        {
//            _logger.LogWarning("CAPTCHA was detected by HTTP action's run. Initiating network reset procedure.");
//            _ = Task.Run(() => HandleCaptchaNetworkResetAndRestartAsync());
//            return Ok(new { Message = "CAPTCHA detected. Network reset and automatic restart process initiated." });
//        }
//        else if (cancellationToken.IsCancellationRequested)
//        {
//            _logger.LogInformation("Scraping process was cancelled (manual stop likely). Not redirecting.");
//            return Ok(new { Message = "Google scraping process was cancelled." });
//        }
//        else
//        {
//            _logger.LogInformation("Google scraping completed successfully via HTTP action. Redirecting...");
//            return RedirectToAction("GetUniqueScrapingUrls", "PriceScraping");
//        }
//    }








//    private async Task<bool> PerformScrapingLogicInternalAsyncWithCaptchaFlag(CancellationToken cancellationToken)
//    {
//        _logger.LogInformation($"Google PerformScrapingLogicInternalAsyncWithCaptchaFlag started. Consecutive CAPTCHA resets: {_consecutiveCaptchaResets}");

//        bool persistentErrorDetected = false;

//        using (var scope = _serviceScopeFactory.CreateScope())
//        {
//            var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//            var settings = await dbContext.Settings.FirstOrDefaultAsync(CancellationToken.None);

//            if (settings == null)
//            {
//                _logger.LogError("Settings not found in the database.");
//                return persistentErrorDetected;
//            }


//            // --- [NOWOŚĆ] START GENERATORÓW CIASTEK -------------------------------------------
//            // Musimy to odpalić ZANIM zaczniemy kolejkować zadania.
//            // Pobieramy liczbę wątków z bazy (SemophoreGoogle) i dajemy np. 15-20 sek na rozgrzewkę.
//            int botCount = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
//            await GoogleMainPriceScraper.InitializeGeneratorsAsync(botCount, headStartSeconds: 20);
//            // -----------------------------------------------------------------------------------

//            // --- KLUCZOWA ZMIANA TUTAJ ---
//            // Musimy pobrać rekordy, które mają URL LUB mają włączony tryb HID
//            var coOfrsToScrape = await dbContext.CoOfrs
//                .Where(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && !c.GoogleIsScraped)
//                .ToListAsync(cancellationToken);

//            if (cancellationToken.IsCancellationRequested)
//            {
//                _logger.LogInformation("Scraping (internal) cancelled before processing products.");
//                return persistentErrorDetected;
//            }

//            if (!coOfrsToScrape.Any())
//            {
//                _logger.LogInformation("No Google products found to scrape (internal).");
//                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "No Google products found to scrape.", CancellationToken.None);
//                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
//                return persistentErrorDetected;
//            }

//            _logger.LogInformation($"Found {coOfrsToScrape.Count} Google products to scrape (internal).");
//            await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", 0, coOfrsToScrape.Count, 0, 0, CancellationToken.None);

//            int totalScrapedCount = 0;
//            int totalRejectedCount = 0;
//            var stopwatch = new Stopwatch();
//            stopwatch.Start();

//            int maxConcurrentScrapers = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
//            var semaphore = new SemaphoreSlim(maxConcurrentScrapers, maxConcurrentScrapers);
//            var tasks = new List<Task>();
//            var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

//            for (int i = 0; i < maxConcurrentScrapers; i++)
//            {
//                tasks.Add(Task.Run(async () =>
//                {
//                    GoogleMainPriceScraper scraper;
//                    try
//                    {
//                        await semaphore.WaitAsync(cancellationToken);
//                        if (cancellationToken.IsCancellationRequested) return;

//                        scraper = new GoogleMainPriceScraper();

//                        while (true)
//                        {
//                            if (cancellationToken.IsCancellationRequested) break;

//                            CoOfrClass coOfr = null;
//                            lock (productQueue)
//                            {
//                                if (productQueue.Count > 0) coOfr = productQueue.Dequeue();
//                            }
//                            if (coOfr == null) break;

//                            try
//                            {
//                                using (var productTaskScope = _serviceScopeFactory.CreateScope())
//                                {
//                                    var productDbContext = productTaskScope.ServiceProvider.GetRequiredService<PriceSafariContext>();

//                                    // POPRAWKA LOGOWANIA: Jeśli URL jest null, logujemy ID katalogu/oferty
//                                    string identifier = coOfr.UseGoogleHidOffer ? $"HID:{coOfr.GoogleHid}" : coOfr.GoogleOfferUrl;
//                                    _logger.LogDebug($"Task {Task.CurrentId}: Scraping {identifier}");

//                                    var scrapedPrices = await scraper.ScrapePricesAsync(coOfr);

//                                    if (cancellationToken.IsCancellationRequested) break;

//                                    var coOfrTracked = await productDbContext.CoOfrs.FindAsync(coOfr.Id);
//                                    if (coOfrTracked == null) continue;

//                                    if (scrapedPrices.Any())
//                                    {
//                                        foreach (var ph in scrapedPrices)
//                                        {
//                                            ph.CoOfrClassId = coOfrTracked.Id;
//                                            // CID może być null dla HID, co jest poprawne (scraper i tak spróbuje go wyciągnąć)
//                                            ph.GoogleCid = coOfrTracked.GoogleCid;
//                                        }

//                                        productDbContext.CoOfrPriceHistories.AddRange(scrapedPrices);
//                                        coOfrTracked.GoogleIsScraped = true;
//                                        coOfrTracked.GooglePricesCount = scrapedPrices.Count;
//                                        coOfrTracked.GoogleIsRejected = false;
//                                    }
//                                    else
//                                    {
//                                        coOfrTracked.GoogleIsScraped = true;
//                                        coOfrTracked.GoogleIsRejected = true;
//                                        coOfrTracked.GooglePricesCount = 0;
//                                        Interlocked.Increment(ref totalRejectedCount);
//                                    }

//                                    await productDbContext.SaveChangesAsync(CancellationToken.None);
//                                    await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfrTracked.Id, coOfrTracked.GoogleIsScraped, coOfrTracked.GoogleIsRejected, coOfrTracked.GooglePricesCount, "Google", CancellationToken.None);
//                                }

//                                Interlocked.Increment(ref totalScrapedCount);

//                                if (!cancellationToken.IsCancellationRequested)
//                                {
//                                    double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
//                                    int currentTotalRejected = Volatile.Read(ref totalRejectedCount);

//                                    await _hubContext.Clients.All.SendAsync(
//                                        "ReceiveProgressUpdate",
//                                        totalScrapedCount,
//                                        coOfrsToScrape.Count,
//                                        elapsedSeconds,
//                                        currentTotalRejected,
//                                        CancellationToken.None
//                                    );
//                                }
//                            }
//                            // **ZMIANA**: Usunęliśmy blok 'catch (CaptchaDetectedException)'.
//                            // Nowy scraper obsługuje błędy sieciowe wewnętrznie przez ponowienia.
//                            // Ogólny blok 'catch' przechwyci ewentualne inne, nieoczekiwane błędy.
//                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
//                            {
//                                _logger.LogInformation($"Task {Task.CurrentId}: Operation cancelled for product {coOfr?.Id}.");
//                                break;
//                            }
//                            catch (Exception ex)
//                            {
//                                _logger.LogError(ex, $"Task {Task.CurrentId}: Error scraping product {coOfr?.Id}. Product will be skipped.");
//                                // W przyszłości można by tu oznaczać produkt jako odrzucony
//                            }
//                        }
//                    }
//                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
//                    {
//                        _logger.LogInformation($"Task {Task.CurrentId} cancelled during setup.");
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.LogError(ex, $"Task {Task.CurrentId}: Critical error in task execution.");
//                    }
//                    finally
//                    {
//                        // **ZMIANA**: Nie ma już metody CloseAsync(), więc zwalniamy tylko semafor.
//                        semaphore.Release();
//                        _logger.LogDebug($"Task {Task.CurrentId} finished, semaphore released.");
//                    }
//                }));
//            }

//            await Task.WhenAll(tasks);
//            _logger.LogInformation("All Google scraping tasks have completed or been cancelled for this internal run.");
//            stopwatch.Stop();

//            if (!persistentErrorDetected && !cancellationToken.IsCancellationRequested)
//            {
//                _logger.LogInformation("Internal scraping run completed successfully.");
//                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google scraping run completed successfully.", CancellationToken.None);
//                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
//            }
//            else if (!persistentErrorDetected && cancellationToken.IsCancellationRequested)
//            {
//                _logger.LogInformation("Internal scraping run was cancelled (likely manual stop).");
//            }

//        }
//        return persistentErrorDetected;
//    }






//    private async Task HandleCaptchaNetworkResetAndRestartAsync()
//    {
//        bool canAttemptReset;
//        lock (_networkResetProcessLock)
//        {
//            canAttemptReset = !_isNetworkResetInProgress;
//            if (canAttemptReset) _isNetworkResetInProgress = true;
//        }

//        if (!canAttemptReset)
//        {
//            _logger.LogInformation("Network reset is already in progress. Skipping new attempt for this CAPTCHA event.");
//            return;
//        }

//        try
//        {
//            if (_consecutiveCaptchaResets >= MAX_CONSECUTIVE_CAPTCHA_RESETS)
//            {
//                _logger.LogError($"Max consecutive CAPTCHA resets ({MAX_CONSECUTIVE_CAPTCHA_RESETS}) reached. Stopping automatic restarts for Google.");
//                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google: Max network reset attempts after CAPTCHA reached. Manual intervention required.", CancellationToken.None);
//                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
//                return;
//            }

//            _consecutiveCaptchaResets++;
//            _logger.LogInformation($"Attempting network reset for Google due to CAPTCHA. Attempt {_consecutiveCaptchaResets}/{MAX_CONSECUTIVE_CAPTCHA_RESETS}.");
//            await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: CAPTCHA. Attempting network reset (attempt {_consecutiveCaptchaResets}/{MAX_CONSECUTIVE_CAPTCHA_RESETS})...", CancellationToken.None);

//            bool resetSuccess = false;
//            try
//            {
//                resetSuccess = await _networkControlService.TriggerNetworkDisableAndResetAsync();
//            }
//            catch (Exception netEx)
//            {
//                _logger.LogError(netEx, "Exception during NetworkControlService.TriggerNetworkDisableAndResetAsync.");
//            }

//            if (resetSuccess)
//            {
//                _logger.LogInformation("Network reset successful for Google. Preparing to restart scraping.");
//                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google: Network reset successful. Restarting scraping in a moment...", CancellationToken.None);
//                await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);

//                CancellationToken newCancellationTokenForRestart;

//                lock (_cancellationTokenLock)
//                {

//                    if (_googleCancellationTokenSource != null && !_googleCancellationTokenSource.IsCancellationRequested)
//                    {
//                        _googleCancellationTokenSource.Cancel();
//                    }
//                    _googleCancellationTokenSource?.Dispose();
//                    _googleCancellationTokenSource = new CancellationTokenSource();
//                    _captchaGlobalSignal = false;
//                    newCancellationTokenForRestart = _googleCancellationTokenSource.Token;
//                    _logger.LogInformation("Token and CAPTCHA signal reset for automatic restart.");
//                }

//                _logger.LogInformation("Restarting Google scraping logic automatically after network reset.");

//                bool captchaDetectedAgain = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(newCancellationTokenForRestart);

//                if (captchaDetectedAgain)
//                {
//                    _logger.LogWarning("CAPTCHA detected AGAIN immediately after network reset and restart. Will attempt another reset if limit not reached (via new Task.Run).");

//                    _ = Task.Run(() => HandleCaptchaNetworkResetAndRestartAsync());
//                }

//            }
//            else
//            {
//                _logger.LogError("Network reset failed for Google. Automatic restart will not occur for this CAPTCHA event.");
//                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google: Network reset failed. Scraping will not restart automatically this time.", CancellationToken.None);

//            }
//        }
//        catch (Exception ex)
//        {
//            _logger.LogError(ex, "Unhandled exception in HandleCaptchaNetworkResetAndRestartAsync for Google.");
//            await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google: Critical error during network reset and restart process.", CancellationToken.None);
//        }
//        finally
//        {
//            lock (_networkResetProcessLock)
//            {
//                _isNetworkResetInProgress = false;
//                _logger.LogInformation("Network reset and restart process concluded. _isNetworkResetInProgress set to false.");
//            }
//        }
//    }
//}









//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Logging;
//using Microsoft.Extensions.DependencyInjection;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using PriceSafari.Models;
//using PriceSafari.Services.ControlNetwork;
//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;
//using PriceSafari.Services;

//public class GoogleMainPriceScraperController : Controller
//{
//    private readonly IHubContext<ScrapingHub> _hubContext;
//    private readonly INetworkControlService _networkControlService;
//    private readonly ILogger<GoogleMainPriceScraperController> _logger;
//    private readonly IServiceScopeFactory _serviceScopeFactory;

//    private static CancellationTokenSource _googleCancellationTokenSource = new CancellationTokenSource();
//    private static readonly object _cancellationTokenLock = new object();
//    private static volatile bool _captchaGlobalSignal = false;

//    private static volatile bool _isNetworkResetInProgress = false;
//    private static readonly object _networkResetProcessLock = new object();
//    private static int _consecutiveCaptchaResets = 0;
//    private const int MAX_CONSECUTIVE_CAPTCHA_RESETS = 3;

//    public GoogleMainPriceScraperController(
//        IHubContext<ScrapingHub> hubContext,
//        INetworkControlService networkControlService,
//        ILogger<GoogleMainPriceScraperController> logger,
//        IServiceScopeFactory serviceScopeFactory)
//    {
//        _hubContext = hubContext;
//        _networkControlService = networkControlService;
//        _logger = logger;
//        _serviceScopeFactory = serviceScopeFactory;
//    }

//    // --- NOWA METODA POMOCNICZA DLA FIRE-AND-FORGET ---
//    private void FireAndForget(string methodName, params object[] args)
//    {
//        Task.Run(async () =>
//        {
//            try
//            {
//                await _hubContext.Clients.All.SendCoreAsync(methodName, args);
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, $"Failed to send SignalR message: {methodName}");
//            }
//        });
//    }
//    // --------------------------------------------------

//    private void PrepareForNewScrapingSession(bool triggeredByCaptcha = false)
//    {
//        lock (_cancellationTokenLock)
//        {
//            if (_googleCancellationTokenSource != null && !_googleCancellationTokenSource.IsCancellationRequested)
//            {
//                _logger.LogInformation("Previous CancellationToken is being cancelled.");
//                _googleCancellationTokenSource.Cancel();
//            }
//            _googleCancellationTokenSource?.Dispose();
//            _googleCancellationTokenSource = new CancellationTokenSource();
//            _logger.LogInformation("New CancellationTokenSource created for Google scraping.");

//            if (!triggeredByCaptcha)
//            {
//                _captchaGlobalSignal = false;
//                _logger.LogInformation("CAPTCHA signal flag reset for a new manual session.");
//            }
//        }
//    }

//    private void SignalCaptchaAndCancelTasks()
//    {
//        lock (_cancellationTokenLock)
//        {
//            if (!_captchaGlobalSignal)
//            {
//                _logger.LogWarning("CAPTCHA DETECTED GLOBALLY! Initiating shutdown of current Google scraping tasks.");
//                _captchaGlobalSignal = true;
//            }
//            if (_googleCancellationTokenSource != null && !_googleCancellationTokenSource.IsCancellationRequested)
//            {
//                _googleCancellationTokenSource.Cancel();
//                _logger.LogInformation("CancellationToken cancelled due to CAPTCHA detection.");
//            }
//        }
//    }

//    [HttpPost]
//    public IActionResult StopScrapingGoogle()
//    {
//        _logger.LogInformation("StopScrapingGoogle action called by user.");

//        // Zatrzymujemy Batch Processor (zapisujemy resztki z RAMu)
//        // Uwaga: To jest operacja async, ale w controllerze sync puszczamy to w tło lub czekamy
//        Task.Run(async () => await ResultBatchProcessor.StopAndFlushAsync());

//        GoogleMainPriceScraper.StopAndCleanUp();

//        lock (_networkResetProcessLock)
//        {
//            PrepareForNewScrapingSession(triggeredByCaptcha: false);
//            _isNetworkResetInProgress = false;
//            _consecutiveCaptchaResets = 0;
//        }

//        _logger.LogInformation("Google scraping stop requested. Tasks will be cancelled.");
//        return Ok(new { Message = "Scraping stopped for Google. All tasks will be cancelled upon checking token." });
//    }

//    [HttpPost]
//    public async Task<IActionResult> StartScraping()
//    {
//        _logger.LogInformation("Google StartScraping HTTP action called.");
//        CancellationToken cancellationToken;
//        lock (_networkResetProcessLock)
//        {
//            if (_isNetworkResetInProgress)
//            {
//                _logger.LogWarning("Manual scraping start requested, but network reset is already in progress. Aborting.");
//                return Conflict(new { Message = "Network reset is currently in progress. Please wait." });
//            }
//            PrepareForNewScrapingSession(triggeredByCaptcha: false);
//            _consecutiveCaptchaResets = 0;
//            cancellationToken = _googleCancellationTokenSource.Token;
//        }

//        bool captchaWasDetectedInRun = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(cancellationToken);

//        if (captchaWasDetectedInRun)
//        {
//            _logger.LogWarning("CAPTCHA was detected by HTTP action's run. Initiating network reset procedure.");
//            _ = Task.Run(() => HandleCaptchaNetworkResetAndRestartAsync());
//            return Ok(new { Message = "CAPTCHA detected. Network reset and automatic restart process initiated." });
//        }
//        else if (cancellationToken.IsCancellationRequested)
//        {
//            _logger.LogInformation("Scraping process was cancelled (manual stop likely). Not redirecting.");
//            return Ok(new { Message = "Google scraping process was cancelled." });
//        }
//        else
//        {
//            _logger.LogInformation("Google scraping completed successfully via HTTP action. Redirecting...");
//            return RedirectToAction("GetUniqueScrapingUrls", "PriceScraping");
//        }
//    }

//    private async Task<bool> PerformScrapingLogicInternalAsyncWithCaptchaFlag(CancellationToken cancellationToken)
//    {
//        _logger.LogInformation($"Google PerformScrapingLogicInternalAsyncWithCaptchaFlag started. Consecutive CAPTCHA resets: {_consecutiveCaptchaResets}");

//        bool persistentErrorDetected = false;

//        using (var scope = _serviceScopeFactory.CreateScope())
//        {
//            var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//            var settings = await dbContext.Settings.FirstOrDefaultAsync(CancellationToken.None);

//            if (settings == null)
//            {
//                _logger.LogError("Settings not found in the database.");
//                return persistentErrorDetected;
//            }

//            // [BATCHING]: Uruchamiamy procesor
//            ResultBatchProcessor.Initialize(_serviceScopeFactory);

//            int botCount = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
//            await GoogleMainPriceScraper.InitializeGeneratorsAsync(botCount, headStartSeconds: 20);

//            var coOfrsToScrape = await dbContext.CoOfrs
//                .Where(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && !c.GoogleIsScraped)
//                .ToListAsync(cancellationToken);

//            if (cancellationToken.IsCancellationRequested) return persistentErrorDetected;

//            if (!coOfrsToScrape.Any())
//            {
//                _logger.LogInformation("No Google products found to scrape (internal).");
//                FireAndForget("ReceiveGeneralMessage", "No Google products found to scrape.");
//                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
//                return persistentErrorDetected;
//            }

//            _logger.LogInformation($"Found {coOfrsToScrape.Count} Google products to scrape (internal).");
//            FireAndForget("ReceiveProgressUpdate", 0, coOfrsToScrape.Count, 0, 0);

//            int totalScrapedCount = 0;
//            int totalRejectedCount = 0;
//            var stopwatch = new Stopwatch();
//            stopwatch.Start();

//            int maxConcurrentScrapers = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
//            var semaphore = new SemaphoreSlim(maxConcurrentScrapers, maxConcurrentScrapers);
//            var tasks = new List<Task>();
//            var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

//            for (int i = 0; i < maxConcurrentScrapers; i++)
//            {
//                tasks.Add(Task.Run(async () =>
//                {
//                    GoogleMainPriceScraper scraper;
//                    try
//                    {
//                        await semaphore.WaitAsync(cancellationToken);
//                        if (cancellationToken.IsCancellationRequested) return;

//                        scraper = new GoogleMainPriceScraper();

//                        while (true)
//                        {
//                            if (cancellationToken.IsCancellationRequested) break;

//                            CoOfrClass coOfr = null;
//                            lock (productQueue)
//                            {
//                                if (productQueue.Count > 0) coOfr = productQueue.Dequeue();
//                            }
//                            if (coOfr == null) break;

//                            try
//                            {
//                                // Używamy scope TYLKO do pobrania/aktualizacji statusu produktu.
//                                // Zapis dużych danych (historii) idzie przez BatchProcessor.
//                                using (var productTaskScope = _serviceScopeFactory.CreateScope())
//                                {
//                                    var productDbContext = productTaskScope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//                                    string identifier = coOfr.UseGoogleHidOffer ? $"HID:{coOfr.GoogleHid}" : coOfr.GoogleOfferUrl;
//                                    _logger.LogDebug($"Task {Task.CurrentId}: Scraping {identifier}");

//                                    var scrapedPrices = await scraper.ScrapePricesAsync(coOfr);

//                                    if (cancellationToken.IsCancellationRequested) break;

//                                    var coOfrTracked = await productDbContext.CoOfrs.FindAsync(coOfr.Id);
//                                    if (coOfrTracked == null) continue;

//                                    if (scrapedPrices.Any())
//                                    {
//                                        foreach (var ph in scrapedPrices)
//                                        {
//                                            ph.CoOfrClassId = coOfrTracked.Id;
//                                            ph.GoogleCid = coOfrTracked.GoogleCid;
//                                        }

//                                        // [BATCHING]: Zamiast AddRange, wrzucamy do kolejki
//                                        ResultBatchProcessor.Enqueue(scrapedPrices);

//                                        coOfrTracked.GoogleIsScraped = true;
//                                        coOfrTracked.GooglePricesCount = scrapedPrices.Count;
//                                        coOfrTracked.GoogleIsRejected = false;
//                                    }
//                                    else
//                                    {
//                                        coOfrTracked.GoogleIsScraped = true;
//                                        coOfrTracked.GoogleIsRejected = true;
//                                        coOfrTracked.GooglePricesCount = 0;
//                                        Interlocked.Increment(ref totalRejectedCount);
//                                    }

//                                    // Tutaj zapisujemy TYLKO status produktu (szybka operacja)
//                                    await productDbContext.SaveChangesAsync(CancellationToken.None);

//                                    // [SIGNALR ASYNC]
//                                    FireAndForget("ReceiveScrapingUpdate", coOfrTracked.Id, coOfrTracked.GoogleIsScraped, coOfrTracked.GoogleIsRejected, coOfrTracked.GooglePricesCount, "Google");
//                                }

//                                Interlocked.Increment(ref totalScrapedCount);

//                                if (!cancellationToken.IsCancellationRequested)
//                                {
//                                    double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
//                                    int currentTotalRejected = Volatile.Read(ref totalRejectedCount);

//                                    // [SIGNALR ASYNC]
//                                    FireAndForget("ReceiveProgressUpdate", totalScrapedCount, coOfrsToScrape.Count, elapsedSeconds, currentTotalRejected);
//                                }
//                            }
//                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
//                            {
//                                break;
//                            }
//                            catch (Exception ex)
//                            {
//                                _logger.LogError(ex, $"Task {Task.CurrentId}: Error scraping product {coOfr?.Id}. Product will be skipped.");
//                            }
//                        }
//                    }
//                    catch (OperationCanceledException) { }
//                    catch (Exception ex)
//                    {
//                        _logger.LogError(ex, $"Task {Task.CurrentId}: Critical error in task execution.");
//                    }
//                    finally
//                    {
//                        semaphore.Release();
//                    }
//                }));
//            }

//            await Task.WhenAll(tasks);
//            _logger.LogInformation("All Google scraping tasks have completed.");

//            // [BATCHING]: Zapisujemy to, co zostało w kolejce
//            await ResultBatchProcessor.StopAndFlushAsync();

//            stopwatch.Stop();

//            if (!persistentErrorDetected && !cancellationToken.IsCancellationRequested)
//            {
//                _logger.LogInformation("Internal scraping run completed successfully.");
//                FireAndForget("ReceiveGeneralMessage", "Google scraping run completed successfully.");
//                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
//            }
//        }
//        return persistentErrorDetected;
//    }

//    private async Task HandleCaptchaNetworkResetAndRestartAsync()
//    {
//        bool canAttemptReset;
//        lock (_networkResetProcessLock)
//        {
//            canAttemptReset = !_isNetworkResetInProgress;
//            if (canAttemptReset) _isNetworkResetInProgress = true;
//        }

//        if (!canAttemptReset) return;

//        try
//        {
//            if (_consecutiveCaptchaResets >= MAX_CONSECUTIVE_CAPTCHA_RESETS)
//            {
//                FireAndForget("ReceiveGeneralMessage", "Google: Max network reset attempts after CAPTCHA reached. Manual intervention required.");
//                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
//                return;
//            }

//            _consecutiveCaptchaResets++;
//            FireAndForget("ReceiveGeneralMessage", $"Google: CAPTCHA. Attempting network reset (attempt {_consecutiveCaptchaResets}/{MAX_CONSECUTIVE_CAPTCHA_RESETS})...");

//            bool resetSuccess = false;
//            try
//            {
//                resetSuccess = await _networkControlService.TriggerNetworkDisableAndResetAsync();
//            }
//            catch (Exception netEx)
//            {
//                _logger.LogError(netEx, "Exception during NetworkControlService.TriggerNetworkDisableAndResetAsync.");
//            }

//            if (resetSuccess)
//            {
//                FireAndForget("ReceiveGeneralMessage", "Google: Network reset successful. Restarting scraping in a moment...");
//                await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);

//                CancellationToken newCancellationTokenForRestart;

//                lock (_cancellationTokenLock)
//                {
//                    if (_googleCancellationTokenSource != null && !_googleCancellationTokenSource.IsCancellationRequested)
//                    {
//                        _googleCancellationTokenSource.Cancel();
//                    }
//                    _googleCancellationTokenSource?.Dispose();
//                    _googleCancellationTokenSource = new CancellationTokenSource();
//                    _captchaGlobalSignal = false;
//                    newCancellationTokenForRestart = _googleCancellationTokenSource.Token;
//                }

//                bool captchaDetectedAgain = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(newCancellationTokenForRestart);

//                if (captchaDetectedAgain)
//                {
//                    _ = Task.Run(() => HandleCaptchaNetworkResetAndRestartAsync());
//                }
//            }
//            else
//            {
//                FireAndForget("ReceiveGeneralMessage", "Google: Network reset failed. Scraping will not restart automatically this time.");
//            }
//        }
//        catch (Exception ex)
//        {
//            _logger.LogError(ex, "Unhandled exception in HandleCaptchaNetworkResetAndRestartAsync.");
//            FireAndForget("ReceiveGeneralMessage", "Google: Critical error during network reset.");
//        }
//        finally
//        {
//            lock (_networkResetProcessLock)
//            {
//                _isNetworkResetInProgress = false;
//            }
//        }
//    }
//}



















using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
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
using PriceSafari.Services;

public class GoogleMainPriceScraperController : Controller
{
    private readonly IHubContext<ScrapingHub> _hubContext;
    private readonly INetworkControlService _networkControlService;
    private readonly ILogger<GoogleMainPriceScraperController> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    private static CancellationTokenSource _googleCancellationTokenSource = new CancellationTokenSource();
    private static readonly object _cancellationTokenLock = new object();
    private static volatile bool _captchaGlobalSignal = false;

    private static volatile bool _isNetworkResetInProgress = false;
    private static readonly object _networkResetProcessLock = new object();
    private static int _consecutiveCaptchaResets = 0;
    private const int MAX_CONSECUTIVE_CAPTCHA_RESETS = 3;

    public GoogleMainPriceScraperController(
        IHubContext<ScrapingHub> hubContext,
        INetworkControlService networkControlService,
        ILogger<GoogleMainPriceScraperController> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        _hubContext = hubContext;
        _networkControlService = networkControlService;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
    }

    private void FireAndForget(string methodName, params object[] args)
    {
        Task.Run(async () =>
        {
            try
            {
                await _hubContext.Clients.All.SendCoreAsync(methodName, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send SignalR message: {methodName}");
            }
        });
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
            _googleCancellationTokenSource?.Dispose();
            _googleCancellationTokenSource = new CancellationTokenSource();
            _logger.LogInformation("New CancellationTokenSource created for Google scraping.");

            if (!triggeredByCaptcha)
            {
                _captchaGlobalSignal = false;
                _logger.LogInformation("CAPTCHA signal flag reset for a new manual session.");
            }
        }
    }

    // Usunięto nieużywaną metodę SignalCaptchaAndCancelTasks, chyba że jest potrzebna gdzie indziej

    [HttpPost]
    public IActionResult StopScrapingGoogle()
    {
        _logger.LogInformation("StopScrapingGoogle action called by user.");

        // Sprzątanie przy ręcznym stopie
        Task.Run(async () => await ResultBatchProcessor.StopAndFlushAsync());
        GlobalCookieWarehouse.StopAndClear();

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
    public async Task<IActionResult> StartScraping()
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

        bool captchaWasDetectedInRun = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(cancellationToken);

        if (captchaWasDetectedInRun)
        {
            _logger.LogWarning("CAPTCHA was detected by HTTP action's run. Initiating network reset procedure.");
            _ = Task.Run(() => HandleCaptchaNetworkResetAndRestartAsync());
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

    private async Task<bool> PerformScrapingLogicInternalAsyncWithCaptchaFlag(CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Google PerformScrapingLogicInternalAsyncWithCaptchaFlag started. Consecutive CAPTCHA resets: {_consecutiveCaptchaResets}");

        bool persistentErrorDetected = false;

        // Używamy try-finally, aby ZAWSZE posprzątać (nawet jak wystąpi błąd)
        try
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                var settings = await dbContext.Settings.FirstOrDefaultAsync(CancellationToken.None);

                if (settings == null)
                {
                    _logger.LogError("Settings not found in the database.");
                    return persistentErrorDetected;
                }

                // [INIT] Start Batch Processor & Generators
                ResultBatchProcessor.Initialize(_serviceScopeFactory);

                // --- ZMIANA: Pobieranie ustawień z modelu Settings ---
                int generatorsCount = settings.GoogleGeneratorsCount;
                bool headlessMode = settings.HeadLessForGoogleGenerators;

                // Przekazujemy oba parametry
                GlobalCookieWarehouse.StartGenerators(generatorsCount, headlessMode);

                var coOfrsToScrape = await dbContext.CoOfrs
                    .Where(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && !c.GoogleIsScraped)
                    .ToListAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested) return persistentErrorDetected;

                if (!coOfrsToScrape.Any())
                {
                    _logger.LogInformation("No Google products found to scrape (internal).");
                    FireAndForget("ReceiveGeneralMessage", "No Google products found to scrape.");
                    lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
                    return persistentErrorDetected;
                }

                _logger.LogInformation($"Found {coOfrsToScrape.Count} Google products to scrape (internal).");
                FireAndForget("ReceiveProgressUpdate", 0, coOfrsToScrape.Count, 0, 0);

                // [WARM-UP] Czekamy na ciastka
                int maxConcurrentScrapers = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
                int minCookiesToStart = Math.Min(maxConcurrentScrapers, 3);

                FireAndForget("ReceiveGeneralMessage", $"[SYSTEM] Rozgrzewanie magazynu ciastek (Oczekuję na {minCookiesToStart} sesji)...");

                // Dodano timeout 60s, żeby nie wisiało w nieskończoność
                int warmUpWaits = 0;
                while (GlobalCookieWarehouse.AvailableCookies < minCookiesToStart && !cancellationToken.IsCancellationRequested && warmUpWaits < 60)
                {
                    await Task.Delay(1000, cancellationToken);
                    warmUpWaits++;
                }

                if (cancellationToken.IsCancellationRequested) return persistentErrorDetected;

                FireAndForget("ReceiveGeneralMessage", $"[SYSTEM] Startuję {maxConcurrentScrapers} wątków HTTP (Magazyn: {GlobalCookieWarehouse.AvailableCookies}).");

                int totalScrapedCount = 0;
                int totalRejectedCount = 0;
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                var semaphore = new SemaphoreSlim(maxConcurrentScrapers, maxConcurrentScrapers);
                var tasks = new List<Task>();
                var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

                for (int i = 0; i < maxConcurrentScrapers; i++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await semaphore.WaitAsync(cancellationToken);
                            if (cancellationToken.IsCancellationRequested) return;

                            var scraper = new GoogleMainPriceScraper();

                            while (true)
                            {
                                if (cancellationToken.IsCancellationRequested) break;

                                CoOfrClass coOfr = null;
                                lock (productQueue)
                                {
                                    if (productQueue.Count > 0) coOfr = productQueue.Dequeue();
                                }
                                if (coOfr == null) break;

                                try
                                {
                                    using (var productTaskScope = _serviceScopeFactory.CreateScope())
                                    {
                                        var productDbContext = productTaskScope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                                        string identifier = coOfr.UseGoogleHidOffer ? $"HID:{coOfr.GoogleHid}" : coOfr.GoogleOfferUrl;

                                        var scrapedPrices = await scraper.ScrapePricesAsync(coOfr);

                                        if (cancellationToken.IsCancellationRequested) break;

                                        var coOfrTracked = await productDbContext.CoOfrs.FindAsync(coOfr.Id);
                                        if (coOfrTracked == null) continue;

                                        if (scrapedPrices.Any())
                                        {
                                            foreach (var ph in scrapedPrices)
                                            {
                                                ph.CoOfrClassId = coOfrTracked.Id;
                                                ph.GoogleCid = coOfrTracked.GoogleCid;
                                            }
                                            ResultBatchProcessor.Enqueue(scrapedPrices);

                                            coOfrTracked.GoogleIsScraped = true;
                                            coOfrTracked.GooglePricesCount = scrapedPrices.Count;
                                            coOfrTracked.GoogleIsRejected = false;
                                        }
                                        else
                                        {
                                            coOfrTracked.GoogleIsScraped = true;
                                            coOfrTracked.GoogleIsRejected = true;
                                            coOfrTracked.GooglePricesCount = 0;
                                            Interlocked.Increment(ref totalRejectedCount);
                                        }

                                        await productDbContext.SaveChangesAsync(CancellationToken.None);
                                        FireAndForget("ReceiveScrapingUpdate", coOfrTracked.Id, coOfrTracked.GoogleIsScraped, coOfrTracked.GoogleIsRejected, coOfrTracked.GooglePricesCount, "Google");
                                    }

                                    Interlocked.Increment(ref totalScrapedCount);

                                    if (!cancellationToken.IsCancellationRequested)
                                    {
                                        double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                                        int currentTotalRejected = Volatile.Read(ref totalRejectedCount);
                                        FireAndForget("ReceiveProgressUpdate", totalScrapedCount, coOfrsToScrape.Count, elapsedSeconds, currentTotalRejected);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Task {Task.CurrentId}: Error processing product.");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Critical worker error");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
                stopwatch.Stop();

                if (!persistentErrorDetected && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Internal scraping run completed successfully.");
                    FireAndForget("ReceiveGeneralMessage", "Google scraping run completed successfully.");
                    lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
                }
            }
        }
        // --- ZMIANA: Dodajemy obsługę OperationCanceledException ---
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Zadanie zostało zatrzymane przez użytkownika (Task Cancelled).");
            // Nie robimy nic więcej, to normalne zachowanie przy STOP
        }
        catch (Exception ex)
        {
            // Sprawdzamy czy wewnętrzny wyjątek to nie anulowanie (czasem jest opakowany)
            if (ex is TaskCanceledException || (ex.InnerException is TaskCanceledException))
            {
                _logger.LogInformation("Zadanie zostało zatrzymane (Task Cancelled wrapper).");
            }
            else
            {
                _logger.LogError(ex, "Critical error in main scraping loop.");
            }
        }
        finally
        {
            _logger.LogInformation("[CLEANUP] Stopping Batch Processor and Generators.");
            await ResultBatchProcessor.StopAndFlushAsync();
            GlobalCookieWarehouse.StopAndClear();
        }

        return persistentErrorDetected;
    }

    private async Task HandleCaptchaNetworkResetAndRestartAsync()
    {
        bool canAttemptReset;
        lock (_networkResetProcessLock)
        {
            canAttemptReset = !_isNetworkResetInProgress;
            if (canAttemptReset) _isNetworkResetInProgress = true;
        }

        if (!canAttemptReset) return;

        try
        {
            // W razie resetu sieci też warto wyczyścić stare generatory przed nową próbą
            GlobalCookieWarehouse.StopAndClear();

            if (_consecutiveCaptchaResets >= MAX_CONSECUTIVE_CAPTCHA_RESETS)
            {
                FireAndForget("ReceiveGeneralMessage", "Google: Max network reset attempts after CAPTCHA reached. Manual intervention required.");
                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
                return;
            }

            _consecutiveCaptchaResets++;
            FireAndForget("ReceiveGeneralMessage", $"Google: CAPTCHA. Attempting network reset (attempt {_consecutiveCaptchaResets}/{MAX_CONSECUTIVE_CAPTCHA_RESETS})...");

            bool resetSuccess = false;
            try
            {
                resetSuccess = await _networkControlService.TriggerNetworkDisableAndResetAsync();
            }
            catch (Exception netEx)
            {
                _logger.LogError(netEx, "Exception during NetworkControlService.");
            }

            if (resetSuccess)
            {
                FireAndForget("ReceiveGeneralMessage", "Google: Network reset successful. Restarting scraping in a moment...");
                await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);

                CancellationToken newCancellationTokenForRestart;

                lock (_cancellationTokenLock)
                {
                    if (_googleCancellationTokenSource != null && !_googleCancellationTokenSource.IsCancellationRequested)
                    {
                        _googleCancellationTokenSource.Cancel();
                    }
                    _googleCancellationTokenSource?.Dispose();
                    _googleCancellationTokenSource = new CancellationTokenSource();
                    _captchaGlobalSignal = false;
                    newCancellationTokenForRestart = _googleCancellationTokenSource.Token;
                }

                bool captchaDetectedAgain = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(newCancellationTokenForRestart);

                if (captchaDetectedAgain)
                {
                    _ = Task.Run(() => HandleCaptchaNetworkResetAndRestartAsync());
                }
            }
            else
            {
                FireAndForget("ReceiveGeneralMessage", "Google: Network reset failed. Scraping will not restart automatically this time.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in HandleCaptchaNetworkResetAndRestartAsync.");
            FireAndForget("ReceiveGeneralMessage", "Google: Critical error during network reset.");
        }
        finally
        {
            lock (_networkResetProcessLock)
            {
                _isNetworkResetInProgress = false;
            }
        }
    }
}