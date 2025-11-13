using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.ViewModels
{
    public class UserPaymentDataViewModel
    {
        // --- DODANO TO POLE ---
        [Required]
        public int StoreId { get; set; }
        // ---------------------

        public int? PaymentDataId { get; set; } // Nullable, bo przy tworzeniu nowego może być null

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

        [EmailAddress]
        [Display(Name = "E-mail do automatycznej wysyłki faktur (opcjonalny)")]
        public string? InvoiceAutoMail { get; set; }

        [Display(Name = "Automatyczna wysyłka faktur na e-mail")]
        public bool InvoiceAutoMailSend { get; set; }
    }
}