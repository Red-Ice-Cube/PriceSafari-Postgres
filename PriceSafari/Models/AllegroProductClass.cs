using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class AllegroProductClass
    {
        [Key]
        public int AllegroProductId { get; set; }

        [ForeignKey("StoreClass")]
        public int StoreId { get; set; }
        public StoreClass Store { get; set; }


        public string AllegroProductName { get; set; }

        public string AllegroOfferUrl { get; set; }


        public DateTime AddedDate { get; set; } = DateTime.UtcNow;


        public decimal? MarginPrice { get; set; }


        public bool IsScrapable { get; set; } = false;  
        public bool IsRejected { get; set; } = false; 


        public ICollection<ProductFlag> ProductFlags { get; set; } = new List<ProductFlag>();
    }
}
