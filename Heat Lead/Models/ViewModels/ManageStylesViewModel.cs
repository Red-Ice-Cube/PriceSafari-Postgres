using Heat_Lead.Models;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class ManageStylesViewModel
{
    public IEnumerable<CanvasJSStyle> Styles { get; set; } = new List<CanvasJSStyle>();
    public int AffiliateLinkId { get; set; }
    public int? StyleId { get; set; }

    public string? ProductName { get; set; }
    public decimal? ProductPrice { get; set; }
    public string? ProductImage { get; set; }

    [Required(ErrorMessage = "Nazwa jest wymagana.")]
    [StringLength(30, ErrorMessage = "Maksymalnie 30 znaków")]
    public string Name { get; set; }

    [StringLength(18, ErrorMessage = "Button text cannot exceed 18 characters.")]
    [RegularExpression(@"^[\p{L}\p{N}\s]*$", ErrorMessage = "Invalid characters in text.")]
    public string? ButtonText { get; set; }

    public string? ButtonColor { get; set; }
    public string? ButtonTextColor { get; set; }
    public string? FrameColor { get; set; }
    public string? FrameTextColor { get; set; }
    public string? FrameExtraTextColor { get; set; }
}