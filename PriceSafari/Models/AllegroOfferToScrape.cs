using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace PriceSafari.Models
{

    public class AllegroOfferToScrape
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string AllegroOfferUrl { get; set; }

  
        public List<int> AllegroProductIds { get; set; } = new List<int>();


        public bool IsScraped { get; set; } = false;

 
        public bool IsRejected { get; set; } = false;


        public DateTime AddedDate { get; set; } = DateTime.UtcNow;

        public int CollectedPricesCount { get; set; } = 0;
    }
}