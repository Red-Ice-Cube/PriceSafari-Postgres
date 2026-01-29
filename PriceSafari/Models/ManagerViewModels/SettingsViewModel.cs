namespace PriceSafari.Models.ManagerViewModels
{
    public class SettingsViewModel
    {
        public bool VerificationRequired { get; set; }

        public int Semophore { get; set; }
        public int SemophoreGoogle { get; set; }
        public int WarmUp { get; set; }
        public bool Headless { get; set; }
        public bool GetCeneoName { get; set; }  
        public bool JS { get; set; }
        public bool Style { get; set; }

        public bool ControlXY { get; set; }
        public bool ExpandAndCompareGoogleOffers { get; set; } = true;

        public bool HeadLessForGoogleGenerators { get; set; }
        public int GoogleGeneratorsCount { get; set; }
    }

    public class EditVerificationRequiredViewModel
    {
        public bool VerificationRequired { get; set; }
    }


    public class EditSpeedSettingsViewModel
    {
        public int Semophore { get; set; }
        public int SemophoreGoogle { get; set; }
        public int WarmUp { get; set; }

        public bool Headless { get; set; }
        public bool JS { get; set; }
        public bool Style { get; set; }
        public bool GetCeneoName { get; set; }

        public bool ControlXY { get; set; }

        public bool ExpandAndCompareGoogleOffers { get; set; } = true;

        public bool HeadLessForGoogleGenerators { get; set; }
        public int GoogleGeneratorsCount { get; set; }
    }
}
