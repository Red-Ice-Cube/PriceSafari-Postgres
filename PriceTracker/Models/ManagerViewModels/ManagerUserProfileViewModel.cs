namespace PriceTracker.Models.ManagerViewModels
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
        public string? Description { get; set; }
    }
}