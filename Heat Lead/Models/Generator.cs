using Heat_Lead.Areas.Identity.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Heat_Lead.Models
{
    public class Generator
    {
        [Key]
        public int CodeId { get; set; }

        public string? CodeAFI { get; set; }
        public string? UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual Heat_LeadUser? Heat_LeadUser { get; set; }

        public string? CodePAR { get; set; }

        public int? CategoryId { get; set; }

        [ForeignKey("CategoryId")]
        public Category? Category { get; set; }

        public string? CodeCAT { get; set; }
        public int? StoreId { get; set; }

        public string? CodeSTO { get; set; }

        [ForeignKey("ProductId")]
        public Product? Produkty { get; set; }

        public int? ProductId { get; set; }

        [ForeignKey("StoreId")]
        public Store? Store { get; set; }
    }
}