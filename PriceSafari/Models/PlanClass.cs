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
        public decimal NetPrice { get; set; }

        [Display(Name = "Plan Testowy")]
        public bool IsTestPlan { get; set; } = false;

        [Display(Name = "Maks prod. do analizy (porównywarki cenowe)")]
        [Range(1, int.MaxValue, ErrorMessage = "Liczba produktów musi być większa od zera.")]
        public int? ProductsToScrap { get; set; } 

        [Display(Name = "Maks prod. do analizy (marketplace)")]
        [Range(1, int.MaxValue, ErrorMessage = "Liczba produktów musi być większa od zera.")]
        public int? ProductsToScrapAllegro { get; set; }


        [Required(ErrorMessage = "Liczba analiz jest wymagana.")]
        [Display(Name = "Ilość analiz")]
        [Range(1, int.MaxValue, ErrorMessage = "Ilość analiz musi być większa od zera.")]
        public int ScrapesPerInvoice { get; set; }

    
        [Display(Name = "Źródło Ceneo")]
        public bool Ceneo { get; set; } = false;

        [Display(Name = "Źródło Google Shopping")]
        public bool GoogleShopping { get; set; } = false;

        [Display(Name = "Źródło Allegro")]
        public bool Allegro { get; set; } = false;

        [Display(Name = "Informacje")]
        public string Info { get; set; }

        public ICollection<StoreClass> Stores { get; set; } = new List<StoreClass>();
        public ICollection<InvoiceClass> Invoices { get; set; } = new List<InvoiceClass>();
    }
}
