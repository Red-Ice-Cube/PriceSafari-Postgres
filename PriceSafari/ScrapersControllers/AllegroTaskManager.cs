//using System.Collections.Concurrent;
//using System.Threading;

//namespace PriceSafari.ScrapersControllers
//{
//    public enum ScrapingStatus { Pending, Running, Cancelled }
//    public enum ScraperLiveStatus
//    {
//        Idle,
//        Busy,
//        Offline,
//        ResettingNetwork
//    }

//    public class ScrapingTaskState
//    {
//        public ScrapingStatus Status { get; set; } = ScrapingStatus.Pending;
//        public string? AssignedScraperName { get; set; }
//        public string LastProgressMessage { get; set; } = "Oczekuje na rozpoczęcie...";
//        public DateTime LastUpdateTime { get; set; } = DateTime.UtcNow;

//        private int _collectedOffersCount = 0;
//        public int CollectedOffersCount => _collectedOffersCount;

//        public void IncrementOffers(int count)
//        {
//            Interlocked.Add(ref _collectedOffersCount, count);
//        }
//    }

//    public class ScraperClient
//    {
//        public string Name { get; set; }
//        public ScraperLiveStatus Status { get; set; }
//        public string? CurrentTaskUsername { get; set; }
//        public DateTime LastCheckIn { get; set; }
//    }

//    public static class AllegroTaskManager
//    {

//        public static readonly ConcurrentDictionary<string, ScrapingTaskState> ActiveTasks = new();

//        public static readonly ConcurrentDictionary<string, ScraperClient> ActiveScrapers = new();
//    }
//}