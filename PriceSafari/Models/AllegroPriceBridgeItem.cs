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
        public decimal PriceBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? CommissionBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginAmountBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginPercentBefore { get; set; }

        public string RankingBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceAfter_Simulated { get; set; }

        //[Column(TypeName = "decimal(18,2)")]
        //public decimal? CommissionAfter_Simulated { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginAmountAfter_Simulated { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginPercentAfter_Simulated { get; set; }

        public string RankingAfter_Simulated { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PriceAfter_Verified { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? CommissionAfter_Verified { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginAmountAfter_Verified { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MarginPercentAfter_Verified { get; set; }
    }
}