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

        public virtual ProductClass Product { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceAfter { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginPrice { get; set; }

        public string? RankingGoogleBefore { get; set; }
        public string? RankingCeneoBefore { get; set; }

        public string? RankingGoogleAfterSimulated { get; set; }
        public string? RankingCeneoAfterSimulated { get; set; }

        public string? Mode { get; set; }
        public decimal? PriceIndexTarget { get; set; }
        public decimal? StepPriceApplied { get; set; }

        public bool Success { get; set; } = true;

        public decimal? MinPriceLimit { get; set; }

        public decimal? MaxPriceLimit { get; set; }

        public bool? WasLimitedByMin { get; set; }

        public bool? WasLimitedByMax { get; set; }

    }
}

