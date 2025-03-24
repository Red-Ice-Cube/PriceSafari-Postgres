using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace PriceSafari.Models
{
    public class CompetitorPresetClass
    {
        [Key]
        public int PresetId { get; set; }

        // Który sklep (StoreId) dotyczy tego preset-u
        public int StoreId { get; set; }
        [ForeignKey(nameof(StoreId))]
        public StoreClass Store { get; set; }

        public string PresetName { get; set; }

        // Czy nasz sklep liczymy jako Google?
        public bool SourceGoogle { get; set; } = true;

        // Czy nasz sklep liczymy jako Ceneo?
        public bool SourceCeneo { get; set; } = true;

        // Czy używamy sklepów, których użytkownik nie ustawił wprost?
        public bool UseUnmarkedStores { get; set; } = true;


        public bool NowInUse { get; set; }
        // Lista konkurentów zdefiniowanych w tym presecie
        public List<CompetitorPresetItem> CompetitorItems { get; set; } = new List<CompetitorPresetItem>();

    }
}
