using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace PriceSafari.Models
{
    public class UserPaymentData
    {
        [Key]
        public int PaymentDataId { get; set; }

        [ForeignKey(nameof(PriceSafariUser))]
        public string UserId { get; set; }

        public PriceSafariUser User { get; set; }

        [Required]
        [Display(Name = "Nazwa firmy")]
        public string CompanyName { get; set; }

        [Required]
        [Display(Name = "Adres")]
        public string Address { get; set; }

        [Required]
        [Display(Name = "Kod pocztowy")]
        public string PostalCode { get; set; }

        [Required]
        [Display(Name = "Miasto")]
        public string City { get; set; }

        [Required]
        [Display(Name = "NIP")]
        public string NIP { get; set; }
    }
}
