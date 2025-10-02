using System.ComponentModel.DataAnnotations;

public class CoOfrClass
{
    [Key]
    public int Id { get; set; }
    public string? OfferUrl { get; set; } //Ceneo ofer
    public string? GoogleOfferUrl { get; set; } 
    public string? GoogleGid { get; set; } 
    public List<int> ProductIds { get; set; } = new List<int>();
    public List<int> ProductIdsGoogle { get; set; } = new List<int>();
    public bool IsScraped { get; set; }
    public bool GoogleIsScraped { get; set; }
    public int PricesCount { get; set; }
    public int GooglePricesCount { get; set; }
    public bool IsRejected { get; set; }
    public bool GoogleIsRejected { get; set; }
    public bool IsGoogle { get; set; }

    // Add these two new properties
    public List<string> StoreNames { get; set; } = new List<string>();
    public List<string> StoreProfiles { get; set; } = new List<string>();
}
