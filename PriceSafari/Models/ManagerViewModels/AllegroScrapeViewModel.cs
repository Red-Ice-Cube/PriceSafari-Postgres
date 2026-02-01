//using PriceSafari.ScrapersControllers;
//using static PriceSafari.Controllers.ManagerControllers.AllegroScrapeController;

//namespace PriceSafari.Models.ManagerViewModels
//{
//    public class AllegroScrapeViewModel
//    {
//        public List<AllegroOfferToScrape> PreparedOffers { get; set; }
//        public ScrapingProcessStatus CurrentStatus { get; set; }
//        public ICollection<HybridScraperClient> ActiveScrapers { get; set; } 
//        public ScrapingStatsViewModel Stats { get; set; }
//    }
//}



using PriceSafari.Controllers.ManagerControllers;
using PriceSafari.Models;
using PriceSafari.ScrapersControllers;
using PriceSafari.Services.AllegroServices;

namespace PriceSafari.Models.ManagerViewModels
{
    public class AllegroScrapeViewModel
    {
        // Oferty do scrapowania
        public List<AllegroOfferToScrape> PreparedOffers { get; set; } = new();

        // Status procesu
        public ScrapingProcessStatus CurrentStatus { get; set; }

        // Podstawowe dane scraperów (dla kompatybilności wstecznej)
        public IEnumerable<HybridScraperClient> ActiveScrapers { get; set; } = new List<HybridScraperClient>();

        // Szczegółowe dane scraperów z statystykami
        public List<ScraperDetailsDto> ScrapersDetails { get; set; } = new();

        // Ostatnie logi
        public List<ScraperLogEntry> RecentLogs { get; set; } = new();

        // Statystyki
        public ScrapingStatsViewModel Stats { get; set; } = new();

        // Podsumowanie dla dashboardu (jako dynamic/object)
        public object? DashboardSummary { get; set; }
    }
}