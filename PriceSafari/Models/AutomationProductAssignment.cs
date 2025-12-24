using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class AutomationProductAssignment
    {
        [Key]
        public int Id { get; set; }

        // ==============================================================================
        // RELACJA DO REGUŁY (Rodzic)
        // ==============================================================================
        [Required]
        public int AutomationRuleId { get; set; }

        [ForeignKey("AutomationRuleId")]
        public virtual AutomationRule AutomationRule { get; set; }

        // ==============================================================================
        // RELACJA DO PRODUKTU (Może być ProductClass LUB AllegroProductClass)
        // ==============================================================================

        // Opcja 1: Produkt ze sklepu (Ceneo/Google)
        public int? ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual ProductClass Product { get; set; }

        // Opcja 2: Produkt z Marketplace (Allegro)
        public int? AllegroProductId { get; set; }

        [ForeignKey("AllegroProductId")]
        public virtual AllegroProductClass AllegroProduct { get; set; }

        // ==============================================================================
        // METADANE
        // ==============================================================================
        public DateTime AssignedDate { get; set; } = DateTime.UtcNow;

        // Tutaj w przyszłości dodamy pola do symulacji, np.:
        // public decimal? LastSimulatedPrice { get; set; }
        // public DateTime? LastSimulationDate { get; set; }
    }
}