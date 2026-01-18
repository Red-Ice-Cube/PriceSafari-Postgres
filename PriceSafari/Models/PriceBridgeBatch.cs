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
        public int SuccessfulCount { get; set; } = 0;

        public int FailedCount { get; set; } = 0;


        public int? TotalProductsCount { get; set; }

     
        public int? TargetMetCount { get; set; }

        public int? TargetUnmetCount { get; set; }


        public int? PriceIncreasedCount { get; set; }   
        public int? PriceDecreasedCount { get; set; }   
        public int? PriceMaintainedCount { get; set; }

        public PriceExportMethod ExportMethod { get; set; }

        public virtual ICollection<PriceBridgeItem> BridgeItems { get; set; }
    }
}