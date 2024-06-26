using Heat_Lead.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Heat_Lead.Models
{
    public class CanvasJS
    {
        [Key]
        public int CanvasJSId { get; set; }

        public string SecCan { get; set; }

        public string ScriptName { get; set; }

        [Required]
        public int AffiliateLinkId { get; set; }

        [ForeignKey("AffiliateLinkId")]
        public virtual AffiliateLink AffiliateLink { get; set; }

        public string ScriptCode { get; set; }

        public int? StyleId { get; set; }

        [ForeignKey("StyleId")]
        public virtual CanvasJSStyle Style { get; set; }

        public string? ProductImage { get; set; }

        public string? ProductName { get; set; }
        public decimal? ProductPrice { get; set; }
    }

    public class CanvasJSStyle
    {
        [Key]
        public int CanvasJSStyleId { get; set; }

        public string CanvaStyleName { get; set; }
        public string UserId { get; set; }
        public Heat_LeadUser User { get; set; }

        [StringLength(18, ErrorMessage = "Button text cannot exceed 18 characters.")]
        [RegularExpression(@"^[\p{L}\p{N}\s]*$", ErrorMessage = "Invalid characters in text.")]
        public string? ButtonText { get; set; }

        public string? ButtonColor { get; set; }
        public string? ButtonTextColor { get; set; }
        public string? FrameColor { get; set; }
        public string? FrameTextColor { get; set; }
        public string? ExtraTextColor { get; set; }
    }
}