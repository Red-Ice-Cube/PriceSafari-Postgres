using System.Collections.Concurrent;
using PriceSafari.Models; // Załóżmy, że ScraperClient jest w tym namespace

namespace PriceSafari.ScrapersControllers
{
    // Definicja statusu całego procesu scrapowania
    public enum ScrapingProcessStatus { Idle, Running, Stopping }

    // Prosta klasa do reprezentowania klienta scrapera (można użyć istniejącej)
    public class HybridScraperClient
    {
        public string Name { get; set; }
        public ScraperLiveStatus Status { get; set; } // Używamy enum z poprzedniego kodu
        public DateTime LastCheckIn { get; set; }
        public int? CurrentTaskId { get; set; } // Zmieniamy z string na int?
    }

    /// <summary>
    /// Zarządza stanem procesu scrapowania szczegółów ofert Allegro.
    /// </summary>
    public static class AllegroScrapeManager
    {
        /// <summary>
        /// Globalny status całego procesu scrapowania (włączony/wyłączony).
        /// </summary>
        public static ScrapingProcessStatus CurrentStatus { get; set; } = ScrapingProcessStatus.Idle;

        /// <summary>
        /// Lista scraperów w Pythonie, które aktywnie odpytują API o zadania.
        /// </summary>
        public static readonly ConcurrentDictionary<string, HybridScraperClient> ActiveScrapers = new();
    }
}