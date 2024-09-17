using PriceSafari.Services;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class PriceData
    {
        [Key]
        public int PriceDataId { get; set; }

        // Powiązanie z produktem
        [ForeignKey("ProductClass")]
        public int ProductId { get; set; }
        public ProductClass Product { get; set; }

        // Cena produktu w danym regionie
        public decimal Price { get; set; }

        // Powiązanie z regionem
        [ForeignKey("Region")]
        public int RegionId { get; set; }
        public Region Region { get; set; }

        // Powiązanie ze scrapowaniem
        [ForeignKey("ScrapeRun")]
        public int ScrapeRunId { get; set; }
        public ScrapeRun ScrapeRun { get; set; }

        public string StoreName { get; set; }
        public string OfferUrl { get; set; }
    }
}
