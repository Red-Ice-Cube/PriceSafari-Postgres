namespace PriceSafari.Models.ViewModels
{
    public class CompetitorPresetViewModel
    {
        public int PresetId { get; set; }          // 0 -> nowy
        public int StoreId { get; set; }
        public string PresetName { get; set; }
        public bool NowInUse { get; set; }
        public bool SourceGoogle { get; set; }
        public bool SourceCeneo { get; set; }
        public bool UseUnmarkedStores { get; set; }

        public List<CompetitorItemDto> Competitors { get; set; }
    }

    public class CompetitorItemDto
    {
        public string StoreName { get; set; }
        public bool IsGoogle { get; set; }
        public bool UseCompetitor { get; set; }
    }


}
