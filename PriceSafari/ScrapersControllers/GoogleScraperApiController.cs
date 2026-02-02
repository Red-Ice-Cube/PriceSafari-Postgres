

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