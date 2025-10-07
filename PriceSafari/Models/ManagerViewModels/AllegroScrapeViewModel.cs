using PriceSafari.ScrapersControllers;
using static PriceSafari.Controllers.ManagerControllers.AllegroScrapeController;

namespace PriceSafari.Models.ManagerViewModels
{
    public class AllegroScrapeViewModel
    {
        public List<AllegroOfferToScrape> PreparedOffers { get; set; }
        public ScrapingProcessStatus CurrentStatus { get; set; }
        public ICollection<HybridScraperClient> ActiveScrapers { get; set; } 
        public ScrapingStatsViewModel Stats { get; set; }
    }
}
