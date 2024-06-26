using Heat_Lead.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Heat_Lead.Models
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }

        public string OrderNumber { get; set; }

        public decimal AffiliateCommision { get; set; }

        public string ResponseProductId { get; set; }

        public string? CodeAFI { get; set; }

        public int? InterceptOrderId { get; set; }

        public decimal ProductPrice { get; set; }

        public int Amount { get; set; }

        public int CategoryId { get; set; }

        [ForeignKey("ProductId")]
        public int? ProductId { get; set; }

        public virtual Product Product { get; set; }

        public DateTime CreationDate { get; set; } = DateTime.Now;

        public string? UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual Heat_LeadUser? Heat_LeadUser { get; set; }

        [ForeignKey("AffiliateLinkId")]
        public int? AffiliateLinkId { get; set; }

        public virtual AffiliateLink? AffiliateLink { get; set; }

        public DateTime? ValidationEndDate { get; set; }

        public bool IsDeleted { get; set; } = false;

        public bool InValidation { get; set; }

        public bool IsAccepted { get; set; } = true;
        public bool InWallet { get; set; } = false;

        public void UpdateValidationStatus()
        {
            if (ValidationEndDate.HasValue)
            {
                InValidation = DateTime.Now <= ValidationEndDate.Value;
            }
            else
            {
                InValidation = false;
            }
        }

        public void EnsureAffiliationInfoPresent()
        {
            if (string.IsNullOrEmpty(CodeAFI) && (!InterceptOrderId.HasValue || InterceptOrderId.Value == 0) && !AffiliateLinkId.HasValue)
            {
                throw new InvalidOperationException("Order must have a CodeAFI, InterceptOrderId, or AffiliateLinkId.");
            }
        }
    }
}