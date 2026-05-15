using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using System.Text.Json.Serialization;

namespace PriceSafari.Services.CeneoExternalScraping
{
    [ApiController]
    [Route("api/ceneo-external-scrape")]
    public class CeneoExternalScrapeApiController : ControllerBase
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<CeneoExternalScrapeApiController> _logger;
        private readonly CeneoExternalScrapingService _scrapingService;
        private const string ApiKey = "2764udhnJUDI8392j83jfi2ijdo1949rncowp89i3rnfiiui1203kfnf9030rfpPkUjHyHt";

        public CeneoExternalScrapeApiController(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            ILogger<CeneoExternalScrapeApiController> logger,
            CeneoExternalScrapingService scrapingService)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
            _scrapingService = scrapingService;
        }

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var settings = await _context.Settings.FirstOrDefaultAsync();

            return Ok(new
            {
                maxWorkers = settings?.Semophore > 0 ? settings.Semophore : 1,
                headlessMode = settings?.HeadLess ?? true,
                javaScript = settings?.JavaScript ?? false,
                warmUpTime = settings?.WarmUpTime ?? 5,
                getCeneoName = settings?.GetCeneoName ?? false,
                captchaCountdownSeconds = 10,
                batchSize = CeneoExternalScrapeManager.BatchSize
            });
        }

        [HttpGet("get-task")]
        public async Task<IActionResult> GetTaskBatch(
            [FromQuery] string scraperName,
            [FromQuery] string? currentStatus = null,
            [FromQuery] string? ipAddress = null)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();
            if (string.IsNullOrWhiteSpace(scraperName))
                return BadRequest(new { error = "scraperName is required" });

            var scraper = CeneoExternalScrapeManager.RegisterScraperCheckIn(scraperName, ipAddress);

            if (currentStatus == "SOLVING_CAPTCHA" && scraper.Status != CeneoExternalScraperLiveStatus.SolvingCaptcha)
                CeneoExternalScrapeManager.MarkScraperSolvingCaptcha(scraperName, "Zgłoszony przez Python");
            else if (currentStatus == "CAPTCHA_SOLVED" && scraper.Status == CeneoExternalScraperLiveStatus.SolvingCaptcha)
                CeneoExternalScrapeManager.MarkScraperCaptchaSolved(scraperName);

            await BroadcastScraperStatus(scraper);

            if (!CeneoExternalScrapeManager.CanScraperReceiveTask(scraperName))
            {
                var reason = CeneoExternalScrapeManager.CurrentStatus != CeneoExternalScrapingProcessStatus.Running
                    ? "Scraping process is not running."
                    : scraper.Status == CeneoExternalScraperLiveStatus.SolvingCaptcha
                        ? "Scraper is solving captcha."
                        : "Scraper is manually paused (hibernation).";

                return Ok(new
                {
                    tasks = new List<object>(),
                    done = false,
                    message = reason,
                    shouldHibernate = true
                });
            }

            var existingBatch = CeneoExternalScrapeManager.GetActiveScraperBatch(scraperName);
            if (existingBatch != null)
            {
                _logger.LogWarning("Scraper {ScraperName} ma aktywną paczkę {BatchId} - zwracam ponownie",
                    scraperName, existingBatch.BatchId);
                CeneoExternalScrapeManager.AddLog(scraperName, "WARNING",
                    "Scraper odpytał ponownie - ma aktywną paczkę", existingBatch.BatchId);

                var existingTasks = await _context.CoOfrs
                    .Where(c => existingBatch.TaskIds.Contains(c.Id))
                    .Select(c => new
                    {
                        taskId = c.Id,
                        url = c.OfferUrl,
                        storeNames = c.StoreNames ?? new List<string>(),
                        storeProfiles = c.StoreProfiles ?? new List<string>()
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

            List<CoOfrClass> offersToScrape;
            string batchId;
            List<int> taskIds;

            lock (CeneoExternalScrapeManager.BatchAssignmentLock)
            {
                var assignedTaskIds = CeneoExternalScrapeManager.GetAllActiveTaskIds();

                offersToScrape = _context.CoOfrs
                    .Where(c => !c.IsScraped
                                && !string.IsNullOrEmpty(c.OfferUrl)
                                && !assignedTaskIds.Contains(c.Id))
                    .OrderBy(c => c.Id)
                    .Take(CeneoExternalScrapeManager.BatchSize)
                    .ToList();

                if (!offersToScrape.Any())
                {
                    batchId = null!;
                    taskIds = null!;
                }
                else
                {
                    batchId = CeneoExternalScrapeManager.GenerateBatchId();
                    taskIds = offersToScrape.Select(c => c.Id).ToList();
                    CeneoExternalScrapeManager.RegisterAssignedBatch(batchId, scraperName, taskIds);
                }
            }

            if (!offersToScrape.Any())
            {
                var anyActiveBatches = CeneoExternalScrapeManager.HasActiveBatches();

                if (!anyActiveBatches && CeneoExternalScrapeManager.CurrentStatus == CeneoExternalScrapingProcessStatus.Running)
                {
                    var anyPending = await _context.CoOfrs
                        .AnyAsync(c => !c.IsScraped && !string.IsNullOrEmpty(c.OfferUrl));

                    if (!anyPending)
                    {
                        CeneoExternalScrapeManager.FinishProcess();
                        await _hubContext.Clients.All.SendAsync("CeneoExternalScrapingFinished", new
                        {
                            endTime = CeneoExternalScrapeManager.ScrapingEndTime,
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

            await BroadcastScraperStatus(scraper);
            await BroadcastDashboardUpdate();
            await BroadcastLogs();

            var tasksForPython = offersToScrape.Select(c => new
            {
                taskId = c.Id,
                url = c.OfferUrl,
                storeNames = c.StoreNames ?? new List<string>(),
                storeProfiles = c.StoreProfiles ?? new List<string>()
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

        [HttpPost("submit-batch-results")]
        public async Task<IActionResult> SubmitBatchResults(
            [FromBody] CeneoExternalBatchResultsDto batchResults,
            [FromQuery] string scraperName = "unknown")
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            if (batchResults?.Results == null || !batchResults.Results.Any())
                return BadRequest(new { error = "No results provided" });

            var batchId = batchResults.BatchId ?? "UNKNOWN";
            var results = batchResults.Results;

            if (CeneoExternalScrapeManager.AssignedBatches.TryGetValue(batchId, out var existingBatch))
            {
                if (existingBatch.IsTimedOut)
                {
                    _logger.LogWarning($"[API] Odrzucono wyniki paczki {batchId} - paczka wygasła (TIMEOUT).");
                    return Ok(new { success = false, message = "Batch timed out" });
                }
            }

            _logger.LogInformation("Otrzymano wyniki dla paczki {BatchId}: {Count} URLi od {ScraperName}",
                batchId, results.Count, scraperName);

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
                    offer.IsScraped = true;
                    offer.IsRejected = false;
                    offer.PricesCount = result.Offers.Count;
                    if (result.SalesCount.HasValue)
                        offer.CeneoSalesCount = result.SalesCount.Value;

                    successCount++;
                    totalPricesCollected += result.Offers.Count;

                    foreach (var scraped in result.Offers)
                    {
                        newPriceHistories.Add(new CoOfrPriceHistoryClass
                        {
                            CoOfrClassId = offer.Id,
                            StoreName = scraped.StoreName,
                            Price = scraped.Price,
                            ShippingCostNum = scraped.ShippingCostNum,
                            CeneoInStock = scraped.CeneoInStock,
                            IsBidding = scraped.IsBidding,
                            Position = scraped.Position,
                            ExportedName = scraped.CeneoName
                        });
                    }
                }
                else if (result.Status == "rejected"
                         || (result.Status == "success" && (result.Offers == null || !result.Offers.Any())))
                {
                    offer.IsScraped = true;
                    offer.IsRejected = true;
                    offer.PricesCount = 0;
                    rejectedCount++;
                }
                else
                {
                    failedCount++;
                }

                await _hubContext.Clients.All.SendAsync("CeneoExternalUpdateOfferRow", new
                {
                    id = offer.Id,
                    isScraped = offer.IsScraped,
                    isRejected = offer.IsRejected,
                    pricesCount = offer.PricesCount
                });
            }

            if (newPriceHistories.Any())
                await _context.CoOfrPriceHistories.AddRangeAsync(newPriceHistories);
            await _context.SaveChangesAsync();

            CeneoExternalScrapeManager.CompleteBatch(batchId, successCount, failedCount, rejectedCount, totalPricesCollected);

            if (CeneoExternalScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

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

        [HttpPost("report-captcha")]
        public async Task<IActionResult> ReportCaptcha([FromBody] CeneoExternalCaptchaReportDto report)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();
            if (string.IsNullOrWhiteSpace(report.ScraperName))
                return BadRequest(new { error = "scraperName is required" });

            if (report.Status == "started")
                CeneoExternalScrapeManager.MarkScraperSolvingCaptcha(report.ScraperName, report.Reason);
            else if (report.Status == "completed")
                CeneoExternalScrapeManager.MarkScraperCaptchaSolved(report.ScraperName);

            if (CeneoExternalScrapeManager.ActiveScrapers.TryGetValue(report.ScraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            await BroadcastLogs();
            await BroadcastDashboardUpdate();

            _logger.LogWarning("Scraper {ScraperName} zgłosił CAPTCHA ({Status}): {Reason}",
                report.ScraperName, report.Status, report.Reason);

            return Ok(new { acknowledged = true });
        }

        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromBody] CeneoExternalHeartbeatDto heartbeat)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var scraper = CeneoExternalScrapeManager.RegisterScraperCheckIn(heartbeat.ScraperName, heartbeat.IpAddress);
            await BroadcastScraperStatus(scraper);

            return Ok(new
            {
                acknowledged = true,
                shouldHibernate = !CeneoExternalScrapeManager.CanScraperReceiveTask(heartbeat.ScraperName),
                globalStatus = CeneoExternalScrapeManager.CurrentStatus.ToString()
            });
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var totalTasks = await _context.CoOfrs
                .CountAsync(c => !string.IsNullOrEmpty(c.OfferUrl));
            var completedTasks = await _context.CoOfrs
                .CountAsync(c => !string.IsNullOrEmpty(c.OfferUrl) && c.IsScraped);

            return Ok(new
            {
                isEnabled = CeneoExternalScrapeManager.CurrentStatus == CeneoExternalScrapingProcessStatus.Running,
                status = CeneoExternalScrapeManager.CurrentStatus.ToString(),
                startedAt = CeneoExternalScrapeManager.ScrapingStartTime,
                totalTasks = totalTasks,
                completedTasks = completedTasks,
                remainingTasks = totalTasks - completedTasks,
                progressPercent = totalTasks > 0 ? Math.Round((double)completedTasks / totalTasks * 100, 1) : 0,
                activeScrapers = CeneoExternalScrapeManager.GetScrapersDetails(),
                activeBatchesCount = CeneoExternalScrapeManager.AssignedBatches.Values.Count(b => !b.IsCompleted && !b.IsTimedOut)
            });
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartScraping()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var (success, message, totalUrls) = await _scrapingService.StartScrapingProcessAsync();
            return Ok(new { success, message, totalUrls });
        }

        [HttpPost("stop")]
        public async Task<IActionResult> StopScraping()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();
            var (success, message) = await _scrapingService.StopScrapingProcessAsync();
            return Ok(new { success, message });
        }

        private async Task BroadcastScraperStatus(CeneoExternalScraperClient scraper)
        {
            CeneoExternalScrapeManager.ScraperStatistics.TryGetValue(scraper.Name, out var stats);
            var activeBatch = CeneoExternalScrapeManager.GetActiveScraperBatch(scraper.Name);

            await _hubContext.Clients.All.SendAsync("CeneoExternalUpdateScraperStatus", new
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
                captchaCount = stats?.CaptchaCount ?? 0,
                activeBatchTaskCount = activeBatch?.TaskIds.Count ?? 0,
                activeBatchAssignedAt = activeBatch?.AssignedAt
            });
        }

        private async Task BroadcastDashboardUpdate()
        {
            await _hubContext.Clients.All.SendAsync("CeneoExternalUpdateDashboard",
                CeneoExternalScrapeManager.GetDashboardSummary());
        }

        private async Task BroadcastLogs()
        {
            await _hubContext.Clients.All.SendAsync("CeneoExternalUpdateLogs",
                CeneoExternalScrapeManager.GetRecentLogs(20));
        }

        private async Task BroadcastStatsUpdate()
        {
            var stats = await _context.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.OfferUrl))
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    total = g.Count(),
                    scraped = g.Count(c => c.IsScraped),
                    rejected = g.Count(c => c.IsRejected),
                    prices = g.Sum(c => c.PricesCount)
                })
                .FirstOrDefaultAsync();

            if (stats != null)
            {
                await _hubContext.Clients.All.SendAsync("CeneoExternalUpdateStats", new
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

    // DTOs
    public class CeneoExternalBatchResultsDto
    {
        [JsonPropertyName("batchId")]
        public string? BatchId { get; set; }

        [JsonPropertyName("results")]
        public List<CeneoExternalUrlResultDto> Results { get; set; } = new();
    }

    public class CeneoExternalUrlResultDto
    {
        [JsonPropertyName("taskId")]
        public int TaskId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("salesCount")]
        public int? SalesCount { get; set; }

        [JsonPropertyName("offers")]
        public List<CeneoExternalScrapedOfferDto>? Offers { get; set; }
    }

    public class CeneoExternalScrapedOfferDto
    {
        [JsonPropertyName("storeName")]
        public string StoreName { get; set; } = string.Empty;

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("shippingCostNum")]
        public decimal? ShippingCostNum { get; set; }

        [JsonPropertyName("ceneoInStock")]
        public bool? CeneoInStock { get; set; }

        [JsonPropertyName("isBidding")]
        public string IsBidding { get; set; } = "0";

        [JsonPropertyName("position")]
        public string? Position { get; set; }

        [JsonPropertyName("ceneoName")]
        public string? CeneoName { get; set; }
    }

    public class CeneoExternalHeartbeatDto
    {
        public string ScraperName { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
    }

    public class CeneoExternalCaptchaReportDto
    {
        public string ScraperName { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string? Status { get; set; }
    }
}