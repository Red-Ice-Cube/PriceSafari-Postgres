using PriceSafari.Models; // Upewnij się, że ten using jest dodany, aby mieć dostęp do enumów!
using System.Collections.Generic;

namespace PriceSafari.Models.ViewModels
{
    public class CompetitorPresetViewModel
    {
        public int PresetId { get; set; }
        public int StoreId { get; set; }
        public string PresetName { get; set; }

        public PresetType Type { get; set; } // <-- ZMIANA: Dodano typ presetu

        public bool NowInUse { get; set; }
        public bool SourceGoogle { get; set; }
        public bool SourceCeneo { get; set; }
        public bool UseUnmarkedStores { get; set; }

        public List<CompetitorItemDto> Competitors { get; set; }
    }

    public class CompetitorItemDto
    {
        public string StoreName { get; set; }

        // public bool IsGoogle { get; set; } // <-- ZMIANA: Usunięto
        public DataSourceType DataSource { get; set; } // <-- ZMIANA: Dodano źródło danych

        public bool UseCompetitor { get; set; }
    }
}