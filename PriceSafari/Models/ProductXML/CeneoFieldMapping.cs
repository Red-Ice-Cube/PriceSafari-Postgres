using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.ProductXML
{
    public class CeneoFieldMapping
    {
        [Key]
        public int Id { get; set; }
        public int StoreId { get; set; }

        // Pola: FieldName i LocalName
        public string FieldName { get; set; }
        public string LocalName { get; set; }
    }
}
