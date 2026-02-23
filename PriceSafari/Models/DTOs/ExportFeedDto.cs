using System.Xml.Serialization;

namespace PriceSafari.Models.DTOs
{
    [XmlRoot("PriceSafariFeed")]
    public class ExportFeedDto
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public DateTime GeneratedAt { get; set; }

        [XmlArray("Products")]
        [XmlArrayItem("Product")]
        public List<ExportProductDto> Products { get; set; } = new List<ExportProductDto>();
    }

    public class ExportProductDto
    {
        public int ProductId { get; set; }
        public string? ExternalId { get; set; } // np. SKU/ID z systemu klienta
        public string? ProducerCode { get; set; }
        public string? Ean { get; set; }
        public string ProductName { get; set; }

        public decimal? MyPrice { get; set; }
        public decimal? MyShippingCost { get; set; }

        [XmlArray("Competitors")]
        [XmlArrayItem("Competitor")]
        public List<ExportCompetitorDto> Competitors { get; set; } = new List<ExportCompetitorDto>();
    }

    public class ExportCompetitorDto
    {
        public string StoreName { get; set; }
        public decimal Price { get; set; }
        public decimal? ShippingCost { get; set; }
        public string Source { get; set; } // "Google" lub "Ceneo"
    }
}