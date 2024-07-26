using System.ComponentModel.DataAnnotations;

public class CoOfrClass
{
    [Key]
    public int Id { get; set; }
    public string OfferUrl { get; set; }
    public List<int> ProductIds { get; set; } = new List<int>();
    public bool IsScraped { get; set; }
    public string? ScrapingMethod { get; set; }  // Nowe pole
    public int PricesCount { get; set; }  // Nowe pole
}
