using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{

    public class AllegroPriceBridgeItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int AllegroPriceBridgeBatchId { get; set; }
        [ForeignKey("AllegroPriceBridgeBatchId")]
        public virtual AllegroPriceBridgeBatch PriceBridgeBatch { get; set; }

        [Required]
        public int AllegroProductId { get; set; }
        [ForeignKey("AllegroProductId")]
        public virtual AllegroProductClass AllegroProduct { get; set; }

        [Required]
        public string AllegroOfferId { get; set; }

        public bool Success { get; set; }

        public string ErrorMessage { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginPrice { get; set; }

        public bool IncludeCommissionInMargin { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? CommissionBefore { get; set; }

        public string RankingBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceAfter_Simulated { get; set; }

        public string RankingAfter_Simulated { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PriceAfter_Verified { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? CommissionAfter_Verified { get; set; }

        public string? Mode { get; set; } // "profit" lub "competitiveness"

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PriceIndexTarget { get; set; } // np. 100.00, 95.00

        [Column(TypeName = "decimal(18,2)")]
        public decimal? StepPriceApplied { get; set; }
    }
}