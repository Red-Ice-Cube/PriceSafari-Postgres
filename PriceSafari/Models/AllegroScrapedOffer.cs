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

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? DeliveryCost { get; set; }

        public int? DeliveryTime { get; set; }

        public int? Popularity { get; set; }

    
        public bool SuperSeller { get; set; }
        public bool Smart { get; set; }


    

        public bool IsBestPriceGuarantee { get; set; }
        public bool TopOffer { get; set; }

        // nowe miejsca 


        public bool SuperPrice { get; set; }
        public bool Promoted { get; set; }
        public bool Sponsored { get; set; }
        // koniec nowych miejsc

        public int AllegroOfferToScrapeId { get; set; }

        [ForeignKey("AllegroOfferToScrapeId")]
        public virtual AllegroOfferToScrape AllegroOfferToScrape { get; set; }
    }
}