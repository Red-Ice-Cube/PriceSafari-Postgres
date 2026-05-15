using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;

namespace PriceSafari.Services.CeneoExternalScraping
{
    public class CeneoExternalScrapingService
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<CeneoExternalScrapingService> _logger;

        public CeneoExternalScrapingService(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            ILogger<CeneoExternalScrapingService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task<(bool success, string message, int totalUrls)> StartScrapingProcessAsync()
        {
            _logger.LogInformation("Uruchamianie procesu scrapowania Ceneo External...");

            if (CeneoExternalScrapeManager.CurrentStatus == CeneoExternalScrapingProcessStatus.Running)
                return (false, "Proces już działa.", 0);

            var anyActiveScrapers = CeneoExternalScrapeManager.ActiveScrapers.Values
                .Any(s => s.Status != CeneoExternalScraperLiveStatus.Offline && s.Status != CeneoExternalScraperLiveStatus.Stopped);

            if (!anyActiveScrapers)
            {
                const string msg = "Brak aktywnych scraperów. Uruchom skrypt Python.";
                _logger.LogWarning(msg);
                return (false, msg, 0);
            }

            var urlsToScrape = await _context.CoOfrs
                .CountAsync(c => !c.IsScraped && !string.IsNullOrEmpty(c.OfferUrl));

            if (urlsToScrape == 0)
                return (true, "Brak URLi do scrapowania.", 0);

            CeneoExternalScrapeManager.ResetForNewProcess();

            await _hubContext.Clients.All.SendAsync("CeneoExternalScrapingStarted", new
            {
                startTime = CeneoExternalScrapeManager.ScrapingStartTime,
                totalUrls = urlsToScrape
            });

            await BroadcastDashboard();

            _logger.LogInformation("Proces scrapowania Ceneo External uruchomiony. URLi: {Count}", urlsToScrape);
            return (true, $"Proces uruchomiony. {urlsToScrape} URLi do przetworzenia.", urlsToScrape);
        }

        public async Task<(bool success, string message)> StopScrapingProcessAsync()
        {
            CeneoExternalScrapeManager.CurrentStatus = CeneoExternalScrapingProcessStatus.Idle;
            CeneoExternalScrapeManager.ScrapingEndTime = DateTime.UtcNow;
            CeneoExternalScrapeManager.AddSystemLog("WARNING", "Proces zatrzymany ręcznie");

            await _hubContext.Clients.All.SendAsync("CeneoExternalScrapingStopped", new
            {
                endTime = CeneoExternalScrapeManager.ScrapingEndTime
            });

            await BroadcastDashboard();

            _logger.LogInformation("Proces scrapowania Ceneo External zatrzymany.");
            return (true, "Proces zatrzymany.");
        }

        public async Task PauseScraperAsync(string scraperName)
        {
            CeneoExternalScrapeManager.PauseScraper(scraperName);
            if (CeneoExternalScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);
            await BroadcastLogs();
        }

        public async Task ResumeScraperAsync(string scraperName)
        {
            CeneoExternalScrapeManager.ResumeScraper(scraperName);
            if (CeneoExternalScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);
            await BroadcastLogs();
        }

        public async Task<int> CheckAndHandleTimeoutsAsync()
        {
            if (CeneoExternalScrapeManager.CurrentStatus != CeneoExternalScrapingProcessStatus.Running)
                return 0;

            var timedOutBatches = CeneoExternalScrapeManager.FindAndMarkTimedOutBatches();
            if (!timedOutBatches.Any()) return 0;

            _logger.LogWarning("Znaleziono {Count} paczek z timeoutem.", timedOutBatches.Count);

            foreach (var (batchId, scraperName, taskIds) in timedOutBatches)
            {
                if (CeneoExternalScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                {
                    scraper.Status = CeneoExternalScraperLiveStatus.Offline;
                    scraper.CurrentBatchId = null;
                    await BroadcastScraperStatus(scraper);
                }
            }

            await BroadcastLogs();
            await BroadcastDashboard();
            return timedOutBatches.Count;
        }

        public async Task<int> CheckAndMarkOfflineScrapersAsync()
        {
            var markedOffline = CeneoExternalScrapeManager.MarkInactiveScrapersAsOffline();

            foreach (var scraperName in markedOffline)
            {
                if (CeneoExternalScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                    await BroadcastScraperStatus(scraper);
            }

            if (markedOffline.Any())
            {
                await BroadcastLogs();
                await BroadcastDashboard();
            }
            return markedOffline.Count;
        }

        public async Task<CeneoExternalDbStatsDto> GetDatabaseStatsAsync()
        {
            var stats = await _context.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.OfferUrl))
                .GroupBy(_ => 1)
                .Select(g => new CeneoExternalDbStatsDto
                {
                    TotalUrls = g.Count(),
                    ScrapedUrls = g.Count(c => c.IsScraped),
                    RejectedUrls = g.Count(c => c.IsRejected),
                    TotalPrices = g.Sum(c => c.PricesCount)
                })
                .FirstOrDefaultAsync() ?? new CeneoExternalDbStatsDto();

            stats.PendingUrls = stats.TotalUrls - stats.ScrapedUrls;
            return stats;
        }

        public async Task<List<CeneoExternalUrlDto>> GetUrlsAsync()
        {
            var list = await _context.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.OfferUrl))
                .OrderBy(c => c.Id)
                .Select(c => new CeneoExternalUrlDto
                {
                    Id = c.Id,
                    OfferUrl = c.OfferUrl,
                    IsScraped = c.IsScraped,
                    IsRejected = c.IsRejected,
                    PricesCount = c.PricesCount,
                    CeneoSalesCount = c.CeneoSalesCount,
                    ProductIds = c.ProductIds
                })
                .ToListAsync();

            // Defensywnie - ProductIds może być null z DB
            foreach (var item in list)
            {
                if (item.ProductIds == null)
                    item.ProductIds = new List<int>();
            }

            return list;
        }
        public async Task<int> ResetRejectedUrlsAsync()
        {
            var rejected = await _context.CoOfrs
                .Where(c => c.IsRejected)
                .ToListAsync();

            foreach (var offer in rejected)
            {
                offer.IsRejected = false;
                offer.IsScraped = false;
            }

            await _context.SaveChangesAsync();
            CeneoExternalScrapeManager.AddSystemLog("INFO", $"Zresetowano {rejected.Count} odrzuconych URLi");
            await BroadcastLogs();
            return rejected.Count;
        }

        public async Task<int> ClearCollectedDataAsync()
        {
            var ceneoPriceHistories = await _context.CoOfrPriceHistories
                .Where(ph => ph.ExportedName != null || ph.StoreName != null && ph.GoogleStoreName == null)
                .ToListAsync();

            int deletedPrices = ceneoPriceHistories.Count;
            _context.CoOfrPriceHistories.RemoveRange(ceneoPriceHistories);

            var offers = await _context.CoOfrs
                .Where(c => c.IsScraped || c.IsRejected)
                .ToListAsync();

            foreach (var offer in offers)
            {
                offer.IsScraped = false;
                offer.IsRejected = false;
                offer.PricesCount = 0;
            }

            await _context.SaveChangesAsync();

            CeneoExternalScrapeManager.AddSystemLog("WARNING", $"Wyczyszczono {deletedPrices} cen Ceneo, zresetowano {offers.Count} URLi");
            await BroadcastLogs();
            return deletedPrices;
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
                activeBatchTaskCount = activeBatch?.TaskIds.Count ?? 0
            });
        }

        private async Task BroadcastDashboard()
        {
            await _hubContext.Clients.All.SendAsync("CeneoExternalUpdateDashboard",
                CeneoExternalScrapeManager.GetDashboardSummary());
        }

        private async Task BroadcastLogs()
        {
            await _hubContext.Clients.All.SendAsync("CeneoExternalUpdateLogs",
                CeneoExternalScrapeManager.GetRecentLogs(20));
        }
    }

    public class CeneoExternalDbStatsDto
    {
        public int TotalUrls { get; set; }
        public int ScrapedUrls { get; set; }
        public int RejectedUrls { get; set; }
        public int PendingUrls { get; set; }
        public int TotalPrices { get; set; }
    }

    public class CeneoExternalUrlDto
    {
        public int Id { get; set; }
        public string? OfferUrl { get; set; }
        public bool IsScraped { get; set; }
        public bool IsRejected { get; set; }
        public int PricesCount { get; set; }
        public int? CeneoSalesCount { get; set; }
        public List<int> ProductIds { get; set; } = new();
    }

    public class CeneoExternalScrapingMonitorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CeneoExternalScrapingMonitorService> _logger;
        private const int CheckIntervalSeconds = 20;

        public CeneoExternalScrapingMonitorService(
            IServiceProvider serviceProvider,
            ILogger<CeneoExternalScrapingMonitorService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CeneoExternalScrapingMonitorService uruchomiony.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scrapingService = scope.ServiceProvider.GetRequiredService<CeneoExternalScrapingService>();

                    var timedOutCount = await scrapingService.CheckAndHandleTimeoutsAsync();
                    if (timedOutCount > 0)
                        _logger.LogWarning("Obsłużono {Count} paczek z timeoutem.", timedOutCount);

                    var offlineCount = await scrapingService.CheckAndMarkOfflineScrapersAsync();
                    if (offlineCount > 0)
                        _logger.LogInformation("Oznaczono {Count} scraperów jako offline.", offlineCount);

                    // Sprawdź czy proces można zakończyć (brak pending + brak aktywnych paczek)
                    if (CeneoExternalScrapeManager.CurrentStatus == CeneoExternalScrapingProcessStatus.Running)
                    {
                        var stats = await scrapingService.GetDatabaseStatsAsync();
                        if (stats.PendingUrls == 0 && !CeneoExternalScrapeManager.HasActiveBatches())
                        {
                            _logger.LogInformation("Wszystkie URLe przetworzone. Kończę proces Ceneo External.");
                            CeneoExternalScrapeManager.FinishProcess();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd w CeneoExternalScrapingMonitorService.");
                }

                await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), stoppingToken);
            }

            _logger.LogInformation("CeneoExternalScrapingMonitorService zatrzymany.");
        }
    }
}