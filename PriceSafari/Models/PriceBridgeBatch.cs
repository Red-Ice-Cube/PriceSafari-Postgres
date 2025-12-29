using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    // 1. Definicja Enuma dla typu eksportu
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
        // Uwaga: Tutaj upewnij się, że typ to StoreClass lub Store w zależności od nazwy Twojej klasy encji
        public virtual StoreClass Store { get; set; }

        public int ScrapHistoryId { get; set; }

        public string? UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual PriceSafariUser User { get; set; }

        public DateTime ExecutionDate { get; set; } = DateTime.Now;
        public int SuccessfulCount { get; set; }

        // 2. Nowa właściwość przechowująca sposób eksportu
        public PriceExportMethod ExportMethod { get; set; }

        public virtual ICollection<PriceBridgeItem> BridgeItems { get; set; }
    }
}