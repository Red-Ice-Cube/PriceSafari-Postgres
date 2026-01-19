namespace PriceSafari.Models.ViewModels
{
    public class AutomationStoreListViewModel
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public string LogoUrl { get; set; }

        public bool OnCeneo { get; set; }
        public bool OnGoogle { get; set; }
        public bool OnAllegro { get; set; }

        // Porównywarki (Ceneo/Google)
        public int ComparisonRulesActiveCount { get; set; }
        public int ComparisonRulesInactiveCount { get; set; }

        // Marketplace (Allegro)
        public int MarketplaceRulesActiveCount { get; set; }
        public int MarketplaceRulesInactiveCount { get; set; }
    }
}