using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

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
        [StringLength(7)]

        public string ColorHex { get; set; } = "#3d85c6";

        [Required]
        public AutomationSourceType SourceType { get; set; }

        public bool IsActive { get; set; } = false;

        [Required]
        public AutomationStrategyMode StrategyMode { get; set; } = AutomationStrategyMode.Competitiveness;

        public int? CompetitorPresetId { get; set; }

        [ForeignKey("CompetitorPresetId")]
        [ValidateNever]
        public virtual CompetitorPresetClass CompetitorPreset { get; set; }

        [ForeignKey("StoreId")]
        [ValidateNever]
        public virtual StoreClass Store { get; set; }
    }
}