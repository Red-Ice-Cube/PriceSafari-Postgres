using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace PriceTracker.Models
{
    public class PriceTrackerUserStore
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("PriceTrackerUser")]
        public string UserId { get; set; }
        public PriceTrackerUser PriceTrackerUser { get; set; }

        [ForeignKey("StoreClass")]
        public int StoreId { get; set; }
        public StoreClass StoreClass { get; set; }
    }
}
