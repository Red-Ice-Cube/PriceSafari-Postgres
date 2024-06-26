using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceTracker.Models
{
    public class PriceHistoryClass
    {
        [Key]
        public int Id { get; set; }
        public int ProductId { get; set; }
        public ProductClass Product { get; set; }
        public string StoreName { get; set; }
        public DateTime Date { get; set; }
        public decimal Price { get; set; }
        public string OfferUrl { get; set; }
        public int ScrapHistoryId { get; set; }
        public ScrapHistoryClass ScrapHistory { get; set; }
        public string ShippingCost { get; set; }
        public decimal? ShippingCostNum { get; set; }
        public string Availability { get; set; }
        public int? AvailabilityNum { get; set; }
    }
}
