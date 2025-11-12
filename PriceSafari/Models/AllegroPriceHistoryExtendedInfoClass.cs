using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class AllegroPriceHistoryExtendedInfoClass
    {

        [Key]
        public int Id { get; set; }

        public int AllegroProductId { get; set; }
        public AllegroProductClass AllegroProduct { get; set; }

        public int ScrapHistoryId { get; set; }
        public AllegroScrapeHistory ScrapHistory { get; set; }



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

    }
}
