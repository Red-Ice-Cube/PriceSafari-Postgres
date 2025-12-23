namespace PriceSafari.Models.ViewModels
{
    public class AutomationStoreListViewModel
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public string LogoUrl { get; set; }

        // Flagi dostępności modułów w sklepie
        public bool OnCeneo { get; set; }
        public bool OnGoogle { get; set; }
        public bool OnAllegro { get; set; }

        // Liczniki reguł
        public int ComparisonRulesCount { get; set; }
        public int MarketplaceRulesCount { get; set; }
    }
}