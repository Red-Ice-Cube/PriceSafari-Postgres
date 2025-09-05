namespace PriceSafari.Models.ManagerViewModels
{
    public class ManagerUserProfileViewModel
    {
        // Istniejące właściwości
        public string? UserName { get; set; }
        public string? UserSurname { get; set; }
        public string? UserEmail { get; set; }
        public string UserCode { get; set; }
        public bool Status { get; set; } // To jest 'IsActive', zostawmy dla kompatybilności
        public DateTime UserJoin { get; set; }
        public bool Verification { get; set; }

        // --- POCZĄTEK NOWYCH WŁAŚCIWOŚCI ---

        // Status i aktywność
        public UserStatus UserStatus { get; set; } // Pełny status z enuma
        public DateTime? LastLogin { get; set; }
        public int LoginCount { get; set; }

        // Dane dla Ceneo
        public string? CeneoStoreName { get; set; }
        public string? CeneoFeedUrl { get; set; }
        public DateTime? CeneoFeedSubmittedOn { get; set; }

        // Dane dla Google
        public string? GoogleStoreName { get; set; }
        public string? GoogleFeedUrl { get; set; }
        public DateTime? GoogleFeedSubmittedOn { get; set; }


        public string? Role { get; set; }
        public string? PhoneNumber { get; set; }

        public int? UserMessageId { get; set; }
        public string UserMessageContent { get; set; }
    }
}