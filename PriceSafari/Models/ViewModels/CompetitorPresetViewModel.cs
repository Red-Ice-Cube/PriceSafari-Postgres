public class CompetitorPresetViewModel
{
    public int StoreId { get; set; }
    public bool SourceGoogle { get; set; }
    public bool SourceCeneo { get; set; }
    public bool UseUnmarkedStores { get; set; }

    // Lista konkurentów
    public List<CompetitorItemViewModel> Competitors { get; set; }
}

public class CompetitorItemViewModel
{
    public string StoreName { get; set; }
    public bool IsGoogle { get; set; }
    public bool UseCompetitor { get; set; }
}
