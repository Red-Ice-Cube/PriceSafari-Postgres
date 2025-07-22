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
        bool captchaDetectedInThisRun = false;

        using (var scope = _serviceScopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
            var settings = await dbContext.Settings.FirstOrDefaultAsync(CancellationToken.None);

            if (settings == null)
            {
                _logger.LogError("Settings not found in the database.");
                return captchaDetectedInThisRun;
            }

            var coOfrsToScrape = await dbContext.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
                .ToListAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Scraping (internal) cancelled before processing products.");
                return captchaDetectedInThisRun;
            }

            if (!coOfrsToScrape.Any())
            {
                _logger.LogInformation("No Google products found to scrape (internal).");
                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "No Google products found to scrape.", CancellationToken.None);
                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
                return captchaDetectedInThisRun;
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
                        await scraper.InitializeAsync(settings);
                        if (cancellationToken.IsCancellationRequested) return;

                        while (true)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            CoOfrClass coOfr = null;
                            lock (productQueue) { if (productQueue.Count > 0) coOfr = productQueue.Dequeue(); }
                            if (coOfr == null) break;

                            try
                            {

                                using (var productTaskScope = _serviceScopeFactory.CreateScope())
                                {
                                    var productDbContext = productTaskScope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                    _logger.LogDebug($"Task {Task.CurrentId}: Scraping {coOfr.GoogleOfferUrl}");
                                    var scrapedPrices = await scraper.ScrapePricesAsync(coOfr.GoogleOfferUrl);
                                    if (cancellationToken.IsCancellationRequested) break;

                                    var coOfrTracked = await productDbContext.CoOfrs.FindAsync(coOfr.Id);
                                    if (coOfrTracked == null)
                                    {
                                        _logger.LogWarning($"Product with ID {coOfr.Id} not found in DB within product task scope.");
                                        continue;
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
                                        coOfrTracked.GoogleIsScraped = true;
                                        coOfrTracked.GoogleIsRejected = true;
                                        coOfrTracked.GooglePricesCount = 0;
                                        Interlocked.Increment(ref totalRejectedCount);
                                    }

                                    await productDbContext.SaveChangesAsync(CancellationToken.None);
                                    await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfrTracked.Id, coOfrTracked.GoogleIsScraped, coOfrTracked.GoogleIsRejected, coOfrTracked.GooglePricesCount, "Google", CancellationToken.None);
                                }

                                Interlocked.Increment(ref totalScrapedCount);

                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;

                                    int currentTotalRejected = Volatile.Read(ref totalRejectedCount);

                                    await _hubContext.Clients.All.SendAsync(
                                        "ReceiveProgressUpdate",
                                        totalScrapedCount,
                                        coOfrsToScrape.Count,
                                        elapsedSeconds,
                                        currentTotalRejected,
                                        CancellationToken.None
                                    );
                                }
                            }
                            catch (CaptchaDetectedException ex)
                            {
                                _logger.LogWarning(ex, $"Task {Task.CurrentId}: CAPTCHA DETECTED by scraper for product {coOfr?.Id}.");
                                captchaDetectedInThisRun = true;
                                SignalCaptchaAndCancelTasks();
                                break;
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                _logger.LogInformation($"Task {Task.CurrentId}: Operation cancelled for product {coOfr?.Id}.");
                                break;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Task {Task.CurrentId}: Error scraping product {coOfr?.Id}.");
                                if (coOfr != null) { }
                            }
                        }
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
                }));
            }

            await Task.WhenAll(tasks);
            _logger.LogInformation("All Google scraping tasks have completed or been cancelled for this internal run.");
            stopwatch.Stop();

            if (!captchaDetectedInThisRun && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Internal scraping run completed successfully without CAPTCHA.");
                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google scraping run completed successfully.", CancellationToken.None);
                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
            }
            else if (!captchaDetectedInThisRun && cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Internal scraping run was cancelled (likely manual stop).");

            }

        }
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
                lock (_networkResetProcessLock) { _consecutiveCaptchaResets = 0; }
                return;
            }

            _consecutiveCaptchaResets++;
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
                    _logger.LogInformation("Token and CAPTCHA signal reset for automatic restart.");
                }

                _logger.LogInformation("Restarting Google scraping logic automatically after network reset.");

                bool captchaDetectedAgain = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(newCancellationTokenForRestart);

                if (captchaDetectedAgain)
                {
                    _logger.LogWarning("CAPTCHA detected AGAIN immediately after network reset and restart. Will attempt another reset if limit not reached (via new Task.Run).");

                    _ = Task.Run(() => HandleCaptchaNetworkResetAndRestartAsync());
                }

            }
            else
            {
                _logger.LogError("Network reset failed for Google. Automatic restart will not occur for this CAPTCHA event.");
                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google: Network reset failed. Scraping will not restart automatically this time.", CancellationToken.None);

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

