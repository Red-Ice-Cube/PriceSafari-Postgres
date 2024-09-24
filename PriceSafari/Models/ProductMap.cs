using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class ProductMap
    {
        [Key]
        public int ProductMapId { get; set; }
        public int StoreId { get; set; }
        public string ExternalId { get; set; }
        public string Url { get; set; }
        public string CatalogNumber { get; set; }
        public string Ean { get; set; }
        public string? MainUrl { get; set; }

        public string ExportedName { get; set; }
    }
}
