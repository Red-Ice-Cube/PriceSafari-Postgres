using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class PriceValueClass
    {
        public int PriceValueClassId { get; set; }
        public int StoreId { get; set; }
        public decimal SetPrice1 { get; set; } = 2.00m;
        public decimal SetPrice2 { get; set; } = 2.00m;
       
        public bool UsePriceDiff { get; set; } = true;

        public decimal PriceStep { get; set; } = -0.01m;

        public decimal SetSafariPrice1 { get; set; } = 2.00m;
        public decimal SetSafariPrice2 { get; set; } = 2.00m;
        public bool UsePriceDiffSafari { get; set; } = true;

        public decimal PriceIndexTargetPercent { get; set; } = 100.00m;

        public bool UsePriceWithDelivery { get; set; } = false;

        public string IdentifierForSimulation { get; set; } = "EAN";
        public bool UseMarginForSimulation { get; set; } = true;

        public bool EnforceMinimalMargin { get; set; } = true;
        public decimal MinimalMarginPercent { get; set; } = 0.00m;







        // sekcja allegro


        public bool AllegroUsePriceDiff { get; set; } = true;
        public decimal AllegroPriceStep { get; set; } = -0.01m;
        public decimal AllegroSetPrice1 { get; set; } = 2.00m;
        public decimal AllegroSetPrice2 { get; set; } = 2.00m;
        public decimal AllegroPriceIndexTargetPercent { get; set; } = 100.00m;




        public string AllegroIdentifierForSimulation { get; set; } = "ID";
        public bool AllegroUseMarginForSimulation { get; set; } = true;

        public bool AllegroEnforceMinimalMargin { get; set; } = true;
        public decimal AllegroMinimalMarginPercent { get; set; } = 0.00m;

        public bool AllegroIncludeCommisionInPriceChange { get; set; } = false;


        public bool AllegroChangePriceForBagdeSuperPrice { get; set; } = false;
        public bool AllegroChangePriceForBagdeTopOffer { get; set; } = false;
        public bool AllegroChangePriceForBagdeBestPriceGuarantee { get; set; } = false;
        public bool AllegroChangePriceForBagdeInCampaign { get; set; } = false;

        public StoreClass Store { get; set; }




        // =====================================================================
        // === USTAWIENIA WIDOKU PRODUCENTA (gdy Store.IsProducer == true) ===
        // =====================================================================

        [Display(Name = "Źródło ceny referencyjnej (Sklep / MAP)")]
        public ProducerComparisonSource ProducerComparisonSource { get; set; } = ProducerComparisonSource.MapPrice;

        [Display(Name = "Progi w kwocie zamiast w procentach")]
        public bool ProducerUseAmount { get; set; } = false;

        // --- Progi procentowe (% różnicy: marketPrice vs referencePrice) ---
        // Wartości DODATNIE - kierunek (poniżej/powyżej) wynika z nazwy

        [Display(Name = "Próg ciemnoczerwony - bardzo poniżej (%)")]
        public decimal ProducerThresholdRedDarkPercent { get; set; } = 20.00m;

        [Display(Name = "Próg czerwony - poniżej (%)")]
        public decimal ProducerThresholdRedPercent { get; set; } = 10.00m;

        [Display(Name = "Próg pomarańczowo-czerwony - lekko poniżej (%)")]
        public decimal ProducerThresholdRedLightPercent { get; set; } = 1.00m;

        [Display(Name = "Próg jasnozielony - lekko powyżej (%)")]
        public decimal ProducerThresholdGreenLightPercent { get; set; } = 1.00m;

        [Display(Name = "Próg zielony - powyżej (%)")]
        public decimal ProducerThresholdGreenPercent { get; set; } = 10.00m;

        [Display(Name = "Próg ciemnozielony - bardzo powyżej (%)")]
        public decimal ProducerThresholdGreenDarkPercent { get; set; } = 20.00m;

        // --- Progi kwotowe (PLN różnicy) ---

        [Display(Name = "Próg ciemnoczerwony - bardzo poniżej (PLN)")]
        public decimal ProducerThresholdRedDarkAmount { get; set; } = 50.00m;

        [Display(Name = "Próg czerwony - poniżej (PLN)")]
        public decimal ProducerThresholdRedAmount { get; set; } = 20.00m;

        [Display(Name = "Próg pomarańczowo-czerwony - lekko poniżej (PLN)")]
        public decimal ProducerThresholdRedLightAmount { get; set; } = 5.00m;

        [Display(Name = "Próg jasnozielony - lekko powyżej (PLN)")]
        public decimal ProducerThresholdGreenLightAmount { get; set; } = 5.00m;

        [Display(Name = "Próg zielony - powyżej (PLN)")]
        public decimal ProducerThresholdGreenAmount { get; set; } = 20.00m;

        [Display(Name = "Próg ciemnozielony - bardzo powyżej (PLN)")]
        public decimal ProducerThresholdGreenDarkAmount { get; set; } = 50.00m;
    }
}
