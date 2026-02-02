using PriceSafari.Models;
using System.Collections.Generic;

namespace PriceSafari.Models.ViewModels
{
    public class CompetitorPresetViewModel
    {
        public int PresetId { get; set; }
        public int StoreId { get; set; }
        public string PresetName { get; set; }

        public PresetType Type { get; set; }

        public bool NowInUse { get; set; }
        public bool SourceGoogle { get; set; }
        public bool SourceCeneo { get; set; }
        public bool UseUnmarkedStores { get; set; }
        public bool IncludeNoDeliveryInfo { get; set; }
        public int MinDeliveryDays { get; set; }
        public int MaxDeliveryDays { get; set; }

        public List<CompetitorItemDto> Competitors { get; set; }
    }

    public class CompetitorItemDto
    {
        public string StoreName { get; set; }

        public DataSourceType DataSource { get; set; }

        public bool UseCompetitor { get; set; }
    }
}