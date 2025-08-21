using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

        [ForeignKey("Store")]
        public int StoreId { get; set; }
        public StoreClass Store { get; set; }

        public bool IsMarketplace { get; set; } = false;

        public ICollection<ProductFlag> ProductFlags { get; set; } = new List<ProductFlag>();
    }
}