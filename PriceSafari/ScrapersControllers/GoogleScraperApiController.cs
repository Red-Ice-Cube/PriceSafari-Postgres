

//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using PriceSafari.Models;
//using PriceSafari.Services.GoogleScraping;
//using System.Text.Json.Serialization;

//namespace PriceSafari.ScrapersControllers
//{
//    [ApiController]
//    [Route("api/google-scrape")]
//    public class GoogleScraperApiController : ControllerBase
//    {
//        private readonly PriceSafariContext _dbContext;
//        private readonly ILogger<GoogleScraperApiController> _logger;
//        private readonly IHubContext<ScrapingHub> _hubContext;
//        private readonly IServiceScopeFactory _serviceScopeFactory;
//        private readonly GoogleScrapingStateService _stateService;

//        public GoogleScraperApiController(
//            PriceSafariContext dbContext,
//            ILogger<GoogleScraperApiController> logger,
//            IHubContext<ScrapingHub> hubContext,
//            IServiceScopeFactory serviceScopeFactory,
//            GoogleScrapingStateService stateService)
//        {
//            _dbContext = dbContext;
//            _logger = logger;
//            _hubContext = hubContext;
//            _serviceScopeFactory = serviceScopeFactory;
//            _stateService = stateService;
//        }

//        // === HELPER: Fire and Forget SignalR ===
//        private void FireAndForget(string methodName, params object[] args)
//        {
//            Task.Run(async () =>
//            {
//                try
//                {
//                    await _hubContext.Clients.All.SendCoreAsync(methodName, args);
//                }
//                catch (Exception ex)
//                {
//                    _logger.LogError(ex, $"Failed to send SignalR message: {methodName}");
//                }
//            });
//        }

//        #region DTOs

//        public class ScraperTask
//        {
//            public int TaskId { get; set; }
//            public string? Url { get; set; }
//            public string? GoogleCid { get; set; }
//            public string? GoogleGid { get; set; }
//            public string? GoogleHid { get; set; }
//            public bool UseGoogleHidOffer { get; set; }
//            public bool UseGPID { get; set; }
//            public bool UseWRGA { get; set; }
//        }

//        public class ScraperResult
//        {
//            [JsonPropertyName("taskId")]
//            public int TaskId { get; set; }

//            [JsonPropertyName("status")]
//            public string Status { get; set; } = "";

//            [JsonPropertyName("offers")]
//            public List<OfferResult> Offers { get; set; } = new();
//        }

//        public class OfferResult
//        {
//            [JsonPropertyName("googleStoreName")]
//            public string GoogleStoreName { get; set; } = "";

//            [JsonPropertyName("googlePrice")]
//            public decimal GooglePrice { get; set; }

//            [JsonPropertyName("googlePriceWithDelivery")]
//            public decimal GooglePriceWithDelivery { get; set; }

//            [JsonPropertyName("googlePosition")]
//            public string GooglePosition { get; set; } = "0";

//            [JsonPropertyName("isBidding")]
//            public string? IsBidding { get; set; }

//            [JsonPropertyName("googleInStock")]
//            public bool GoogleInStock { get; set; }

//            [JsonPropertyName("googleOfferPerStoreCount")]
//            public int GoogleOfferPerStoreCount { get; set; }
//        }

//        #endregion

//        // =====================================================================
//        // GET /api/google-scrape/get-task?scraperName=XXX
//        // =====================================================================
//        [HttpGet("get-task")]
//        public IActionResult GetTask([FromQuery] string scraperName)
//        {
//            if (string.IsNullOrEmpty(scraperName))
//            {
//                return BadRequest(new { error = "scraperName is required" });
//            }

//            // Aktualizuj heartbeat scrapera
//            _stateService.UpdateScraperHeartbeat(scraperName);
//            _stateService.CleanupDeadScrapers();

//            if (!_stateService.IsScrapingEnabled)
//            {
//                _logger.LogDebug($"[{scraperName}] Scraping disabled - returning empty");
//                return Ok(new { tasks = new List<ScraperTask>(), done = false, message = "scraping_disabled" });
//            }

//            List<ScraperTask> tasks;
//            lock (_stateService.StateLock)
//            {
//                var inProgressIds = _stateService.GetTasksInProgressIds();

//                var coOfrsToScrape = _dbContext.CoOfrs
//                    .Where(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
//                                && !c.GoogleIsScraped
//                                && !inProgressIds.Contains(c.Id))
//                    .Take(GoogleScrapingStateService.BATCH_SIZE)
//                    .ToList();

//                if (!coOfrsToScrape.Any())
//                {
//                    _logger.LogInformation($"[{scraperName}] No more tasks - scraping complete");

//                    var remaining = _dbContext.CoOfrs.Count(c =>
//                        (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && !c.GoogleIsScraped);

//                    if (remaining == 0 && _stateService.TasksInProgressCount == 0)
//                    {
//                        _stateService.MarkAsCompleted();

//                        // Finalny SignalR update
//                        FireAndForget("ReceiveProgressUpdate",
//                            _stateService.TotalProcessedInSession,
//                            _stateService.TotalTasksInSession,
//                            _stateService.ElapsedSeconds,
//                            _stateService.TotalRejectedInSession);

//                        FireAndForget("ReceiveGeneralMessage",
//                            $"[PYTHON API] Google scraping completed! Processed: {_stateService.TotalProcessedInSession}, " +
//                            $"Rejected: {_stateService.TotalRejectedInSession}, " +
//                            $"Time: {_stateService.ElapsedTime.TotalMinutes:F1} min");

//                        return Ok(new { tasks = new List<ScraperTask>(), done = true, message = "all_completed" });
//                    }

//                    return Ok(new { tasks = new List<ScraperTask>(), done = false, message = "no_tasks_available" });
//                }

//                tasks = coOfrsToScrape.Select(c => new ScraperTask
//                {
//                    TaskId = c.Id,
//                    Url = c.GoogleOfferUrl,
//                    GoogleCid = c.GoogleCid,
//                    GoogleGid = c.GoogleGid,
//                    GoogleHid = c.GoogleHid,
//                    UseGoogleHidOffer = c.UseGoogleHidOffer,
//                    UseGPID = c.UseGPID,
//                    UseWRGA = c.UseWRGA
//                }).ToList();

//                _stateService.ReserveTasks(tasks.Select(t => t.TaskId).ToList(), scraperName);
//            }

//            _stateService.SetScraperWorking(scraperName, tasks.Count);

//            _logger.LogInformation($"[{scraperName}] Assigned {tasks.Count} tasks");

//            return Ok(new { tasks, done = false });
//        }

//        // =====================================================================
//        // POST /api/google-scrape/submit-batch-results
//        // =====================================================================
//        [HttpPost("submit-batch-results")]
//        public IActionResult SubmitBatchResults(
//            [FromBody] List<ScraperResult>? results,
//            [FromQuery] string scraperName = "unknown")
//        {
//            if (results == null || !results.Any())
//            {
//                return BadRequest(new { error = "No results provided" });
//            }

//            _logger.LogInformation($"[{scraperName}] Received {results.Count} results. Starting background save.");

//            // Zapisz wyniki w tle
//            _ = Task.Run(async () =>
//            {
//                using var scope = _serviceScopeFactory.CreateScope();
//                var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//                await ProcessResultsInBackground(results, scraperName, dbContext);

//                // DOPIERO PO ZAPISIE usuwamy z _tasksInProgress
//                _stateService.ReleaseTasks(results.Select(r => r.TaskId).ToList());
//            });

//            return Ok(new { success = true, message = "Results accepted and processing in background" });
//        }

//        private async Task ProcessResultsInBackground(List<ScraperResult> results, string scraperName, PriceSafariContext dbContext)
//        {
//            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
//            try
//            {
//                var taskIds = results.Select(r => r.TaskId).ToList();
//                var coOfrsDict = await dbContext.CoOfrs
//                    .Where(c => taskIds.Contains(c.Id))
//                    .ToDictionaryAsync(c => c.Id);

//                var allHistories = new List<CoOfrPriceHistoryClass>();
//                var signalRUpdates = new List<object>();
//                int batchSuccessCount = 0;
//                int batchRejectedCount = 0;

//                foreach (var result in results)
//                {
//                    if (!coOfrsDict.TryGetValue(result.TaskId, out var coOfr)) continue;

//                    if (result.Status == "success" && result.Offers.Any())
//                    {
//                        var histories = result.Offers.Select(o => new CoOfrPriceHistoryClass
//                        {
//                            CoOfrClassId = coOfr.Id,
//                            GoogleStoreName = o.GoogleStoreName,
//                            GooglePrice = o.GooglePrice,
//                            GooglePriceWithDelivery = o.GooglePriceWithDelivery,
//                            GooglePosition = o.GooglePosition,
//                            GoogleInStock = o.GoogleInStock
//                        }).ToList();

//                        allHistories.AddRange(histories);
//                        coOfr.GoogleIsScraped = true;
//                        coOfr.GoogleIsRejected = false;
//                        coOfr.GooglePricesCount = histories.Count;
//                        batchSuccessCount++;
//                    }
//                    else
//                    {
//                        coOfr.GoogleIsScraped = true;
//                        coOfr.GoogleIsRejected = true;
//                        coOfr.GooglePricesCount = 0;
//                        batchRejectedCount++;
//                    }

//                    signalRUpdates.Add(new
//                    {
//                        id = coOfr.Id,
//                        isScraped = coOfr.GoogleIsScraped,
//                        isRejected = coOfr.GoogleIsRejected,
//                        pricesCount = coOfr.GooglePricesCount
//                    });
//                }

//                if (allHistories.Any()) await dbContext.CoOfrPriceHistories.AddRangeAsync(allHistories);
//                await dbContext.SaveChangesAsync();

//                // Aktualizacja statystyk sesji
//                _stateService.AddProcessedResults(batchSuccessCount, batchRejectedCount);
//                _stateService.UpdateScraperStats(scraperName, results.Count);

//                // Wysyłanie SignalR co 100 wyników
//                if (_stateService.ShouldSendProgressUpdate(out int currentProcessed))
//                {
//                    FireAndForget("ReceiveProgressUpdate",
//                        currentProcessed,
//                        _stateService.TotalTasksInSession,
//                        _stateService.ElapsedSeconds,
//                        _stateService.TotalRejectedInSession);

//                    _logger.LogInformation($"[SignalR] Progress: {currentProcessed}/{_stateService.TotalTasksInSession} " +
//                                          $"({_stateService.TotalRejectedInSession} rejected) in {_stateService.ElapsedSeconds:F0}s");
//                }

//                // Wysyłamy batch update dla UI
//                FireAndForget("ReceiveBatchScrapingUpdate", signalRUpdates);

//                _logger.LogInformation($"[{scraperName}] Background save finished in {stopwatch.ElapsedMilliseconds}ms. " +
//                                      $"Session total: {_stateService.TotalProcessedInSession}");
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, $"[{scraperName}] Error saving results in background");
//            }
//        }

//        // =====================================================================
//        // POST /api/google-scrape/start
//        // =====================================================================
//        [HttpPost("start")]
//        public async Task<IActionResult> StartScraping()
//        {
//            // Pobierz statystyki
//            var totalTasks = await _dbContext.CoOfrs
//                .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer);
//            var completedTasks = await _dbContext.CoOfrs
//                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsScraped);

//            if (!_stateService.TryStartScraping(totalTasks))
//            {
//                return Ok(new { success = false, message = "Scraping already running" });
//            }

//            _logger.LogInformation("Google scraping ENABLED via API");

//            FireAndForget("ReceiveGeneralMessage", $"[PYTHON API] Google scraping started! ({totalTasks - completedTasks} tasks remaining)");
//            FireAndForget("ReceiveProgressUpdate", completedTasks, totalTasks, 0, 0);

//            return Ok(new { success = true, message = "Scraping started" });
//        }

//        // =====================================================================
//        // POST /api/google-scrape/stop
//        // =====================================================================
//        [HttpPost("stop")]
//        public IActionResult StopScraping()
//        {
//            var (processed, rejected, elapsed) = _stateService.StopScraping();

//            _logger.LogInformation("Google scraping STOPPED via API");

//            FireAndForget("ReceiveGeneralMessage", $"[PYTHON API] Google scraping stopped. Processed: {processed}, Rejected: {rejected}");

//            return Ok(new { success = true, message = "Scraping stopped" });
//        }

//        // =====================================================================
//        // GET /api/google-scrape/status
//        // =====================================================================
//        [HttpGet("status")]
//        public async Task<IActionResult> GetStatus()
//        {
//            _stateService.CleanupDeadScrapers();

//            var totalTasks = await _dbContext.CoOfrs
//                .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer);

//            var completedTasks = await _dbContext.CoOfrs
//                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsScraped);

//            var rejectedTasks = await _dbContext.CoOfrs
//                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsRejected);

//            var status = _stateService.GetFullStatus(totalTasks, completedTasks, rejectedTasks);

//            return Ok(status);
//        }

//        // =====================================================================
//        // GET /api/google-scrape/settings
//        // =====================================================================
//        [HttpGet("settings")]
//        public async Task<IActionResult> GetSettings()
//        {
//            var settings = await _dbContext.Settings.FirstOrDefaultAsync();

//            if (settings == null)
//            {
//                return Ok(new
//                {
//                    generatorsCount = 2,
//                    headlessMode = true,
//                    maxWorkers = 1,
//                    headStartDuration = 45
//                });
//            }

//            return Ok(new
//            {
//                generatorsCount = settings.GoogleGeneratorsCount > 0 ? settings.GoogleGeneratorsCount : 2,
//                headlessMode = settings.HeadLessForGoogleGenerators,
//                maxWorkers = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1,
//                headStartDuration = 45
//            });
//        }
//    }
//}




using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using System.Text.Json.Serialization;

namespace PriceSafari.Services.GoogleScraping
{
    [ApiController]
    [Route("api/google-scrape")]
    public class GoogleScrapeApiController : ControllerBase
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<GoogleScrapeApiController> _logger;
        private const string ApiKey = "2764udhnJUDI8392j83jfi2ijdo1949rncowp89i3rnfiiui1203kfnf9030rfpPkUjHyHt";

        public GoogleScrapeApiController(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            ILogger<GoogleScrapeApiController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        // ===== USTAWIENIA =====
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var settings = await _context.Settings.FirstOrDefaultAsync();

            return Ok(new
            {
                generatorsCount = settings?.GoogleGeneratorsCount > 0 ? settings.GoogleGeneratorsCount : 2,
                headlessMode = settings?.HeadLessForGoogleGenerators ?? true,
                maxWorkers = settings?.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1,
                headStartDuration = 50,
                batchSize = GoogleScrapeManager.BatchSize
            });
        }

        // ===== POBIERANIE ZADAŃ =====
        [HttpGet("get-task")]
        public async Task<IActionResult> GetTaskBatch(
            [FromQuery] string scraperName,
            [FromQuery] string? currentStatus = null,
            [FromQuery] string? ipAddress = null)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            if (string.IsNullOrWhiteSpace(scraperName))
                return BadRequest(new { error = "scraperName is required" });

            // 1. Zarejestruj check-in scrapera
            var scraper = GoogleScrapeManager.RegisterScraperCheckIn(scraperName, ipAddress);

            // 2. Obsługa statusu NUKE od Pythona
            if (currentStatus == "NUKE_IN_PROGRESS" && scraper.Status != GoogleScraperLiveStatus.ResettingNetwork)
            {
                GoogleScrapeManager.MarkScraperNuking(scraperName, "Zgłoszony przez Python");
            }
            else if (currentStatus == "NUKE_COMPLETED" && scraper.Status == GoogleScraperLiveStatus.ResettingNetwork)
            {
                GoogleScrapeManager.MarkScraperNukeCompleted(scraperName, ipAddress);
            }

            // 3. Wyślij aktualizację statusu scrapera na front
            await BroadcastScraperStatus(scraper);

            // 4. Sprawdź czy scraper może otrzymać zadanie
            if (!GoogleScrapeManager.CanScraperReceiveTask(scraperName))
            {
                var reason = GoogleScrapeManager.CurrentStatus != GoogleScrapingProcessStatus.Running
                    ? "Scraping process is not running."
                    : scraper.Status == GoogleScraperLiveStatus.ResettingNetwork
                        ? "Scraper is in NUKE protocol."
                        : "Scraper is manually paused (hibernation).";

                return Ok(new
                {
                    tasks = new List<object>(),
                    done = false,
                    message = reason,
                    shouldHibernate = true
                });
            }

            // 5. Sprawdź czy scraper ma już przydzieloną aktywną paczkę
            var existingBatch = GoogleScrapeManager.GetActiveScraperBatch(scraperName);
            if (existingBatch != null)
            {
                _logger.LogWarning("Scraper {ScraperName} ma aktywną paczkę {BatchId} - zwracam ponownie",
                    scraperName, existingBatch.BatchId);

                GoogleScrapeManager.AddLog(scraperName, "WARNING",
                    "Scraper odpytał ponownie - ma aktywną paczkę", existingBatch.BatchId);

                var existingTasks = await _context.CoOfrs
                    .Where(c => existingBatch.TaskIds.Contains(c.Id))
                    .Select(c => new
                    {
                        taskId = c.Id,
                        url = c.GoogleOfferUrl,
                        googleCid = c.GoogleCid,
                        googleGid = c.GoogleGid,
                        googleHid = c.GoogleHid,
                        useGoogleHidOffer = c.UseGoogleHidOffer,
                        useGPID = c.UseGPID,
                        useWRGA = c.UseWRGA
                    })
                    .ToListAsync();

                return Ok(new
                {
                    batchId = existingBatch.BatchId,
                    tasks = existingTasks,
                    done = false,
                    isResend = true
                });
            }

            // 6. Pobierz nowe zadania z bazy
            var offersToScrape = await _context.CoOfrs
                .Where(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
                            && !c.GoogleIsScraped)
                .OrderBy(c => c.Id)
                .Take(GoogleScrapeManager.BatchSize)
                .ToListAsync();

            // 7. Brak zadań
            if (!offersToScrape.Any())
            {
                var anyActiveBatches = GoogleScrapeManager.HasActiveBatches();

                if (!anyActiveBatches && GoogleScrapeManager.CurrentStatus == GoogleScrapingProcessStatus.Running)
                {
                    var anyPending = await _context.CoOfrs
                        .AnyAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
                                       && !c.GoogleIsScraped);

                    if (!anyPending)
                    {
                        GoogleScrapeManager.FinishProcess();

                        await _hubContext.Clients.All.SendAsync("GoogleScrapingFinished", new
                        {
                            endTime = GoogleScrapeManager.ScrapingEndTime,
                            message = "Scraping completed"
                        });

                        return Ok(new
                        {
                            tasks = new List<object>(),
                            done = true,
                            message = "Scraping completed",
                            shouldHibernate = true
                        });
                    }
                }

                return Ok(new
                {
                    tasks = new List<object>(),
                    done = false,
                    message = "No pending tasks. Waiting for batches to complete."
                });
            }

            // 8. Zarejestruj paczkę w managerze
            var batchId = GoogleScrapeManager.GenerateBatchId();
            var taskIds = offersToScrape.Select(c => c.Id).ToList();
            GoogleScrapeManager.RegisterAssignedBatch(batchId, scraperName, taskIds);

            // 9. Wyślij aktualizacje na front
            await BroadcastScraperStatus(scraper);
            await BroadcastDashboardUpdate();
            await BroadcastLogs();

            // 10. Zwróć zadania do scrapera
            var tasksForPython = offersToScrape.Select(c => new
            {
                taskId = c.Id,
                url = c.GoogleOfferUrl,
                googleCid = c.GoogleCid,
                googleGid = c.GoogleGid,
                googleHid = c.GoogleHid,
                useGoogleHidOffer = c.UseGoogleHidOffer,
                useGPID = c.UseGPID,
                useWRGA = c.UseWRGA
            });

            _logger.LogInformation("Przydzielono paczkę {BatchId} ({Count} URLi) do {ScraperName}",
                batchId, offersToScrape.Count, scraperName);

            return Ok(new
            {
                batchId = batchId,
                tasks = tasksForPython,
                done = false
            });
        }

        // ===== WYSYŁANIE WYNIKÓW =====
        [HttpPost("submit-batch-results")]
        public async Task<IActionResult> SubmitBatchResults(
            [FromBody] GoogleBatchResultsDto batchResults,
            [FromQuery] string scraperName = "unknown")
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            if (batchResults?.Results == null || !batchResults.Results.Any())
                return BadRequest(new { error = "No results provided" });

            var batchId = batchResults.BatchId ?? "UNKNOWN";
            var results = batchResults.Results;

            _logger.LogInformation("Otrzymano wyniki dla paczki {BatchId}: {Count} URLi od {ScraperName}",
                batchId, results.Count, scraperName);

            // Pobierz oferty do aktualizacji
            var taskIds = results.Select(r => r.TaskId).ToList();
            var offersToUpdate = await _context.CoOfrs
                .Where(c => taskIds.Contains(c.Id))
                .ToListAsync();

            var newPriceHistories = new List<CoOfrPriceHistoryClass>();
            int successCount = 0, failedCount = 0, rejectedCount = 0, totalPricesCollected = 0;

            foreach (var result in results)
            {
                var offer = offersToUpdate.FirstOrDefault(c => c.Id == result.TaskId);
                if (offer == null) continue;

                if (result.Status == "success" && result.Offers?.Any() == true)
                {
                    offer.GoogleIsScraped = true;
                    offer.GoogleIsRejected = false;
                    offer.GooglePricesCount = result.Offers.Count;
                    successCount++;
                    totalPricesCollected += result.Offers.Count;

                    foreach (var scraped in result.Offers)
                    {
                        newPriceHistories.Add(new CoOfrPriceHistoryClass
                        {
                            CoOfrClassId = offer.Id,
                            GoogleStoreName = scraped.GoogleStoreName,
                            GooglePrice = scraped.GooglePrice,
                            GooglePriceWithDelivery = scraped.GooglePriceWithDelivery,
                            GooglePosition = scraped.GooglePosition,
                            GoogleInStock = scraped.GoogleInStock
                        });
                    }
                }
                else if (result.Status == "rejected")
                {
                    offer.GoogleIsScraped = true;
                    offer.GoogleIsRejected = true;
                    offer.GooglePricesCount = 0;
                    rejectedCount++;
                }
                else
                {
                    failedCount++;
                }

                // Wyślij aktualizację wiersza na front
                await _hubContext.Clients.All.SendAsync("GoogleUpdateOfferRow", new
                {
                    id = offer.Id,
                    isScraped = offer.GoogleIsScraped,
                    isRejected = offer.GoogleIsRejected,
                    pricesCount = offer.GooglePricesCount
                });
            }

            // Zapisz do bazy
            if (newPriceHistories.Any())
                await _context.CoOfrPriceHistories.AddRangeAsync(newPriceHistories);
            await _context.SaveChangesAsync();

            // Oznacz paczkę jako ukończoną
            GoogleScrapeManager.CompleteBatch(batchId, successCount, failedCount, rejectedCount, totalPricesCollected);

            // Aktualizuj status scrapera
            if (GoogleScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            // Wyślij aktualizacje na front
            await BroadcastDashboardUpdate();
            await BroadcastLogs();
            await BroadcastStatsUpdate();

            _logger.LogInformation("Paczka {BatchId} ukończona: {Success} OK, {Rejected} odrz., {Failed} błędy, {Prices} cen",
                batchId, successCount, rejectedCount, failedCount, totalPricesCollected);

            return Ok(new
            {
                success = true,
                batchId = batchId,
                successCount = successCount,
                rejectedCount = rejectedCount,
                failedCount = failedCount,
                pricesCollected = totalPricesCollected
            });
        }

        // ===== RAPORTOWANIE NUKE =====
        [HttpPost("report-nuke")]
        public async Task<IActionResult> ReportNuke([FromBody] NukeReportDto report)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            if (string.IsNullOrWhiteSpace(report.ScraperName))
                return BadRequest(new { error = "scraperName is required" });

            if (report.Status == "started")
            {
                GoogleScrapeManager.MarkScraperNuking(report.ScraperName, report.Reason);
            }
            else if (report.Status == "completed")
            {
                GoogleScrapeManager.MarkScraperNukeCompleted(report.ScraperName, report.NewIpAddress);
            }

            if (GoogleScrapeManager.ActiveScrapers.TryGetValue(report.ScraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            await BroadcastLogs();
            await BroadcastDashboardUpdate();

            _logger.LogWarning("Scraper {ScraperName} zgłosił NUKE ({Status}): {Reason}",
                report.ScraperName, report.Status, report.Reason);

            return Ok(new { acknowledged = true });
        }

        // ===== HEARTBEAT =====
        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromBody] GoogleHeartbeatDto heartbeat)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var scraper = GoogleScrapeManager.RegisterScraperCheckIn(heartbeat.ScraperName, heartbeat.IpAddress);
            await BroadcastScraperStatus(scraper);

            return Ok(new
            {
                acknowledged = true,
                shouldHibernate = !GoogleScrapeManager.CanScraperReceiveTask(heartbeat.ScraperName),
                globalStatus = GoogleScrapeManager.CurrentStatus.ToString()
            });
        }

        // ===== STATUS =====
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var totalTasks = await _context.CoOfrs
                .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer);

            var completedTasks = await _context.CoOfrs
                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsScraped);

            return Ok(new
            {
                isEnabled = GoogleScrapeManager.CurrentStatus == GoogleScrapingProcessStatus.Running,
                status = GoogleScrapeManager.CurrentStatus.ToString(),
                startedAt = GoogleScrapeManager.ScrapingStartTime,
                totalTasks = totalTasks,
                completedTasks = completedTasks,
                remainingTasks = totalTasks - completedTasks,
                progressPercent = totalTasks > 0 ? Math.Round((double)completedTasks / totalTasks * 100, 1) : 0,
                activeScrapers = GoogleScrapeManager.GetScrapersDetails(),
                activeBatchesCount = GoogleScrapeManager.AssignedBatches.Values.Count(b => !b.IsCompleted && !b.IsTimedOut)
            });
        }

        // ===== START/STOP =====
        [HttpPost("start")]
        public async Task<IActionResult> StartScraping()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            if (GoogleScrapeManager.CurrentStatus == GoogleScrapingProcessStatus.Running)
                return Ok(new { success = false, message = "Already running" });

            var totalToScrape = await _context.CoOfrs
                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && !c.GoogleIsScraped);

            if (totalToScrape == 0)
                return Ok(new { success = false, message = "No URLs to scrape" });

            GoogleScrapeManager.ResetForNewProcess();

            await _hubContext.Clients.All.SendAsync("GoogleScrapingStarted", new
            {
                startTime = GoogleScrapeManager.ScrapingStartTime,
                totalUrls = totalToScrape
            });

            return Ok(new { success = true, message = $"Started. {totalToScrape} URLs to process." });
        }

        [HttpPost("stop")]
        public async Task<IActionResult> StopScraping()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            GoogleScrapeManager.CurrentStatus = GoogleScrapingProcessStatus.Idle;
            GoogleScrapeManager.ScrapingEndTime = DateTime.UtcNow;
            GoogleScrapeManager.AddSystemLog("WARNING", "Scraping zatrzymany przez API");

            await _hubContext.Clients.All.SendAsync("GoogleScrapingStopped", new
            {
                endTime = GoogleScrapeManager.ScrapingEndTime
            });

            return Ok(new { success = true, message = "Stopped" });
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
                activeBatchTaskCount = activeBatch?.TaskIds.Count ?? 0,
                activeBatchAssignedAt = activeBatch?.AssignedAt
            });
        }

        private async Task BroadcastDashboardUpdate()
        {
            await _hubContext.Clients.All.SendAsync("GoogleUpdateDashboard",
                GoogleScrapeManager.GetDashboardSummary());
        }

        private async Task BroadcastLogs()
        {
            await _hubContext.Clients.All.SendAsync("GoogleUpdateLogs",
                GoogleScrapeManager.GetRecentLogs(20));
        }

        private async Task BroadcastStatsUpdate()
        {
            var stats = await _context.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    total = g.Count(),
                    scraped = g.Count(c => c.GoogleIsScraped),
                    rejected = g.Count(c => c.GoogleIsRejected),
                    prices = g.Sum(c => c.GooglePricesCount)
                })
                .FirstOrDefaultAsync();

            if (stats != null)
            {
                await _hubContext.Clients.All.SendAsync("GoogleUpdateStats", new
                {
                    totalUrls = stats.total,
                    scrapedUrls = stats.scraped,
                    rejectedUrls = stats.rejected,
                    pendingUrls = stats.total - stats.scraped,
                    totalPrices = stats.prices
                });
            }
        }
    }

    // ===== DTOs =====

    public class GoogleBatchResultsDto
    {
        [JsonPropertyName("batchId")]
        public string? BatchId { get; set; }

        [JsonPropertyName("results")]
        public List<GoogleUrlResultDto> Results { get; set; } = new();
    }

    public class GoogleUrlResultDto
    {
        [JsonPropertyName("taskId")]
        public int TaskId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("offers")]
        public List<GoogleScrapedOfferDto>? Offers { get; set; }
    }

    public class GoogleScrapedOfferDto
    {
        [JsonPropertyName("googleStoreName")]
        public string GoogleStoreName { get; set; } = string.Empty;

        [JsonPropertyName("googlePrice")]
        public decimal GooglePrice { get; set; }

        [JsonPropertyName("googlePriceWithDelivery")]
        public decimal GooglePriceWithDelivery { get; set; }

        [JsonPropertyName("googlePosition")]
        public string GooglePosition { get; set; } = "0";

        [JsonPropertyName("googleInStock")]
        public bool GoogleInStock { get; set; }
    }

    public class GoogleHeartbeatDto
    {
        public string ScraperName { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
    }

    public class NukeReportDto
    {
        public string ScraperName { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string? Status { get; set; } // "started" lub "completed"
        public string? NewIpAddress { get; set; }
    }
}