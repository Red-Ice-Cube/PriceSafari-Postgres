using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace PriceSafari.Models
{
    public class FlagsClass
    {
        [Key]
        public int FlagId { get; set; }

        [Required]
        public string FlagName { get; set; }

        [Required]
        public string FlagColor { get; set; }

        public int StoreId { get; set; }
        public bool IsMarketplace { get; set; } = false;

        public ICollection<ProductFlag> ProductFlags { get; set; } = new List<ProductFlag>();
    }
}
