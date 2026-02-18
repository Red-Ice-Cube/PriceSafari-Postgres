using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Services.GoogleScraping;

namespace PriceSafari.Services.ScheduleService
{

// <summary>

// Serwis zarządzający procesem scrapowania Google Shopping

// </summary>

    public class GoogleScraperService
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<GoogleScraperService> _logger;

        public GoogleScraperService(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            ILogger<GoogleScraperService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

// <summary>

// Uruchamia proces scrapowania Google

// </summary>

        public async Task<(bool success, string message)> StartScrapingProcessAsync()
        {
            _logger.LogInformation("Uruchamianie procesu scrapowania Google...");

            if (GoogleScrapeManager.CurrentStatus == GoogleScrapingProcessStatus.Running)
                return (false, "Proces już działa.");

            var anyActiveScrapers = GoogleScrapeManager.ActiveScrapers.Values
                .Any(s => s.Status != GoogleScraperLiveStatus.Offline && s.Status != GoogleScraperLiveStatus.Stopped);

            if (!anyActiveScrapers)
                return (false, "Brak aktywnych scraperów. Uruchom skrypt Python.");

            var urlsToScrape = await _context.CoOfrs
                .CountAsync(c => (!string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer) && !c.GoogleIsScraped);

            if (urlsToScrape == 0)
                return (true, "Brak URLi do scrapowania.");

            GoogleScrapeManager.ResetForNewProcess();

            await _hubContext.Clients.All.SendAsync("GoogleScrapingStarted", new
            {
                startTime = GoogleScrapeManager.ScrapingStartTime,
                totalUrls = urlsToScrape
            });

            await BroadcastDashboard();

            _logger.LogInformation("Proces scrapowania Google uruchomiony. URLi: {Count}", urlsToScrape);
            return (true, $"Proces uruchomiony. {urlsToScrape} URLi do przetworzenia.");
        }

// <summary>

// Zatrzymuje proces scrapowania Google

// </summary>

        public async Task<(bool success, string message)> StopScrapingProcessAsync()
        {
            GoogleScrapeManager.CurrentStatus = GoogleScrapingProcessStatus.Idle;
            GoogleScrapeManager.ScrapingEndTime = DateTime.UtcNow;
            GoogleScrapeManager.AddSystemLog("WARNING", "Proces zatrzymany ręcznie");

            await _hubContext.Clients.All.SendAsync("GoogleScrapingStopped", new
            {
                endTime = GoogleScrapeManager.ScrapingEndTime
            });

            await BroadcastDashboard();

            _logger.LogInformation("Proces scrapowania Google zatrzymany.");
            return (true, "Proces zatrzymany.");
        }

// <summary>

// Zatrzymuje indywidualnego scrapera

// </summary>

        public async Task PauseScraperAsync(string scraperName)
        {
            GoogleScrapeManager.PauseScraper(scraperName);

            if (GoogleScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            await BroadcastLogs();
            _logger.LogInformation("Scraper {ScraperName} zatrzymany.", scraperName);
        }

// <summary>

// Wznawia indywidualnego scrapera

// </summary>

        public async Task ResumeScraperAsync(string scraperName)
        {
            GoogleScrapeManager.ResumeScraper(scraperName);

            if (GoogleScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            await BroadcastLogs();
            _logger.LogInformation("Scraper {ScraperName} wznowiony.", scraperName);
        }

// <summary>

// Sprawdza i obsługuje timeout paczek

// </summary>

        public async Task<int> CheckAndHandleTimeoutsAsync()
        {
            if (GoogleScrapeManager.CurrentStatus != GoogleScrapingProcessStatus.Running)
                return 0;

            var timedOutBatches = GoogleScrapeManager.FindAndMarkTimedOutBatches();

            if (!timedOutBatches.Any())
                return 0;

            _logger.LogWarning("Znaleziono {Count} paczek z timeoutem.", timedOutBatches.Count);

            foreach (var (batchId, scraperName, taskIds) in timedOutBatches)
            {

                if (GoogleScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                {
                    scraper.Status = GoogleScraperLiveStatus.Offline;
                    scraper.CurrentBatchId = null;
                    await BroadcastScraperStatus(scraper);
                }
            }

            await BroadcastLogs();
            await BroadcastDashboard();

            return timedOutBatches.Count;
        }

// <summary>

// Sprawdza i oznacza nieaktywne scrapery jako offline

// </summary>

        public async Task<int> CheckAndMarkOfflineScrapersAsync()
        {
            var markedOffline = GoogleScrapeManager.MarkInactiveScrapersAsOffline();

            foreach (var scraperName in markedOffline)
            {
                if (GoogleScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                    await BroadcastScraperStatus(scraper);
            }

            if (markedOffline.Any())
            {
                await BroadcastLogs();
                await BroadcastDashboard();
            }

            return markedOffline.Count;
        }

// <summary>

// Pobiera statystyki z bazy danych

// </summary>

        public async Task<GoogleDbStatsDto> GetDatabaseStatsAsync()
        {
            var stats = await _context.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)
                .GroupBy(_ => 1)
                .Select(g => new GoogleDbStatsDto
                {
                    TotalUrls = g.Count(),
                    ScrapedUrls = g.Count(c => c.GoogleIsScraped),
                    RejectedUrls = g.Count(c => c.GoogleIsRejected),
                    TotalPrices = g.Sum(c => c.GooglePricesCount),
                    UrlsWithWRGA = g.Count(c => c.UseWRGA),
                    UrlsWithGPID = g.Count(c => c.UseGPID),
                    UrlsWithHidOffer = g.Count(c => c.UseGoogleHidOffer)
                })
                .FirstOrDefaultAsync() ?? new GoogleDbStatsDto();

            stats.PendingUrls = stats.TotalUrls - stats.ScrapedUrls;
            return stats;
        }

// <summary>

// Pobiera WSZYSTKIE URLe do widoku (bez limitu, naturalna kolejność)

// </summary>

        public async Task<List<GoogleUrlDto>> GetUrlsAsync()
        {
            return await _context.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) || c.UseGoogleHidOffer)

                .Select(c => new GoogleUrlDto
                {
                    Id = c.Id,
                    GoogleOfferUrl = c.GoogleOfferUrl,
                    GoogleCid = c.GoogleCid,
                    GoogleGid = c.GoogleGid,
                    GoogleHid = c.GoogleHid,
                    UseGoogleHidOffer = c.UseGoogleHidOffer,
                    UseWRGA = c.UseWRGA,
                    UseGPID = c.UseGPID,
                    GoogleIsScraped = c.GoogleIsScraped,
                    GoogleIsRejected = c.GoogleIsRejected,
                    GooglePricesCount = c.GooglePricesCount,
                    ProductIdsGoogle = c.ProductIdsGoogle ?? new List<int>()
                })
                .ToListAsync();
        }

// <summary>

// Resetuje odrzucone URLe

// </summary>

        public async Task<int> ResetRejectedUrlsAsync()
        {
            var rejected = await _context.CoOfrs
                .Where(c => c.GoogleIsRejected)
                .ToListAsync();

            foreach (var offer in rejected)
            {
                offer.GoogleIsRejected = false;
                offer.GoogleIsScraped = false;
            }

            await _context.SaveChangesAsync();

            GoogleScrapeManager.AddSystemLog("INFO", $"Zresetowano {rejected.Count} odrzuconych URLi");
            await BroadcastLogs();

            return rejected.Count;
        }

// <summary>

// Czyści zebrane dane Google

// </summary>

        public async Task<int> ClearCollectedDataAsync()
        {
            var deletedPrices = await _context.CoOfrPriceHistories.CountAsync();
            _context.CoOfrPriceHistories.RemoveRange(_context.CoOfrPriceHistories);

            var offers = await _context.CoOfrs
                .Where(c => c.GoogleIsScraped || c.GoogleIsRejected)
                .ToListAsync();

            foreach (var offer in offers)
            {
                offer.GoogleIsScraped = false;
                offer.GoogleIsRejected = false;
                offer.GooglePricesCount = 0;
            }

            await _context.SaveChangesAsync();

            GoogleScrapeManager.AddSystemLog("WARNING", $"Wyczyszczono {deletedPrices} cen, zresetowano {offers.Count} URLi");
            await BroadcastLogs();

            return deletedPrices;
        }

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
                activeBatchTaskCount = activeBatch?.TaskIds.Count ?? 0
            });
        }

        private async Task BroadcastDashboard()
        {
            await _hubContext.Clients.All.SendAsync("GoogleUpdateDashboard",
                GoogleScrapeManager.GetDashboardSummary());
        }

        private async Task BroadcastLogs()
        {
            await _hubContext.Clients.All.SendAsync("GoogleUpdateLogs",
                GoogleScrapeManager.GetRecentLogs(20));
        }
    }

    public class GoogleDbStatsDto
    {
        public int TotalUrls { get; set; }
        public int ScrapedUrls { get; set; }
        public int RejectedUrls { get; set; }
        public int PendingUrls { get; set; }
        public int TotalPrices { get; set; }
        public int UrlsWithWRGA { get; set; }
        public int UrlsWithGPID { get; set; }
        public int UrlsWithHidOffer { get; set; }
    }

    public class GoogleUrlDto
    {
        public int Id { get; set; }
        public string? GoogleOfferUrl { get; set; }
        public string? GoogleCid { get; set; }
        public string? GoogleGid { get; set; }
        public string? GoogleHid { get; set; }
        public bool UseGoogleHidOffer { get; set; }
        public bool UseWRGA { get; set; }
        public bool UseGPID { get; set; }
        public bool GoogleIsScraped { get; set; }
        public bool GoogleIsRejected { get; set; }
        public int GooglePricesCount { get; set; }
        public List<int> ProductIdsGoogle { get; set; } = new();
    }

// <summary>

// BackgroundService monitorujący timeouty i status scraperów Google

// </summary>

    public class GoogleScrapingMonitorService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<GoogleScrapingMonitorService> _logger;
        private const int CheckIntervalSeconds = 20;

        public GoogleScrapingMonitorService(
            IServiceProvider serviceProvider,
            ILogger<GoogleScrapingMonitorService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("GoogleScrapingMonitorService uruchomiony.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scrapingService = scope.ServiceProvider.GetRequiredService<GoogleScraperService>();

                    var timedOutCount = await scrapingService.CheckAndHandleTimeoutsAsync();
                    if (timedOutCount > 0)
                        _logger.LogWarning("Obsłużono {Count} paczek z timeoutem.", timedOutCount);

                    var offlineCount = await scrapingService.CheckAndMarkOfflineScrapersAsync();
                    if (offlineCount > 0)
                        _logger.LogInformation("Oznaczono {Count} scraperów jako offline.", offlineCount);

                    if (GoogleScrapeManager.CurrentStatus == GoogleScrapingProcessStatus.Running)
                    {
                        var stats = await scrapingService.GetDatabaseStatsAsync();
                        if (stats.PendingUrls == 0 && !GoogleScrapeManager.HasActiveBatches())
                        {
                            _logger.LogInformation("Wszystkie URLe przetworzone. Kończę proces.");
                            GoogleScrapeManager.FinishProcess();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd w GoogleScrapingMonitorService.");
                }

                await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), stoppingToken);
            }

            _logger.LogInformation("GoogleScrapingMonitorService zatrzymany.");
        }
    }
}