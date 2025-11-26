using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class UserPaymentData
    {
        [Key]
        public int PaymentDataId { get; set; }

        // --- RELACJA ---
        [Required]
        public int StoreId { get; set; }

        [ForeignKey("StoreId")]
        public StoreClass? Store { get; set; } // Tu też warto dodać '?' aby uniknąć ostrzeżeń kompilatora
        // ----------------

        // USUNIĘTO [Required]
        [Display(Name = "Nazwa firmy")]
        public string? CompanyName { get; set; } // Dodano '?' (Nullable)

        // USUNIĘTO [Required]
        [Display(Name = "Adres")]
        public string? Address { get; set; } // Dodano '?'

        // USUNIĘTO [Required]
        [Display(Name = "Kod pocztowy")]
        public string? PostalCode { get; set; } // Dodano '?'

        // USUNIĘTO [Required]
        [Display(Name = "Miasto")]
        public string? City { get; set; } // Dodano '?'

        // USUNIĘTO [Required]
        [Display(Name = "NIP")]
        public string? NIP { get; set; } // Dodano '?'

        public string? InvoiceAutoMail { get; set; }
        public bool InvoiceAutoMailSend { get; set; } = false;
    }
}