using ChartJs.Blazor.ChartJS.PieChart;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models
{
    public class Region
    {
        [Key]
        public int RegionId { get; set; }

        public string Name { get; set; } // Nazwa regionu, np. PL, DE
        public string Currency { get; set; } // Waluta regionu, np. PLN, EUR

        public ICollection<PriceData> PriceData { get; set; } = new List<PriceData>();
    }
}
