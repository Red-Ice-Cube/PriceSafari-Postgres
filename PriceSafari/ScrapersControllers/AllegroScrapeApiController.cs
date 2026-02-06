

//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using PriceSafari.Models;
//using Microsoft.AspNetCore.SignalR;

//namespace PriceSafari.ScrapersControllers
//{
//    [ApiController]
//    [Route("api/allegro-scrape")]
//    public class AllegroScrapeApiController : ControllerBase
//    {
//        private readonly PriceSafariContext _context;
//        private readonly IHubContext<ScrapingHub> _hubContext;
//        private readonly ILogger<AllegroScrapeApiController> _logger;
//        private const string ApiKey = "2764udhnJUDI8392j83jfi2ijdo1949rncowp89i3rnfiiui1203kfnf9030rfpPkUjHyHt";

//        public AllegroScrapeApiController(
//            PriceSafariContext context,
//            IHubContext<ScrapingHub> hubContext,
//            ILogger<AllegroScrapeApiController> logger)
//        {
//            _context = context;
//            _hubContext = hubContext;
//            _logger = logger;
//        }

//        // ===== ENDPOINT USTAWIEŃ =====
//        [HttpGet("settings")]
//        public async Task<IActionResult> GetSettings()
//        {
//            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

//            var settings = await _context.Settings.FirstOrDefaultAsync();

//            if (settings == null)
//            {
//                return Ok(new
//                {
//                    generatorsCount = 1,
//                    headlessMode = true,
//                    maxWorkers = 1
//                });
//            }

//            return Ok(new
//            {
//                generatorsCount = settings.GeneratorsAllegroCount > 0 ? settings.GeneratorsAllegroCount : 1,
//                headlessMode = settings.HeadLessForAllegroGenerators,
//                maxWorkers = settings.SemophoreAllegroCount > 0 ? settings.SemophoreAllegroCount : 1
//            });
//        }

//        // ===== GŁÓWNY ENDPOINT - POBIERANIE ZADAŃ =====
//        [HttpGet("get-task")]
//        public async Task<IActionResult> GetTaskBatch([FromQuery] string scraperName)
//        {
//            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

//            if (string.IsNullOrWhiteSpace(scraperName))
//            {
//                return BadRequest(new { error = "scraperName is required" });
//            }

//            // 1. Zarejestruj check-in scrapera
//            var scraper = AllegroScrapeManager.RegisterScraperCheckIn(scraperName);

//            // 2. Wyślij aktualizację statusu scrapera na front
//            await BroadcastScraperStatus(scraper);

//            // 3. Sprawdź czy scraper może otrzymać zadanie
//            if (!AllegroScrapeManager.CanScraperReceiveTask(scraperName))
//            {
//                var reason = AllegroScrapeManager.CurrentStatus != ScrapingProcessStatus.Running
//                    ? "Scraping process is paused."
//                    : "Scraper is manually paused (hibernation mode).";

//                return Ok(new { message = reason, shouldHibernate = true });
//            }

//            // 4. Sprawdź czy scraper ma już przydzieloną aktywną paczkę (która nie wygasła)
//            var existingBatch = AllegroScrapeManager.GetActiveScraperBatch(scraperName);
//            if (existingBatch != null)
//            {
//                // Scraper pyta ponownie o zadania, ale ma już paczkę - może restartowal
//                // Zwróć mu tę samą paczkę ponownie
//                _logger.LogWarning("Scraper {ScraperName} już ma aktywną paczkę {BatchId}. Zwracam tę samą.",
//                    scraperName, existingBatch.BatchId);

//                AllegroScrapeManager.AddLog(scraperName, "WARNING",
//                    "Scraper odpytał ponownie - ma już aktywną paczkę. Możliwy restart.");

//                var existingTasks = await _context.AllegroOffersToScrape
//                    .Where(o => existingBatch.TaskIds.Contains(o.Id))
//                    .Select(o => new { taskId = o.Id, url = o.AllegroOfferUrl })
//                    .ToListAsync();

//                return Ok(new
//                {
//                    batchId = existingBatch.BatchId,
//                    tasks = existingTasks,
//                    isResend = true
//                });
//            }

//            // 5. Pobierz nowe zadania z bazy
//            var offersToScrape = await _context.AllegroOffersToScrape
//                .Where(o => !o.IsScraped && !o.IsRejected && !o.IsProcessing)
//                .OrderBy(o => o.Id)
//                .Take(AllegroScrapeManager.BatchSize)
//                .ToListAsync();

//            // 6. Jeśli brak zadań - sprawdź czy proces się zakończył
//            if (!offersToScrape.Any())
//            {
//                var anyTasksStillProcessing = await _context.AllegroOffersToScrape
//                    .AnyAsync(o => o.IsProcessing);

//                var anyActiveBatches = AllegroScrapeManager.HasActiveBatches();

//                if (!anyTasksStillProcessing && !anyActiveBatches &&
//                AllegroScrapeManager.CurrentStatus == ScrapingProcessStatus.Running)
//                {
//                    // Koniec zadań - zatrzymaj proces
//                    AllegroScrapeManager.FinishProcess();

//                    await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", new
//                    {
//                        status = "Idle",
//                        endTime = AllegroScrapeManager.ScrapingEndTime,
//                        message = "Scraping completed"
//                    });

//                    AllegroScrapeManager.AddSystemLog("SUCCESS", "Wszystkie URLe zostały przetworzone. Proces zakończony.");

//                    // NIE wysyłaj done=true - scraper ma czekać na nowe zadania
//                    return Ok(new
//                    {
//                        message = "Scraping process completed. Waiting for new tasks.",
//                        shouldHibernate = true  // <-- Sygnał do hibernacji, nie do zakończenia
//                    });
//                }

//                return Ok(new { message = "No pending tasks available. Waiting for other batches to complete." });
//            }

//            // 7. Oznacz oferty jako przetwarzane w bazie
//            foreach (var offer in offersToScrape)
//            {
//                offer.IsProcessing = true;
//            }
//            await _context.SaveChangesAsync();

//            // 8. Zarejestruj paczkę w managerze
//            var batchId = AllegroScrapeManager.GenerateBatchId();
//            var taskIds = offersToScrape.Select(o => o.Id).ToList();
//            AllegroScrapeManager.RegisterAssignedBatch(batchId, scraperName, taskIds);

//            // 9. Wyślij aktualizacje na front
//            foreach (var offer in offersToScrape)
//            {
//                await _hubContext.Clients.All.SendAsync("UpdateAllegroOfferRow", offer);
//            }
//            await BroadcastScraperStatus(scraper);
//            await BroadcastDashboardUpdate();
//            await BroadcastNewLogEntry(scraperName);

//            // 10. Zwróć zadania do scrapera
//            var tasksForPython = offersToScrape.Select(o => new { taskId = o.Id, url = o.AllegroOfferUrl });

//            _logger.LogInformation("Przydzielono paczkę {BatchId} ({Count} URLi) do scrapera {ScraperName}",
//                batchId, offersToScrape.Count, scraperName);

//            return Ok(new
//            {
//                batchId = batchId,
//                tasks = tasksForPython
//            });
//        }

//        // ===== ENDPOINT - WYSYŁANIE WYNIKÓW =====
//        [HttpPost("submit-batch-results")]
//        public async Task<IActionResult> SubmitBatchResults([FromBody] BatchResultsDto batchResults)
//        {
//            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

//            if (batchResults == null || batchResults.Results == null || !batchResults.Results.Any())
//            {
//                return BadRequest(new { error = "No results provided" });
//            }

//            var batchId = batchResults.BatchId;
//            var results = batchResults.Results;

//            _logger.LogInformation("Otrzymano wyniki dla paczki {BatchId}: {Count} URLi", batchId, results.Count);

//            // Pobierz oferty do aktualizacji
//            var taskIds = results.Select(r => r.TaskId).ToList();
//            var offersToUpdate = await _context.AllegroOffersToScrape
//                .Where(o => taskIds.Contains(o.Id))
//                .ToListAsync();

//            var newScrapedOffers = new List<AllegroScrapedOffer>();
//            int successCount = 0;
//            int failedCount = 0;

//            foreach (var result in results)
//            {
//                var offer = offersToUpdate.FirstOrDefault(o => o.Id == result.TaskId);
//                if (offer == null) continue;

//                offer.IsProcessing = false;

//                if (result.Status == "success")
//                {
//                    var validOffers = result.Offers
//                        .Where(o => o.SellerName != "Brak sprzedawcy" && !string.IsNullOrWhiteSpace(o.SellerName))
//                        .ToList();

//                    offer.IsScraped = true;
//                    offer.CollectedPricesCount = validOffers.Count;
//                    successCount++;

//                    foreach (var scraped in validOffers)
//                    {
//                        newScrapedOffers.Add(new AllegroScrapedOffer
//                        {
//                            AllegroOfferToScrapeId = offer.Id,
//                            SellerName = scraped.SellerName,
//                            Price = scraped.Price,
//                            DeliveryCost = scraped.DeliveryCost,
//                            DeliveryTime = scraped.DeliveryTime,
//                            Popularity = scraped.Popularity,
//                            SuperSeller = scraped.SuperSeller,
//                            Smart = scraped.Smart,
//                            IsBestPriceGuarantee = scraped.IsBestPriceGuarantee,
//                            TopOffer = scraped.TopOffer,
//                            SuperPrice = scraped.SuperPrice,
//                            Promoted = scraped.Promoted,
//                            Sponsored = scraped.Sponsored,
//                            IdAllegro = scraped.IdAllegro,
//                        });
//                    }
//                }
//                else
//                {
//                    offer.IsRejected = true;
//                    failedCount++;
//                }

//                // Wyślij aktualizację wiersza na front
//                await _hubContext.Clients.All.SendAsync("UpdateAllegroOfferRow", offer);
//            }

//            // Zapisz do bazy
//            if (newScrapedOffers.Any())
//            {
//                await _context.AllegroScrapedOffers.AddRangeAsync(newScrapedOffers);
//            }
//            await _context.SaveChangesAsync();

//            // Oznacz paczkę jako ukończoną
//            AllegroScrapeManager.CompleteBatch(batchId, successCount, failedCount);

//            // Znajdź scrapera który wysłał wyniki
//            var batch = AllegroScrapeManager.AssignedBatches.GetValueOrDefault(batchId);
//            var scraperName = batch?.ScraperName ?? "Unknown";

//            // Aktualizuj status scrapera
//            if (AllegroScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
//            {
//                await BroadcastScraperStatus(scraper);
//            }

//            // Wyślij aktualizacje na front
//            await BroadcastDashboardUpdate();
//            await BroadcastNewLogEntry(scraperName);
//            await BroadcastStatsUpdate();

//            _logger.LogInformation("Paczka {BatchId} ukończona. Sukces: {Success}, Błędy: {Failed}",
//                batchId, successCount, failedCount);

//            return Ok(new
//            {
//                message = "Batch processed successfully.",
//                batchId = batchId,
//                successCount = successCount,
//                failedCount = failedCount
//            });
//        }

//        // ===== ENDPOINT - STATUS SCRAPERA (dla kontroli indywidualnej) =====
//        [HttpGet("scraper-status/{scraperName}")]
//        public IActionResult GetScraperStatus(string scraperName)
//        {
//            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

//            var isPaused = false;
//            if (AllegroScrapeManager.ScraperStatistics.TryGetValue(scraperName, out var stats))
//            {
//                isPaused = stats.IsManuallyPaused;
//            }

//            return Ok(new
//            {
//                scraperName = scraperName,
//                isManuallyPaused = isPaused,
//                globalStatus = AllegroScrapeManager.CurrentStatus.ToString(),
//                canReceiveTask = AllegroScrapeManager.CanScraperReceiveTask(scraperName)
//            });
//        }

//        // ===== ENDPOINT - HEARTBEAT (lekki check-in bez pobierania zadań) =====
//        [HttpPost("heartbeat")]
//        public async Task<IActionResult> Heartbeat([FromBody] HeartbeatDto heartbeat)
//        {
//            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

//            var scraper = AllegroScrapeManager.RegisterScraperCheckIn(heartbeat.ScraperName);
//            await BroadcastScraperStatus(scraper);

//            return Ok(new
//            {
//                acknowledged = true,
//                shouldHibernate = !AllegroScrapeManager.CanScraperReceiveTask(heartbeat.ScraperName),
//                globalStatus = AllegroScrapeManager.CurrentStatus.ToString()
//            });
//        }

//        // ===== METODY POMOCNICZE - BROADCAST =====

//        private async Task BroadcastScraperStatus(HybridScraperClient scraper)
//        {
//            // Pobierz dodatkowe dane o scraperze
//            AllegroScrapeManager.ScraperStatistics.TryGetValue(scraper.Name, out var stats);
//            var activeBatch = AllegroScrapeManager.GetActiveScraperBatch(scraper.Name);

//            await _hubContext.Clients.All.SendAsync("UpdateDetailScraperStatus", new
//            {
//                name = scraper.Name,
//                status = scraper.Status.ToString(),
//                statusCode = (int)scraper.Status,
//                lastCheckIn = scraper.LastCheckIn,
//                currentBatchId = scraper.CurrentBatchId,
//                currentTaskId = scraper.CurrentTaskId,
//                isManuallyPaused = stats?.IsManuallyPaused ?? false,

//                // Statystyki
//                totalUrlsProcessed = stats?.TotalUrlsProcessed ?? 0,
//                totalUrlsSuccess = stats?.TotalUrlsSuccess ?? 0,
//                totalUrlsFailed = stats?.TotalUrlsFailed ?? 0,
//                successRate = stats?.SuccessRate ?? 0,
//                urlsPerMinute = stats?.UrlsPerMinute ?? 0,
//                batchesCompleted = stats?.TotalBatchesCompleted ?? 0,
//                batchesTimedOut = stats?.TotalBatchesTimedOut ?? 0,
//                currentBatchNumber = stats?.CurrentBatchNumber ?? 0,

//                // Aktywna paczka
//                activeBatchTaskCount = activeBatch?.TaskIds.Count ?? 0,
//                activeBatchAssignedAt = activeBatch?.AssignedAt
//            });
//        }

//        private async Task BroadcastDashboardUpdate()
//        {
//            await _hubContext.Clients.All.SendAsync("UpdateDashboard",
//                AllegroScrapeManager.GetDashboardSummary());
//        }

//        private async Task BroadcastNewLogEntry(string scraperName)
//        {
//            var recentLogs = AllegroScrapeManager.GetRecentLogs(10);
//            await _hubContext.Clients.All.SendAsync("UpdateLogs", recentLogs);
//        }

//        private async Task BroadcastStatsUpdate()
//        {
//            // Pobierz aktualne statystyki z bazy
//            var stats = await _context.AllegroOffersToScrape
//                .GroupBy(_ => 1)
//                .Select(g => new
//                {
//                    total = g.Count(),
//                    scraped = g.Count(o => o.IsScraped),
//                    rejected = g.Count(o => o.IsRejected),
//                    processing = g.Count(o => o.IsProcessing),
//                    prices = g.Sum(o => o.CollectedPricesCount)
//                })
//                .FirstOrDefaultAsync();

//            if (stats != null)
//            {
//                await _hubContext.Clients.All.SendAsync("UpdateStats", new
//                {
//                    totalUrls = stats.total,
//                    scrapedUrls = stats.scraped,
//                    rejectedUrls = stats.rejected,
//                    processingUrls = stats.processing,
//                    totalPricesCollected = stats.prices
//                });
//            }
//        }
//    }

//    // ===== DTOs =====

//    public class BatchResultsDto
//    {
//        public string BatchId { get; set; } = string.Empty;
//        public List<UrlResultDto> Results { get; set; } = new();
//    }

//    public class UrlResultDto
//    {
//        public int TaskId { get; set; }
//        public string Status { get; set; } = string.Empty;
//        public List<ScrapedOfferDto> Offers { get; set; } = new();
//    }

//    public class ScrapedOfferDto
//    {
//        public string SellerName { get; set; } = string.Empty;
//        public decimal Price { get; set; }
//        public decimal? DeliveryCost { get; set; }
//        public int? DeliveryTime { get; set; }
//        public int? Popularity { get; set; }
//        public bool SuperSeller { get; set; }
//        public bool Smart { get; set; }
//        public bool IsBestPriceGuarantee { get; set; }
//        public bool TopOffer { get; set; }
//        public bool SuperPrice { get; set; }
//        public bool Promoted { get; set; }
//        public bool Sponsored { get; set; }
//        public long IdAllegro { get; set; }
//    }

//    public class HeartbeatDto
//    {
//        public string ScraperName { get; set; } = string.Empty;
//        public string? CurrentStatus { get; set; }
//    }
//}





using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json.Serialization;

namespace PriceSafari.ScrapersControllers
{
    [ApiController]
    [Route("api/allegro-scrape")]
    public class AllegroScrapeApiController : ControllerBase
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<AllegroScrapeApiController> _logger;
        private const string ApiKey = "2764udhnJUDI8392j83jfi2ijdo1949rncowp89i3rnfiiui1203kfnf9030rfpPkUjHyHt";

        public AllegroScrapeApiController(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            ILogger<AllegroScrapeApiController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        // ===== ENDPOINT USTAWIEŃ =====
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var settings = await _context.Settings.FirstOrDefaultAsync();

            if (settings == null)
            {
                return Ok(new
                {
                    generatorsCount = 1,
                    headlessMode = true,
                    maxWorkers = 1,
                    headStartDuration = 50,
                    batchSize = AllegroScrapeManager.BatchSize
                });
            }

            return Ok(new
            {
                generatorsCount = settings.GeneratorsAllegroCount > 0 ? settings.GeneratorsAllegroCount : 1,
                headlessMode = settings.HeadLessForAllegroGenerators,
                maxWorkers = settings.SemophoreAllegroCount > 0 ? settings.SemophoreAllegroCount : 1,
                headStartDuration = 50,
                batchSize = AllegroScrapeManager.BatchSize
            });
        }

        // ===== GŁÓWNY ENDPOINT - POBIERANIE ZADAŃ =====
        [HttpGet("get-task")]
        public async Task<IActionResult> GetTaskBatch(
            [FromQuery] string scraperName,
            [FromQuery] string? currentStatus = null,
            [FromQuery] string? ipAddress = null)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            if (string.IsNullOrWhiteSpace(scraperName))
                return BadRequest(new { error = "scraperName is required" });

            // 1. Zarejestruj check-in
            var scraper = AllegroScrapeManager.RegisterScraperCheckIn(scraperName, ipAddress);

            // 2. Synchronizacja NUKE
            if (currentStatus == "NUKE_IN_PROGRESS" && scraper.Status != ScraperLiveStatus.ResettingNetwork)
            {
                AllegroScrapeManager.MarkScraperNuking(scraperName, "Zgłoszony przez Python (reconnect)");
            }
            else if (currentStatus == "NUKE_COMPLETED" && scraper.Status == ScraperLiveStatus.ResettingNetwork)
            {
                AllegroScrapeManager.MarkScraperNukeCompleted(scraperName, ipAddress);
            }

            // 3. Broadcast statusu
            await BroadcastScraperStatus(scraper);

            // 4. Sprawdzenie czy może dostać zadanie
            if (!AllegroScrapeManager.CanScraperReceiveTask(scraperName))
            {
                var reason = AllegroScrapeManager.CurrentStatus != ScrapingProcessStatus.Running
                    ? "Scraping process is paused."
                    : scraper.Status == ScraperLiveStatus.ResettingNetwork
                        ? "Scraper is in NUKE protocol."
                        : "Scraper is manually paused (hibernation mode).";

                return Ok(new { message = reason, shouldHibernate = true });
            }

            // 5. Sprawdzenie aktywnej paczki
            var existingBatch = AllegroScrapeManager.GetActiveScraperBatch(scraperName);
            if (existingBatch != null)
            {
                _logger.LogWarning("Scraper {ScraperName} ma aktywną paczkę {BatchId}. Zwracam tę samą.", scraperName, existingBatch.BatchId);
                AllegroScrapeManager.AddLog(scraperName, "WARNING", "Scraper odpytał ponownie - ma już aktywną paczkę. Możliwy restart.");

                var existingTasks = await _context.AllegroOffersToScrape
                    .Where(o => existingBatch.TaskIds.Contains(o.Id))
                    .Select(o => new { taskId = o.Id, url = o.AllegroOfferUrl })
                    .ToListAsync();

                return Ok(new { batchId = existingBatch.BatchId, tasks = existingTasks, isResend = true });
            }

            // 6. Pobieranie nowych zadań (z LOCKIEM globalnym)
            List<AllegroOfferToScrape> offersToScrape;
            string batchId;
            List<int> taskIds;
            var timedOutBatches = AllegroScrapeManager.FindAndMarkTimedOutBatches();
            if (timedOutBatches.Any())
            {
                var allTimedOutTaskIds = timedOutBatches.SelectMany(b => b.TaskIds).ToList();
                var stuckOffers = await _context.AllegroOffersToScrape
                    .Where(o => allTimedOutTaskIds.Contains(o.Id) && o.IsProcessing && !o.IsScraped)
                    .ToListAsync();

                foreach (var offer in stuckOffers)
                    offer.IsProcessing = false;

                if (stuckOffers.Any())
                    await _context.SaveChangesAsync();
            }
            // 6. Pobieranie nowych zadań (z LOCKIEM globalnym)
            lock (AllegroScrapeManager.BatchAssignmentLock)
            {
                var assignedTaskIds = AllegroScrapeManager.GetAllActiveTaskIds();

                offersToScrape = _context.AllegroOffersToScrape
                    .Where(o => !o.IsScraped
                             && !o.IsRejected
                             // && !o.IsProcessing  <--- USUŃ TĘ LINIJKĘ!
                             && !assignedTaskIds.Contains(o.Id)) // To wystarczy do blokowania duplikatów
                    .OrderBy(o => o.Id)
                    .Take(AllegroScrapeManager.BatchSize)
                    .ToList();

                if (!offersToScrape.Any())
                {
                    batchId = null!;
                    taskIds = null!;
                }
                else
                {
                    batchId = AllegroScrapeManager.GenerateBatchId();
                    taskIds = offersToScrape.Select(o => o.Id).ToList();
                    AllegroScrapeManager.RegisterAssignedBatch(batchId, scraperName, taskIds);
                }
            }

            // 7. Obsługa braku zadań
            if (!offersToScrape.Any())
            {
                var anyActive = AllegroScrapeManager.HasActiveBatches();
                if (!anyActive && AllegroScrapeManager.CurrentStatus == ScrapingProcessStatus.Running)
                {
                    var anyPending = await _context.AllegroOffersToScrape.AnyAsync(o => !o.IsScraped && !o.IsRejected);
                    if (!anyPending)
                    {
                        AllegroScrapeManager.FinishProcess();
                        await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", new
                        {
                            status = "Idle",
                            endTime = AllegroScrapeManager.ScrapingEndTime,
                            message = "Scraping completed"
                        });
                        AllegroScrapeManager.AddSystemLog("SUCCESS", "Wszystkie URLe zostały przetworzone. Proces zakończony.");
                        return Ok(new { message = "Scraping process completed.", shouldHibernate = true });
                    }
                }
                return Ok(new { message = "No pending tasks available." });
            }

            // 8. Zapis do bazy (IsProcessing)
            foreach (var offer in offersToScrape) offer.IsProcessing = true;
            await _context.SaveChangesAsync();

            // 9. Aktualizacje frontu
            foreach (var offer in offersToScrape) await _hubContext.Clients.All.SendAsync("UpdateAllegroOfferRow", offer);
            await BroadcastScraperStatus(scraper);
            await BroadcastDashboardUpdate();
            await BroadcastLogs();

            // 10. Wynik
            var tasksForPython = offersToScrape.Select(o => new { taskId = o.Id, url = o.AllegroOfferUrl });
            _logger.LogInformation("Przydzielono paczkę {BatchId} ({Count} URLi) do {ScraperName}", batchId, offersToScrape.Count, scraperName);

            return Ok(new { batchId = batchId, tasks = tasksForPython });
        }

        // ===== ENDPOINT - WYSYŁANIE WYNIKÓW =====
        [HttpPost("submit-batch-results")]
        public async Task<IActionResult> SubmitBatchResults([FromBody] BatchResultsDto batchResults)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            if (batchResults?.Results == null || !batchResults.Results.Any())
                return BadRequest(new { error = "No results provided" });

            var batchId = batchResults.BatchId ?? "UNKNOWN";
            var results = batchResults.Results;

            // Sprawdzenie Timeout
            if (AllegroScrapeManager.AssignedBatches.TryGetValue(batchId, out var existingBatch))
            {
                if (existingBatch.IsTimedOut)
                {
                    _logger.LogWarning($"[API] Odrzucono wyniki paczki {batchId} - paczka wygasła (TIMEOUT).");
                    return Ok(new { success = false, message = "Batch timed out" });
                }
            }

            _logger.LogInformation("Otrzymano wyniki dla paczki {BatchId}: {Count} URLi", batchId, results.Count);

            var taskIds = results.Select(r => r.TaskId).ToList();
            var offersToUpdate = await _context.AllegroOffersToScrape
                .Where(o => taskIds.Contains(o.Id))
                .ToListAsync();

            var newScrapedOffers = new List<AllegroScrapedOffer>();
            int successCount = 0, failedCount = 0;

            foreach (var result in results)
            {
                var offer = offersToUpdate.FirstOrDefault(o => o.Id == result.TaskId);
                if (offer == null) continue;

                offer.IsProcessing = false;

                if (result.Status == "success" && result.Offers?.Any() == true)
                {
                    var validOffers = result.Offers.Where(o => !string.IsNullOrWhiteSpace(o.SellerName) && o.SellerName != "Brak sprzedawcy").ToList();

                    offer.IsScraped = true;
                    offer.IsRejected = false;
                    offer.CollectedPricesCount = validOffers.Count;
                    successCount++;

                    foreach (var scraped in validOffers)
                    {
                        newScrapedOffers.Add(new AllegroScrapedOffer
                        {
                            AllegroOfferToScrapeId = offer.Id,
                            SellerName = scraped.SellerName,
                            Price = scraped.Price,
                            DeliveryCost = scraped.DeliveryCost,
                            DeliveryTime = scraped.DeliveryTime,
                            Popularity = scraped.Popularity,
                            SuperSeller = scraped.SuperSeller,
                            Smart = scraped.Smart,
                            IsBestPriceGuarantee = scraped.IsBestPriceGuarantee,
                            TopOffer = scraped.TopOffer,
                            SuperPrice = scraped.SuperPrice,
                            Promoted = scraped.Promoted,
                            Sponsored = scraped.Sponsored,
                            IdAllegro = scraped.IdAllegro,
                        });
                    }
                }
                else if (result.Status == "rejected" || (result.Status == "success" && (result.Offers == null || !result.Offers.Any())))
                {
                    offer.IsScraped = true;
                    offer.IsRejected = true;
                    offer.CollectedPricesCount = 0;
                    failedCount++; // Traktujemy jako failed w statystykach paczki, ale jako scraped w bazie
                }
                else
                {
                    // Python zdecydował że URL jest zły — oznaczamy jako odrzucony
                    offer.IsScraped = true;
                    offer.IsRejected = true;
                    offer.IsProcessing = false;
                    offer.CollectedPricesCount = 0;
                    failedCount++;
                }

                await _hubContext.Clients.All.SendAsync("UpdateAllegroOfferRow", offer);
            }

            if (newScrapedOffers.Any()) await _context.AllegroScrapedOffers.AddRangeAsync(newScrapedOffers);
            await _context.SaveChangesAsync();

            AllegroScrapeManager.CompleteBatch(batchId, successCount, failedCount);

            var scraperName = existingBatch?.ScraperName ?? "Unknown";
            if (AllegroScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            await BroadcastDashboardUpdate();
            await BroadcastLogs();
            await BroadcastStatsUpdate();

            return Ok(new { success = true, batchId = batchId, successCount = successCount, failedCount = failedCount });
        }

        // ===== RAPORTOWANIE NUKE =====
        [HttpPost("report-nuke")]
        public async Task<IActionResult> ReportNuke([FromBody] NukeReportDto report)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();
            if (string.IsNullOrWhiteSpace(report.ScraperName)) return BadRequest(new { error = "scraperName is required" });

            if (report.Status == "started")
                AllegroScrapeManager.MarkScraperNuking(report.ScraperName, report.Reason);
            else if (report.Status == "completed")
                AllegroScrapeManager.MarkScraperNukeCompleted(report.ScraperName, report.NewIpAddress);
            else if (report.Status == "failed")
                AllegroScrapeManager.AddLog(report.ScraperName, "ERROR", $"NUKE FAILED: {report.Reason}");

            if (AllegroScrapeManager.ActiveScrapers.TryGetValue(report.ScraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            await BroadcastLogs();
            await BroadcastDashboardUpdate();

            return Ok(new { acknowledged = true });
        }

        // ===== HEARTBEAT =====
        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromBody] HeartbeatDto heartbeat)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();
            var scraper = AllegroScrapeManager.RegisterScraperCheckIn(heartbeat.ScraperName);
            await BroadcastScraperStatus(scraper);
            return Ok(new { acknowledged = true, shouldHibernate = !AllegroScrapeManager.CanScraperReceiveTask(heartbeat.ScraperName) });
        }

        // ===== START/STOP =====
        [HttpPost("start")]
        public async Task<IActionResult> StartScraping()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();
            if (AllegroScrapeManager.CurrentStatus == ScrapingProcessStatus.Running) return Ok(new { success = false, message = "Already running" });

            var totalToScrape = await _context.AllegroOffersToScrape.CountAsync(o => !o.IsScraped);
            if (totalToScrape == 0) return Ok(new { success = false, message = "No URLs to scrape" });

            AllegroScrapeManager.ResetForNewProcess();
            await _hubContext.Clients.All.SendAsync("GoogleScrapingStarted", new { startTime = AllegroScrapeManager.ScrapingStartTime, totalUrls = totalToScrape });
            return Ok(new { success = true, message = $"Started. {totalToScrape} URLs to process." });
        }

        [HttpPost("stop")]
        public async Task<IActionResult> StopScraping()
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();
            AllegroScrapeManager.CurrentStatus = ScrapingProcessStatus.Idle;
            AllegroScrapeManager.ScrapingEndTime = DateTime.UtcNow;
            AllegroScrapeManager.AddSystemLog("WARNING", "Scraping zatrzymany przez API");
            await _hubContext.Clients.All.SendAsync("GoogleScrapingStopped", new { endTime = AllegroScrapeManager.ScrapingEndTime });
            return Ok(new { success = true, message = "Stopped" });
        }

        // ===== HELPERS =====
        private async Task BroadcastScraperStatus(HybridScraperClient scraper)
        {
            AllegroScrapeManager.ScraperStatistics.TryGetValue(scraper.Name, out var stats);
            var activeBatch = AllegroScrapeManager.GetActiveScraperBatch(scraper.Name);

            await _hubContext.Clients.All.SendAsync("UpdateDetailScraperStatus", new
            {
                name = scraper.Name,
                status = scraper.Status.ToString(),
                statusCode = (int)scraper.Status,
                lastCheckIn = scraper.LastCheckIn,
                currentBatchId = scraper.CurrentBatchId,
                currentTaskId = scraper.CurrentTaskId,
                currentIpAddress = scraper.CurrentIpAddress,
                nukeCount = scraper.NukeCount,
                isManuallyPaused = stats?.IsManuallyPaused ?? false,

                totalUrlsProcessed = stats?.TotalUrlsProcessed ?? 0,
                totalUrlsSuccess = stats?.TotalUrlsSuccess ?? 0,
                totalUrlsFailed = stats?.TotalUrlsFailed ?? 0,
                successRate = stats?.SuccessRate ?? 0,
                urlsPerMinute = stats?.UrlsPerMinute ?? 0,
                batchesCompleted = stats?.TotalBatchesCompleted ?? 0,
                batchesTimedOut = stats?.TotalBatchesTimedOut ?? 0,
                currentBatchNumber = stats?.CurrentBatchNumber ?? 0,

                activeBatchTaskCount = activeBatch?.TaskIds.Count ?? 0,
                activeBatchAssignedAt = activeBatch?.AssignedAt
            });
        }

        private async Task BroadcastDashboardUpdate()
        {
            await _hubContext.Clients.All.SendAsync("UpdateDashboard", AllegroScrapeManager.GetDashboardSummary());
        }

        private async Task BroadcastLogs()
        {
            var recentLogs = AllegroScrapeManager.GetRecentLogs(10);
            await _hubContext.Clients.All.SendAsync("UpdateLogs", recentLogs);
        }

        private async Task BroadcastStatsUpdate()
        {
            var stats = await _context.AllegroOffersToScrape.GroupBy(_ => 1).Select(g => new
            {
                total = g.Count(),
                scraped = g.Count(o => o.IsScraped),
                rejected = g.Count(o => o.IsRejected),
                processing = g.Count(o => o.IsProcessing),
                prices = g.Sum(o => o.CollectedPricesCount)
            }).FirstOrDefaultAsync();

            if (stats != null)
            {
                await _hubContext.Clients.All.SendAsync("UpdateStats", new
                {
                    totalUrls = stats.total,
                    scrapedUrls = stats.scraped,
                    rejectedUrls = stats.rejected,
                    processingUrls = stats.processing,
                    totalPricesCollected = stats.prices
                });
            }
        }
    }

    // ===== DTOs (Data Transfer Objects) =====

    public class BatchResultsDto
    {
        [JsonPropertyName("batchId")]
        public string? BatchId { get; set; }

        [JsonPropertyName("results")]
        public List<UrlResultDto> Results { get; set; } = new();
    }

    public class UrlResultDto
    {
        [JsonPropertyName("taskId")]
        public int TaskId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("offers")]
        public List<ScrapedOfferDto>? Offers { get; set; }
    }

    public class ScrapedOfferDto
    {
        [JsonPropertyName("sellerName")]
        public string SellerName { get; set; } = string.Empty;

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("deliveryCost")]
        public decimal? DeliveryCost { get; set; }

        [JsonPropertyName("deliveryTime")]
        public int? DeliveryTime { get; set; }

        [JsonPropertyName("popularity")]
        public int? Popularity { get; set; }

        [JsonPropertyName("superSeller")]
        public bool SuperSeller { get; set; }

        [JsonPropertyName("smart")]
        public bool Smart { get; set; }

        [JsonPropertyName("isBestPriceGuarantee")]
        public bool IsBestPriceGuarantee { get; set; }

        [JsonPropertyName("topOffer")]
        public bool TopOffer { get; set; }

        [JsonPropertyName("superPrice")]
        public bool SuperPrice { get; set; }

        [JsonPropertyName("promoted")]
        public bool Promoted { get; set; }

        [JsonPropertyName("sponsored")]
        public bool Sponsored { get; set; }

        [JsonPropertyName("idAllegro")]
        public long IdAllegro { get; set; }
    }

    public class HeartbeatDto
    {
        public string ScraperName { get; set; } = string.Empty;
        public string? IpAddress { get; set; } // Opcjonalne w DTO
    }

    public class NukeReportDto
    {
        public string ScraperName { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string? Status { get; set; }
        public string? NewIpAddress { get; set; }
    }
}
