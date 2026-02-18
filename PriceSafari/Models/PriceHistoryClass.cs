using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class PriceHistoryClass
    {
        [Key]
        public int Id { get; set; }
        public int ProductId { get; set; }
        public ProductClass Product { get; set; }
        public string StoreName { get; set; }
        public decimal Price { get; set; }
        public string? IsBidding { get; set; }
        public int? Position { get; set; }
        public int ScrapHistoryId { get; set; }
        public ScrapHistoryClass ScrapHistory { get; set; }
        public decimal? ShippingCostNum { get; set; }

        public bool? CeneoInStock { get; set; }

        public bool IsGoogle { get; set; }

        public bool? GoogleInStock { get; set; }
        public int? GoogleOfferPerStoreCount { get; set; }

        public string? GoogleOfferUrl { get; set; }
    }
}