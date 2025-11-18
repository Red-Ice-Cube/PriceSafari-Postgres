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

        [Display(Name = "Produkty do Zeskrobania")]
        public int? ProductsToScrap { get; set; }

        public int? ProductsToScrapAllegro { get; set; } = 0;

        [Display(Name = "Plan")]
        public int? PlanId { get; set; }

        [ValidateNever]
        public PlanClass Plan { get; set; }

        [Display(Name = "Procent Rabatu")]
        public decimal? DiscountPercentage { get; set; } = 0;

        [Display(Name = "Pozostała Ilość Dni w programie")]
        public int RemainingDays { get; set; } = 0;

        [Display(Name = "Czy jest płacącym klientem?")]
        public bool IsPayingCustomer { get; set; } = false;

        [Display(Name = "Data rozpoczęcia subskrypcji")]
        [DataType(DataType.Date)]
        public DateTime? SubscriptionStartDate { get; set; }
        [Display(Name = "Użytkownik chce zrezygnować")]
        public bool UserWantsExit { get; set; } = false;

        public bool IsActive => RemainingDays > 0;

        public string? StoreNameGoogle { get; set; }
        public string? StoreNameCeneo { get; set; }

        public bool UseGoogleXMLFeedPrice { get; set; } = false;

        public string? StoreNameAllegro { get; set; }

        public bool OnCeneo { get; set; } = false;
        public bool OnGoogle { get; set; } = false;
        public bool OnAllegro { get; set; } = false;

        [Display(Name = "Pobieraj dane z API Allegro")]
        public bool FetchExtendedAllegroData { get; set; } = false;

        [Display(Name = "Token API Allegro")]
        public string? AllegroApiToken { get; set; }

        [Display(Name = "Token Allegro jest aktywny")]
        public bool IsAllegroTokenActive { get; set; } = false;

        public bool IsAllegroPriceBridgeActive { get; set; } = false;

        public UserPaymentData? PaymentData { get; set; }

        public string? ImojePaymentProfileId { get; set; }
        public bool IsRecurringActive { get; set; } = false;

        [Display(Name = "Numer karty (maskowany)")]
        public string? CardMaskedNumber { get; set; }

        [Display(Name = "Wystawca karty")]
        public string? CardBrand { get; set; }

        [Display(Name = "Rok ważności karty")]
        public string? CardExpYear { get; set; }

        [Display(Name = "Miesiąc ważności karty")]
        public string? CardExpMonth { get; set; }

        public ICollection<ScrapHistoryClass> ScrapHistories { get; set; } = new List<ScrapHistoryClass>();
        public ICollection<ProductClass> Products { get; set; } = new List<ProductClass>();
        public ICollection<AllegroProductClass> AllegroProducts { get; set; } = new List<AllegroProductClass>();
        public ICollection<AllegroScrapeHistory> AllegroScrapeHistories { get; set; } = new List<AllegroScrapeHistory>();

        public ICollection<CategoryClass> Categories { get; set; } = new List<CategoryClass>();
        public ICollection<PriceValueClass> PriceValues { get; set; } = new List<PriceValueClass>();
        public ICollection<FlagsClass> Flags { get; set; } = new List<FlagsClass>();
        public ICollection<PriceSafariUserStore> UserStores { get; set; } = new List<PriceSafariUserStore>();
        public ICollection<PriceSafariReport> PriceSafariReports { get; set; } = new List<PriceSafariReport>();
        public ICollection<InvoiceClass> Invoices { get; set; } = new List<InvoiceClass>();
        public ICollection<ScheduleTaskStore> ScheduleTaskStores { get; set; } = new List<ScheduleTaskStore>();
    }
}