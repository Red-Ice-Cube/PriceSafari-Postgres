using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class UserPaymentData
    {
        [Key]
        public int PaymentDataId { get; set; }

        // --- NOWA LOGIKA (RELACJA 1:1 ZE SKLEPEM) ---
        [Required]
        public int StoreId { get; set; }

        [ForeignKey("StoreId")]
        public StoreClass Store { get; set; }
        // -------------------------------------------

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

        public string? InvoiceAutoMail { get; set; }
        public bool InvoiceAutoMailSend { get; set; } = false;
    }
}