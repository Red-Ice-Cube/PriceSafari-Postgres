using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{

    public enum PriceExportMethod
    {
        Csv = 0,
        Excel = 1,
        Api = 2
    }

    public class PriceBridgeBatch
    {
        [Key]
        public int Id { get; set; }

        public bool IsAutomation { get; set; } = false;
        public int? AutomationRuleId { get; set; }

        public int StoreId { get; set; }
        [ForeignKey("StoreId")]

        public virtual StoreClass Store { get; set; }

        public int ScrapHistoryId { get; set; }

        public string? UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual PriceSafariUser User { get; set; }

        public DateTime ExecutionDate { get; set; } = DateTime.Now;
        public int SuccessfulCount { get; set; }


        public int? TotalProductsCount { get; set; }

        // Ile produktów spełniało założenia strategii (np. przebiło rywala)
        public int? TargetMetCount { get; set; }

        // Ile produktów nie spełniało (np. blokada minimalnej marży)
        public int? TargetUnmetCount { get; set; }


        public int? PriceIncreasedCount { get; set; }   // Ile podwyżek
        public int? PriceDecreasedCount { get; set; }   // Ile obniżek
        public int? PriceMaintainedCount { get; set; }

        public PriceExportMethod ExportMethod { get; set; }

        public virtual ICollection<PriceBridgeItem> BridgeItems { get; set; }
    }
}