using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization; // Ważne dla uniknięcia pętli przy AJAX

namespace PriceSafari.Models
{
    public class ContactLabel
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } // Nazwa, np. "VIP", "Zadzwonić później"

        [Required]
        public string HexColor { get; set; } = "#007bff"; // Domyślny kolor

        // Relacja zwrotna (JsonIgnore zapobiega błędom przy serializacji do JS)
        [JsonIgnore]
        public virtual ICollection<ClientProfile> ClientProfiles { get; set; }
    }
}