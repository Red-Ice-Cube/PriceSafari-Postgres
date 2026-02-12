using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Services.GoogleScraping;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PriceSafari.ScrapersControllers
{
    [ApiController]
    [Route("api/global-scrape")]
    public class GlobalScrapeApiController : ControllerBase
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<GlobalScrapeApiController> _logger;
        private const string ApiKey = "2764udhnJUDI8392j83jfi2ijdo1949rncowp89i3rnfiiui1203kfnf9030rfpPkUjHyHt";

        public GlobalScrapeApiController(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            ILogger<GlobalScrapeApiController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

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
                batchSize = GoogleGlobalScrapeManager.BatchSize
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

            var scraper = GoogleGlobalScrapeManager.RegisterScraperCheckIn(scraperName, ipAddress);

            // Obsługa protokołu NUKE
            if (currentStatus == "NUKE_IN_PROGRESS" && scraper.Status != GlobalScraperLiveStatus.ResettingNetwork)
            {
                GoogleGlobalScrapeManager.MarkScraperNuking(scraperName, "Zgłoszony przez Python");
            }
            else if (currentStatus == "NUKE_COMPLETED" && scraper.Status == GlobalScraperLiveStatus.ResettingNetwork)
            {
                GoogleGlobalScrapeManager.MarkScraperNukeCompleted(scraperName, ipAddress);
            }

            await BroadcastScraperStatus(scraper);

            if (!GoogleGlobalScrapeManager.CanScraperReceiveTask(scraperName))
            {
                var reason = GoogleGlobalScrapeManager.CurrentStatus != GlobalScrapingProcessStatus.Running
                    ? "Scraping process is not running."
                    : scraper.Status == GlobalScraperLiveStatus.ResettingNetwork
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

            // Sprawdź czy scraper ma już aktywną paczkę
            var existingBatch = GoogleGlobalScrapeManager.GetActiveScraperBatch(scraperName);
            if (existingBatch != null)
            {
                _logger.LogWarning("Global Scraper {ScraperName} ma aktywną paczkę {BatchId} - zwracam ponownie",
                    scraperName, existingBatch.BatchId);

                GoogleGlobalScrapeManager.AddLog(scraperName, "WARNING",
                    "Scraper odpytał ponownie - ma aktywną paczkę", existingBatch.BatchId);

                var existingTasks = await _context.GoogleScrapingProducts
                    .Where(gsp => existingBatch.TaskIds.Contains(gsp.ScrapingProductId))
                    .Select(gsp => new
                    {
                        taskId = gsp.ScrapingProductId,
                        googleUrl = gsp.GoogleUrl,
                        catalogId = ExtractCatalogIdStatic(gsp.GoogleUrl),
                        countryCode = gsp.CountryCode,
                        regionId = gsp.RegionId
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

            // Pobierz nowe taski
            List<GoogleScrapingProduct> productsToScrape;
            string batchId;
            List<int> taskIds;

            lock (GoogleGlobalScrapeManager.BatchAssignmentLock)
            {
                var assignedTaskIds = GoogleGlobalScrapeManager.GetAllActiveTaskIds();
                _logger.LogDebug("Global: Aktywne paczki zawierają {Count} produktów", assignedTaskIds.Count);

                var query = _context.GoogleScrapingProducts
                    .Where(gsp => gsp.IsScraped == null
                                  && !string.IsNullOrEmpty(gsp.GoogleUrl)
                                  && !assignedTaskIds.Contains(gsp.ScrapingProductId));

                // Filtr regionu jeśli ustawiony
                if (GoogleGlobalScrapeManager.ActiveRegionFilter.HasValue)
                {
                    query = query.Where(gsp => gsp.RegionId == GoogleGlobalScrapeManager.ActiveRegionFilter.Value);
                }

                productsToScrape = query
                    .OrderBy(gsp => gsp.ScrapingProductId)
                    .Take(GoogleGlobalScrapeManager.BatchSize)
                    .ToList();

                if (!productsToScrape.Any())
                {
                    batchId = null!;
                    taskIds = null!;
                }
                else
                {
                    batchId = GoogleGlobalScrapeManager.GenerateBatchId();
                    taskIds = productsToScrape.Select(gsp => gsp.ScrapingProductId).ToList();
                    GoogleGlobalScrapeManager.RegisterAssignedBatch(batchId, scraperName, taskIds);
                }
            }

            if (!productsToScrape.Any())
            {
                var anyActiveBatches = GoogleGlobalScrapeManager.HasActiveBatches();

                if (!anyActiveBatches && GoogleGlobalScrapeManager.CurrentStatus == GlobalScrapingProcessStatus.Running)
                {
                    var pendingQuery = _context.GoogleScrapingProducts
                        .Where(gsp => gsp.IsScraped == null && !string.IsNullOrEmpty(gsp.GoogleUrl));

                    if (GoogleGlobalScrapeManager.ActiveRegionFilter.HasValue)
                        pendingQuery = pendingQuery.Where(gsp => gsp.RegionId == GoogleGlobalScrapeManager.ActiveRegionFilter.Value);

                    var anyPending = await pendingQuery.AnyAsync();

                    if (!anyPending)
                    {
                        GoogleGlobalScrapeManager.FinishProcess();

                        await _hubContext.Clients.All.SendAsync("GlobalScrapingFinished", new
                        {
                            endTime = GoogleGlobalScrapeManager.ScrapingEndTime,
                            message = "Global scraping completed"
                        });

                        return Ok(new
                        {
                            tasks = new List<object>(),
                            done = true,
                            message = "Global scraping completed",
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

            var tasksForPython = productsToScrape.Select(gsp => new
            {
                taskId = gsp.ScrapingProductId,
                googleUrl = gsp.GoogleUrl,
                catalogId = ExtractCatalogIdStatic(gsp.GoogleUrl),
                countryCode = gsp.CountryCode ?? "pl",
                regionId = gsp.RegionId
            });

            _logger.LogInformation("Global: Przydzielono paczkę {BatchId} ({Count} prod.) do {ScraperName}",
                batchId, productsToScrape.Count, scraperName);

            return Ok(new
            {
                batchId = batchId,
                tasks = tasksForPython,
                done = false
            });
        }

        [HttpPost("submit-batch-results")]
        public async Task<IActionResult> SubmitBatchResults(
            [FromBody] GlobalBatchResultsDto batchResults,
            [FromQuery] string scraperName = "unknown")
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            if (batchResults?.Results == null || !batchResults.Results.Any())
                return BadRequest(new { error = "No results provided" });

            var batchId = batchResults.BatchId ?? "UNKNOWN";
            var results = batchResults.Results;

            if (GoogleGlobalScrapeManager.AssignedBatches.TryGetValue(batchId, out var existingBatch))
            {
                if (existingBatch.IsTimedOut)
                {
                    _logger.LogWarning($"[GLOBAL API] Odrzucono wyniki paczki {batchId} od {scraperName} - paczka wygasła (TIMEOUT).");
                    return Ok(new { success = false, message = "Batch timed out" });
                }
            }

            _logger.LogInformation("Global: Otrzymano wyniki dla paczki {BatchId}: {Count} produktów od {ScraperName}",
                batchId, results.Count, scraperName);

            var taskIds = results.Select(r => r.TaskId).ToList();
            var productsToUpdate = await _context.GoogleScrapingProducts
                .Where(gsp => taskIds.Contains(gsp.ScrapingProductId))
                .ToListAsync();

            var newPriceData = new List<PriceData>();
            int successCount = 0, failedCount = 0, rejectedCount = 0, totalPricesCollected = 0;

            foreach (var result in results)
            {
                var product = productsToUpdate.FirstOrDefault(gsp => gsp.ScrapingProductId == result.TaskId);
                if (product == null) continue;

                if (result.Status == "success" && result.Offers?.Any() == true)
                {
                    product.IsScraped = true;
                    product.OffersCount = result.Offers.Count;
                    successCount++;
                    totalPricesCollected += result.Offers.Count;

                    foreach (var offer in result.Offers)
                    {
                        newPriceData.Add(new PriceData
                        {
                            ScrapingProductId = product.ScrapingProductId,
                            RegionId = product.RegionId,
                            StoreName = offer.StoreName,
                            Price = offer.Price,
                            PriceWithDelivery = offer.PriceWithDelivery,
                            OfferUrl = offer.OfferUrl
                        });
                    }
                }
                else if (result.Status == "rejected" ||
                         (result.Status == "success" && (result.Offers == null || !result.Offers.Any())))
                {
                    product.IsScraped = true;
                    product.OffersCount = 0;
                    rejectedCount++;
                }
                else
                {
                    failedCount++;
                }

                // Broadcast aktualizacji wiersza produktu
                await _hubContext.Clients.All.SendAsync("GlobalUpdateProductRow", new
                {
                    id = product.ScrapingProductId,
                    isScraped = product.IsScraped,
                    offersCount = product.OffersCount
                });
            }

            if (newPriceData.Any())
                await _context.PriceData.AddRangeAsync(newPriceData);

            await _context.SaveChangesAsync();

            GoogleGlobalScrapeManager.CompleteBatch(batchId, successCount, failedCount, rejectedCount, totalPricesCollected);

            if (GoogleGlobalScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            await BroadcastDashboardUpdate();
            await BroadcastLogs();
            await BroadcastStatsUpdate();

            _logger.LogInformation("Global paczka {BatchId} ukończona: {Success} OK, {Rejected} odrz., {Failed} błędy, {Prices} cen",
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

        [HttpPost("report-nuke")]
        public async Task<IActionResult> ReportNuke([FromBody] GlobalNukeReportDto report)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            if (string.IsNullOrWhiteSpace(report.ScraperName))
                return BadRequest(new { error = "scraperName is required" });

            if (report.Status == "started")
                GoogleGlobalScrapeManager.MarkScraperNuking(report.ScraperName, report.Reason);
            else if (report.Status == "completed")
                GoogleGlobalScrapeManager.MarkScraperNukeCompleted(report.ScraperName, report.NewIpAddress);

            if (GoogleGlobalScrapeManager.ActiveScrapers.TryGetValue(report.ScraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            await BroadcastLogs();
            await BroadcastDashboardUpdate();

            return Ok(new { acknowledged = true });
        }

        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromBody] GlobalHeartbeatDto heartbeat)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var scraper = GoogleGlobalScrapeManager.RegisterScraperCheckIn(heartbeat.ScraperName, heartbeat.IpAddress);
            await BroadcastScraperStatus(scraper);

            return Ok(new
            {
                acknowledged = true,
                shouldHibernate = !GoogleGlobalScrapeManager.CanScraperReceiveTask(heartbeat.ScraperName),
                globalStatus = GoogleGlobalScrapeManager.CurrentStatus.ToString()
            });
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var baseQuery = _context.GoogleScrapingProducts
                .Where(gsp => !string.IsNullOrEmpty(gsp.GoogleUrl));

            if (GoogleGlobalScrapeManager.ActiveRegionFilter.HasValue)
                baseQuery = baseQuery.Where(gsp => gsp.RegionId == GoogleGlobalScrapeManager.ActiveRegionFilter.Value);

            var totalTasks = await baseQuery.Where(gsp => gsp.IsScraped == null || gsp.IsScraped == true).CountAsync();
            var completedTasks = await baseQuery.Where(gsp => gsp.IsScraped == true).CountAsync();

            return Ok(new
            {
                isEnabled = GoogleGlobalScrapeManager.CurrentStatus == GlobalScrapingProcessStatus.Running,
                status = GoogleGlobalScrapeManager.CurrentStatus.ToString(),
                startedAt = GoogleGlobalScrapeManager.ScrapingStartTime,
                activeRegionFilter = GoogleGlobalScrapeManager.ActiveRegionFilter,
                totalTasks = totalTasks,
                completedTasks = completedTasks,
                remainingTasks = totalTasks - completedTasks,
                progressPercent = totalTasks > 0 ? Math.Round((double)completedTasks / totalTasks * 100, 1) : 0,
                activeScrapers = GoogleGlobalScrapeManager.GetScrapersDetails(),
                activeBatchesCount = GoogleGlobalScrapeManager.AssignedBatches.Values.Count(b => !b.IsCompleted && !b.IsTimedOut)
            });
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartScraping([FromQuery] int? regionId = null)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            if (GoogleGlobalScrapeManager.CurrentStatus == GlobalScrapingProcessStatus.Running)
                return Ok(new { success = false, message = "Already running" });

            var query = _context.GoogleScrapingProducts
                .Where(gsp => gsp.IsScraped == null && !string.IsNullOrEmpty(gsp.GoogleUrl));

            if (regionId.HasValue)
                query = query.Where(gsp => gsp.RegionId == regionId.Value);

            var totalToScrape = await query.CountAsync();

            if (totalToScrape == 0)
                return Ok(new { success = false, message = "No products to scrape" });

            GoogleGlobalScrapeManager.ResetForNewProcess(regionId);

            await _hubContext.Clients.All.SendAsync("GlobalScrapingStarted", new
            {
                startTime = GoogleGlobalScrapeManager.ScrapingStartTime,
                totalProducts = totalToScrape,
                regionFilter = regionId
            });

            return Ok(new { success = true, message = $"Started. {totalToScrape} products to process." });
        }

        [HttpPost("stop")]
        public async Task<IActionResult> StopScraping()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            GoogleGlobalScrapeManager.CurrentStatus = GlobalScrapingProcessStatus.Idle;
            GoogleGlobalScrapeManager.ScrapingEndTime = DateTime.UtcNow;
            GoogleGlobalScrapeManager.AddSystemLog("WARNING", "Global scraping zatrzymany przez API");

            await _hubContext.Clients.All.SendAsync("GlobalScrapingStopped", new
            {
                endTime = GoogleGlobalScrapeManager.ScrapingEndTime
            });

            return Ok(new { success = true, message = "Stopped" });
        }

        // ===================== HELPER: Extract Catalog ID =====================

        private static string? ExtractCatalogIdStatic(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            var m1 = Regex.Match(url, @"(?:/|-)product/(\d+)", RegexOptions.IgnoreCase);
            if (m1.Success) return m1.Groups[1].Value;

            var m2 = Regex.Match(url, @"[?&]cid=(\d+)", RegexOptions.IgnoreCase);
            if (m2.Success) return m2.Groups[1].Value;

            var m3 = Regex.Match(url, @"cid:(\d+)", RegexOptions.IgnoreCase);
            if (m3.Success) return m3.Groups[1].Value;

            var m4 = Regex.Match(url, @"shopping/(?:product|offers)/(\d+)", RegexOptions.IgnoreCase);
            if (m4.Success) return m4.Groups[1].Value;

            return null;
        }

        // ===================== BROADCAST HELPERS =====================

        private async Task BroadcastScraperStatus(GlobalScraperClient scraper)
        {
            GoogleGlobalScrapeManager.ScraperStatistics.TryGetValue(scraper.Name, out var stats);
            var activeBatch = GoogleGlobalScrapeManager.GetActiveScraperBatch(scraper.Name);

            await _hubContext.Clients.All.SendAsync("GlobalUpdateScraperStatus", new
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
            await _hubContext.Clients.All.SendAsync("GlobalUpdateDashboard",
                GoogleGlobalScrapeManager.GetDashboardSummary());
        }

        private async Task BroadcastLogs()
        {
            await _hubContext.Clients.All.SendAsync("GlobalUpdateLogs",
                GoogleGlobalScrapeManager.GetRecentLogs(20));
        }

        private async Task BroadcastStatsUpdate()
        {
            var baseQuery = _context.GoogleScrapingProducts
                .Where(gsp => !string.IsNullOrEmpty(gsp.GoogleUrl));

            if (GoogleGlobalScrapeManager.ActiveRegionFilter.HasValue)
                baseQuery = baseQuery.Where(gsp => gsp.RegionId == GoogleGlobalScrapeManager.ActiveRegionFilter.Value);

            var stats = await baseQuery
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    total = g.Count(),
                    scraped = g.Count(gsp => gsp.IsScraped == true),
                    rejected = g.Count(gsp => gsp.IsScraped == true && gsp.OffersCount == 0),
                    prices = g.Sum(gsp => gsp.OffersCount)
                })
                .FirstOrDefaultAsync();

            if (stats != null)
            {
                await _hubContext.Clients.All.SendAsync("GlobalUpdateStats", new
                {
                    totalProducts = stats.total,
                    scrapedProducts = stats.scraped,
                    rejectedProducts = stats.rejected,
                    pendingProducts = stats.total - stats.scraped,
                    totalPrices = stats.prices
                });
            }
        }
    }

    // ===================== DTOs =====================

    public class GlobalBatchResultsDto
    {
        [JsonPropertyName("batchId")]
        public string? BatchId { get; set; }

        [JsonPropertyName("results")]
        public List<GlobalTaskResultDto> Results { get; set; } = new();
    }

    public class GlobalTaskResultDto
    {
        [JsonPropertyName("taskId")]
        public int TaskId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("offers")]
        public List<GlobalScrapedOfferDto>? Offers { get; set; }
    }

    public class GlobalScrapedOfferDto
    {
        [JsonPropertyName("storeName")]
        public string StoreName { get; set; } = string.Empty;

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("priceWithDelivery")]
        public decimal PriceWithDelivery { get; set; }

        [JsonPropertyName("offerUrl")]
        public string OfferUrl { get; set; } = string.Empty;

        [JsonPropertyName("isInStock")]
        public bool IsInStock { get; set; } = true;
    }

    public class GlobalHeartbeatDto
    {
        public string ScraperName { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
    }

    public class GlobalNukeReportDto
    {
        public string ScraperName { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string? Status { get; set; }
        public string? NewIpAddress { get; set; }
    }
}