using Heat_Lead.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Heat_Lead.Models
{
    public class Wallet
    {
        [Key]
        public int WalletId { get; set; }

        public decimal TotalEarnings { get; set; } = 0.00M;

        public decimal ReadyEarnings { get; set; } = 0.00M;

        public decimal PaidEarnings { get; set; } = 0.00M;

        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual Heat_LeadUser Heat_LeadUser { get; set; }

        public virtual ICollection<Paycheck> Paycheck { get; set; }
    }
}