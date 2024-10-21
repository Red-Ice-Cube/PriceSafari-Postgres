using System;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class InvoiceClass
    {
        [Key]
        public int InvoiceId { get; set; }

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

        [Display(Name = "Ilość Zeskrobań")]
        public int ScrapesIncluded { get; set; }
    }
}
