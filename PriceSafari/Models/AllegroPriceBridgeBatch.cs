using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{

    public class AllegroPriceBridgeBatch
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public DateTime ExecutionDate { get; set; } = DateTime.UtcNow;

        [Required]
        public int StoreId { get; set; }
        [ForeignKey("StoreId")]
        public virtual StoreClass Store { get; set; }

        [Required]
        public int AllegroScrapeHistoryId { get; set; }
        [ForeignKey("AllegroScrapeHistoryId")]
        public virtual AllegroScrapeHistory AllegroScrapeHistory { get; set; }

        public string UserId { get; set; }
        [ForeignKey("UserId")]
        public virtual PriceSafariUser User { get; set; }

        public int SuccessfulCount { get; set; } = 0;

        public int FailedCount { get; set; } = 0;

        public virtual ICollection<AllegroPriceBridgeItem> BridgeItems { get; set; } = new List<AllegroPriceBridgeItem>();
    }
}