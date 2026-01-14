using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class ProductGoogleCatalog
    {
        [Key]
        public int Id { get; set; }

        public int ProductId { get; set; }

        [ForeignKey("ProductId")]
        public ProductClass Product { get; set; }

        // Cid jest teraz opcjonalny (string?), bo możemy mieć tylko Hid
        public string? GoogleCid { get; set; }

        public string GoogleGid { get; set; }

        // TO JEST POLE, KTÓREGO BRAKOWAŁO (GoogleUrl)
        public string GoogleUrl { get; set; }

        // Nowe pole, o które prosiłeś
        public string? GoogleHid { get; set; }

        public DateTime FoundDate { get; set; } = DateTime.UtcNow;
    }
}