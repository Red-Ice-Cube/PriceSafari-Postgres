using PriceSafari.Models;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

public class GlobalPriceReport
{
    [Key]
    public int ReportId { get; set; }

    public int ScrapingProductId { get; set; }

    public int ProductId { get; set; }
    [ForeignKey("ProductId")]
    public ProductClass Product { get; set; }  // Dodanie relacji do ProductClass

    public decimal Price { get; set; }
    public decimal PriceWithDelivery { get; set; }
    public string StoreName { get; set; }
    public string OfferUrl { get; set; }
    public int RegionId { get; set; }

    
}
