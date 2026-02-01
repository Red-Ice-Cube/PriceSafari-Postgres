//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using PriceSafari.ScrapersControllers;

//namespace PriceSafari.Services.AllegroServices
//{
//    public class AllegroScrapingService
//    {
//        private readonly PriceSafariContext _context;
//        private readonly IHubContext<ScrapingHub> _hubContext;
//        private readonly ILogger<AllegroScrapingService> _logger;

//        public AllegroScrapingService(
//            PriceSafariContext context,
//            IHubContext<ScrapingHub> hubContext,
//            ILogger<AllegroScrapingService> logger)
//        {
//            _context = context;
//            _hubContext = hubContext;
//            _logger = logger;
//        }
//        public async Task<(bool success, string message, int totalUrls)> StartScrapingProcessAsync()
//        {
//            _logger.LogInformation("Próba uruchomienia procesu scrapowania Allegro...");

//            var anyActiveScrapers = AllegroScrapeManager.ActiveScrapers.Values.Any(s => s.Status != ScraperLiveStatus.Offline);
//            if (!anyActiveScrapers)
//            {
//                const string errorMsg = "Nie można uruchomić procesu. Żaden scraper nie jest aktywny (online).";
//                _logger.LogWarning(errorMsg);
//                return (false, errorMsg, 0);
//            }

//            var urlsToScrape = await _context.AllegroOffersToScrape
//                .Where(o => !o.IsScraped && !o.IsRejected)
//                .ToListAsync();

//            int totalUrls = urlsToScrape.Count;

//            if (totalUrls == 0)
//            {
//                const string infoMsg = "Brak oczekujących URL-i do scrapowania. Proces nie został uruchomiony.";
//                _logger.LogInformation(infoMsg);
//                return (true, infoMsg, 0);
//            }

//            var orphanedTasks = await _context.AllegroOffersToScrape
//                .Where(o => o.IsProcessing)
//                .ToListAsync();

//            if (orphanedTasks.Any())
//            {
//                foreach (var task in orphanedTasks)
//                {
//                    task.IsProcessing = false;
//                }
//                await _context.SaveChangesAsync();
//                _logger.LogInformation("Zresetowano stan dla {Count} zawieszonych zadań.", orphanedTasks.Count);
//            }

//            AllegroScrapeManager.CurrentStatus = ScrapingProcessStatus.Running;
//            AllegroScrapeManager.ScrapingStartTime = DateTime.UtcNow;

//            await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", new
//            {
//                status = "Running",
//                startTime = AllegroScrapeManager.ScrapingStartTime,
//                endTime = (DateTime?)null
//            });

//            const string successMsg = "Proces scrapowania ofert Allegro został pomyślnie uruchomiony.";
//            _logger.LogInformation(successMsg);

//            return (true, successMsg, totalUrls);
//        }
//    }
//}


using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.ScrapersControllers;

namespace PriceSafari.Services.AllegroServices
{
    /// <summary>
    /// Serwis zarządzający procesem scrapowania Allegro
    /// </summary>
    public class AllegroScrapingService
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<AllegroScrapingService> _logger;

        public AllegroScrapingService(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            ILogger<AllegroScrapingService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// Uruchamia proces scrapowania
        /// </summary>
        public async Task<(bool success, string message, int totalUrls)> StartScrapingProcessAsync()
        {
            _logger.LogInformation("Próba uruchomienia procesu scrapowania Allegro...");

            // Sprawdź czy są aktywne scrapery
            var anyActiveScrapers = AllegroScrapeManager.ActiveScrapers.Values
                .Any(s => s.Status != ScraperLiveStatus.Offline && s.Status != ScraperLiveStatus.Stopped);

            if (!anyActiveScrapers)
            {
                const string errorMsg = "Nie można uruchomić procesu. Żaden scraper nie jest aktywny (online).";
                _logger.LogWarning(errorMsg);
                return (false, errorMsg, 0);
            }

            // Sprawdź ile URLi do przetworzenia
            var urlsToScrape = await _context.AllegroOffersToScrape
                .Where(o => !o.IsScraped && !o.IsRejected)
                .ToListAsync();

            int totalUrls = urlsToScrape.Count;

            if (totalUrls == 0)
            {
                const string infoMsg = "Brak oczekujących URL-i do scrapowania. Proces nie został uruchomiony.";
                _logger.LogInformation(infoMsg);
                return (true, infoMsg, 0);
            }

            // Zresetuj zawieszone zadania (IsProcessing = true ale nie przypisane do żadnej aktywnej paczki)
            var orphanedTasks = await _context.AllegroOffersToScrape
                .Where(o => o.IsProcessing)
                .ToListAsync();

            if (orphanedTasks.Any())
            {
                foreach (var task in orphanedTasks)
                {
                    task.IsProcessing = false;
                }
                await _context.SaveChangesAsync();

                _logger.LogInformation("Zresetowano stan dla {Count} zawieszonych zadań.", orphanedTasks.Count);
                AllegroScrapeManager.AddSystemLog("INFO", $"Zresetowano {orphanedTasks.Count} zawieszonych zadań");
            }

            // Uruchom proces w managerze
            AllegroScrapeManager.ResetForNewProcess();

            // Powiadom front
            await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", new
            {
                status = "Running",
                startTime = AllegroScrapeManager.ScrapingStartTime,
                endTime = (DateTime?)null,
                message = "Scraping started"
            });

            await _hubContext.Clients.All.SendAsync("UpdateDashboard", AllegroScrapeManager.GetDashboardSummary());

            const string successMsg = "Proces scrapowania ofert Allegro został pomyślnie uruchomiony.";
            _logger.LogInformation("{Message} URLi do przetworzenia: {Count}", successMsg, totalUrls);

            return (true, successMsg, totalUrls);
        }

        /// <summary>
        /// Zatrzymuje proces scrapowania
        /// </summary>
        public async Task<(bool success, string message)> StopScrapingProcessAsync()
        {
            _logger.LogInformation("Zatrzymywanie procesu scrapowania Allegro...");

            AllegroScrapeManager.CurrentStatus = ScrapingProcessStatus.Idle;
            AllegroScrapeManager.ScrapingEndTime = DateTime.UtcNow;
            AllegroScrapeManager.AddSystemLog("WARNING", "Proces scrapowania zatrzymany ręcznie");

            // Powiadom front
            await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", new
            {
                status = "Idle",
                startTime = AllegroScrapeManager.ScrapingStartTime,
                endTime = AllegroScrapeManager.ScrapingEndTime,
                message = "Scraping stopped manually"
            });

            await _hubContext.Clients.All.SendAsync("UpdateDashboard", AllegroScrapeManager.GetDashboardSummary());

            return (true, "Proces scrapowania został zatrzymany.");
        }

        /// <summary>
        /// Zatrzymuje indywidualnego scrapera
        /// </summary>
        public async Task<(bool success, string message)> PauseScraperAsync(string scraperName)
        {
            if (!AllegroScrapeManager.ActiveScrapers.ContainsKey(scraperName))
            {
                return (false, $"Scraper '{scraperName}' nie istnieje.");
            }

            AllegroScrapeManager.PauseScraper(scraperName);

            // Powiadom front
            if (AllegroScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
            {
                await BroadcastScraperStatusAsync(scraper);
            }

            await _hubContext.Clients.All.SendAsync("UpdateLogs", AllegroScrapeManager.GetRecentLogs(10));

            _logger.LogInformation("Scraper {ScraperName} został zatrzymany (hibernacja).", scraperName);

            return (true, $"Scraper '{scraperName}' został zatrzymany i przeszedł w tryb hibernacji.");
        }

        /// <summary>
        /// Wznawia indywidualnego scrapera
        /// </summary>
        public async Task<(bool success, string message)> ResumeScraperAsync(string scraperName)
        {
            if (!AllegroScrapeManager.ActiveScrapers.ContainsKey(scraperName))
            {
                return (false, $"Scraper '{scraperName}' nie istnieje.");
            }

            AllegroScrapeManager.ResumeScraper(scraperName);

            // Powiadom front
            if (AllegroScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
            {
                await BroadcastScraperStatusAsync(scraper);
            }

            await _hubContext.Clients.All.SendAsync("UpdateLogs", AllegroScrapeManager.GetRecentLogs(10));

            _logger.LogInformation("Scraper {ScraperName} został wznowiony.", scraperName);

            return (true, $"Scraper '{scraperName}' został wznowiony.");
        }

        /// <summary>
        /// Sprawdza i obsługuje timeout paczek - wywoływane przez BackgroundService
        /// </summary>
        public async Task<int> CheckAndHandleTimeoutsAsync()
        {
            if (AllegroScrapeManager.CurrentStatus != ScrapingProcessStatus.Running)
                return 0;

            // Znajdź paczki które przekroczyły timeout
            var timedOutBatches = AllegroScrapeManager.FindAndMarkTimedOutBatches();

            if (!timedOutBatches.Any())
                return 0;

            _logger.LogWarning("Znaleziono {Count} paczek które przekroczyły timeout.", timedOutBatches.Count);

            // Zwróć URLe do puli
            foreach (var (batchId, scraperName, taskIds) in timedOutBatches)
            {
                var offers = await _context.AllegroOffersToScrape
                    .Where(o => taskIds.Contains(o.Id))
                    .ToListAsync();

                foreach (var offer in offers)
                {
                    // Zwróć do puli tylko te które nie zostały jeszcze przetworzone
                    if (!offer.IsScraped && !offer.IsRejected)
                    {
                        offer.IsProcessing = false;
                    }
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Paczka {BatchId} od {ScraperName}: {Count} URLi wróciło do puli.",
                    batchId, scraperName, taskIds.Count);

                // Powiadom front o każdej ofercie
                foreach (var offer in offers)
                {
                    await _hubContext.Clients.All.SendAsync("UpdateAllegroOfferRow", offer);
                }

                // Aktualizuj status scrapera
                if (AllegroScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                {
                    scraper.Status = ScraperLiveStatus.Offline;
                    scraper.CurrentBatchId = null;
                    scraper.CurrentTaskId = null;
                    await BroadcastScraperStatusAsync(scraper);
                }
            }

            // Wyślij logi i aktualizacje
            await _hubContext.Clients.All.SendAsync("UpdateLogs", AllegroScrapeManager.GetRecentLogs(10));
            await _hubContext.Clients.All.SendAsync("UpdateDashboard", AllegroScrapeManager.GetDashboardSummary());

            return timedOutBatches.Count;
        }

        /// <summary>
        /// Sprawdza i oznacza nieaktywne scrapery jako offline
        /// </summary>
        public async Task<int> CheckAndMarkOfflineScrapersAsync()
        {
            var markedOffline = AllegroScrapeManager.MarkInactiveScrapersAsOffline();

            foreach (var scraperName in markedOffline)
            {
                if (AllegroScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                {
                    await BroadcastScraperStatusAsync(scraper);
                }
            }

            if (markedOffline.Any())
            {
                await _hubContext.Clients.All.SendAsync("UpdateLogs", AllegroScrapeManager.GetRecentLogs(10));
            }

            return markedOffline.Count;
        }

        /// <summary>
        /// Pobiera aktualne statystyki do widoku
        /// </summary>
        public async Task<ScrapingStatsDto> GetCurrentStatsAsync()
        {
            var dbStats = await _context.AllegroOffersToScrape
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Scraped = g.Count(o => o.IsScraped),
                    Rejected = g.Count(o => o.IsRejected),
                    Processing = g.Count(o => o.IsProcessing),
                    Prices = g.Sum(o => o.CollectedPricesCount)
                })
                .FirstOrDefaultAsync();

            return new ScrapingStatsDto
            {
                TotalUrls = dbStats?.Total ?? 0,
                ScrapedUrls = dbStats?.Scraped ?? 0,
                RejectedUrls = dbStats?.Rejected ?? 0,
                ProcessingUrls = dbStats?.Processing ?? 0,
                TotalPricesCollected = dbStats?.Prices ?? 0
            };
        }

        /// <summary>
        /// Pobiera szczegółowe dane scraperów
        /// </summary>
        public List<ScraperDetailsDto> GetScrapersDetails()
        {
            return AllegroScrapeManager.ActiveScrapers.Values
                .OrderBy(s => s.Name)
                .Select(scraper =>
                {
                    AllegroScrapeManager.ScraperStatistics.TryGetValue(scraper.Name, out var stats);
                    var activeBatch = AllegroScrapeManager.GetActiveScraperBatch(scraper.Name);

                    return new ScraperDetailsDto
                    {
                        Name = scraper.Name,
                        Status = scraper.Status.ToString(),
                        StatusCode = (int)scraper.Status,
                        LastCheckIn = scraper.LastCheckIn,
                        CurrentBatchId = scraper.CurrentBatchId,
                        IsManuallyPaused = stats?.IsManuallyPaused ?? false,
                        TotalUrlsProcessed = stats?.TotalUrlsProcessed ?? 0,
                        TotalUrlsSuccess = stats?.TotalUrlsSuccess ?? 0,
                        TotalUrlsFailed = stats?.TotalUrlsFailed ?? 0,
                        SuccessRate = stats?.SuccessRate ?? 0,
                        UrlsPerMinute = stats?.UrlsPerMinute ?? 0,
                        BatchesCompleted = stats?.TotalBatchesCompleted ?? 0,
                        BatchesTimedOut = stats?.TotalBatchesTimedOut ?? 0,
                        CurrentBatchNumber = stats?.CurrentBatchNumber ?? 0,
                        ActiveBatchTaskCount = activeBatch?.TaskIds.Count ?? 0,
                        ActiveBatchAssignedAt = activeBatch?.AssignedAt
                    };
                })
                .ToList();
        }

        private async Task BroadcastScraperStatusAsync(HybridScraperClient scraper)
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
    }

    // ===== DTOs =====

    public class ScrapingStatsDto
    {
        public int TotalUrls { get; set; }
        public int ScrapedUrls { get; set; }
        public int RejectedUrls { get; set; }
        public int ProcessingUrls { get; set; }
        public int TotalPricesCollected { get; set; }
    }

    public class ScraperDetailsDto
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public DateTime LastCheckIn { get; set; }
        public string? CurrentBatchId { get; set; }
        public bool IsManuallyPaused { get; set; }
        public int TotalUrlsProcessed { get; set; }
        public int TotalUrlsSuccess { get; set; }
        public int TotalUrlsFailed { get; set; }
        public double SuccessRate { get; set; }
        public double UrlsPerMinute { get; set; }
        public int BatchesCompleted { get; set; }
        public int BatchesTimedOut { get; set; }
        public int CurrentBatchNumber { get; set; }
        public int ActiveBatchTaskCount { get; set; }
        public DateTime? ActiveBatchAssignedAt { get; set; }
    }

    // ===== BACKGROUND SERVICE =====

    /// <summary>
    /// BackgroundService monitorujący timeouty i status scraperów
    /// </summary>
    public class AllegroScrapingMonitorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AllegroScrapingMonitorService> _logger;
        private const int CheckIntervalSeconds = 15; // Sprawdzaj co 15 sekund

        public AllegroScrapingMonitorService(
            IServiceProvider serviceProvider,
            ILogger<AllegroScrapingMonitorService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AllegroScrapingMonitorService uruchomiony.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scrapingService = scope.ServiceProvider.GetRequiredService<AllegroScrapingService>();

                    // Sprawdź timeouty paczek
                    var timedOutCount = await scrapingService.CheckAndHandleTimeoutsAsync();
                    if (timedOutCount > 0)
                    {
                        _logger.LogWarning("Obsłużono {Count} paczek z timeoutem.", timedOutCount);
                    }

                    // Sprawdź nieaktywne scrapery
                    var offlineCount = await scrapingService.CheckAndMarkOfflineScrapersAsync();
                    if (offlineCount > 0)
                    {
                        _logger.LogInformation("Oznaczono {Count} scraperów jako offline.", offlineCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd w AllegroScrapingMonitorService.");
                }

                await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), stoppingToken);
            }

            _logger.LogInformation("AllegroScrapingMonitorService zatrzymany.");
        }
    }
}
