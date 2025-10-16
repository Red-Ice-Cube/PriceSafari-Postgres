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

        // nasze dodatkowe dane. 

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? ApiAllegroPrice { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? ApiAllegroPriceFromUser { get; set; }


        [Column(TypeName = "decimal(18, 2)")]
        public decimal? ApiAllegroCommission { get; set; }

        public bool? AnyPromoActive { get; set; }

    }
}
