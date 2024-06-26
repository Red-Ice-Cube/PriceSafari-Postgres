using Heat_Lead.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Heat_Lead.Models
{
    public class Paycheck
    {
        [Key]
        public int PaycheckId { get; set; }

        public string PartnerName { get; set; }
        public string PartnerSurname { get; set; }
        public string PartnerEmail { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string PostalCode { get; set; }
        public string? Pesel { get; set; }
        public string? CompanyTaxNumber { get; set; }
        public string? CompanyName { get; set; }
        public string BankAccountNumber { get; set; }
        public string? TaxOffice { get; set; }
        public decimal Amount { get; set; }

        public DateTime CreationDate { get; set; } = DateTime.Now;

        public bool MoneySended { get; set; } = false;

        public bool IsCompany { get; set; } = false;

        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual Heat_LeadUser Heat_LeadUser { get; set; }

        public int WalletId { get; set; }

        [ForeignKey("WalletId")]
        public virtual Wallet Wallet { get; set; }
    }
}