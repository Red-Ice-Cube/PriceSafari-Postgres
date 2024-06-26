using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Heat_Lead.Models
{
    public class Product
    {
        [Key]
        public int ProductId { get; set; }

        public string ProductName { get; set; }
        public decimal AffiliateCommission { get; set; }
        public string ProductURL { get; set; }
        public string ProductImage { get; set; }
        public decimal ProductPrice { get; set; }

        public int? CategoryId { get; set; }

        [ForeignKey("CategoryId")]
        public Category? Category { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<ProductIdStore> StoreProductIds { get; set; }
    }
}