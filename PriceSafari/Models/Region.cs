using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class Region
    {
        [Key]
        public int RegionId { get; set; }

        [Required(ErrorMessage = "Region name is required.")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Currency is required.")]
        public string Currency { get; set; }

        [Column(TypeName = "decimal(18, 4)")]
        public decimal CurrencyValue { get; set; }

        // Nowe pola dla lokalizacji i języka
        public string CountryCode { get; set; } // np. 'pl', 'de', 'sk'
        public string LanguageCode { get; set; } // np. 'pl', 'de', 'sk'

        // Nawigacja do PriceData
        public ICollection<PriceData> PriceData { get; set; } = new List<PriceData>();

        public ICollection<GoogleScrapingProduct> GoogleScrapingProducts { get; set; } = new List<GoogleScrapingProduct>();
        public ICollection<GlobalPriceReport> GlobalPriceReports { get; set; } = new List<GlobalPriceReport>();
    }
}
