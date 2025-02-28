using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using PriceSafari.Models.SchedulePlan;

namespace PriceSafari.Models
{
    public class StoreClass
    {
        [Key]
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public string? StoreProfile { get; set; }
        public string? StoreApiUrl { get; set; }
        public string? StoreApiKey { get; set; }
        public string? StoreLogoUrl { get; set; }

        public string? ProductMapXmlUrl { get; set; }

        public string? ProductMapXmlUrlGoogle { get; set; }

        public string? GoogleMiG {  get; set; }

        [Display(Name = "Produkty do Zeskrobania")]
        public int? ProductsToScrap { get; set; }

       

        [Display(Name = "Plan")]
        public int? PlanId { get; set; }

        [ValidateNever]
        public PlanClass Plan { get; set; }

        [Display(Name = "Procent Rabatu")]
        public decimal? DiscountPercentage { get; set; } = 0;

        [Display(Name = "Pozostała Ilość Zeskrobań")]
        public int RemainingScrapes { get; set; } = 0;

        public bool IsActive
        {
            get
            {
                return RemainingScrapes > 0;
            }
        }


        public string? StoreNameGoogle { get; set; }   // np. "Akson"
        public string? StoreNameCeneo { get; set; }    // np. "akson.pl"


        // Navigation properties
        public ICollection<ScrapHistoryClass> ScrapHistories { get; set; } = new List<ScrapHistoryClass>();
        public ICollection<ProductClass> Products { get; set; } = new List<ProductClass>();
        public ICollection<CategoryClass> Categories { get; set; } = new List<CategoryClass>();
        public ICollection<PriceValueClass> PriceValues { get; set; } = new List<PriceValueClass>();
        public ICollection<FlagsClass> Flags { get; set; } = new List<FlagsClass>();
        public ICollection<PriceSafariUserStore> UserStores { get; set; } = new List<PriceSafariUserStore>();
        public ICollection<PriceSafariReport> PriceSafariReports { get; set; } = new List<PriceSafariReport>();

        public ICollection<InvoiceClass> Invoices { get; set; } = new List<InvoiceClass>();
        public ICollection<ScheduleTaskStore> ScheduleTaskStores { get; set; }
            = new List<ScheduleTaskStore>();


    }
}
