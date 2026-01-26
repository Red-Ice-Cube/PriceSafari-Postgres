namespace PriceSafari.Models
{
    public class DeviceStatus
    {
        public int Id { get; set; }

        public string DeviceName { get; set; }

        public bool IsOnline { get; set; }

        public DateTime LastCheck { get; set; }

        
        public bool UrlScalEnabled { get; set; }
        public bool GooCrawEnabled { get; set; }
        public bool CenCrawEnabled { get; set; }
        public bool BaseScalEnabled { get; set; }
        public bool ApiBotEnabled { get; set; }

        public bool AleBaseScalEnabled { get; set; }
        public bool UrlScalAleEnabled { get; set; }
        public bool AleCrawEnabled { get; set; }
        public bool AleApiBotEnabled { get; set; }

        public bool InvoiceGeneratorEnabled { get; set; }
        public bool PaymentProcessorEnabled { get; set; }
        public bool EmailSenderEnabled { get; set; }

        public bool MarketPlaceAutomationEnabled { get; set; }
        public bool PriceComparisonAutomationEnabled { get; set; }
    }
}