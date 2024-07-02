using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace PriceTracker.Models
{
    public class ProductFlag
    {
        [Key]
        public int ProductFlagId { get; set; }

        [ForeignKey("Product")]
        public int ProductId { get; set; }
        public ProductClass Product { get; set; }

        [ForeignKey("Flag")]
        public int FlagId { get; set; }
        public FlagsClass Flag { get; set; }
    }
}
