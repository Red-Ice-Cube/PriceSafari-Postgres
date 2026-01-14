using PriceSafari.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class ProductGoogleCatalog
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }

    [ForeignKey("ProductId")]
    public ProductClass Product { get; set; }

    public string? GoogleCid { get; set; } // Catalog ID
    public string? GoogleGid { get; set; } // Global Product ID
    public string? GoogleHid { get; set; } // Headline Offer DocID

    // Flaga: true gdy produkt to pojedyncza oferta (przez HID), false gdy to katalog (przez CID)
    public bool IsExtendedOfferByHid { get; set; }

    public DateTime FoundDate { get; set; } = DateTime.UtcNow;
}