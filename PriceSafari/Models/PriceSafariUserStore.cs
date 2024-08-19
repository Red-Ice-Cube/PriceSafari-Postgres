using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace PriceSafari.Models
{
    public class PriceSafariUserStore
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("PriceSafariUser")]
        public string UserId { get; set; }
        public PriceSafariUser PriceSafariUser { get; set; }

        [ForeignKey("StoreClass")]
        public int StoreId { get; set; }
        public StoreClass StoreClass { get; set; }
    }
}
