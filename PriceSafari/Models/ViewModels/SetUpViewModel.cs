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

        public bool IsCeneoSubmitted { get; set; }
        public bool IsGoogleSubmitted { get; set; }
        public int? AdminMessageId { get; set; }
        public string AdminMessageContent { get; set; }
        public bool IsAdminMessageRead { get; set; }
        public string? PendingStoreNameAllegro { get; set; }
        public bool IsAllegroSubmitted { get; set; }
    }
}
