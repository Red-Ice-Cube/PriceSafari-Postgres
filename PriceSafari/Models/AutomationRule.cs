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

    public enum AutomationEffectiveStatus
    {
        /// <summary>Wyłączony (IsActive = false)</summary>
        Disabled = 0,

        /// <summary>Aktywny — działa normalnie</summary>
        Active = 1,

        /// <summary>Oczekuje — zaplanowany, ale jeszcze nie rozpoczęty</summary>
        Scheduled = 2,

        /// <summary>Zakończony — przekroczono EndDate</summary>
        Expired = 3
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

        /// <summary>
        /// Czy automat ma ograniczenie czasowe (zaplanowany zakres działania).
        /// Jeśli false — automat działa normalnie wg IsActive.
        /// Jeśli true — automat działa tylko w zakresie ScheduledStartDate–ScheduledEndDate.
        /// </summary>
        public bool IsTimeLimited { get; set; } = false;

        /// <summary>
        /// Data rozpoczęcia działania automatu (opcjonalna).
        /// Jeśli podana i IsTimeLimited=true, automat nie wykona się przed tą datą.
        /// Zakres inkluzywny — w tym dniu automat już działa.
        /// </summary>
        [Column(TypeName = "date")]
        public DateTime? ScheduledStartDate { get; set; }

        /// <summary>
        /// Data zakończenia działania automatu (opcjonalna).
        /// Jeśli podana i IsTimeLimited=true, automat nie wykona się po tej dacie.
        /// Zakres inkluzywny — w tym dniu automat jeszcze działa.
        /// </summary>
        [Column(TypeName = "date")]
        public DateTime? ScheduledEndDate { get; set; }


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
        public bool EnforceMinimalMarkup { get; set; } = true;

        /// <summary>
        /// Wartość minimalnego narzutu/marży. Może być ujemna.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal MinimalMarkupValue { get; set; } = 0.00m;

        /// <summary>
        /// Czy MinimalMarginValue jest wyrażone w procentach (true) czy w walucie (false).
        /// </summary>
        public bool IsMinimalMarkupPercent { get; set; } = true;


        /// <summary>
        /// Czy blokować podwyzke ceny powyzej maxymalnego zysku/narzutu.
        /// Jeśli true, system nie ustawi ceny wyzej niż (CenaZakupu + MaxMargin).
        /// </summary>
        public bool EnforceMaxMarkup { get; set; } = false;

        /// <summary>
        /// Wartość maxymalnego narzutu. Może być tylko dodatnia.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal MaxMarkupValue { get; set; } = 100.00m;

        /// <summary>
        /// Czy MaxMarginValue jest wyrażone w procentach (true) czy w walucie (false).
        /// </summary>
        public bool IsMaxMarkupPercent { get; set; } = true;


        // =================================================================================
        // SEKCJA 2b: KROKOWE ZMIANY CEN
        // =================================================================================

        /// <summary>
        /// Czy włączyć ograniczenie maksymalnej jednorazowej obniżki ceny.
        /// Jeśli true, cena nie spadnie o więcej niż GradualDecreaseValue na jedno wykonanie.
        /// </summary>
        [Display(Name = "Krokowe obniżki cen")]
        public bool EnableGradualDecrease { get; set; } = false;

        /// <summary>
        /// Czy GradualDecreaseValue jest wyrażone w procentach (true) czy w walucie (false).
        /// </summary>
        public bool IsGradualDecreasePercent { get; set; } = true;

        /// <summary>
        /// Maksymalna jednorazowa obniżka ceny (wartość dodatnia, min 0.01).
        /// Np. 10 przy IsGradualDecreasePercent=false oznacza max -10 zł na raz.
        /// Np. 5 przy IsGradualDecreasePercent=true oznacza max -5% od aktualnej ceny na raz.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal GradualDecreaseValue { get; set; } = 10.00m;

        /// <summary>
        /// Czy włączyć ograniczenie maksymalnej jednorazowej podwyżki ceny.
        /// Jeśli true, cena nie wzrośnie o więcej niż GradualIncreaseValue na jedno wykonanie.
        /// </summary>
        [Display(Name = "Krokowe podwyżki cen")]
        public bool EnableGradualIncrease { get; set; } = false;

        /// <summary>
        /// Czy GradualIncreaseValue jest wyrażone w procentach (true) czy w walucie (false).
        /// </summary>
        public bool IsGradualIncreasePercent { get; set; } = true;

        /// <summary>
        /// Maksymalna jednorazowa podwyżka ceny (wartość dodatnia, min 0.01).
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal GradualIncreaseValue { get; set; } = 10.00m;

        [Display(Name = "Pomiń zmianę, jeśli limit narzutu uniemożliwia osiągnięcie celu")]
        public bool SkipIfMarkupLimited { get; set; } = false;

        // =================================================================================
        // SEKCJA 3: SPECYFICZNE DLA PRICE COMPARISON (Google/Ceneo)
        // =================================================================================

        /// <summary>
        /// Czy przy porównywaniu cen konkurencji uwzględniać koszty dostawy.
        /// Ma zastosowanie TYLKO gdy SourceType == PriceComparison.
        /// </summary>
        public bool UsePriceWithDelivery { get; set; } = false;


        /// <summary>
        /// Jeśli TRUE: Blokuje zmianę ceny, gdy Najlepszy Rywal jest z Ceneo, a Twój sklep nie ma oferty na Ceneo.
        /// Jeśli FALSE: Pozwala na zmianę ceny (bazując na cenie z Google/XML), ale wyświetla ostrzeżenie.
        /// </summary>
        [Display(Name = "Wymagaj obecności Twojej oferty na Ceneo")]
        public bool RequireOwnOfferOnCeneo { get; set; } = true;

        /// <summary>
        /// Jeśli TRUE: Blokuje zmianę ceny, gdy Najlepszy Rywal jest z Google, a Twój sklep nie ma oferty na Google.
        /// Jeśli FALSE: Pozwala na zmianę ceny (bazując na cenie z Ceneo/XML), ale wyświetla ostrzeżenie.
        /// </summary>
        [Display(Name = "Wymagaj obecności Twojej oferty na Google")]
        public bool RequireOwnOfferOnGoogle { get; set; } = true;

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


        /// <summary>
        /// Czy blokować obniżkę ceny, jeśli spadłaby ona poniżej progu Smart (zdefiniowanego w SkipIfValueGoBelow).
        /// </summary>
        [Display(Name = "Ochrona progu Smart!")]
        public bool BlockAtSmartValue { get; set; } = false;

        /// <summary>
        /// Wartość graniczna (np. 35.00 zł, 40.00 zł). Jeśli wyliczona cena ma spaść poniżej tej wartości
        /// (ale jest wyższa od 0), automat zablokuje zmianę lub ustawi równo tę wartość (zależnie od logiki).
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Wartość progu Smart")]
        public decimal SkipIfValueGoBelow { get; set; } = 35.00m;






        // =================================================================================
        // HELPER: Efektywny status automatu
        // =================================================================================

        /// <summary>
        /// Oblicza efektywny status automatu uwzględniając planowanie czasowe.
        /// </summary>
        [NotMapped]
        public AutomationEffectiveStatus EffectiveStatus
        {
            get
            {
                if (!IsActive)
                    return AutomationEffectiveStatus.Disabled;

                if (!IsTimeLimited)
                    return AutomationEffectiveStatus.Active;

                var today = DateTime.Today;

                // Sprawdź czy jeszcze nie rozpoczęty
                if (ScheduledStartDate.HasValue && today < ScheduledStartDate.Value.Date)
                    return AutomationEffectiveStatus.Scheduled;

                // Sprawdź czy już zakończony (EndDate jest inkluzywne, więc >)
                if (ScheduledEndDate.HasValue && today > ScheduledEndDate.Value.Date)
                    return AutomationEffectiveStatus.Expired;

                // W zakresie lub brak ograniczenia z danej strony
                return AutomationEffectiveStatus.Active;
            }
        }

        /// <summary>
        /// Czy automat powinien się wykonać w danym momencie.
        /// </summary>
        [NotMapped]
        public bool CanExecute => EffectiveStatus == AutomationEffectiveStatus.Active;
    }


}