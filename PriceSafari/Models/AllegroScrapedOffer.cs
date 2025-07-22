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
        [Column(TypeName = "decimal(18, 2)")] // Poprawny typ dla waluty w bazie danych
        public decimal Price { get; set; }

        // Klucz obcy do tabeli AllegroOfferToScrape
        public int AllegroOfferToScrapeId { get; set; }

        // Właściwość nawigacyjna
        [ForeignKey("AllegroOfferToScrapeId")]
        public virtual AllegroOfferToScrape AllegroOfferToScrape { get; set; }
    }
}