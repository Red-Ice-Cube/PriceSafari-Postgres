using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class ProductGoogleCatalog
    {

        [Key]
        public int Id { get; set; }

        public int ProductId { get; set; }
        [ForeignKey("ProductId")]
        public ProductClass Product { get; set; }

        public string GoogleCid { get; set; }
        public string GoogleGid { get; set; }
        public string GoogleUrl { get; set; }

        public DateTime FoundDate { get; set; } = DateTime.UtcNow;
    }
}
