using Schema.NET;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class PriceBridgeItem
    {
        [Key]
        public int Id { get; set; }

        public int PriceBridgeBatchId { get; set; }
        [ForeignKey("PriceBridgeBatchId")]
        public virtual PriceBridgeBatch Batch { get; set; }

        public int ProductId { get; set; }
        [ForeignKey("ProductId")]
        // ZMIANA: Product -> ProductClass
        public virtual ProductClass Product { get; set; }

        // Dane historyczne w momencie zmiany
        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceAfter { get; set; } // Nowa cena

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginPrice { get; set; } // Cena zakupu w momencie zmiany

        // Rankingi przed zmianą (informacyjnie)
        public string? RankingGoogleBefore { get; set; }
        public string? RankingCeneoBefore { get; set; }

        // Symulowane rankingi po zmianie
        public string? RankingGoogleAfterSimulated { get; set; }
        public string? RankingCeneoAfterSimulated { get; set; }

        public string? Mode { get; set; }
        public decimal? PriceIndexTarget { get; set; }
        public decimal? StepPriceApplied { get; set; }

        public bool Success { get; set; } = true; // W tym modelu eksport zawsze zakłada sukces


        public decimal? MinPriceLimit { get; set; }      // Wyliczony limit MIN (kwota)
        public decimal? MaxPriceLimit { get; set; }      // Wyliczony limit MAX (kwota)
        public bool? WasLimitedByMin { get; set; }        // Czy cena uderzyła w podłogę?
        public bool? WasLimitedByMax { get; set; }        // Czy cena uderzyła w sufit?
    }
}
