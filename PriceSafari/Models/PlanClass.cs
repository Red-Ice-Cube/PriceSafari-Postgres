


using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace PriceSafari.Models
{
    public class PlanClass
    {
        [Key]
        public int PlanId { get; set; }

        [Required(ErrorMessage = "Nazwa planu jest wymagana.")]
        [Display(Name = "Nazwa Planu")]
        public string PlanName { get; set; }

        [Required(ErrorMessage = "Cena netto jest wymagana.")]
        [Display(Name = "Cena Netto")]
        [Range(0, double.MaxValue, ErrorMessage = "Cena musi być liczbą dodatnią.")]
        public decimal NetPrice { get; set; } // Net price of the plan

        [Display(Name = "Plan Testowy")]
        public bool IsTestPlan { get; set; } = false; // Indicates if it's a test plan


        [Required(ErrorMessage = "Liczba produktów do zeskrobania jest wymagana.")]
        [Display(Name = "Produkty do Zeskrobania")]
        [Range(1, int.MaxValue, ErrorMessage = "Liczba produktów musi być większa od zera.")]
        public int ProductsToScrap { get; set; } // Number of products to scrape assigned to the plan

        [Required(ErrorMessage = "Liczba zeskrobań jest wymagana.")]
        [Display(Name = "Ilość Zeskrobań")]
        [Range(1, int.MaxValue, ErrorMessage = "Ilość zeskrobań musi być większa od zera.")]
        public int ScrapesPerInvoice { get; set; } // Number of scrapes included per invoice

        // Navigation property
        public ICollection<StoreClass> Stores { get; set; } = new List<StoreClass>();
        public ICollection<InvoiceClass> Invoices { get; set; } = new List<InvoiceClass>();
    }
}
