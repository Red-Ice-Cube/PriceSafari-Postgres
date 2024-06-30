using System.ComponentModel.DataAnnotations;

namespace PriceTracker.Models
{
    public class Settings
    {
        [Key]
        public int SettingsId { get; set; }

        public string? ContactEmail { get; set; }
        public string? ContactNumber { get; set; }

        public bool VerificationRequired { get; set; } = false;



        public Settings()
        {

            VerificationRequired = false;
        }
    }
}