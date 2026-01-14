using PriceSafari.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class CoOfrClass
{
    [Key]
    public int Id { get; set; }
    public string? OfferUrl { get; set; }
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

    public int? CeneoSalesCount { get; set; }


    // zmienne dla googla
    public bool UseGPID { get; set; } = false;
    public bool UseWRGA { get; set; } = false;
    public List<string> StoreNames { get; set; } = new List<string>();
    public List<string> StoreProfiles { get; set; } = new List<string>();

    public virtual ICollection<CoOfrStoreData> StoreData { get; set; } = new List<CoOfrStoreData>();

    public string? GoogleCid { get; set; }

    // DODAJ TO: Pozwala łatwo odróżnić w bazie główne zadanie od tych "dodatkowych"
    public bool IsAdditionalCatalog { get; set; } = false;

}