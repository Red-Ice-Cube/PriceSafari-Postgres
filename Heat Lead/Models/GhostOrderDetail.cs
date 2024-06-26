using System.ComponentModel.DataAnnotations;

namespace Heat_Lead.Models
{
    public class GhostOrderDetail
    {
        [Key]
        public int GhostOrderDetailId { get; set; }

        public int OrderId { get; set; }
        public string ResponseProductId { get; set; }
        public int ProductQuantity { get; set; }
        public int InterceptOrderId { get; set; }
        public string OrderNumber { get; set; }
        public int AffiliateLinkId { get; set; }
        public string UserId { get; set; }
        public DateTime CreationDate { get; set; } = DateTime.Now;

        public bool OverTime { get; set; }
        public bool UnknowProduct { get; set; }
    }
}