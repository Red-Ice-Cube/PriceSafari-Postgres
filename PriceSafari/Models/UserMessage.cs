using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class UserMessage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Content { get; set; } // Treść wiadomości w HTML

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsRead { get; set; } = false;

        // Klucz obcy do użytkownika
        [Required]
        public string UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual PriceSafariUser User { get; set; }
    }
}