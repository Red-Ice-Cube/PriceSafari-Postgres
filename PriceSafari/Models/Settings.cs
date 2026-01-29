using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class Settings
    {
        [Key]
        public int SettingsId { get; set; }

 
        public bool VerificationRequired { get; set; } = true;

        public int Semophore { get; set; } = 1;
        public int SemophoreGoogle { get; set; } = 1;
        public int WarmUpTime { get; set; } = 30;
        public bool HeadLess { get; set; } = false;
        public bool JavaScript { get; set; } = false;
        public bool Styles { get; set; } = false;


        public bool GetCeneoName { get; set; } = false;

        public bool ControlXY { get; set; } = false;


        
        public bool ExpandAndCompareGoogleOffers { get; set; } = true;

        //nowo dodane 
        public bool HeadLessForGoogleGenerators { get; set; } = false;
        public int GoogleGeneratorsCount { get; set; } = 1;


        public Settings()
        {

            VerificationRequired = true;
        }
    }
}