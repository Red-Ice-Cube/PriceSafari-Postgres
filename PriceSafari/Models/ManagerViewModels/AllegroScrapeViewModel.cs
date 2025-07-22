using PriceSafari.ScrapersControllers;

namespace PriceSafari.Models.ManagerViewModels
{
    public class AllegroScrapeViewModel
    {
        public List<AllegroOfferToScrape> PreparedOffers { get; set; }
        public ScrapingProcessStatus CurrentStatus { get; set; }
        public ICollection<HybridScraperClient> ActiveScrapers { get; set; } // <-- ZMIANA TUTAJ
    }
}
