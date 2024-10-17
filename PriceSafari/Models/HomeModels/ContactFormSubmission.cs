namespace PriceSafari.Models.HomeModels
{
    public class ContactFormSubmission
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string CompanyName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public bool ConsentToDataProcessing { get; set; }
        public string PhoneNumber { get; set; }
        public bool PrefersPhone { get; set; }
        public DateTime SubmissionDate { get; set; }
    }

}
