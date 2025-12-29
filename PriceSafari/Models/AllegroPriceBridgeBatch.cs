using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{

    public class AllegroPriceBridgeBatch
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public DateTime ExecutionDate { get; set; } = DateTime.UtcNow;

        [Required]
        public int StoreId { get; set; }
        [ForeignKey("StoreId")]
        public virtual StoreClass Store { get; set; }

        public bool IsAutomation { get; set; } = false;
        public int? AutomationRuleId { get; set; }

        [Required]
        public int AllegroScrapeHistoryId { get; set; }
        [ForeignKey("AllegroScrapeHistoryId")]
        public virtual AllegroScrapeHistory AllegroScrapeHistory { get; set; }

        public string UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual PriceSafariUser User { get; set; }

        public int SuccessfulCount { get; set; } = 0;

        public int FailedCount { get; set; } = 0;


        public int? TotalProductsCount { get; set; }

        // Ile produktów spełniało założenia strategii (np. przebiło rywala)
        public int? TargetMetCount { get; set; }

        // Ile produktów nie spełniało (np. blokada minimalnej marży)
        public int? TargetUnmetCount { get; set; }

        public virtual ICollection<AllegroPriceBridgeItem> BridgeItems { get; set; } = new List<AllegroPriceBridgeItem>();
    }
}