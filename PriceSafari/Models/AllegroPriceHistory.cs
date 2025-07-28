using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class AllegroPriceHistory
    {
        [Key]
        public int Id { get; set; }

        public int AllegroProductId { get; set; }

        [ForeignKey("AllegroProductId")]
        public virtual AllegroProductClass AllegroProduct { get; set; }

        public int AllegroScrapeHistoryId { get; set; }

        [ForeignKey("AllegroScrapeHistoryId")]
        public virtual AllegroScrapeHistory AllegroScrapeHistory { get; set; }

        [Required]
        public string SellerName { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal Price { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? DeliveryCost { get; set; }

        public int? DeliveryTime { get; set; }

        public int? Popularity { get; set; }

        public bool SuperSeller { get; set; }
        public bool Smart { get; set; }

        
        public bool IsBestPriceGuarantee { get; set; }
        public bool TopOffer { get; set; }



        //nowe miejsca
        public bool SuperPrice { get; set; }
        public bool Promoted { get; set; }
        public bool Sponsored { get; set; }
    }
}