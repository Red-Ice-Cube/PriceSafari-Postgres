using System.Collections.Concurrent;
using PriceSafari.Models;

namespace PriceSafari.ScrapersControllers
{

    public enum ScrapingProcessStatus { Idle, Running, Stopping }

    public class HybridScraperClient
    {
        public string Name { get; set; }
        public ScraperLiveStatus Status { get; set; }
        public DateTime LastCheckIn { get; set; }
        public int? CurrentTaskId { get; set; }
    }

    public static class AllegroScrapeManager
    {

        public static ScrapingProcessStatus CurrentStatus { get; set; } = ScrapingProcessStatus.Idle;

        public static readonly ConcurrentDictionary<string, HybridScraperClient> ActiveScrapers = new();
    }
}