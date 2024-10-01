using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class GoogleScrapingProduct
    {
        [Key]
        public int ScrapingProductId { get; set; }

        public List<int> ProductIds { get; set; } = new List<int>();

        public string GoogleUrl { get; set; }

        public int RegionId { get; set; }  // Klucz obcy dla Region

        [ForeignKey("RegionId")]
        public Region Region { get; set; }  // Nawigacja do Region

 
        public string CountryCode { get; set; } // Nowe pole

        public bool? IsScraped { get; set; }

        public int OffersCount { get; set; }

        public int PriceSafariRaportId { get; set; }

        public ICollection<ProductClass> Products { get; set; } = new List<ProductClass>();

        // Nawigacja do PriceData
        public ICollection<PriceData> PriceData { get; set; } = new List<PriceData>();
    }




}
