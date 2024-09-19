using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class GoogleScrapingProduct
    {
        [Key]
        public int ScrapingProductId { get; set; }

        public List<int> ProductIds { get; set; } = new List<int>();

        public string GoogleUrl { get; set; }
        public int RegionId { get; set; }
        public bool? IsScraped { get; set; }
        public int OffersCount { get; set; }
        public int PriceSafariRaportId { get; set; }

        // Nawigacja do klasy ProductClass
        public ICollection<ProductClass> Products { get; set; } = new List<ProductClass>();

        // Nawigacja do PriceData
        public ICollection<PriceData> PriceData { get; set; } = new List<PriceData>();  // Dodanie relacji nawigacyjnej do PriceData
    }
}
