//using System.Collections.Concurrent;
//using PriceSafari.Models;

//namespace PriceSafari.ScrapersControllers
//{

//    public enum ScrapingProcessStatus { Idle, Running, Stopping }

//    public class HybridScraperClient
//    {
//        public string Name { get; set; }
//        public ScraperLiveStatus Status { get; set; }
//        public DateTime LastCheckIn { get; set; }
//        public int? CurrentTaskId { get; set; }
//    }

//    public static class AllegroScrapeManager
//    {

//        public static ScrapingProcessStatus CurrentStatus { get; set; } = ScrapingProcessStatus.Idle;

//        public static readonly ConcurrentDictionary<string, HybridScraperClient> ActiveScrapers = new();
//        public static DateTime? ScrapingStartTime { get; set; }
//        public static DateTime? ScrapingEndTime { get; set; }
//    }
//}




using System.Collections.Concurrent;

namespace PriceSafari.ScrapersControllers
{
    // ===== STATUSY =====
    public enum ScrapingProcessStatus
    {
        Idle,
        Running,
        Paused
    }

    public enum ScraperLiveStatus
    {
        Idle,      // Czeka na zadania
        Busy,      // Przetwarza paczkę
        Offline,   // Nie odpowiada
        Stopped,
        ResettingNetwork
    }

    // ===== MODELE DANYCH =====

    /// <summary>
    /// Reprezentuje pojedynczy scraper (klienta Python)
    /// </summary>
    public class HybridScraperClient
    {
        public string Name { get; set; } = string.Empty;
        public ScraperLiveStatus Status { get; set; } = ScraperLiveStatus.Offline;
        public DateTime LastCheckIn { get; set; } = DateTime.MinValue;
        public int? CurrentTaskId { get; set; }
        public string? CurrentBatchId { get; set; }
    }

    /// <summary>
    /// Paczka URLi przydzielona do scrapera
    /// </summary>
    public class AssignedBatch
    {
        public string BatchId { get; set; } = string.Empty;
        public string ScraperName { get; set; } = string.Empty;
        public List<int> TaskIds { get; set; } = new();
        public DateTime AssignedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsTimedOut { get; set; }
        public int ProcessedCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
    }

    /// <summary>
    /// Statystyki per scraper - do wyświetlania na froncie
    /// </summary>
    public class ScraperStats
    {
        public string ScraperName { get; set; } = string.Empty;
        public int TotalUrlsProcessed { get; set; }
        public int TotalUrlsSuccess { get; set; }
        public int TotalUrlsFailed { get; set; }
        public int TotalBatchesCompleted { get; set; }
        public int TotalBatchesTimedOut { get; set; }
        public int CurrentBatchNumber { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public DateTime? FirstSeenAt { get; set; }
        public bool IsManuallyPaused { get; set; } // Indywidualne zatrzymanie

        // Wyliczane
        public double SuccessRate => TotalUrlsProcessed > 0
            ? Math.Round((double)TotalUrlsSuccess / TotalUrlsProcessed * 100, 1)
            : 0;

        public double UrlsPerMinute { get; set; }
    }

    /// <summary>
    /// Wpis logu do wyświetlenia na froncie
    /// </summary>
    public class ScraperLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string ScraperName { get; set; } = string.Empty;
        public string Level { get; set; } = "INFO"; // INFO, WARNING, ERROR, SUCCESS
        public string Message { get; set; } = string.Empty;
        public string? BatchId { get; set; }
    }

    // ===== GŁÓWNY MANAGER =====

    /// <summary>
    /// Centralny manager scrapowania Allegro - zarządza scraperami, paczkami i statystykami
    /// </summary>
    public static class AllegroScrapeManager
    {
        // === Konfiguracja ===
        public const int BatchSize = 100;
        public const int BatchTimeoutSeconds = 300; // 5 minut
        public const int ScraperOfflineThresholdSeconds = 60; // Po 60s bez kontaktu = offline
        public const int MaxLogEntries = 200;

        // === Stan globalny ===
        public static ScrapingProcessStatus CurrentStatus { get; set; } = ScrapingProcessStatus.Idle;
        public static DateTime? ScrapingStartTime { get; set; }
        public static DateTime? ScrapingEndTime { get; set; }

        // === Kolekcje ===

        /// <summary>
        /// Aktywne scrapery (key = nazwa scrapera)
        /// </summary>
        public static ConcurrentDictionary<string, HybridScraperClient> ActiveScrapers { get; } = new();

        /// <summary>
        /// Przydzielone paczki (key = batchId)
        /// </summary>
        public static ConcurrentDictionary<string, AssignedBatch> AssignedBatches { get; } = new();

        /// <summary>
        /// Statystyki per scraper (key = nazwa scrapera)
        /// </summary>
        public static ConcurrentDictionary<string, ScraperStats> ScraperStatistics { get; } = new();

        /// <summary>
        /// Logi do wyświetlenia na froncie (ostatnie N wpisów)
        /// </summary>
        public static ConcurrentQueue<ScraperLogEntry> RecentLogs { get; } = new();

        // === Lock do operacji przydzielania paczek ===
        private static readonly object _batchAssignmentLock = new();
        private static int _batchCounter = 0;

        // ===== METODY LOGOWANIA =====

        public static void AddLog(string scraperName, string level, string message, string? batchId = null)
        {
            var entry = new ScraperLogEntry
            {
                Timestamp = DateTime.UtcNow,
                ScraperName = scraperName,
                Level = level,
                Message = message,
                BatchId = batchId
            };

            RecentLogs.Enqueue(entry);

            // Utrzymuj max N wpisów
            while (RecentLogs.Count > MaxLogEntries)
            {
                RecentLogs.TryDequeue(out _);
            }
        }

        public static void AddSystemLog(string level, string message)
        {
            AddLog("SYSTEM", level, message);
        }

        // ===== METODY ZARZĄDZANIA SCRAPERAMI =====

        /// <summary>
        /// Rejestruje check-in scrapera (wywoływane przy każdym zapytaniu o zadanie)
        /// </summary>
        public static HybridScraperClient RegisterScraperCheckIn(string scraperName)
        {
            var scraper = ActiveScrapers.AddOrUpdate(
                scraperName,
                new HybridScraperClient
                {
                    Name = scraperName,
                    Status = ScraperLiveStatus.Idle,
                    LastCheckIn = DateTime.UtcNow
                },
                (key, existing) =>
                {
                    // Jeśli był Offline lub Stopped, zmień na Idle
                    if (existing.Status == ScraperLiveStatus.Offline)
                    {
                        existing.Status = ScraperLiveStatus.Idle;
                    }
                    existing.LastCheckIn = DateTime.UtcNow;
                    return existing;
                });

            // Upewnij się że ma wpis w statystykach
            ScraperStatistics.GetOrAdd(scraperName, _ => new ScraperStats
            {
                ScraperName = scraperName,
                FirstSeenAt = DateTime.UtcNow
            });

            return scraper;
        }

        /// <summary>
        /// Sprawdza czy scraper może otrzymać nowe zadanie
        /// </summary>
        public static bool CanScraperReceiveTask(string scraperName)
        {
            // Sprawdź czy proces w ogóle działa
            if (CurrentStatus != ScrapingProcessStatus.Running)
                return false;

            // Sprawdź indywidualne zatrzymanie
            if (ScraperStatistics.TryGetValue(scraperName, out var stats))
            {
                if (stats.IsManuallyPaused)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Zatrzymuje konkretny scraper (indywidualnie)
        /// </summary>
        public static void PauseScraper(string scraperName)
        {
            if (ScraperStatistics.TryGetValue(scraperName, out var stats))
            {
                stats.IsManuallyPaused = true;
            }

            if (ActiveScrapers.TryGetValue(scraperName, out var scraper))
            {
                scraper.Status = ScraperLiveStatus.Stopped;
            }

            AddLog(scraperName, "WARNING", "Scraper zatrzymany ręcznie (hibernacja)");
        }

        /// <summary>
        /// Wznawia konkretny scraper
        /// </summary>
        public static void ResumeScraper(string scraperName)
        {
            if (ScraperStatistics.TryGetValue(scraperName, out var stats))
            {
                stats.IsManuallyPaused = false;
            }

            if (ActiveScrapers.TryGetValue(scraperName, out var scraper))
            {
                scraper.Status = ScraperLiveStatus.Idle;
            }

            AddLog(scraperName, "INFO", "Scraper wznowiony");
        }

        /// <summary>
        /// Oznacza scrapery bez aktywności jako offline
        /// </summary>
        public static List<string> MarkInactiveScrapersAsOffline()
        {
            var markedOffline = new List<string>();
            var threshold = DateTime.UtcNow.AddSeconds(-ScraperOfflineThresholdSeconds);

            foreach (var kvp in ActiveScrapers)
            {
                if (kvp.Value.LastCheckIn < threshold &&
                    kvp.Value.Status != ScraperLiveStatus.Offline &&
                    kvp.Value.Status != ScraperLiveStatus.Stopped)
                {
                    kvp.Value.Status = ScraperLiveStatus.Offline;
                    markedOffline.Add(kvp.Key);
                    AddLog(kvp.Key, "WARNING", $"Scraper oznaczony jako OFFLINE (brak kontaktu > {ScraperOfflineThresholdSeconds}s)");
                }
            }

            return markedOffline;
        }

        // ===== METODY ZARZĄDZANIA PACZKAMI =====

        /// <summary>
        /// Generuje unikalny ID paczki
        /// </summary>
        public static string GenerateBatchId()
        {
            var counter = Interlocked.Increment(ref _batchCounter);
            return $"BATCH-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{counter:D4}";
        }

        /// <summary>
        /// Rejestruje przydzieloną paczkę
        /// </summary>
        public static void RegisterAssignedBatch(string batchId, string scraperName, List<int> taskIds)
        {
            var batch = new AssignedBatch
            {
                BatchId = batchId,
                ScraperName = scraperName,
                TaskIds = taskIds,
                AssignedAt = DateTime.UtcNow
            };

            AssignedBatches[batchId] = batch;

            // Aktualizuj statystyki scrapera
            if (ScraperStatistics.TryGetValue(scraperName, out var stats))
            {
                stats.CurrentBatchNumber++;
                stats.LastActivityAt = DateTime.UtcNow;
            }

            // Aktualizuj status scrapera
            if (ActiveScrapers.TryGetValue(scraperName, out var scraper))
            {
                scraper.Status = ScraperLiveStatus.Busy;
                scraper.CurrentBatchId = batchId;
                scraper.CurrentTaskId = taskIds.FirstOrDefault();
            }

            AddLog(scraperName, "INFO", $"Przydzielono paczkę: {taskIds.Count} URLi", batchId);
        }

        /// <summary>
        /// Oznacza paczkę jako ukończoną
        /// </summary>
        public static void CompleteBatch(string batchId, int successCount, int failedCount)
        {
            if (!AssignedBatches.TryGetValue(batchId, out var batch))
                return;

            batch.IsCompleted = true;
            batch.CompletedAt = DateTime.UtcNow;
            batch.ProcessedCount = successCount + failedCount;
            batch.SuccessCount = successCount;
            batch.FailedCount = failedCount;

            // Aktualizuj statystyki scrapera
            if (ScraperStatistics.TryGetValue(batch.ScraperName, out var stats))
            {
                stats.TotalUrlsProcessed += batch.ProcessedCount;
                stats.TotalUrlsSuccess += successCount;
                stats.TotalUrlsFailed += failedCount;
                stats.TotalBatchesCompleted++;
                stats.LastActivityAt = DateTime.UtcNow;

                // Oblicz prędkość (URLe na minutę)
                if (stats.FirstSeenAt.HasValue)
                {
                    var totalMinutes = (DateTime.UtcNow - stats.FirstSeenAt.Value).TotalMinutes;
                    if (totalMinutes > 0)
                    {
                        stats.UrlsPerMinute = Math.Round(stats.TotalUrlsProcessed / totalMinutes, 1);
                    }
                }
            }

            // Aktualizuj status scrapera na Idle
            if (ActiveScrapers.TryGetValue(batch.ScraperName, out var scraper))
            {
                scraper.Status = ScraperLiveStatus.Idle;
                scraper.CurrentBatchId = null;
                scraper.CurrentTaskId = null;
            }

            var duration = batch.CompletedAt.Value - batch.AssignedAt;
            AddLog(batch.ScraperName, "SUCCESS",
                $"Paczka ukończona: {successCount} OK, {failedCount} błędów (czas: {duration.TotalSeconds:F1}s)",
                batchId);
        }

        /// <summary>
        /// Znajduje paczki które przekroczyły timeout i zwraca ich TaskIds do ponownego przetworzenia
        /// </summary>
        public static List<(string BatchId, string ScraperName, List<int> TaskIds)> FindAndMarkTimedOutBatches()
        {
            var timedOut = new List<(string, string, List<int>)>();
            var threshold = DateTime.UtcNow.AddSeconds(-BatchTimeoutSeconds);

            foreach (var kvp in AssignedBatches)
            {
                var batch = kvp.Value;
                if (!batch.IsCompleted && !batch.IsTimedOut && batch.AssignedAt < threshold)
                {
                    batch.IsTimedOut = true;

                    // Aktualizuj statystyki
                    if (ScraperStatistics.TryGetValue(batch.ScraperName, out var stats))
                    {
                        stats.TotalBatchesTimedOut++;
                    }

                    timedOut.Add((batch.BatchId, batch.ScraperName, batch.TaskIds));

                    AddLog(batch.ScraperName, "ERROR",
                        $"TIMEOUT! Paczka nie została ukończona w {BatchTimeoutSeconds}s. URLe wracają do puli.",
                        batch.BatchId);
                }
            }

            return timedOut;
        }

        /// <summary>
        /// Sprawdza czy są jakieś aktywne (nieukończone) paczki
        /// </summary>
        public static bool HasActiveBatches()
        {
            return AssignedBatches.Values.Any(b => !b.IsCompleted && !b.IsTimedOut);
        }

        /// <summary>
        /// Pobiera aktywną paczkę dla scrapera (jeśli istnieje)
        /// </summary>
        public static AssignedBatch? GetActiveScraperBatch(string scraperName)
        {
            return AssignedBatches.Values
                .FirstOrDefault(b => b.ScraperName == scraperName && !b.IsCompleted && !b.IsTimedOut);
        }

        // ===== METODY RESETOWANIA =====

        /// <summary>
        /// Resetuje cały stan managera (przy starcie nowego procesu scrapowania)
        /// </summary>
        public static void ResetForNewProcess()
        {
            CurrentStatus = ScrapingProcessStatus.Running;
            ScrapingStartTime = DateTime.UtcNow;
            ScrapingEndTime = null;

            AssignedBatches.Clear();
            _batchCounter = 0;

            // Reset statystyk (ale zachowaj scrapery)
            foreach (var stats in ScraperStatistics.Values)
            {
                stats.TotalUrlsProcessed = 0;
                stats.TotalUrlsSuccess = 0;
                stats.TotalUrlsFailed = 0;
                stats.TotalBatchesCompleted = 0;
                stats.TotalBatchesTimedOut = 0;
                stats.CurrentBatchNumber = 0;
                stats.UrlsPerMinute = 0;
                // Nie resetuj IsManuallyPaused - to decyzja użytkownika
            }

            // Wyczyść stare logi
            while (RecentLogs.TryDequeue(out _)) { }

            AddSystemLog("INFO", "Rozpoczęto nowy proces scrapowania");
        }

        /// <summary>
        /// Kończy proces scrapowania
        /// </summary>
        public static void FinishProcess()
        {
            CurrentStatus = ScrapingProcessStatus.Idle;
            ScrapingEndTime = DateTime.UtcNow;

            // Resetuj statusy scraperów
            foreach (var scraper in ActiveScrapers.Values)
            {
                if (scraper.Status == ScraperLiveStatus.Busy)
                {
                    scraper.Status = ScraperLiveStatus.Idle;
                }
                scraper.CurrentBatchId = null;
                scraper.CurrentTaskId = null;
            }

            AddSystemLog("SUCCESS", "Proces scrapowania zakończony");
        }

        // ===== METODY POMOCNICZE DO FRONTU =====

        /// <summary>
        /// Pobiera podsumowanie dla frontu
        /// </summary>
        public static object GetDashboardSummary()
        {
            var onlineScrapers = ActiveScrapers.Values
                .Where(s => s.Status != ScraperLiveStatus.Offline)
                .ToList();

            var activeBatches = AssignedBatches.Values
                .Where(b => !b.IsCompleted && !b.IsTimedOut)
                .ToList();

            return new
            {
                status = CurrentStatus.ToString(),
                startTime = ScrapingStartTime,
                endTime = ScrapingEndTime,
                scrapersOnline = onlineScrapers.Count,
                scrapersBusy = onlineScrapers.Count(s => s.Status == ScraperLiveStatus.Busy),
                activeBatchesCount = activeBatches.Count,
                totalBatchesProcessed = AssignedBatches.Values.Count(b => b.IsCompleted),
                totalBatchesTimedOut = AssignedBatches.Values.Count(b => b.IsTimedOut)
            };
        }

        /// <summary>
        /// Pobiera szczegółowe dane scraperów dla frontu
        /// </summary>
        public static List<object> GetScrapersDetails()
        {
            return ActiveScrapers.Values
                .OrderBy(s => s.Name)
                .Select(scraper =>
                {
                    ScraperStatistics.TryGetValue(scraper.Name, out var stats);
                    var activeBatch = GetActiveScraperBatch(scraper.Name);

                    return new
                    {
                        name = scraper.Name,
                        status = scraper.Status.ToString(),
                        statusCode = (int)scraper.Status,
                        lastCheckIn = scraper.LastCheckIn,
                        currentBatchId = scraper.CurrentBatchId,
                        isManuallyPaused = stats?.IsManuallyPaused ?? false,

                        // Statystyki
                        totalUrlsProcessed = stats?.TotalUrlsProcessed ?? 0,
                        totalUrlsSuccess = stats?.TotalUrlsSuccess ?? 0,
                        totalUrlsFailed = stats?.TotalUrlsFailed ?? 0,
                        successRate = stats?.SuccessRate ?? 0,
                        urlsPerMinute = stats?.UrlsPerMinute ?? 0,
                        batchesCompleted = stats?.TotalBatchesCompleted ?? 0,
                        batchesTimedOut = stats?.TotalBatchesTimedOut ?? 0,
                        currentBatchNumber = stats?.CurrentBatchNumber ?? 0,

                        // Aktywna paczka
                        activeBatchTaskCount = activeBatch?.TaskIds.Count ?? 0,
                        activeBatchAssignedAt = activeBatch?.AssignedAt
                    };
                })
                .Cast<object>()
                .ToList();
        }

        /// <summary>
        /// Pobiera ostatnie logi
        /// </summary>
        public static List<ScraperLogEntry> GetRecentLogs(int count = 50)
        {
            return RecentLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToList();
        }
    }
}
