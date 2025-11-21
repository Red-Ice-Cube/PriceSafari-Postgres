using System;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class InvoiceClass
    {
        [Key]
        public int InvoiceId { get; set; }

        [Required]
        [Display(Name = "Numer Faktury")]
        public string InvoiceNumber { get; set; }

        [Required]
        [Display(Name = "Sklep")]
        public int StoreId { get; set; }
        public StoreClass Store { get; set; }

        [Required]
        [Display(Name = "Plan")]
        public int PlanId { get; set; }
        public PlanClass Plan { get; set; }

        [Required]
        [Display(Name = "Data Wystawienia")]
        [DataType(DataType.Date)]
        public DateTime IssueDate { get; set; }

        [Display(Name = "Data Płatności")]
        [DataType(DataType.Date)]
        public DateTime? PaymentDate { get; set; }

        [Required]
        [Display(Name = "Czy Opłacona")]
        public bool IsPaid { get; set; } = false;

        [Required]
        [Display(Name = "Kwota Netto")]
        public decimal NetAmount { get; set; }

        [Display(Name = "Ilość Dni Dostępu")]
        public int DaysIncluded { get; set; }

        [Display(Name = "Maksymalna ilość produktów (Porównywarki cenowe)")]
        public int? UrlsIncluded { get; set; }

        [Display(Name = "Maksymalna ilość produktów (Marketplace)")]
        public int? UrlsIncludedAllegro { get; set; }

        [Display(Name = "Rabat (%)")]
        public decimal AppliedDiscountPercentage { get; set; } = 0;

        [Display(Name = "Kwota Rabatu")]
        public decimal AppliedDiscountAmount { get; set; } = 0;

        public string? OriginalProformaNumber { get; set; }

        public DateTime? DueDate { get; set; }

        public bool IsPaidByCard { get; set; } = false;

        [Display(Name = "Wysłano e-mailem")]
        public bool IsSentByEmail { get; set; } = false;

        public string CompanyName { get; set; }
        public string Address { get; set; }
        public string PostalCode { get; set; }
        public string City { get; set; }
        public string NIP { get; set; }
    }
}