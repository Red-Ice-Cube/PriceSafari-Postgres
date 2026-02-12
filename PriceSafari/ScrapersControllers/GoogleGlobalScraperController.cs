using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Services.GoogleScraping;

namespace PriceSafari.ScrapersControllers
{
    [Authorize(Roles = "Admin")]
    [Route("ManagerPanel/GlobalOfferScraper")]
    public class GoogleGlobalScraperController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<GoogleGlobalScraperController> _logger;

        public GoogleGlobalScraperController(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            ILogger<GoogleGlobalScraperController> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpGet("GlobalScraperEU")]
        public async Task<IActionResult> GlobalScraperEU([FromQuery] int? regionFilter = null)
        {
            var dbStats = await GetDatabaseStatsAsync(regionFilter);
            var products = await GetProductsAsync(regionFilter);
            var scrapers = GoogleGlobalScrapeManager.GetScrapersDetails();
            var logs = GoogleGlobalScrapeManager.GetRecentLogs(50);
            var regions = await GetRegionsAsync();

            var viewModel = new GlobalScraperEUViewModel
            {
                DbStats = dbStats,
                Products = products,
                Scrapers = scrapers,
                RecentLogs = logs,
                Regions = regions,
                CurrentStatus = GoogleGlobalScrapeManager.CurrentStatus,
                ScrapingStartTime = GoogleGlobalScrapeManager.ScrapingStartTime,
                ScrapingEndTime = GoogleGlobalScrapeManager.ScrapingEndTime,
                BatchSize = GoogleGlobalScrapeManager.BatchSize,
                BatchTimeoutSeconds = GoogleGlobalScrapeManager.BatchTimeoutSeconds,
                ActiveRegionFilter = regionFilter ?? GoogleGlobalScrapeManager.ActiveRegionFilter
            };

            return View("~/Views/ManagerPanel/GlobalOfferScraper/GlobalScraperEU.cshtml", viewModel);
        }

        // ===== AKCJE STEROWANIA (JSON dla JavaScript) =====

        [HttpPost("StartScraping")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartScraping([FromQuery] int? regionId = null)
        {
            if (GoogleGlobalScrapeManager.CurrentStatus == GlobalScrapingProcessStatus.Running)
                return Json(new { success = false, message = "Scraping już działa." });

            var query = _context.GoogleScrapingProducts
                .Where(gsp => gsp.IsScraped == null && !string.IsNullOrEmpty(gsp.GoogleUrl));

            if (regionId.HasValue)
                query = query.Where(gsp => gsp.RegionId == regionId.Value);

            var totalToScrape = await query.CountAsync();

            if (totalToScrape == 0)
                return Json(new { success = false, message = "Brak produktów do scrapowania." });

            GoogleGlobalScrapeManager.ResetForNewProcess(regionId);

            await _hubContext.Clients.All.SendAsync("GlobalScrapingStarted", new
            {
                startTime = GoogleGlobalScrapeManager.ScrapingStartTime,
                totalProducts = totalToScrape,
                regionFilter = regionId
            });

            return Json(new { success = true, message = $"Rozpoczęto. {totalToScrape} produktów do przetworzenia." });
        }

        [HttpPost("StopScraping")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StopScraping()
        {
            if (GoogleGlobalScrapeManager.CurrentStatus != GlobalScrapingProcessStatus.Running)
                return Json(new { success = false, message = "Scraping nie jest uruchomiony." });

            GoogleGlobalScrapeManager.CurrentStatus = GlobalScrapingProcessStatus.Idle;
            GoogleGlobalScrapeManager.ScrapingEndTime = DateTime.UtcNow;
            GoogleGlobalScrapeManager.AddSystemLog("WARNING", "Global scraping zatrzymany z panelu");

            await _hubContext.Clients.All.SendAsync("GlobalScrapingStopped", new
            {
                endTime = DateTime.UtcNow
            });

            return Json(new { success = true, message = "Scraping zatrzymany." });
        }

        [HttpPost("PauseScraper")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PauseScraper(string scraperName)
        {
            GoogleGlobalScrapeManager.PauseScraper(scraperName);

            if (GoogleGlobalScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            return Json(new { success = true, message = $"Scraper '{scraperName}' zatrzymany." });
        }

        [HttpPost("ResumeScraper")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResumeScraper(string scraperName)
        {
            GoogleGlobalScrapeManager.ResumeScraper(scraperName);

            if (GoogleGlobalScrapeManager.ActiveScrapers.TryGetValue(scraperName, out var scraper))
                await BroadcastScraperStatus(scraper);

            return Json(new { success = true, message = $"Scraper '{scraperName}' wznowiony." });
        }

        [HttpPost("ResetRejected")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetRejected([FromQuery] int? regionFilter = null)
        {
            var query = _context.GoogleScrapingProducts
                .Where(gsp => gsp.IsScraped == true && gsp.OffersCount == 0);

            if (regionFilter.HasValue)
                query = query.Where(gsp => gsp.RegionId == regionFilter.Value);

            var rejected = await query.ToListAsync();

            foreach (var product in rejected)
            {
                product.IsScraped = null;
                product.OffersCount = 0;
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Zresetowano {rejected.Count} odrzuconych produktów." });
        }

        [HttpPost("ClearData")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearData([FromQuery] int? regionFilter = null)
        {
            // Pobierz ID produktów do czyszczenia
            var productQuery = _context.GoogleScrapingProducts.AsQueryable();
            if (regionFilter.HasValue)
                productQuery = productQuery.Where(gsp => gsp.RegionId == regionFilter.Value);

            var productIds = await productQuery.Select(gsp => gsp.ScrapingProductId).ToListAsync();

            // Usuń dane cenowe
            var pricesToDelete = _context.PriceData.Where(pd => productIds.Contains(pd.ScrapingProductId));
            var count = await pricesToDelete.CountAsync();
            _context.PriceData.RemoveRange(pricesToDelete);

            // Zresetuj statusy produktów
            var products = await productQuery.Where(gsp => gsp.IsScraped == true).ToListAsync();
            foreach (var product in products)
            {
                product.IsScraped = null;
                product.OffersCount = 0;
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Wyczyszczono {count} zebranych cen." });
        }

        // ===== API DLA AJAX (Zabezpieczone sesją Admina) =====

        [HttpGet("api/stats")]
        public async Task<IActionResult> GetStats([FromQuery] int? regionFilter = null)
        {
            var stats = await GetDatabaseStatsAsync(regionFilter);
            return Json(stats);
        }

        [HttpGet("api/scrapers")]
        public IActionResult GetScrapers()
        {
            var scrapers = GoogleGlobalScrapeManager.GetScrapersDetails();
            return Json(scrapers);
        }

        [HttpGet("api/logs")]
        public IActionResult GetLogs(int count = 50)
        {
            var logs = GoogleGlobalScrapeManager.GetRecentLogs(count);
            return Json(logs);
        }

        [HttpGet("api/dashboard")]
        public IActionResult GetDashboard()
        {
            var dashboard = GoogleGlobalScrapeManager.GetDashboardSummary();
            return Json(dashboard);
        }

        // ===== PRYWATNE METODY POMOCNICZE (DB) =====

        private async Task<GlobalDbStatsDto> GetDatabaseStatsAsync(int? regionFilter = null)
        {
            var query = _context.GoogleScrapingProducts
                .Where(gsp => !string.IsNullOrEmpty(gsp.GoogleUrl));

            if (regionFilter.HasValue)
                query = query.Where(gsp => gsp.RegionId == regionFilter.Value);

            var total = await query.CountAsync();
            var scraped = await query.CountAsync(gsp => gsp.IsScraped == true);
            var rejected = await query.CountAsync(gsp => gsp.IsScraped == true && gsp.OffersCount == 0);
            var pending = await query.CountAsync(gsp => gsp.IsScraped == null);
            var totalPrices = await query.SumAsync(gsp => gsp.OffersCount);

            // Liczba unikalnych regionów
            var regionsCount = await query.Select(gsp => gsp.RegionId).Distinct().CountAsync();

            return new GlobalDbStatsDto
            {
                TotalProducts = total,
                ScrapedProducts = scraped,
                RejectedProducts = rejected,
                PendingProducts = pending,
                TotalPrices = totalPrices,
                RegionsCount = regionsCount
            };
        }

        private async Task<List<GlobalProductDto>> GetProductsAsync(int? regionFilter = null)
        {
            var query = _context.GoogleScrapingProducts
                .Where(gsp => !string.IsNullOrEmpty(gsp.GoogleUrl));

            if (regionFilter.HasValue)
                query = query.Where(gsp => gsp.RegionId == regionFilter.Value);

            return await query
                .OrderBy(gsp => gsp.ScrapingProductId)
                .Take(2000) // Limit dla widoku
                .Select(gsp => new GlobalProductDto
                {
                    ScrapingProductId = gsp.ScrapingProductId,
                    GoogleUrl = gsp.GoogleUrl,
                    CountryCode = gsp.CountryCode ?? "??",
                    RegionId = gsp.RegionId,
                    IsScraped = gsp.IsScraped,
                    OffersCount = gsp.OffersCount
                })
                .ToListAsync();
        }

        private async Task<List<GlobalRegionDto>> GetRegionsAsync()
        {
            // Pobierz regiony które mają przypisane produkty do scrapowania
            var regionIds = await _context.GoogleScrapingProducts
                .Where(gsp => !string.IsNullOrEmpty(gsp.GoogleUrl))
                .Select(gsp => gsp.RegionId)
                .Distinct()
                .ToListAsync();

            return await _context.Regions
                .Where(r => regionIds.Contains(r.RegionId))
                .Select(r => new GlobalRegionDto
                {
                    RegionId = r.RegionId,
                    Name = r.Name,
                    CountryCode = r.CountryCode
                })
                .OrderBy(r => r.Name)
                .ToListAsync();
        }

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
    }

    // ===== DTOs =====

    public class GlobalDbStatsDto
    {
        public int TotalProducts { get; set; }
        public int ScrapedProducts { get; set; }
        public int RejectedProducts { get; set; }
        public int PendingProducts { get; set; }
        public int TotalPrices { get; set; }
        public int RegionsCount { get; set; }
    }

    public class GlobalProductDto
    {
        public int ScrapingProductId { get; set; }
        public string GoogleUrl { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public int RegionId { get; set; }
        public bool? IsScraped { get; set; }
        public int OffersCount { get; set; }
    }

    public class GlobalRegionDto
    {
        public int RegionId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
    }

    // ===== VIEWMODEL =====

    public class GlobalScraperEUViewModel
    {
        public GlobalDbStatsDto DbStats { get; set; } = new();
        public List<GlobalProductDto> Products { get; set; } = new();
        public List<object> Scrapers { get; set; } = new();
        public List<GlobalScraperLogEntry> RecentLogs { get; set; } = new();
        public List<GlobalRegionDto> Regions { get; set; } = new();
        public GlobalScrapingProcessStatus CurrentStatus { get; set; }
        public DateTime? ScrapingStartTime { get; set; }
        public DateTime? ScrapingEndTime { get; set; }
        public int BatchSize { get; set; }
        public int BatchTimeoutSeconds { get; set; }
        public int? ActiveRegionFilter { get; set; }
    }
}