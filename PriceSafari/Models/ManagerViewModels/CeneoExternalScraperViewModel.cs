using PriceSafari.Services.CeneoExternalScraping;

namespace PriceSafari.Models.ManagerViewModels
{
    public class CeneoExternalScraperViewModel
    {
        public CeneoExternalScrapingProcessStatus CurrentStatus { get; set; }
        public DateTime? ScrapingStartTime { get; set; }
        public DateTime? ScrapingEndTime { get; set; }
        public CeneoExternalDbStatsDto DbStats { get; set; } = new();
        public List<CeneoExternalUrlDto> Urls { get; set; } = new();
        public List<object> Scrapers { get; set; } = new();
        public List<CeneoExternalScraperLogEntry> RecentLogs { get; set; } = new();
    }
}