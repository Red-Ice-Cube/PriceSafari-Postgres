

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace PriceSafari.Models
{
    public class CompetitorPresetClass
    {
        [Key]
        public int PresetId { get; set; }

        public int StoreId { get; set; }
        [ForeignKey(nameof(StoreId))]
        public StoreClass Store { get; set; }

       
        public PresetType Type { get; set; }

        public string PresetName { get; set; }

  
        public bool SourceGoogle { get; set; } = true;
        public bool SourceCeneo { get; set; } = true;
        public bool UseUnmarkedStores { get; set; } = true;

        public int MinDeliveryDays { get; set; } = 0; 
        public int MaxDeliveryDays { get; set; } = 31;
        public bool IncludeNoDeliveryInfo { get; set; } = true;

        public bool NowInUse { get; set; }
        public List<CompetitorPresetItem> CompetitorItems { get; set; } = new List<CompetitorPresetItem>();
    }
}