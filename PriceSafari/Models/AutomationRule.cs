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

        // =================================================================================
        // SEKCJA 1: USTAWIENIA STRATEGII (Zależne od StrategyMode)
        // =================================================================================

        /// <summary>
        /// Krok cenowy (np. -0.01 zł przebicia).
        /// Ma zastosowanie TYLKO gdy StrategyMode == Competitiveness (Lider Rynku).
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceStep { get; set; } = -0.01m;

        /// <summary>
        /// Określa, czy PriceStep jest wartością procentową (true) czy kwotową (false).
        /// Ma zastosowanie TYLKO gdy StrategyMode == Competitiveness.
        /// (Dawniej: UsePriceDiff)
        /// </summary>
        public bool IsPriceStepPercent { get; set; } = false;

        /// <summary>
        /// Docelowy Index Cenowy (np. 100% = średnia rynku, 95% = 5% taniej niż rynek).
        /// Ma zastosowanie TYLKO gdy StrategyMode == Profit (Rentowność).
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal PriceIndexTargetPercent { get; set; } = 100.00m;

        // =================================================================================
        // SEKCJA 2: USTAWIENIA MARŻY I KOSZTÓW (Wspólne dla obu źródeł)
        // =================================================================================

        /// <summary>
        /// Czy uwzględniać cenę zakupu (czy brać pod uwagę tylko produkty posiadające cenę zakupu).
        /// (Dawniej: UseMarginForSimulation)
        /// </summary>
        public bool UsePurchasePrice { get; set; } = true;

        /// <summary>
        /// Czy blokować obniżkę ceny poniżej minimalnego zysku/narzutu.
        /// Jeśli true, system nie ustawi ceny niższej niż (CenaZakupu + MinimalMargin).
        /// </summary>
        public bool EnforceMinimalMargin { get; set; } = true;

        /// <summary>
        /// Wartość minimalnego narzutu/marży. Może być ujemna.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal MinimalMarginValue { get; set; } = 0.00m;

        /// <summary>
        /// Czy MinimalMarginValue jest wyrażone w procentach (true) czy w walucie (false).
        /// </summary>
        public bool IsMinimalMarginPercent { get; set; } = true;

        // =================================================================================
        // SEKCJA 3: SPECYFICZNE DLA PRICE COMPARISON (Google/Ceneo)
        // =================================================================================

        /// <summary>
        /// Czy przy porównywaniu cen konkurencji uwzględniać koszty dostawy.
        /// Ma zastosowanie TYLKO gdy SourceType == PriceComparison.
        /// </summary>
        public bool UsePriceWithDelivery { get; set; } = false;

        // =================================================================================
        // SEKCJA 4: SPECYFICZNE DLA MARKETPLACE (Allegro)
        // =================================================================================

        /// <summary>
        /// Czy doliczać prowizję Allegro do ceny końcowej przy zmianie.
        /// Ma zastosowanie TYLKO gdy SourceType == Marketplace.
        /// (Dawniej: AllegroIncludeCommisionInPriceChange)
        /// </summary>
        public bool MarketplaceIncludeCommission { get; set; } = false;

        /// <summary>
        /// Czy zmieniać cenę dla ofert z odznaką Super Sprzedawca / Super Cena.
        /// </summary>
        public bool MarketplaceChangePriceForBadgeSuperPrice { get; set; } = false;

        /// <summary>
        /// Czy zmieniać cenę dla ofert z odznaką Top Oferta.
        /// </summary>
        public bool MarketplaceChangePriceForBadgeTopOffer { get; set; } = false;

        /// <summary>
        /// Czy zmieniać cenę dla ofert z Gwarancją Najniższej Ceny.
        /// </summary>
        public bool MarketplaceChangePriceForBadgeBestPriceGuarantee { get; set; } = false;

        /// <summary>
        /// Czy zmieniać cenę dla ofert biorących udział w kampaniach/akcjach promocyjnych.
        /// </summary>
        public bool MarketplaceChangePriceForBadgeInCampaign { get; set; } = false;
    }
}