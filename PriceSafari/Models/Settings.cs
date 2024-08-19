using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class Settings
    {
        [Key]
        public int SettingsId { get; set; }

        public string? ContactEmail { get; set; }
        public string? ContactNumber { get; set; }

        public bool VerificationRequired { get; set; } = true;

        public int CaptchaSpeed { get; set; } = 3;
        public int ScrapSemaphoreSlim { get; set; } = 3;



        public Settings()
        {

            VerificationRequired = true;
        }
    }
}