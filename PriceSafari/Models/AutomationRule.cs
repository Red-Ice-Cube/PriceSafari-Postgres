using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public enum AutomationSourceType
    {
        [Display(Name = "Porównywarki Cen (Google/Ceneo)")]
        PriceComparison = 0,

        [Display(Name = "Marketplace (Allegro)")]
        Marketplace = 1
    }

    public enum AutomationStrategyMode
    {
        [Display(Name = "Lider Rynku")]
        Competitiveness = 0,

        [Display(Name = "Rentowność")]
        Profit = 1
    }

    public class AutomationRule
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int StoreId { get; set; }

        [Required(ErrorMessage = "Nazwa jest wymagana")]
        [StringLength(100, ErrorMessage = "Nazwa nie może być dłuższa niż 100 znaków")]
        public string Name { get; set; }

        [Required]
        [StringLength(7)] // Format np. #FF0000
        public string ColorHex { get; set; } = "#3d85c6"; // Domyślny kolor

        [Required]
        public AutomationSourceType SourceType { get; set; }

        // Możemy tu dodać pole IsActive, żeby łatwo wyłączać regułę bez usuwania
        public bool IsActive { get; set; } = false;

        [Required]
        public AutomationStrategyMode StrategyMode { get; set; } = AutomationStrategyMode.Competitiveness;

        // ID wybranego presetu konkurencji. Null oznacza "Domyślny / Wszystkie".
        public int? CompetitorPresetId { get; set; }

        // Relacja do presetu (opcjonalna, jeśli chcesz mieć integrity check w bazie)
        [ForeignKey("CompetitorPresetId")]
        public virtual CompetitorPresetClass CompetitorPreset { get; set; }

        // Relacja do sklepu
        [ForeignKey("StoreId")]
        public virtual StoreClass Store { get; set; }
    }
}