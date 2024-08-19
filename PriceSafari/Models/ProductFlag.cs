using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class ProductFlag
    {
        [Key, Column(Order = 0)]
        [ForeignKey("Product")]
        public int ProductId { get; set; }
        public ProductClass Product { get; set; }

        [Key, Column(Order = 1)]
        [ForeignKey("Flag")]
        public int FlagId { get; set; }
        public FlagsClass Flag { get; set; }
    }
}
