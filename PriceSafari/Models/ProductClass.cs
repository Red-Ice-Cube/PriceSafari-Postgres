using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class ProductClass
    {
        [Key]
        public int ProductId { get; set; }
        [ForeignKey("StoreClass")]
        public int StoreId { get; set; }
        public StoreClass Store { get; set; }
        public string ProductName { get; set; }
        public string Category { get; set; }
        public string OfferUrl { get; set; }
        public int? ExternalId { get; set; }   
        public string? CatalogNumber { get; set; }
        public string? Ean { get; set; }
        public string? MainUrl { get; set; }
        public decimal? ExternalPrice { get; set; }
        public bool IsScrapable { get; set; } = false;
        public bool IsRejected { get; set; } = false;

        //CeneoXML
        public string? ExportedNameCeneo { get; set; }

        //GoogleShoping Block

        public bool OnGoogle { get; set; } = false;
        public string? Url { get; set; }
        public string? GoogleUrl { get; set; }
        public string? ProductNameInStoreForGoogle { get; set; }
        public bool? FoundOnGoogle { get; set; }

        public ICollection<PriceHistoryClass> PriceHistories { get; set; } = new List<PriceHistoryClass>();
        public ICollection<ProductFlag> ProductFlags { get; set; } = new List<ProductFlag>();
    }
}
