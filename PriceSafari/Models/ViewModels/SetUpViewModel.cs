namespace PriceSafari.Models.ViewModels
{
    public class SetUpViewModel
    {
        public string UserName { get; set; }

        // Pola do wstępnego wypełnienia formularzy
        public string PendingStoreNameCeneo { get; set; }
        public string PendingCeneoFeedUrl { get; set; }
        public string PendingStoreNameGoogle { get; set; }
        public string PendingGoogleFeedUrl { get; set; }
    }
}
