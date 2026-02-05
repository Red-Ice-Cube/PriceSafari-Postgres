using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.Formula.Functions;
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

            // 6. SEKCJA KRYTYCZNA - przydzielanie paczki z LOCKIEM
            List<CoOfrClass> offersToScrape;
            string batchId;
            List<int> taskIds;

            lock (GoogleScrapeManager.BatchAssignmentLock)
            {
                // 6a. Pobierz ID wszystkich URL które są już przydzielone w aktywnych paczkach
                var assignedTaskIds = GoogleScrapeManager.GetAllActiveTaskIds();

                _logger.LogDebug("Aktywne paczki zawierają {Count} URLi", assignedTaskIds.Count);

                // 6b. Pobierz nowe zadania z bazy WYKLUCZAJĄC już przydzielone
                offersToScrape = _context.CoOfrs
                    .Where(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
                                && !c.GoogleIsScraped
                                && !assignedTaskIds.Contains(c.Id))  // ✅ KLUCZOWA ZMIANA!
                    .OrderBy(c => c.Id)
                    .Take(GoogleScrapeManager.BatchSize)
                    .ToList();  // Synchronicznie w ramach locka!

                // 7. Brak zadań
                if (!offersToScrape.Any())
                {
                    // Musimy zwolnić lock przed async operacjami, więc sprawdzamy stan
                    var anyActiveBatches = GoogleScrapeManager.HasActiveBatches();
                    var needsFinishCheck = !anyActiveBatches &&
                                           GoogleScrapeManager.CurrentStatus == GoogleScrapingProcessStatus.Running;

                    // Zwolnij lock - nie mamy co przydzielać
                    // (lock kończy się automatycznie na końcu bloku)

                    if (needsFinishCheck)
                    {
                        // Ten kod wykonuje się PO ZWOLNIENIU LOCKA (bo jesteśmy w if wewnątrz lock)
                        // Ale to OK - sprawdzamy tylko czy kończyć proces
                    }

                    // Zwracamy odpowiedź - kontynuacja poniżej
                    batchId = null!;
                    taskIds = null!;
                }
                else
                {
                    // 8. Zarejestruj paczkę NATYCHMIAST w ramach locka
                    batchId = GoogleScrapeManager.GenerateBatchId();
                    taskIds = offersToScrape.Select(c => c.Id).ToList();
                    GoogleScrapeManager.RegisterAssignedBatch(batchId, scraperName, taskIds);
                }
            }

            // 7b. Obsługa braku zadań (poza lockiem - możemy robić async)
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

            // SPRAWDZENIE CZY PACZKA NIE WYGASŁA
            if (GoogleScrapeManager.AssignedBatches.TryGetValue(batchId, out var existingBatch))
            {
                if (existingBatch.IsTimedOut)
                {
                    _logger.LogWarning($"[API] Odrzucono wyniki paczki {batchId} od {scraperName} - paczka wygasła (TIMEOUT).");
                    return Ok(new { success = false, message = "Batch timed out" });
                }
            }

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
                else if (result.Status == "rejected" ||
                (result.Status == "success" && (result.Offers == null || !result.Offers.Any())))
                {
                    // "rejected" LUB "success" bez ofert → traktuj jak rejected
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