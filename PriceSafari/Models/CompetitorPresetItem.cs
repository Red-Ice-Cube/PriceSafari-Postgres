using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class CompetitorPresetItem
    {
        [Key]
        public int CompetitorPresetItemId { get; set; }

        // Powiązanie z "główną" tabelą presetów
        public int PresetId { get; set; }
        [ForeignKey(nameof(PresetId))]
        public CompetitorPresetClass Preset { get; set; }

        // Nazwa sklepu w danym źródle
        public string StoreName { get; set; }

        // True => Google, False => Ceneo
        public bool IsGoogle { get; set; }

        // Czy używać tego konkurenta do porównania
        public bool UseCompetitor { get; set; } = false;
    }
}
