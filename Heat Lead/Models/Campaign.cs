using Heat_Lead.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Heat_Lead.Models
{
    public class Campaign
    {
        [Key]
        public int CampaignId { get; set; }

        public string CampaignName { get; set; }
        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual Heat_LeadUser Heat_LeadUser { get; set; }

        public virtual ICollection<CampaignProduct> CampaignProducts { get; set; }

        public virtual ICollection<CampaignCategory> CampaignCategories { get; set; }

        public virtual ICollection<AffiliateLink> AffiliateLinks { get; set; }

        public int Position { get; set; } = int.MaxValue;

        public bool IsActive { get; set; } = true;
        public DateTime CreationDate { get; set; } = DateTime.Now;
    }

    public class CampaignProduct
    {
        public int CampaignId { get; set; }
        public Campaign Campaign { get; set; }

        public int ProductId { get; set; }
        public Product Product { get; set; }
    }

    public class CampaignCategory
    {
        public int CampaignId { get; set; }
        public Campaign Campaign { get; set; }

        public int CategoryId { get; set; }
        public Category Category { get; set; }
    }
}