// W folderze Models
using PriceSafari.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class PriceHistoryExtendedInfoClass
{
    [Key]
    public int Id { get; set; }

    // Klucz obcy do produktu
    public int ProductId { get; set; }
    public ProductClass Product { get; set; }

    // Klucz obcy do cyklu scrapowania
    public int ScrapHistoryId { get; set; }
    public ScrapHistoryClass ScrapHistory { get; set; }

    // --- Nowe dane, które przechowujemy ---

    // Informacja o sprzedaży z Ceneo
    public int? CeneoSalesCount { get; set; }

    // W przyszłości możesz tu dodać inne dane "per produkt per scrap"
    // np. public int? GoogleTotalOffers { get; set; }
    // np. public decimal? CeneoAverageRating { get; set; }
}