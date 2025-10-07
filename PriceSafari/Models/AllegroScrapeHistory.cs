using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{

    public class AllegroScrapeHistory
    {
        [Key]
        public int Id { get; set; }

        public DateTime Date { get; set; } = DateTime.UtcNow;

        
        public int StoreId { get; set; }

        [ForeignKey("StoreId")]
        public virtual StoreClass Store { get; set; }

        
        public int ProcessedUrlsCount { get; set; }

     
        public int SavedOffersCount { get; set; }

        public virtual ICollection<AllegroPriceHistory> PriceHistories { get; set; } = new List<AllegroPriceHistory>();
    }
}