using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace PriceSafari.IntervalPriceChanger.Models
{
    /// <summary>
    /// Szczegóły wykonania interwału dla jednego produktu.
    /// </summary>
    public class IntervalPriceExecutionItem
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BatchId { get; set; }

        [ForeignKey("BatchId")]
        [ValidateNever]
        public virtual IntervalPriceExecutionBatch Batch { get; set; }

        // ═══ PRODUKT ═══
        public int? ProductId { get; set; }
        public int? AllegroProductId { get; set; }

        [StringLength(50)]
        public string AllegroOfferId { get; set; }

        // ═══ CENY — PRZED ZMIANĄ ═══

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PriceBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? CommissionBefore { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PurchasePrice { get; set; }

        // ═══ CENY — PO ZMIANIE ═══

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PriceAfterTarget { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PriceAfterVerified { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? CommissionAfterVerified { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal? PriceChange { get; set; }

        // ═══ KAMPANIE / DOPŁATY (snapshot) ═══
        public bool IsInCampaign { get; set; }
        public bool IsSubsidyActive { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? CustomerVisiblePrice { get; set; }

        // ═══ LIMITY ═══

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MinPriceLimit { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? MaxPriceLimit { get; set; }

        public bool WasLimitedByMin { get; set; }
        public bool WasLimitedByMax { get; set; }

        // ═══ STATUS ═══
        public bool Success { get; set; }
        public IntervalExecutionItemStatus Status { get; set; } = IntervalExecutionItemStatus.Pending;

        [StringLength(200)]
        public string StatusReason { get; set; }
    }

    public enum IntervalExecutionItemStatus
    {
        Pending,
        Success,
        SuccessLimitedMin,
        SuccessLimitedMax,
        BlockedNoPurchasePrice,
        BlockedNoMinLimit,
        BlockedLimitReached,
        BlockedNoPriceData,
        BlockedMinMaxConflict,
        SkippedCollision,
        FailedApi,
        FailedAuth,
        NoChangeNeeded
    }
}