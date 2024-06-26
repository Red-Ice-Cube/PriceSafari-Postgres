using System.ComponentModel.DataAnnotations;

namespace PriceTracker.Models
{
    public class Settings
    {
        [Key]
        public int SettingsId { get; set; }

        public decimal MinimumPayout { get; set; }
        public int TTL { get; set; }
        public int OrderPerClick { get; set; }
        public string? ContactEmail { get; set; }
        public string? ContactNumber { get; set; }

        public bool VerificationRequired { get; set; } = false;

        public bool CollectFingerPrint { get; set; } = false;

        public bool UseEanForTracking { get; set; } = false;

        public int OrdersProcessIntervalInSeconds { get; set; } = 60;

        public Settings()
        {
            MinimumPayout = 100;
            TTL = 30;
            OrderPerClick = 1;
            VerificationRequired = false;
            CollectFingerPrint = false;
            OrdersProcessIntervalInSeconds = 60;
        }
    }
}