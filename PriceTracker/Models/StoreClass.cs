using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PriceTracker.Models
{
    public class StoreClass
    {
        [Key]
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public string? StoreProfile { get; set; }

        public ICollection<ScrapHistoryClass> ScrapHistories { get; set; } = new List<ScrapHistoryClass>();
        public ICollection<ProductClass> Products { get; set; } = new List<ProductClass>();
    }
}
