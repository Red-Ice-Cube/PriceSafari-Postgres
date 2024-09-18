using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class GoogleScrapingProduct
    {
        [Key]
        public int ScrapingProductId { get; set; }

        public List<int> ProductIds { get; set; } = new List<int>(); // Lista ProductIds zamiast pojedynczego ProductId
        public string GoogleUrl { get; set; }
        public int RegionId { get; set; }
        public bool? IsScraped { get; set; }
        public int OffersCount { get; set; }
   
    }
}
