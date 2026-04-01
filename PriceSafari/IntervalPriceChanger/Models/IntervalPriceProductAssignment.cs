using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using PriceSafari.Models;

namespace PriceSafari.IntervalPriceChanger.Models
{
    /// <summary>
    /// Przypisanie produktu do interwału cenowego.
    /// 
    /// ZASADY:
    /// - Produkt MUSI być przypisany do automatu-rodzica (AutomationProductAssignment)
    /// - Produkt może być w JEDNYM interwale naraz (unique index na ProductId/AllegroProductId)
    /// - Ustawienia cenowe (min/max/prowizja) dziedziczone z automatu-rodzica
    /// </summary>
    public class IntervalPriceProductAssignment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int IntervalPriceRuleId { get; set; }

        /// <summary>
        /// ID produktu (gdy SourceType automatu-rodzica == PriceComparison).
        /// </summary>
        public int? ProductId { get; set; }

        /// <summary>
        /// ID produktu Allegro (gdy SourceType automatu-rodzica == Marketplace).
        /// </summary>
        public int? AllegroProductId { get; set; }

        public DateTime AssignedDate { get; set; } = DateTime.UtcNow;

        // ═══════════════════════════════════════════════════════
        // NAWIGACJA
        // ═══════════════════════════════════════════════════════

        [ForeignKey("IntervalPriceRuleId")]
        [ValidateNever]
        public virtual IntervalPriceRule Rule { get; set; }

        [ForeignKey("ProductId")]
        [ValidateNever]
        public virtual ProductClass Product { get; set; }

        [ForeignKey("AllegroProductId")]
        [ValidateNever]
        public virtual AllegroProductClass AllegroProduct { get; set; }
    }
}