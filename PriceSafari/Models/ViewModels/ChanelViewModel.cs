namespace PriceSafari.Models.ViewModels
{
    public class ChanelViewModel
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public DateTime? LastScrapeDate { get; set; }
        public string? LogoUrl { get; set; }
        public int? ProductCount { get; set; }
        public int? AllowedProducts { get; set; }
        public int? AllegroProductCount { get; set; }
        public int? AllegroAllowedProducts { get; set; }

        
        public bool OnCeneo { get; set; }
        public bool OnGoogle { get; set; }
        public bool OnAllegro { get; set; }
    }
}
