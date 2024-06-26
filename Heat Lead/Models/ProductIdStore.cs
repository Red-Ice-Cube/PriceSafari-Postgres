using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Heat_Lead.Models
{
    public class ProductIdStore
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string StoreProductId { get; set; }

        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public Product Product { get; set; }
    }
}