namespace PriceSafari.Models.ViewModels
{
    public class ProducerSettingsViewModel
    {
        public int StoreId { get; set; }
        public ProducerComparisonSource ProducerComparisonSource { get; set; } = ProducerComparisonSource.MapPrice;
        public bool ProducerUseAmount { get; set; } = false;

        // Procenty
        public decimal ProducerThresholdRedDarkPercent { get; set; }
        public decimal ProducerThresholdRedPercent { get; set; }
        public decimal ProducerThresholdRedLightPercent { get; set; }
        public decimal ProducerThresholdGreenLightPercent { get; set; }
        public decimal ProducerThresholdGreenPercent { get; set; }
        public decimal ProducerThresholdGreenDarkPercent { get; set; }

        // Kwoty
        public decimal ProducerThresholdRedDarkAmount { get; set; }
        public decimal ProducerThresholdRedAmount { get; set; }
        public decimal ProducerThresholdRedLightAmount { get; set; }
        public decimal ProducerThresholdGreenLightAmount { get; set; }
        public decimal ProducerThresholdGreenAmount { get; set; }
        public decimal ProducerThresholdGreenDarkAmount { get; set; }

        // Identyfikator (wspólne z marginSettings - producent też potrzebuje wybrać czy EAN/ID/SKU)
        public string IdentifierForSimulation { get; set; } = "EAN";
    }
}