using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class AllegroOfferToScrape
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string AllegroOfferUrl { get; set; }

        //nowa warttosc
        public long AllegroOfferId { get; set; }

        public int StoreId { get; set; }
        [ForeignKey("StoreId")]
        public virtual StoreClass Store { get; set; }

        public List<int> AllegroProductIds { get; set; } = new List<int>();

        public bool IsScraped { get; set; } = false;

        public bool IsRejected { get; set; } = false;

        public DateTime AddedDate { get; set; } = DateTime.UtcNow;

        public int CollectedPricesCount { get; set; } = 0;

   
        public bool IsProcessing { get; set; } = false;


        

        public bool? IsApiProcessed { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? ApiAllegroPrice { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? ApiAllegroPriceFromUser { get; set; }


        [Column(TypeName = "decimal(18, 2)")]
        public decimal? ApiAllegroCommission { get; set; }

        public bool? AnyPromoActive { get; set; }

        public bool? IsSubsidyActive { get; set; }

        // true, jeśli jest to aktywne ZAPROSZENIE do kampanii z dopłatą
        public bool? IsInvitationActive { get; set; }

        // Cena z zaproszenia, jeśli istnieje
        [Column(TypeName = "decimal(18, 2)")]
        public decimal? InvitationPrice { get; set; }
        public string? AllegroEan { get; set; }


        public virtual ICollection<AllegroScrapedOffer> ScrapedOffers { get; set; } = new List<AllegroScrapedOffer>();
    }
}