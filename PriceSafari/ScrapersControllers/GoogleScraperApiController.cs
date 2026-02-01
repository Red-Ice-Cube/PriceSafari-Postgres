//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using PriceSafari.Models;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Text.Json.Serialization;
//using System.Threading.Tasks;

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

//        // === STATYCZNE ZARZĄDZANIE SCRAPERAMI ===
//        private static readonly ConcurrentDictionary<string, ScraperStatus> _activeScrapers = new();
//        private static readonly ConcurrentDictionary<int, string> _tasksInProgress = new();
//        private static readonly object _taskLock = new();
//        private static bool _isScrapingEnabled = false;
//        private static DateTime? _scrapingStartedAt = null;

//        // === STATYSTYKI SESJI (do SignalR) ===
//        private static int _totalProcessedInSession = 0;
//        private static int _totalRejectedInSession = 0;
//        private static int _totalTasksInSession = 0;
//        private static readonly Stopwatch _sessionStopwatch = new();
//        private static int _lastSignalRUpdateAt = 0;
//        private const int SIGNALR_UPDATE_INTERVAL = 100; // Co 100 wyników

//        private const int BATCH_SIZE = 100;
//        private const int SCRAPER_TIMEOUT_SECONDS = 60;

//        public GoogleScraperApiController(
//            PriceSafariContext dbContext,
//            ILogger<GoogleScraperApiController> logger,
//            IHubContext<ScrapingHub> hubContext,
//            IServiceScopeFactory serviceScopeFactory)
//        {
//            _dbContext = dbContext;
//            _logger = logger;
//            _hubContext = hubContext;
//            _serviceScopeFactory = serviceScopeFactory;
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

//        // === MODEL STATUSU SCRAPERA ===
//        public class ScraperStatus
//        {
//            public string ScraperName { get; set; } = "";
//            public DateTime LastHeartbeat { get; set; }
//            public bool IsWorking { get; set; }
//            public int TasksProcessed { get; set; }
//            public int TasksInProgress { get; set; }
//        }

//        // === MODEL ZADANIA ===
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

//        // === MODEL WYNIKU ===
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

//        // =====================================================================
//        // GET /api/google-scrape/get-task?scraperName=XXX
//        // =====================================================================
//        [HttpGet("get-task")]
//        public async Task<IActionResult> GetTask([FromQuery] string scraperName)
//        {
//            if (string.IsNullOrEmpty(scraperName))
//            {
//                return BadRequest(new { error = "scraperName is required" });
//            }

//            // Aktualizuj heartbeat scrapera
//            _activeScrapers.AddOrUpdate(scraperName,
//                new ScraperStatus
//                {
//                    ScraperName = scraperName,
//                    LastHeartbeat = DateTime.UtcNow,
//                    IsWorking = false,
//                    TasksProcessed = 0,
//                    TasksInProgress = 0
//                },
//                (key, existing) =>
//                {
//                    existing.LastHeartbeat = DateTime.UtcNow;
//                    return existing;
//                });

//            CleanupDeadScrapers();

//            if (!_isScrapingEnabled)
//            {
//                _logger.LogDebug($"[{scraperName}] Scraping disabled - returning empty");
//                return Ok(new { tasks = new List<ScraperTask>(), done = false, message = "scraping_disabled" });
//            }

//            List<ScraperTask> tasks;
//            lock (_taskLock)
//            {
//                var inProgressIds = _tasksInProgress.Keys.ToHashSet();

//                var coOfrsToScrape = _dbContext.CoOfrs
//                    .Where(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
//                                && !c.GoogleIsScraped
//                                && !inProgressIds.Contains(c.Id))
//                    .Take(BATCH_SIZE)
//                    .ToList();

//                if (!coOfrsToScrape.Any())
//                {
//                    _logger.LogInformation($"[{scraperName}] No more tasks - scraping complete");

//                    var remaining = _dbContext.CoOfrs.Count(c =>
//                        (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && !c.GoogleIsScraped);

//                    if (remaining == 0 && !_tasksInProgress.Any())
//                    {
//                        _isScrapingEnabled = false;
//                        _sessionStopwatch.Stop();

//                        // Finalny SignalR update
//                        FireAndForget("ReceiveProgressUpdate", _totalProcessedInSession, _totalTasksInSession, _sessionStopwatch.Elapsed.TotalSeconds, _totalRejectedInSession);
//                        FireAndForget("ReceiveGeneralMessage", $"[PYTHON API] Google scraping completed! Processed: {_totalProcessedInSession}, Rejected: {_totalRejectedInSession}, Time: {_sessionStopwatch.Elapsed.TotalMinutes:F1} min");

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

//                foreach (var task in tasks)
//                {
//                    _tasksInProgress.TryAdd(task.TaskId, scraperName);
//                }
//            }

//            if (_activeScrapers.TryGetValue(scraperName, out var status))
//            {
//                status.IsWorking = true;
//                status.TasksInProgress = tasks.Count;
//            }

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

//            // ❌ NIE usuwamy z _tasksInProgress tutaj - dopiero po zapisie!

//            _ = Task.Run(async () =>
//            {
//                using (var scope = _serviceScopeFactory.CreateScope())
//                {
//                    var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//                    await ProcessResultsInBackground(results, scraperName, dbContext);

//                    // ✅ DOPIERO PO ZAPISIE usuwamy z _tasksInProgress
//                    foreach (var result in results)
//                    {
//                        _tasksInProgress.TryRemove(result.TaskId, out _);
//                    }
//                }
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

//                    // Przygotowanie danych do SignalR (pojedyncze aktualizacje)
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

//                // === AKTUALIZACJA STATYSTYK SESJI ===
//                Interlocked.Add(ref _totalProcessedInSession, batchSuccessCount + batchRejectedCount);
//                Interlocked.Add(ref _totalRejectedInSession, batchRejectedCount);

//                // Aktualizacja statusu scrapera
//                if (_activeScrapers.TryGetValue(scraperName, out var status))
//                {
//                    status.TasksProcessed += results.Count;
//                    status.IsWorking = false;
//                }

//                // === WYSYŁANIE SignalR CO 100 WYNIKÓW ===
//                int currentProcessed = _totalProcessedInSession;
//                int lastUpdate = _lastSignalRUpdateAt;

//                // Sprawdź czy minęło 100 od ostatniego update'u
//                if (currentProcessed - lastUpdate >= SIGNALR_UPDATE_INTERVAL)
//                {
//                    // Atomowo aktualizuj _lastSignalRUpdateAt
//                    if (Interlocked.CompareExchange(ref _lastSignalRUpdateAt, currentProcessed, lastUpdate) == lastUpdate)
//                    {
//                        double elapsedSeconds = _sessionStopwatch.Elapsed.TotalSeconds;

//                        // Wysyłamy ReceiveProgressUpdate (ten sam format co wewnętrzny scraper)
//                        FireAndForget("ReceiveProgressUpdate",
//                            currentProcessed,           // totalScrapedCount
//                            _totalTasksInSession,       // totalTasks
//                            elapsedSeconds,             // elapsedSeconds
//                            _totalRejectedInSession);   // totalRejected

//                        _logger.LogInformation($"[SignalR] Progress: {currentProcessed}/{_totalTasksInSession} ({_totalRejectedInSession} rejected) in {elapsedSeconds:F0}s");
//                    }
//                }

//                // Wysyłamy batch update dla UI (aktualizacja wierszy w tabeli)
//                FireAndForget("ReceiveBatchScrapingUpdate", signalRUpdates);

//                _logger.LogInformation($"[{scraperName}] Background save finished in {stopwatch.ElapsedMilliseconds}ms. Session total: {_totalProcessedInSession}");
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
//            lock (_taskLock)
//            {
//                if (_isScrapingEnabled)
//                {
//                    return Ok(new { success = false, message = "Scraping already running" });
//                }

//                _isScrapingEnabled = true;
//                _scrapingStartedAt = DateTime.UtcNow;

//                // Reset statystyk sesji
//                _totalProcessedInSession = 0;
//                _totalRejectedInSession = 0;
//                _lastSignalRUpdateAt = 0;
//                _tasksInProgress.Clear();

//                // Start stopera
//                _sessionStopwatch.Restart();

//                _logger.LogInformation("Google scraping ENABLED via API");
//            }

//            // Pobierz statystyki
//            var totalTasks = await _dbContext.CoOfrs
//                .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer);
//            var completedTasks = await _dbContext.CoOfrs
//                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsScraped);

//            // Zapisz total do statystyk sesji
//            _totalTasksInSession = totalTasks;

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
//            lock (_taskLock)
//            {
//                _isScrapingEnabled = false;
//                _scrapingStartedAt = null;
//                _sessionStopwatch.Stop();
//                _tasksInProgress.Clear();

//                _logger.LogInformation("Google scraping STOPPED via API");
//            }

//            FireAndForget("ReceiveGeneralMessage", $"[PYTHON API] Google scraping stopped. Processed: {_totalProcessedInSession}, Rejected: {_totalRejectedInSession}");

//            return Ok(new { success = true, message = "Scraping stopped" });
//        }

//        // =====================================================================
//        // GET /api/google-scrape/status
//        // =====================================================================
//        [HttpGet("status")]
//        public async Task<IActionResult> GetStatus()
//        {
//            CleanupDeadScrapers();

//            var totalTasks = await _dbContext.CoOfrs
//                .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer);

//            var completedTasks = await _dbContext.CoOfrs
//                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsScraped);

//            var rejectedTasks = await _dbContext.CoOfrs
//                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsRejected);

//            var inProgressTasks = _tasksInProgress.Count;

//            var activeScrapersList = _activeScrapers.Values
//                .Select(s => new
//                {
//                    s.ScraperName,
//                    s.LastHeartbeat,
//                    s.IsWorking,
//                    s.TasksProcessed,
//                    s.TasksInProgress,
//                    SecondsAgo = (int)(DateTime.UtcNow - s.LastHeartbeat).TotalSeconds
//                })
//                .OrderBy(s => s.ScraperName)
//                .ToList();

//            return Ok(new
//            {
//                isEnabled = _isScrapingEnabled,
//                startedAt = _scrapingStartedAt,
//                totalTasks,
//                completedTasks,
//                rejectedTasks,
//                inProgressTasks,
//                remainingTasks = totalTasks - completedTasks,
//                progressPercent = totalTasks > 0 ? Math.Round((double)completedTasks / totalTasks * 100, 1) : 0,
//                activeScrapers = activeScrapersList,
//                activeScrapersCount = activeScrapersList.Count,
//                // Dodatkowe statystyki sesji
//                sessionProcessed = _totalProcessedInSession,
//                sessionRejected = _totalRejectedInSession,
//                sessionElapsedSeconds = _sessionStopwatch.IsRunning ? _sessionStopwatch.Elapsed.TotalSeconds : 0
//            });
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

//        // =====================================================================
//        // Helper: Usuń martwe scrapery
//        // =====================================================================
//        private void CleanupDeadScrapers()
//        {
//            var cutoff = DateTime.UtcNow.AddSeconds(-SCRAPER_TIMEOUT_SECONDS);
//            var deadScrapers = _activeScrapers
//                .Where(kvp => kvp.Value.LastHeartbeat < cutoff)
//                .Select(kvp => kvp.Key)
//                .ToList();

//            foreach (var name in deadScrapers)
//            {
//                _activeScrapers.TryRemove(name, out _);
//                _logger.LogInformation($"Removed dead scraper: {name}");

//                var orphanedTasks = _tasksInProgress
//                    .Where(kvp => kvp.Value == name)
//                    .Select(kvp => kvp.Key)
//                    .ToList();

//                foreach (var taskId in orphanedTasks)
//                {
//                    _tasksInProgress.TryRemove(taskId, out _);
//                }

//                if (orphanedTasks.Any())
//                {
//                    _logger.LogWarning($"Released {orphanedTasks.Count} orphaned tasks from dead scraper: {name}");
//                }
//            }
//        }
//    }
//}



using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Services.GoogleScraping;
using System.Text.Json.Serialization;

namespace PriceSafari.ScrapersControllers
{
    [ApiController]
    [Route("api/google-scrape")]
    public class GoogleScraperApiController : ControllerBase
    {
        private readonly PriceSafariContext _dbContext;
        private readonly ILogger<GoogleScraperApiController> _logger;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly GoogleScrapingStateService _stateService;

        public GoogleScraperApiController(
            PriceSafariContext dbContext,
            ILogger<GoogleScraperApiController> logger,
            IHubContext<ScrapingHub> hubContext,
            IServiceScopeFactory serviceScopeFactory,
            GoogleScrapingStateService stateService)
        {
            _dbContext = dbContext;
            _logger = logger;
            _hubContext = hubContext;
            _serviceScopeFactory = serviceScopeFactory;
            _stateService = stateService;
        }

        // === HELPER: Fire and Forget SignalR ===
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

        #region DTOs

        public class ScraperTask
        {
            public int TaskId { get; set; }
            public string? Url { get; set; }
            public string? GoogleCid { get; set; }
            public string? GoogleGid { get; set; }
            public string? GoogleHid { get; set; }
            public bool UseGoogleHidOffer { get; set; }
            public bool UseGPID { get; set; }
            public bool UseWRGA { get; set; }
        }

        public class ScraperResult
        {
            [JsonPropertyName("taskId")]
            public int TaskId { get; set; }

            [JsonPropertyName("status")]
            public string Status { get; set; } = "";

            [JsonPropertyName("offers")]
            public List<OfferResult> Offers { get; set; } = new();
        }

        public class OfferResult
        {
            [JsonPropertyName("googleStoreName")]
            public string GoogleStoreName { get; set; } = "";

            [JsonPropertyName("googlePrice")]
            public decimal GooglePrice { get; set; }

            [JsonPropertyName("googlePriceWithDelivery")]
            public decimal GooglePriceWithDelivery { get; set; }

            [JsonPropertyName("googlePosition")]
            public string GooglePosition { get; set; } = "0";

            [JsonPropertyName("isBidding")]
            public string? IsBidding { get; set; }

            [JsonPropertyName("googleInStock")]
            public bool GoogleInStock { get; set; }

            [JsonPropertyName("googleOfferPerStoreCount")]
            public int GoogleOfferPerStoreCount { get; set; }
        }

        #endregion

        // =====================================================================
        // GET /api/google-scrape/get-task?scraperName=XXX
        // =====================================================================
        [HttpGet("get-task")]
        public IActionResult GetTask([FromQuery] string scraperName)
        {
            if (string.IsNullOrEmpty(scraperName))
            {
                return BadRequest(new { error = "scraperName is required" });
            }

            // Aktualizuj heartbeat scrapera
            _stateService.UpdateScraperHeartbeat(scraperName);
            _stateService.CleanupDeadScrapers();

            if (!_stateService.IsScrapingEnabled)
            {
                _logger.LogDebug($"[{scraperName}] Scraping disabled - returning empty");
                return Ok(new { tasks = new List<ScraperTask>(), done = false, message = "scraping_disabled" });
            }

            List<ScraperTask> tasks;
            lock (_stateService.StateLock)
            {
                var inProgressIds = _stateService.GetTasksInProgressIds();

                var coOfrsToScrape = _dbContext.CoOfrs
                    .Where(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
                                && !c.GoogleIsScraped
                                && !inProgressIds.Contains(c.Id))
                    .Take(GoogleScrapingStateService.BATCH_SIZE)
                    .ToList();

                if (!coOfrsToScrape.Any())
                {
                    _logger.LogInformation($"[{scraperName}] No more tasks - scraping complete");

                    var remaining = _dbContext.CoOfrs.Count(c =>
                        (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && !c.GoogleIsScraped);

                    if (remaining == 0 && _stateService.TasksInProgressCount == 0)
                    {
                        _stateService.MarkAsCompleted();

                        // Finalny SignalR update
                        FireAndForget("ReceiveProgressUpdate",
                            _stateService.TotalProcessedInSession,
                            _stateService.TotalTasksInSession,
                            _stateService.ElapsedSeconds,
                            _stateService.TotalRejectedInSession);

                        FireAndForget("ReceiveGeneralMessage",
                            $"[PYTHON API] Google scraping completed! Processed: {_stateService.TotalProcessedInSession}, " +
                            $"Rejected: {_stateService.TotalRejectedInSession}, " +
                            $"Time: {_stateService.ElapsedTime.TotalMinutes:F1} min");

                        return Ok(new { tasks = new List<ScraperTask>(), done = true, message = "all_completed" });
                    }

                    return Ok(new { tasks = new List<ScraperTask>(), done = false, message = "no_tasks_available" });
                }

                tasks = coOfrsToScrape.Select(c => new ScraperTask
                {
                    TaskId = c.Id,
                    Url = c.GoogleOfferUrl,
                    GoogleCid = c.GoogleCid,
                    GoogleGid = c.GoogleGid,
                    GoogleHid = c.GoogleHid,
                    UseGoogleHidOffer = c.UseGoogleHidOffer,
                    UseGPID = c.UseGPID,
                    UseWRGA = c.UseWRGA
                }).ToList();

                _stateService.ReserveTasks(tasks.Select(t => t.TaskId).ToList(), scraperName);
            }

            _stateService.SetScraperWorking(scraperName, tasks.Count);

            _logger.LogInformation($"[{scraperName}] Assigned {tasks.Count} tasks");

            return Ok(new { tasks, done = false });
        }

        // =====================================================================
        // POST /api/google-scrape/submit-batch-results
        // =====================================================================
        [HttpPost("submit-batch-results")]
        public IActionResult SubmitBatchResults(
            [FromBody] List<ScraperResult>? results,
            [FromQuery] string scraperName = "unknown")
        {
            if (results == null || !results.Any())
            {
                return BadRequest(new { error = "No results provided" });
            }

            _logger.LogInformation($"[{scraperName}] Received {results.Count} results. Starting background save.");

            // Zapisz wyniki w tle
            _ = Task.Run(async () =>
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                await ProcessResultsInBackground(results, scraperName, dbContext);

                // DOPIERO PO ZAPISIE usuwamy z _tasksInProgress
                _stateService.ReleaseTasks(results.Select(r => r.TaskId).ToList());
            });

            return Ok(new { success = true, message = "Results accepted and processing in background" });
        }

        private async Task ProcessResultsInBackground(List<ScraperResult> results, string scraperName, PriceSafariContext dbContext)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var taskIds = results.Select(r => r.TaskId).ToList();
                var coOfrsDict = await dbContext.CoOfrs
                    .Where(c => taskIds.Contains(c.Id))
                    .ToDictionaryAsync(c => c.Id);

                var allHistories = new List<CoOfrPriceHistoryClass>();
                var signalRUpdates = new List<object>();
                int batchSuccessCount = 0;
                int batchRejectedCount = 0;

                foreach (var result in results)
                {
                    if (!coOfrsDict.TryGetValue(result.TaskId, out var coOfr)) continue;

                    if (result.Status == "success" && result.Offers.Any())
                    {
                        var histories = result.Offers.Select(o => new CoOfrPriceHistoryClass
                        {
                            CoOfrClassId = coOfr.Id,
                            GoogleStoreName = o.GoogleStoreName,
                            GooglePrice = o.GooglePrice,
                            GooglePriceWithDelivery = o.GooglePriceWithDelivery,
                            GooglePosition = o.GooglePosition,
                            GoogleInStock = o.GoogleInStock
                        }).ToList();

                        allHistories.AddRange(histories);
                        coOfr.GoogleIsScraped = true;
                        coOfr.GoogleIsRejected = false;
                        coOfr.GooglePricesCount = histories.Count;
                        batchSuccessCount++;
                    }
                    else
                    {
                        coOfr.GoogleIsScraped = true;
                        coOfr.GoogleIsRejected = true;
                        coOfr.GooglePricesCount = 0;
                        batchRejectedCount++;
                    }

                    signalRUpdates.Add(new
                    {
                        id = coOfr.Id,
                        isScraped = coOfr.GoogleIsScraped,
                        isRejected = coOfr.GoogleIsRejected,
                        pricesCount = coOfr.GooglePricesCount
                    });
                }

                if (allHistories.Any()) await dbContext.CoOfrPriceHistories.AddRangeAsync(allHistories);
                await dbContext.SaveChangesAsync();

                // Aktualizacja statystyk sesji
                _stateService.AddProcessedResults(batchSuccessCount, batchRejectedCount);
                _stateService.UpdateScraperStats(scraperName, results.Count);

                // Wysyłanie SignalR co 100 wyników
                if (_stateService.ShouldSendProgressUpdate(out int currentProcessed))
                {
                    FireAndForget("ReceiveProgressUpdate",
                        currentProcessed,
                        _stateService.TotalTasksInSession,
                        _stateService.ElapsedSeconds,
                        _stateService.TotalRejectedInSession);

                    _logger.LogInformation($"[SignalR] Progress: {currentProcessed}/{_stateService.TotalTasksInSession} " +
                                          $"({_stateService.TotalRejectedInSession} rejected) in {_stateService.ElapsedSeconds:F0}s");
                }

                // Wysyłamy batch update dla UI
                FireAndForget("ReceiveBatchScrapingUpdate", signalRUpdates);

                _logger.LogInformation($"[{scraperName}] Background save finished in {stopwatch.ElapsedMilliseconds}ms. " +
                                      $"Session total: {_stateService.TotalProcessedInSession}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[{scraperName}] Error saving results in background");
            }
        }

        // =====================================================================
        // POST /api/google-scrape/start
        // =====================================================================
        [HttpPost("start")]
        public async Task<IActionResult> StartScraping()
        {
            // Pobierz statystyki
            var totalTasks = await _dbContext.CoOfrs
                .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer);
            var completedTasks = await _dbContext.CoOfrs
                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsScraped);

            if (!_stateService.TryStartScraping(totalTasks))
            {
                return Ok(new { success = false, message = "Scraping already running" });
            }

            _logger.LogInformation("Google scraping ENABLED via API");

            FireAndForget("ReceiveGeneralMessage", $"[PYTHON API] Google scraping started! ({totalTasks - completedTasks} tasks remaining)");
            FireAndForget("ReceiveProgressUpdate", completedTasks, totalTasks, 0, 0);

            return Ok(new { success = true, message = "Scraping started" });
        }

        // =====================================================================
        // POST /api/google-scrape/stop
        // =====================================================================
        [HttpPost("stop")]
        public IActionResult StopScraping()
        {
            var (processed, rejected, elapsed) = _stateService.StopScraping();

            _logger.LogInformation("Google scraping STOPPED via API");

            FireAndForget("ReceiveGeneralMessage", $"[PYTHON API] Google scraping stopped. Processed: {processed}, Rejected: {rejected}");

            return Ok(new { success = true, message = "Scraping stopped" });
        }

        // =====================================================================
        // GET /api/google-scrape/status
        // =====================================================================
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            _stateService.CleanupDeadScrapers();

            var totalTasks = await _dbContext.CoOfrs
                .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer);

            var completedTasks = await _dbContext.CoOfrs
                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsScraped);

            var rejectedTasks = await _dbContext.CoOfrs
                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsRejected);

            var status = _stateService.GetFullStatus(totalTasks, completedTasks, rejectedTasks);

            return Ok(status);
        }

        // =====================================================================
        // GET /api/google-scrape/settings
        // =====================================================================
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            var settings = await _dbContext.Settings.FirstOrDefaultAsync();

            if (settings == null)
            {
                return Ok(new
                {
                    generatorsCount = 2,
                    headlessMode = true,
                    maxWorkers = 1,
                    headStartDuration = 45
                });
            }

            return Ok(new
            {
                generatorsCount = settings.GoogleGeneratorsCount > 0 ? settings.GoogleGeneratorsCount : 2,
                headlessMode = settings.HeadLessForGoogleGenerators,
                maxWorkers = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1,
                headStartDuration = 45
            });
        }
    }
}