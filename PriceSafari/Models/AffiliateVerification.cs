
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class AffiliateVerification
    {
        [Key]
        public int AffiliateVerificationId { get; set; }

        public string UserId { get; set; }
        public PriceSafariUser PriceSafariUser { get; set; }
        //public string? AffiliateDescription { get; set; }
        public bool IsVerified { get; set; } = false;
    }
}