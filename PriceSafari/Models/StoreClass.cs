using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;

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
        public int? ProductsToScrap { get; set; }

        public bool AutoMatching { get; set; } = true;

        // Billing and Plan Properties
        public int? PlanId { get; set; }
        public PlanClass Plan { get; set; }

        public decimal? DiscountPercentage { get; set; } = 0; // Discount percentage
        public DateTime? PlanStartDate { get; set; }
        public DateTime? PlanEndDate { get; set; }
        public bool IsInvoicePaid { get; set; } = false;

        public bool IsActive
        {
            get
            {
                return IsInvoicePaid && PlanEndDate >= DateTime.Now.Date;
            }
        }


        // Navigation properties
        public ICollection<ScrapHistoryClass> ScrapHistories { get; set; } = new List<ScrapHistoryClass>();
        public ICollection<ProductClass> Products { get; set; } = new List<ProductClass>();
        public ICollection<CategoryClass> Categories { get; set; } = new List<CategoryClass>();
        public ICollection<PriceValueClass> PriceValues { get; set; } = new List<PriceValueClass>();
        public ICollection<FlagsClass> Flags { get; set; } = new List<FlagsClass>();
        public ICollection<PriceSafariUserStore> UserStores { get; set; } = new List<PriceSafariUserStore>();
        public ICollection<PriceSafariReport> PriceSafariReports { get; set; } = new List<PriceSafariReport>();
    }
}
