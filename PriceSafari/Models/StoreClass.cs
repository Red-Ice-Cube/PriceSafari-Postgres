using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class StoreClass
    {
        [Key]
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public string? StoreProfile { get; set; }
        public string? StoreApiUrl { get; set; }
        public string? StoreApiKey { get; set; }
        public string? StoreLogoUrl { get; set; }

        public string? ProductMapXmlUrl { get; set; }
        public int? ProductsToScrap { get; set; }

        public ICollection<ScrapHistoryClass> ScrapHistories { get; set; } = new List<ScrapHistoryClass>();
        public ICollection<ProductClass> Products { get; set; } = new List<ProductClass>();
        public ICollection<CategoryClass> Categories { get; set; } = new List<CategoryClass>();
        public ICollection<PriceValueClass> PriceValues { get; set; } = new List<PriceValueClass>();
        public ICollection<FlagsClass> Flags { get; set; } = new List<FlagsClass>();
        public ICollection<PriceSafariUserStore> UserStores { get; set; } = new List<PriceSafariUserStore>();
        public ICollection<PriceSafariReport> PriceSafariReports { get; set; }

    }
}
