using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class CoOfrPriceHistoryClass
    {
        [Key]
        public int Id { get; set; }

        public int CoOfrClassId { get; set; }
        public CoOfrClass CoOfr { get; set; }


        //dane z cene scrapera 
        public string? StoreName { get; set; }
        public decimal? Price { get; set; }
        public string? IsBidding { get; set; } // tutaj tez moze trafic informacja ze mamy dfo czynienia z najlepsza cena Google 
        public string? Position { get; set; }
        public decimal? ShippingCostNum { get; set; }

        // ZMIANA: Z 'int? AvailabilityNum' na 'bool? CeneoInStock'
        public bool? CeneoInStock { get; set; }

        public string? ExportedName { get; set; }



        // nowe pola do danych z google scrapera

        public string? GoogleStoreName { get; set; }
        public decimal? GooglePrice { get; set; }
        public string? GooglePosition { get; set; }
        public decimal? GooglePriceWithDelivery { get; set; }

        public bool? GoogleInStock { get; set; }
        public int? GoogleOfferPerStoreCount { get; set; }

        public string? GoogleCid { get; set; }


    }
}