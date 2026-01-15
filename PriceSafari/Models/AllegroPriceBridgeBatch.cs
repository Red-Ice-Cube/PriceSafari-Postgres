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
        public DateTime ExecutionDate { get; set; } = DateTime.Now;

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


        // o to ponizej nam chodzi 


        public int? TotalProductsCount { get; set; }

      
        public int? TargetMetCount { get; set; }

        public int? TargetUnmetCount { get; set; }


        public int? PriceIncreasedCount { get; set; }  
        public int? PriceDecreasedCount { get; set; }   
        public int? PriceMaintainedCount { get; set; }

        public virtual ICollection<AllegroPriceBridgeItem> BridgeItems { get; set; } = new List<AllegroPriceBridgeItem>();
    }
}