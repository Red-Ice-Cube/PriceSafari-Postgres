using System.ComponentModel.DataAnnotations;

public class CoOfrClass
{
    [Key]
    public int Id { get; set; }
    public string OfferUrl { get; set; }
    public List<int> ProductIds { get; set; } = new List<int>();
    public bool IsScraped { get; set; }
    public string? ScrapingMethod { get; set; }
    public int PricesCount { get; set; }
    public bool IsRejected { get; set; }

    // Add these two new properties
    public List<string> StoreNames { get; set; } = new List<string>();
    public List<string> StoreProfiles { get; set; } = new List<string>();
}
