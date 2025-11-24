namespace PriceSafari.Models.ManagerViewModels
{
    public class ManagerUserProfileViewModel
    {

        public string? UserName { get; set; }
        public string? UserSurname { get; set; }
        public string? UserEmail { get; set; }
        public string UserCode { get; set; }
        public bool Status { get; set; }
        public DateTime UserJoin { get; set; }
        public bool Verification { get; set; }

        public UserStatus UserStatus { get; set; }
        public DateTime? LastLogin { get; set; }
        public int LoginCount { get; set; }

        public string? CeneoStoreName { get; set; }
        public string? CeneoFeedUrl { get; set; }
        public DateTime? CeneoFeedSubmittedOn { get; set; }

        public string? GoogleStoreName { get; set; }
        public string? GoogleFeedUrl { get; set; }
        public DateTime? GoogleFeedSubmittedOn { get; set; }

        public string? Role { get; set; }
        public string? PhoneNumber { get; set; }

        public int? UserMessageId { get; set; }
        public string UserMessageContent { get; set; }
        public DateTime? UserMessageCreatedAt { get; set; }
        public bool UserMessageIsRead { get; set; }
        public string? AllegroStoreName { get; set; }
        public DateTime? AllegroSubmittedOn { get; set; }

      
    }
}