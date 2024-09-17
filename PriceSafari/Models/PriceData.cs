using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class PriceData
    {
        [Key]
        public int PriceDataId { get; set; }

        // Powiązanie z GoogleScrapingProduct
        public int ScrapingProductId { get; set; }

        // Cena produktu
        public decimal Price { get; set; }

        // Cena z dostawą (opcjonalne)
        public decimal PriceWithDelivery { get; set; }

        // Nazwa sklepu
        public string StoreName { get; set; }

        // URL do oferty
        public string OfferUrl { get; set; }

        // Region, w którym cena była sprawdzana
        public int RegionId { get; set; }
    }
}
