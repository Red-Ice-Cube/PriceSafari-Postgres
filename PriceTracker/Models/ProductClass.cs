using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceTracker.Models
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

        public ICollection<PriceHistoryClass> PriceHistories { get; set; } = new List<PriceHistoryClass>();
        public ICollection<ProductFlag> ProductFlags { get; set; } = new List<ProductFlag>();
    }
}
