using PriceTracker.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations;

namespace PriceTracker.Models
{
    public class AffiliateVerification
    {
        [Key]
        public int AffiliateVerificationId { get; set; }

        public string UserId { get; set; }
        public PriceTrackerUser PriceTrackerUser { get; set; }
        public string? AffiliateDescription { get; set; }
        public bool IsVerified { get; set; } = false;
    }
}