using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PriceTracker.Models
{
    public class CategoryClass
    {
        [Key]
        public int CategoryId { get; set; }
        public int StoreId { get; set; }

        public int Depth { get; set; }
        public string CategoryName { get; set; }
        public string CategoryUrl { get; set; }

        public StoreClass Store { get; set; }
    }
}
