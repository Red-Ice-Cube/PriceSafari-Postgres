namespace PriceSafari.Models.ViewModels
{
    public class CompetitorPresetViewModel
    {
        public int StoreId { get; set; }

        // Nowe pola:
        public string PresetName { get; set; }
        public bool NowInUse { get; set; }

        public bool SourceGoogle { get; set; }
        public bool SourceCeneo { get; set; }
        public bool UseUnmarkedStores { get; set; }

        public List<CompetitorItemViewModel> Competitors { get; set; }
    }

    public class CompetitorItemViewModel
    {
        public string StoreName { get; set; }
        public bool IsGoogle { get; set; }
        public bool UseCompetitor { get; set; }
    }

}
