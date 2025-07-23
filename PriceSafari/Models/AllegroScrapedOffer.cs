// Plik: Models/AllegroScrapedOffer.cs

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class AllegroScrapedOffer
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string SellerName { get; set; }

        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        public decimal Price { get; set; }

        // --- NOWE POLA ---
        [Column(TypeName = "decimal(18, 2)")]
        public decimal? DeliveryCost { get; set; } // Może być null

        public int? DeliveryTime { get; set; } // Może być null

        public int? Popularity { get; set; } // Może być null
        // ------------------

        public int AllegroOfferToScrapeId { get; set; }

        [ForeignKey("AllegroOfferToScrapeId")]
        public virtual AllegroOfferToScrape AllegroOfferToScrape { get; set; }
    }
}