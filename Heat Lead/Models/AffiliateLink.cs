using Ganss.Xss;
using Heat_Lead.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Heat_Lead.Models
{
    public class AffiliateLink
    {
        [Key]
        public int AffiliateLinkId { get; set; }

        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual Heat_LeadUser? Heat_LeadUser { get; set; }

        public int? CampaignId { get; set; }

        [ForeignKey("CampaignId")]
        public virtual Campaign Campaign { get; set; }

        public int? StoreId { get; set; }

        [ForeignKey("StoreId")]
        public Store? Store { get; set; }

        public string ProductURL { get; set; }

        [ForeignKey("ProductId")]
        public Product? Product { get; set; }

        public int ProductId { get; set; }

        public string CodeAFI { get; set; }

        public string? AffiliateURL { get; set; }

        public string HeatLeadTrackingCode { get; set; }

        public int ClickCount { get; set; }
        public ICollection<AffiliateLinkClick> AffiliateLinkClick { get; set; }

        public int SoldProductsCount { get; set; } = 0;
        public int ExactSoldProductsCount { get; set; } = 0;

        public virtual ICollection<CanvasJS> CanvasJSItems { get; set; } = new List<CanvasJS>();
        public DateTime CreationDate { get; set; } = DateTime.Now;
    }

    public class AffiliateLinkClick
    {
        private string _hLTT;

        [Key]
        public int AffiliateLinkClickId { get; set; }

        public int AffiliateLinkId { get; set; }


        [ForeignKey("AffiliateLinkId")]
        public AffiliateLink AffiliateLink { get; set; }

        public DateTime ClickTime { get; set; }

        [StringLength(24, MinimumLength = 24)]
        [RegularExpression(@"^[a-zA-Z0-9]*$")]
        public string HLTT
        {
            get => _hLTT;
            set => _hLTT = new HtmlSanitizer().Sanitize(value);
        }

        public int OrdersLeft { get; set; }
    }
}