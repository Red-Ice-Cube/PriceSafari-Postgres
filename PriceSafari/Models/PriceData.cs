using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class PriceData
    {
        [Key]
        public int PriceDataId { get; set; }

        public int ScrapingProductId { get; set; }

        [ForeignKey("ScrapingProductId")]
        public GoogleScrapingProduct ScrapingProduct { get; set; }  // Relacja nawigacyjna do GoogleScrapingProduct

        public decimal Price { get; set; }
        public decimal PriceWithDelivery { get; set; }
        public string StoreName { get; set; }
        public string OfferUrl { get; set; }
        public int RegionId { get; set; }
    }
}
