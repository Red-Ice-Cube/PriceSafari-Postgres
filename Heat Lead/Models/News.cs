using System.ComponentModel.DataAnnotations;

namespace Heat_Lead.Models
{
    public class News
    {
        [Key]
        public int NewsId { get; set; }

        [Required]
        [StringLength(80)]
        public string? Title { get; set; }

        [Required]
        public string? Message { get; set; }

        [Url]
        public string? GraphicUrl { get; set; }

        public DateTime CreationDate { get; set; }
    }
}