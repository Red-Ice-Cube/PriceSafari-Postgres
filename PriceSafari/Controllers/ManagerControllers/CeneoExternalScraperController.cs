using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PriceSafari.Models.ManagerViewModels;
using PriceSafari.Services.CeneoExternalScraping;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CeneoExternalScraperController : Controller
    {
        private readonly CeneoExternalScrapingService _service;
        private readonly ILogger<CeneoExternalScraperController> _logger;

        public CeneoExternalScraperController(
            CeneoExternalScrapingService service,
            ILogger<CeneoExternalScraperController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var viewModel = new CeneoExternalScraperViewModel
            {
                CurrentStatus = CeneoExternalScrapeManager.CurrentStatus,
                ScrapingStartTime = CeneoExternalScrapeManager.ScrapingStartTime,
                ScrapingEndTime = CeneoExternalScrapeManager.ScrapingEndTime,
                DbStats = await _service.GetDatabaseStatsAsync(),
                Urls = await _service.GetUrlsAsync(),
                Scrapers = CeneoExternalScrapeManager.GetScrapersDetails(),
                RecentLogs = CeneoExternalScrapeManager.GetRecentLogs(50)
            };

            return View("~/Views/ManagerPanel/CeneoExternalScraper/Index.cshtml", viewModel);
        }

        [HttpGet]
        public IActionResult GetScrapersDetails()
        {
            var scrapers = CeneoExternalScrapeManager.GetScrapersDetails();
            return Json(scrapers);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> StartScraping()
        {
            var (success, message, totalUrls) = await _service.StartScrapingProcessAsync();
            return Json(new { success, message, totalUrls });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> StopScraping()
        {
            var (success, message) = await _service.StopScrapingProcessAsync();
            return Json(new { success, message });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> PauseScraper(string scraperName)
        {
            if (string.IsNullOrWhiteSpace(scraperName))
                return Json(new { success = false, message = "scraperName is required" });

            await _service.PauseScraperAsync(scraperName);
            return Json(new { success = true });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ResumeScraper(string scraperName)
        {
            if (string.IsNullOrWhiteSpace(scraperName))
                return Json(new { success = false, message = "scraperName is required" });

            await _service.ResumeScraperAsync(scraperName);
            return Json(new { success = true });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetRejected()
        {
            int count = await _service.ResetRejectedUrlsAsync();
            return Json(new { success = true, count });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearData()
        {
            int deletedPrices = await _service.ClearCollectedDataAsync();
            return Json(new { success = true, deletedPrices });
        }

        [HttpGet]
        public async Task<IActionResult> GetStats()
        {
            var stats = await _service.GetDatabaseStatsAsync();
            return Json(new
            {
                totalUrls = stats.TotalUrls,
                scrapedUrls = stats.ScrapedUrls,
                rejectedUrls = stats.RejectedUrls,
                pendingUrls = stats.PendingUrls,
                totalPrices = stats.TotalPrices
            });
        }
    }
}