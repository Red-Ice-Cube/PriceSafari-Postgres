// W folderze Models
using PriceSafari.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class PriceHistoryExtendedInfoClass
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }
    public ProductClass Product { get; set; }

    public int ScrapHistoryId { get; set; }
    public ScrapHistoryClass ScrapHistory { get; set; }



    // Informacja o sprzedaży z Ceneo
    public int? CeneoSalesCount { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? ExtendedDataApiPrice { get; set; }
}