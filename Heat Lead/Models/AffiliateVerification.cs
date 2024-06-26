using Heat_Lead.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations;

namespace Heat_Lead.Models
{
    public class AffiliateVerification
    {
        [Key]
        public int AffiliateVerificationId { get; set; }

        public string UserId { get; set; }
        public Heat_LeadUser Heat_LeadUser { get; set; }
        public string? AffiliateDescription { get; set; }
        public bool IsVerified { get; set; } = false;
    }
}