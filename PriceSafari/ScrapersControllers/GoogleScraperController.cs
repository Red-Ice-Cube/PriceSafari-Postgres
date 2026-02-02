using Microsoft.AspNetCore.Mvc;
using PriceSafari.Services.GoogleScraping;
using PriceSafari.Services.ScheduleService;

namespace PriceSafari.ScrapersControllers
{
    [Route("ManagerPanel/GoogleOfferScraper")]
    public class GoogleScraperController : Controller
    {
        private readonly GoogleScraperService _scrapingService;
        private readonly ILogger<GoogleScraperController> _logger;

        public GoogleScraperController(
            GoogleScraperService scrapingService,
            ILogger<GoogleScraperController> logger)
        {
            _scrapingService = scrapingService;
            _logger = logger;
        }

        // ===== WIDOK GŁÓWNY =====
        [HttpGet("GoogleScraperPl")]
        public async Task<IActionResult> GoogleScraperPl()
        {
            var dbStats = await _scrapingService.GetDatabaseStatsAsync();
            var urls = await _scrapingService.GetUrlsAsync(1000);
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

        // ===== AKCJE STEROWANIA =====

        [HttpPost("StartScraping")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartScraping()
        {
            var (success, message) = await _scrapingService.StartScrapingProcessAsync();

            TempData["Message"] = message;
            TempData["MessageType"] = success ? "success" : "error";

            return RedirectToAction(nameof(GoogleScraperPl));
        }

        [HttpPost("StopScraping")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StopScraping()
        {
            var (success, message) = await _scrapingService.StopScrapingProcessAsync();

            TempData["Message"] = message;
            TempData["MessageType"] = success ? "warning" : "error";

            return RedirectToAction(nameof(GoogleScraperPl));
        }

        [HttpPost("PauseScraper")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PauseScraper(string scraperName)
        {
            await _scrapingService.PauseScraperAsync(scraperName);

            TempData["Message"] = $"Scraper '{scraperName}' zatrzymany.";
            TempData["MessageType"] = "warning";

            return RedirectToAction(nameof(GoogleScraperPl));
        }

        [HttpPost("ResumeScraper")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResumeScraper(string scraperName)
        {
            await _scrapingService.ResumeScraperAsync(scraperName);

            TempData["Message"] = $"Scraper '{scraperName}' wznowiony.";
            TempData["MessageType"] = "success";

            return RedirectToAction(nameof(GoogleScraperPl));
        }

        [HttpPost("ResetRejected")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetRejected()
        {
            var count = await _scrapingService.ResetRejectedUrlsAsync();

            TempData["Message"] = $"Zresetowano {count} odrzuconych URLi.";
            TempData["MessageType"] = "success";

            return RedirectToAction(nameof(GoogleScraperPl));
        }

        [HttpPost("ClearData")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearData()
        {
            var count = await _scrapingService.ClearCollectedDataAsync();

            TempData["Message"] = $"Wyczyszczono {count} zebranych cen.";
            TempData["MessageType"] = "warning";

            return RedirectToAction(nameof(GoogleScraperPl));
        }

        // ===== API DLA AJAX =====

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