using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
 
    public class AllegroPriceHistory
    {
        [Key]
        public int Id { get; set; }

        // Klucz obcy do produktu, którego dotyczy ta cena
        public int AllegroProductId { get; set; }

        [ForeignKey("AllegroProductId")]
        public virtual AllegroProductClass AllegroProduct { get; set; }

        // Klucz obcy do sesji, w której ta cena została zapisana
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
    }
}