using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PriceSafari.Hubs;
using PriceSafari.Services.GoogleScraping;
using PriceSafari.Services.ScheduleService;

namespace PriceSafari.ScrapersControllers
{
    [Authorize(Roles = "Admin")] // 1. Zabezpieczenie dostępu
    [Route("ManagerPanel/GoogleOfferScraper")]
    public class GoogleScraperController : Controller
    {
        private readonly GoogleScraperService _scrapingService;
        private readonly IHubContext<ScrapingHub> _hubContext; // 2. Potrzebne do powiadomienia JS o Starcie/Stopie
        private readonly ILogger<GoogleScraperController> _logger;

        public GoogleScraperController(
            GoogleScraperService scrapingService,
            IHubContext<ScrapingHub> hubContext,
            ILogger<GoogleScraperController> logger)
        {
            _scrapingService = scrapingService;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpGet("GoogleScraperPl")]
        public async Task<IActionResult> GoogleScraperPl()
        {
            var dbStats = await _scrapingService.GetDatabaseStatsAsync();
            var urls = await _scrapingService.GetUrlsAsync();
            var scrapers = GoogleScrapeManager.GetScrapersDetails();
            var logs = GoogleScrapeManager.GetRecentLogs(50);

            var viewModel = new GoogleScraperPlViewModel
            {
                DbStats = dbStats,
                Urls = urls,
                Scrapers = scrapers,
                RecentLogs = logs,
                CurrentStatus = GoogleScrapeManager.CurrentStatus,
                ScrapingStartTime = GoogleScrapeManager.ScrapingStartTime,
                ScrapingEndTime = GoogleScrapeManager.ScrapingEndTime,
                BatchSize = GoogleScrapeManager.BatchSize,
                BatchTimeoutSeconds = GoogleScrapeManager.BatchTimeoutSeconds
            };

            return View("~/Views/ManagerPanel/GoogleOfferScraper/GoogleScraperPl.cshtml", viewModel);
        }

        // ===== AKCJE STEROWANIA (Zwracają JSON dla JavaScript) =====

        [HttpPost("StartScraping")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartScraping()
        {
            // Logika biznesowa w serwisie
            var (success, message) = await _scrapingService.StartScrapingProcessAsync();

            if (success)
            {
                // 3. Wysyłamy sygnał do widoku, żeby ruszył wykres (tak jak robiło to API)
                await _hubContext.Clients.All.SendAsync("GoogleScrapingStarted", new
                {
                    startTime = GoogleScrapeManager.ScrapingStartTime,
                    totalUrls = GoogleScrapeManager.AssignedBatches.Count // lub inna statystyka
                });
            }

            // Zwracamy JSON, bo Twój fetch w JS tego oczekuje
            return Json(new { success, message });
        }

        [HttpPost("StopScraping")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StopScraping()
        {
            var (success, message) = await _scrapingService.StopScrapingProcessAsync();

            if (success)
            {
                // Sygnał do zatrzymania wykresu
                await _hubContext.Clients.All.SendAsync("GoogleScrapingStopped", new
                {
                    endTime = DateTime.UtcNow
                });
            }

            return Json(new { success, message });
        }

        [HttpPost("PauseScraper")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PauseScraper(string scraperName)
        {
            await _scrapingService.PauseScraperAsync(scraperName);
            // Tutaj status scrapera odświeży się sam przez cykliczny UpdateScraperStatus w SignalR
            return Json(new { success = true, message = $"Scraper '{scraperName}' zatrzymany." });
        }

        [HttpPost("ResumeScraper")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResumeScraper(string scraperName)
        {
            await _scrapingService.ResumeScraperAsync(scraperName);
            return Json(new { success = true, message = $"Scraper '{scraperName}' wznowiony." });
        }

        [HttpPost("ResetRejected")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetRejected()
        {
            var count = await _scrapingService.ResetRejectedUrlsAsync();
            // Po resecie JS sam sobie odpyta API/stats, więc wystarczy info o sukcesie
            return Json(new { success = true, message = $"Zresetowano {count} odrzuconych URLi." });
        }

        [HttpPost("ClearData")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearData()
        {
            var count = await _scrapingService.ClearCollectedDataAsync();
            return Json(new { success = true, message = $"Wyczyszczono {count} zebranych cen." });
        }

        // ===== API DLA AJAX (Zabezpieczone sesją Admina) =====

        [HttpGet("api/stats")]
        public async Task<IActionResult> GetStats()
        {
            var stats = await _scrapingService.GetDatabaseStatsAsync();
            return Json(stats);
        }

        [HttpGet("api/scrapers")]
        public IActionResult GetScrapers()
        {
            var scrapers = GoogleScrapeManager.GetScrapersDetails();
            return Json(scrapers);
        }

        [HttpGet("api/logs")]
        public IActionResult GetLogs(int count = 50)
        {
            var logs = GoogleScrapeManager.GetRecentLogs(count);
            return Json(logs);
        }

        [HttpGet("api/dashboard")]
        public IActionResult GetDashboard()
        {
            var dashboard = GoogleScrapeManager.GetDashboardSummary();
            return Json(dashboard);
        }
    }

    // ===== VIEWMODEL =====

    public class GoogleScraperPlViewModel
    {
        public GoogleDbStatsDto DbStats { get; set; } = new();
        public List<GoogleUrlDto> Urls { get; set; } = new();
        public List<object> Scrapers { get; set; } = new();
        public List<GoogleScraperLogEntry> RecentLogs { get; set; } = new();
        public GoogleScrapingProcessStatus CurrentStatus { get; set; }
        public DateTime? ScrapingStartTime { get; set; }
        public DateTime? ScrapingEndTime { get; set; }
        public int BatchSize { get; set; }
        public int BatchTimeoutSeconds { get; set; }
    }
}