namespace PriceSafari.Models.ManagerViewModels
{
    public class SettingsViewModel
    {
        public bool VerificationRequired { get; set; }
        public string? SupervisorEmail { get; set; }
        public string? SupervisorNumber { get; set; }

        public int Semophore { get; set; }
        public int WarmUp { get; set; }
        public bool Headless { get; set; }
    }

    public class EditVerificationRequiredViewModel
    {
        public bool VerificationRequired { get; set; }
    }

    public class EditSupervisorViewModel
    {
        public string? SupervisorEmail { get; set; }
        public string? SupervisorNumber { get; set; }
    }

    public class EditSpeedSettingsViewModel
    {
        public int Semophore { get; set; }
        public int WarmUp { get; set; }

        public bool Headless { get; set; }
    }
}
