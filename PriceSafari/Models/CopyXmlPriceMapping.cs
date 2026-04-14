using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public enum CopyXmlKeyField
    {
        Ean = 0,
        ExternalId = 1
    }

    public class CopyXmlPriceMapping
    {
        [Key]
        public int StoreId { get; set; }

        [ForeignKey(nameof(StoreId))]
        public StoreClass Store { get; set; }

        public CopyXmlKeyField KeyField { get; set; } = CopyXmlKeyField.Ean;


        public string? ProductNodeXPath { get; set; }

        public string? KeyXPath { get; set; }
        public string? PriceXPath { get; set; }
        public string? PromoPriceXPath { get; set; }
        public string? PriceWithShippingXPath { get; set; }
        public string? InStockXPath { get; set; }


        public string? InStockMarkerValue { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}