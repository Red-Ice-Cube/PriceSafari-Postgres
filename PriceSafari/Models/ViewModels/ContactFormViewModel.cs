using System;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.ViewModels
{
    using System.ComponentModel.DataAnnotations;

    public class ContactFormViewModel
    {
        [Required(ErrorMessage = "Email jest wymagany.")]
        [EmailAddress(ErrorMessage = "Nieprawidłowy format email.")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Adres witryny jest wymagany.")]
        [StringLength(100, ErrorMessage = "Adres witryny jest za długi")]
        public string CompanyName { get; set; }

        [Required(ErrorMessage = "Imię jest wymagane.")]
        [StringLength(50)]
        public string FirstName { get; set; }

        [Required(ErrorMessage = "Nazwisko jest wymagane.")]
        [StringLength(50)]
        public string LastName { get; set; }

        [Required(ErrorMessage = "Numer telefonu jest wymagany.")]
        [Phone(ErrorMessage = "Nieprawidłowy format numeru telefonu.")]
        public string PhoneNumber { get; set; }

        [Required]
        public bool ConsentToDataProcessing { get; set; }

        [Required]
        public bool PrefersPhone { get; set; }
    }


}
