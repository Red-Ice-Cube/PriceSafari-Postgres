using System;
using System.ComponentModel.DataAnnotations;
using Ganss.Xss;

namespace Heat_Lead.Models
{
    public class InterceptOrder
    {
        private string _hltt;
        private string _orderId;
        private string _orderKey;

        [Key]
        public int InterceptOrderId { get; set; }

        [Required]
        [RegularExpression(@"^\d+$")]
        public string OrderId
        {
            get => _orderId;
            set => _orderId = new HtmlSanitizer().Sanitize(value);
        }

        [Required]
        [StringLength(40, MinimumLength = 1)]
        [RegularExpression(@"^[a-zA-Z0-9]*$")]
        public string OrderKey
        {
            get => _orderKey;
            set => _orderKey = new HtmlSanitizer().Sanitize(value);
        }

        [Required]
        [StringLength(24, MinimumLength = 24)]
        [RegularExpression(@"^[a-zA-Z0-9]*$")]
        public string HLTT
        {
            get => _hltt;
            set => _hltt = new HtmlSanitizer().Sanitize(value);
        }

        public int AffiliateLinkId { get; set; }

        public DateTime OrderDateTime { get; set; } = DateTime.Now;

        public bool IsProcessed { get; set; } = false;
    }
}
