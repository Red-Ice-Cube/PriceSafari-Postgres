namespace PriceTracker.Models.ManagerViewModels
{
    public class SettingsViewModel
    {

        public bool VerificationRequired { get; set; }
        public string? SupervisorEmail { get; set; }
        public string? SupervisorNumber { get; set; }
       
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

}