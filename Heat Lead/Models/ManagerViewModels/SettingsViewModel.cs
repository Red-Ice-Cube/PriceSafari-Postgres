namespace Heat_Lead.Models.ManagerViewModels
{
    public class SettingsViewModel
    {
        public decimal MinimumPayout { get; set; }
        public int CookieLifeTime { get; set; }
        public string ProgramDescription { get; set; }
        public bool VerificationRequired { get; set; }
        public string? SupervisorEmail { get; set; }
        public string? SupervisorNumber { get; set; }
        public int ApiRequestInterval { get; set; }
        public bool CollectFingerPrint { get; set; }
        public int OrdersPerClick { get; set; }

        public bool TrackByEan {  get; set; }
    }

    public class EditMinimumPayoutViewModel
    {
        public decimal MinimumPayout { get; set; }
    }

    public class EditCookieLifeTimeViewModel
    {
        public int CookieLifeTime { get; set; }
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

    public class EditApiRequestIntervalViewModel
    {
        public int ApiRequestInterval { get; set; }
    }

    public class EditFingerPrintCollectionViewModel
    {
        public bool CollectFingerPrint { get; set; }
    }

    public class EditOrdersPerClickViewModel
    {
        public int OrdersPerClick { get; set; }
    }

    public class EditTrackByEanViewModel
    {
        public bool TrackByEan { get; set; }
    }
}