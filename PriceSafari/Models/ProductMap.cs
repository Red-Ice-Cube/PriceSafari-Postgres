using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class ProductMap
    {
        [Key]
        public int ProductMapId { get; set; }
        public int StoreId { get; set; }
        public string ExternalId { get; set; }
        public string? Url { get; set; }
        public string? Ean { get; set; }

        //grafika z xml ceneo
        public string? MainUrl { get; set; }

        public string? ExportedName { get; set; }

        // Nowe pola dla danych z Google Shopping
        public string? GoogleEan { get; set; }
        public string? GoogleImage { get; set; }
        public string? GoogleExportedName { get; set; }

       

        // NOWE pola cenowe (precyzja do 2 miejsc)
        [Column(TypeName = "decimal(18, 2)")]
        public decimal? GoogleXMLPrice { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? GoogleDeliveryXMLPrice { get; set; }

        // NOWE pola cenowe (precyzja do 2 miejsc)
        [Column(TypeName = "decimal(18, 2)")]
        public decimal? CeneoXMLPrice { get; set; }

        [Column(TypeName = "decimal(18, 2)")]
        public decimal? CeneoDeliveryXMLPrice { get; set; }

        public string? GoogleExportedProducer { get; set; }
        public string? CeneoExportedProducer { get; set; }
    }
}
