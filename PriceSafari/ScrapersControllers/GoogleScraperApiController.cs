using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PriceSafari.ScrapersControllers
{
    [ApiController]
    [Route("api/google-scrape")]
    public class GoogleScraperApiController : ControllerBase
    {
        private readonly PriceSafariContext _dbContext;
        private readonly ILogger<GoogleScraperApiController> _logger;
        private readonly IHubContext<ScrapingHub> _hubContext;

        // === STATYCZNE ZARZĄDZANIE SCRAPERAMI ===
        private static readonly ConcurrentDictionary<string, ScraperStatus> _activeScrapers = new();
        private static readonly ConcurrentDictionary<int, string> _tasksInProgress = new(); // TaskId -> ScraperName
        private static readonly object _taskLock = new();
        private static bool _isScrapingEnabled = false;
        private static DateTime? _scrapingStartedAt = null;
        private static int _totalProcessedInSession = 0;
        private static int _totalRejectedInSession = 0;

        private const int BATCH_SIZE = 200;
        private const int SCRAPER_TIMEOUT_SECONDS = 60;

        public GoogleScraperApiController(
            PriceSafariContext dbContext,
            ILogger<GoogleScraperApiController> logger,
            IHubContext<ScrapingHub> hubContext)
        {
            _dbContext = dbContext;
            _logger = logger;
            _hubContext = hubContext;
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

        // === MODEL STATUSU SCRAPERA ===
        public class ScraperStatus
        {
            public string ScraperName { get; set; } = "";
            public DateTime LastHeartbeat { get; set; }
            public bool IsWorking { get; set; }
            public int TasksProcessed { get; set; }
            public int TasksInProgress { get; set; }
        }

        // === MODEL ZADANIA ===
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

        // === MODEL WYNIKU (z atrybutami JSON) ===
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

        // =====================================================================
        // GET /api/google-scrape/get-task?scraperName=XXX
        // =====================================================================
        [HttpGet("get-task")]
        public async Task<IActionResult> GetTask([FromQuery] string scraperName)
        {
            if (string.IsNullOrEmpty(scraperName))
            {
                return BadRequest(new { error = "scraperName is required" });
            }

            // Aktualizuj heartbeat scrapera
            _activeScrapers.AddOrUpdate(scraperName,
                new ScraperStatus
                {
                    ScraperName = scraperName,
                    LastHeartbeat = DateTime.UtcNow,
                    IsWorking = false,
                    TasksProcessed = 0,
                    TasksInProgress = 0
                },
                (key, existing) =>
                {
                    existing.LastHeartbeat = DateTime.UtcNow;
                    return existing;
                });

            // Wyczyść martwe scrapery i ich zadania
            CleanupDeadScrapers();

            // Sprawdź czy scrapowanie jest włączone
            if (!_isScrapingEnabled)
            {
                _logger.LogDebug($"[{scraperName}] Scraping disabled - returning empty");
                return Ok(new { tasks = new List<ScraperTask>(), done = false, message = "scraping_disabled" });
            }

            // Pobierz paczkę zadań
            List<ScraperTask> tasks;
            lock (_taskLock)
            {
                // Pobierz ID zadań które są już w trakcie
                var inProgressIds = _tasksInProgress.Keys.ToHashSet();

                var coOfrsToScrape = _dbContext.CoOfrs
                    .Where(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
                                && !c.GoogleIsScraped
                                && !inProgressIds.Contains(c.Id))  // Wyklucz już przetwarzane
                    .Take(BATCH_SIZE)
                    .ToList();

                if (!coOfrsToScrape.Any())
                {
                    _logger.LogInformation($"[{scraperName}] No more tasks - scraping complete");

                    var remaining = _dbContext.CoOfrs.Count(c =>
                        (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && !c.GoogleIsScraped);

                    // Sprawdź też czy nie ma zadań w trakcie
                    if (remaining == 0 && !_tasksInProgress.Any())
                    {
                        _isScrapingEnabled = false;
                        FireAndForget("ReceiveGeneralMessage", "Google scraping completed!");
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

                // Oznacz zadania jako "w trakcie" W PAMIĘCI (nie w bazie!)
                foreach (var task in tasks)
                {
                    _tasksInProgress.TryAdd(task.TaskId, scraperName);
                }
            }

            // Aktualizuj status scrapera
            if (_activeScrapers.TryGetValue(scraperName, out var status))
            {
                status.IsWorking = true;
                status.TasksInProgress = tasks.Count;
            }

            _logger.LogInformation($"[{scraperName}] Assigned {tasks.Count} tasks");

            return Ok(new { tasks, done = false });
        }

        // =====================================================================
        // POST /api/google-scrape/submit-batch-results
        // =====================================================================
        [HttpPost("submit-batch-results")]
        public async Task<IActionResult> SubmitBatchResults([FromBody] List<ScraperResult>? results, [FromQuery] string scraperName = "unknown")
        {
            if (results == null || !results.Any())
            {
                _logger.LogWarning($"[{scraperName}] Received null or empty results");
                return BadRequest(new { error = "No results provided" });
            }

            _logger.LogInformation($"[{scraperName}] Received {results.Count} results");

            int successCount = 0;
            int failedCount = 0;

            // Pobierz total do progress
            var totalTasks = await _dbContext.CoOfrs
                .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer);

            foreach (var result in results)
            {
                try
                {
                    // Usuń z listy "w trakcie"
                    _tasksInProgress.TryRemove(result.TaskId, out _);

                    var coOfr = await _dbContext.CoOfrs.FindAsync(result.TaskId);
                    if (coOfr == null)
                    {
                        _logger.LogWarning($"CoOfr {result.TaskId} not found");
                        continue;
                    }

                    if (result.Status == "success" && result.Offers.Any())
                    {
                        // Dodaj wyniki do bazy
                        var priceHistories = result.Offers.Select(o => new CoOfrPriceHistoryClass
                        {
                            CoOfrClassId = coOfr.Id,
                            GoogleCid = coOfr.GoogleCid,
                            GoogleStoreName = o.GoogleStoreName,
                            GooglePrice = o.GooglePrice,
                            GooglePriceWithDelivery = o.GooglePriceWithDelivery,
                            GooglePosition = o.GooglePosition,
                            IsBidding = o.IsBidding,
                            GoogleInStock = o.GoogleInStock,
                            GoogleOfferPerStoreCount = o.GoogleOfferPerStoreCount
                        }).ToList();

                        await _dbContext.CoOfrPriceHistories.AddRangeAsync(priceHistories);

                        coOfr.GoogleIsScraped = true;
                        coOfr.GoogleIsRejected = false;
                        coOfr.GooglePricesCount = priceHistories.Count;

                        successCount++;
                        _totalProcessedInSession++;

                        // === SIGNALR: Aktualizacja wiersza ===
                        FireAndForget("ReceiveScrapingUpdate",
                            coOfr.Id,
                            coOfr.GoogleIsScraped,
                            coOfr.GoogleIsRejected,
                            coOfr.GooglePricesCount,
                            "Google");
                    }
                    else if (result.Status == "rejected" || result.Status == "failed")
                    {
                        coOfr.GoogleIsScraped = true;
                        coOfr.GoogleIsRejected = true;
                        coOfr.GooglePricesCount = 0;

                        failedCount++;
                        _totalRejectedInSession++;

                        // === SIGNALR: Aktualizacja wiersza ===
                        FireAndForget("ReceiveScrapingUpdate",
                            coOfr.Id,
                            coOfr.GoogleIsScraped,
                            coOfr.GoogleIsRejected,
                            coOfr.GooglePricesCount,
                            "Google");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing result for TaskId {result.TaskId}");
                }
            }

            await _dbContext.SaveChangesAsync();

            // Aktualizuj status scrapera
            if (_activeScrapers.TryGetValue(scraperName, out var status))
            {
                status.TasksProcessed += results.Count;
                status.TasksInProgress = 0;
                status.IsWorking = false;
            }

            // === SIGNALR: Progress update ===
            var completedTasks = await _dbContext.CoOfrs
                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsScraped);

            double elapsedSeconds = _scrapingStartedAt.HasValue
                ? (DateTime.UtcNow - _scrapingStartedAt.Value).TotalSeconds
                : 0;

            FireAndForget("ReceiveProgressUpdate",
                completedTasks,
                totalTasks,
                elapsedSeconds,
                _totalRejectedInSession);

            _logger.LogInformation($"[{scraperName}] Processed: {successCount} success, {failedCount} failed");

            return Ok(new { success = true, processed = results.Count, successCount, failedCount });
        }

        // =====================================================================
        // POST /api/google-scrape/start
        // =====================================================================
        [HttpPost("start")]
        public async Task<IActionResult> StartScraping()
        {
            lock (_taskLock)
            {
                if (_isScrapingEnabled)
                {
                    return Ok(new { success = false, message = "Scraping already running" });
                }

                _isScrapingEnabled = true;
                _scrapingStartedAt = DateTime.UtcNow;
                _totalProcessedInSession = 0;
                _totalRejectedInSession = 0;
                _tasksInProgress.Clear();

                _logger.LogInformation("Google scraping ENABLED via API");
            }

            // Pobierz statystyki do SignalR
            var totalTasks = await _dbContext.CoOfrs
                .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer);
            var completedTasks = await _dbContext.CoOfrs
                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsScraped);

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
            lock (_taskLock)
            {
                _isScrapingEnabled = false;
                _scrapingStartedAt = null;

                // Wyczyść zadania w trakcie (bez zmian w bazie!)
                _tasksInProgress.Clear();

                _logger.LogInformation("Google scraping STOPPED via API");
            }

            FireAndForget("ReceiveGeneralMessage", "[PYTHON API] Google scraping stopped.");

            return Ok(new { success = true, message = "Scraping stopped" });
        }

        // =====================================================================
        // GET /api/google-scrape/status
        // =====================================================================
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            CleanupDeadScrapers();

            var totalTasks = await _dbContext.CoOfrs
                .CountAsync(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer);

            var completedTasks = await _dbContext.CoOfrs
                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsScraped);

            var rejectedTasks = await _dbContext.CoOfrs
                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && c.GoogleIsRejected);

            var inProgressTasks = _tasksInProgress.Count;  // Z pamięci, nie z bazy!

            var activeScrapersList = _activeScrapers.Values
                .Select(s => new
                {
                    s.ScraperName,
                    s.LastHeartbeat,
                    s.IsWorking,
                    s.TasksProcessed,
                    s.TasksInProgress,
                    SecondsAgo = (int)(DateTime.UtcNow - s.LastHeartbeat).TotalSeconds
                })
                .OrderBy(s => s.ScraperName)
                .ToList();

            return Ok(new
            {
                isEnabled = _isScrapingEnabled,
                startedAt = _scrapingStartedAt,
                totalTasks,
                completedTasks,
                rejectedTasks,
                inProgressTasks,
                remainingTasks = totalTasks - completedTasks,
                progressPercent = totalTasks > 0 ? Math.Round((double)completedTasks / totalTasks * 100, 1) : 0,
                activeScrapers = activeScrapersList,
                activeScrapersCount = activeScrapersList.Count
            });
        }

        // =====================================================================
        // GET /api/google-scrape/settings
        // Zwraca ustawienia scrapera z bazy danych
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
                headStartDuration = 45  // Możesz też dodać to do Settings w bazie
            });
        }

        // =====================================================================
        // Helper: Usuń martwe scrapery i zwolnij ich zadania
        // =====================================================================
        private void CleanupDeadScrapers()
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-SCRAPER_TIMEOUT_SECONDS);
            var deadScrapers = _activeScrapers
                .Where(kvp => kvp.Value.LastHeartbeat < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var name in deadScrapers)
            {
                _activeScrapers.TryRemove(name, out _);
                _logger.LogInformation($"Removed dead scraper: {name}");

                // Zwolnij zadania tego scrapera (usuń z _tasksInProgress)
                var orphanedTasks = _tasksInProgress
                    .Where(kvp => kvp.Value == name)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var taskId in orphanedTasks)
                {
                    _tasksInProgress.TryRemove(taskId, out _);
                }

                if (orphanedTasks.Any())
                {
                    _logger.LogWarning($"Released {orphanedTasks.Count} orphaned tasks from dead scraper: {name}");
                }
            }
        }
    }
}