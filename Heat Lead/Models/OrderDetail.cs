using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Heat_Lead.Models
{
    public class OrderDetail
    {
        [Key]
        public int OrderDetailId { get; set; }

        public int InterceptOrderId { get; set; }
        public int OrderId { get; set; }
        public string ResponseProductId { get; set; }
        public int? ProductId { get; set; }

        [ForeignKey("ProductId")]
        public virtual Product Product { get; set; }

        public int ProductQuantity { get; set; }

        public string OrderNumber { get; set; }

        public bool IsProcessed { get; set; } = false;

        public int? AffiliateLinkId { get; set; }
        public string UserId { get; set; }

        public int? CampaignId { get; set; }

        public DateTime CreationDate { get; set; } = DateTime.Now;
    }
}