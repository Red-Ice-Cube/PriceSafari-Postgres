using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace PriceSafari.IntervalPriceChanger.Models
{
    /// <summary>
    /// Log jednego wykonania interwału cenowego.
    /// Jeden rekord = "O godzinie 14:20 interwał X przetworzył Y produktów".
    /// </summary>
    public class IntervalPriceExecutionBatch
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int IntervalPriceRuleId { get; set; }

        [ForeignKey("IntervalPriceRuleId")]
        [ValidateNever]
        public virtual IntervalPriceRule IntervalRule { get; set; }

        public int StoreId { get; set; }

        public DateTime ExecutionDate { get; set; } = DateTime.Now;
        public DateTime? EndDate { get; set; }

        /// <summary>Indeks slotu harmonogramu (0-143).</summary>
        public int SlotIndex { get; set; }

        /// <summary>Dzień tygodnia (0=Pn, 6=Nd).</summary>
        public int DayIndex { get; set; }

        // ═══ STATYSTYKI ═══
        public int TotalProductsInInterval { get; set; }
        public int SuccessCount { get; set; }
        public int BlockedCount { get; set; }
        public int SkippedCollisionCount { get; set; }
        public int FailedCount { get; set; }
        public int LimitReachedCount { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal PriceStepApplied { get; set; }
        public bool IsPriceStepPercent { get; set; }

        [StringLength(1)]
        public string StepLetter { get; set; } = "A";

        [StringLength(2000)]
        public string? Comment { get; set; }

        [StringLength(100)]
        public string DeviceName { get; set; }


        // ═══ NAWIGACJA ═══
        [ValidateNever]
        public virtual ICollection<IntervalPriceExecutionItem> Items { get; set; }
    }
}