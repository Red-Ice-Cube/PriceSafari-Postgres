using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PriceSafari.Models
{
    public class CompetitorPresetItem
    {
        [Key]
        public int CompetitorPresetItemId { get; set; }

       
        public int PresetId { get; set; }
        [ForeignKey(nameof(PresetId))]
        public CompetitorPresetClass Preset { get; set; }

 
        public string StoreName { get; set; }


        public DataSourceType DataSource { get; set; }


        public bool UseCompetitor { get; set; } = false;
    }


    public enum PresetType
    {
        PriceComparison, 
        Marketplace      
    }


    public enum DataSourceType
    {
        Google,
        Ceneo,
        Allegro
    }
}
