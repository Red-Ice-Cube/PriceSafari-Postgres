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

        public bool Success { get; set; } = true; // W tym modelu eksport zawsze zakłada sukces
    }
}
