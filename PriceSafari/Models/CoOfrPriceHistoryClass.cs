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

        public string StoreName { get; set; }
        public decimal Price { get; set; }
        public string IsBidding { get; set; }
        public string? Position { get; set; }
        public decimal? ShippingCostNum { get; set; }
        public int? AvailabilityNum { get; set; }

        public string? ExportedName { get; set; }
    }
}
