// Plik: ScrapersControllers/AllegroGatherManager.cs
using System.Collections.Concurrent;
using System.Threading;

namespace PriceSafari.ScrapersControllers
{
    // Status zadania zbierania produktów
    public enum ScrapingStatus
    {
        Pending,
        Running,
        Cancelled
    }

    // Stan zadania zbierania
    public class ScrapingTaskState
    {
        public ScrapingStatus Status { get; set; } = ScrapingStatus.Pending;
        public string? AssignedScraperName { get; set; }
        public string LastProgressMessage { get; set; } = "Oczekuje na rozpoczęcie...";
        public DateTime LastUpdateTime { get; set; } = DateTime.UtcNow;

        private int _collectedOffersCount = 0;
        public int CollectedOffersCount => _collectedOffersCount;

        public void IncrementOffers(int count)
        {
            Interlocked.Add(ref _collectedOffersCount, count);
        }
    }

    // Klient scrapera dla Gather (używa ScraperLiveStatus z AllegroScrapeManager)
    public class ScraperClient
    {
        public string Name { get; set; } = string.Empty;
        public ScraperLiveStatus Status { get; set; }  // Enum z AllegroScrapeManager
        public string? CurrentTaskUsername { get; set; }
        public DateTime LastCheckIn { get; set; }
    }

    // Manager dla Gather (oddzielny od AllegroScrapeManager)
    public static class AllegroGatherManager
    {
        public static readonly ConcurrentDictionary<string, ScrapingTaskState> ActiveTasks = new();
        public static readonly ConcurrentDictionary<string, ScraperClient> ActiveScrapers = new();
    }
}