using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class ScrapeRun
    {
        [Key]
        public int ScrapeRunId { get; set; }

        public DateTime StartTime { get; set; }
 

        public ICollection<PriceData> PriceData { get; set; } = new List<PriceData>();
    }
}
