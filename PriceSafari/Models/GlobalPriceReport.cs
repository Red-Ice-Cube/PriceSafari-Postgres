using PriceSafari.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class GlobalPriceReport
{
    [Key]
    public int ReportId { get; set; }

    public int ScrapingProductId { get; set; }

    public int ProductId { get; set; }
    [ForeignKey("ProductId")]
    public ProductClass Product { get; set; }  // Dodanie relacji do ProductClass

    public decimal Price { get; set; }
    public decimal CalculatedPrice { get; set; }
    public decimal PriceWithDelivery { get; set; }
    public decimal CalculatedPriceWithDelivery { get; set; }
    public string StoreName { get; set; }
    public string OfferUrl { get; set; }

    // Klucz obcy do Region
    public int RegionId { get; set; }

    // Nawigacja do Region
    [ForeignKey("RegionId")]
    public Region Region { get; set; }
}
