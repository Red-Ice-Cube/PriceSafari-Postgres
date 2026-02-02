using System.Collections.Concurrent;

namespace PriceSafari.ScrapersControllers
{

    public enum ScrapingProcessStatus
    {
        Idle,
        Running,
        Paused
    }

    public enum ScraperLiveStatus
    {
        Idle,

        Busy,

        Offline,

        Stopped,
        ResettingNetwork
    }

    // <summary>

    // Reprezentuje pojedynczy scraper (klienta Python)

    // </summary>

    public class HybridScraperClient
    {
        public string Name { get; set; } = string.Empty;
        public ScraperLiveStatus Status { get; set; } = ScraperLiveStatus.Offline;
        public DateTime LastCheckIn { get; set; } = DateTime.MinValue;
        public int? CurrentTaskId { get; set; }
        public string? CurrentBatchId { get; set; }
    }

    // <summary>

    // Paczka URLi przydzielona do scrapera

    // </summary>

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

    // <summary>

    // Statystyki per scraper - do wyświetlania na froncie

    // </summary>

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
        public bool IsManuallyPaused { get; set; }

        public double SuccessRate => TotalUrlsProcessed > 0
            ? Math.Round((double)TotalUrlsSuccess / TotalUrlsProcessed * 100, 1)
            : 0;

        public double UrlsPerMinute { get; set; }
    }

    // <summary>

    // Wpis logu do wyświetlenia na froncie

    // </summary>

    public class ScraperLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string ScraperName { get; set; } = string.Empty;
        public string Level { get; set; } = "INFO";

        public string Message { get; set; } = string.Empty;
        public string? BatchId { get; set; }
    }

    // <summary>

    // Centralny manager scrapowania Allegro - zarządza scraperami, paczkami i statystykami

    // </summary>

    public static class AllegroScrapeManager
    {

        public const int BatchSize = 100;
        public const int BatchTimeoutSeconds = 300;

        public const int ScraperOfflineThresholdSeconds = 60;

        public const int MaxLogEntries = 200;

        public static ScrapingProcessStatus CurrentStatus { get; set; } = ScrapingProcessStatus.Idle;
        public static DateTime? ScrapingStartTime { get; set; }
        public static DateTime? ScrapingEndTime { get; set; }

        // <summary>

        // Aktywne scrapery (key = nazwa scrapera)

        // </summary>

        public static ConcurrentDictionary<string, HybridScraperClient> ActiveScrapers { get; } = new();

        // <summary>

        // Przydzielone paczki (key = batchId)

        // </summary>

        public static ConcurrentDictionary<string, AssignedBatch> AssignedBatches { get; } = new();

        // <summary>

        // Statystyki per scraper (key = nazwa scrapera)

        // </summary>

        public static ConcurrentDictionary<string, ScraperStats> ScraperStatistics { get; } = new();

        // <summary>

        // Logi do wyświetlenia na froncie (ostatnie N wpisów)

        // </summary>

        public static ConcurrentQueue<ScraperLogEntry> RecentLogs { get; } = new();

        private static readonly object _batchAssignmentLock = new();
        private static int _batchCounter = 0;

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

            while (RecentLogs.Count > MaxLogEntries)
            {
                RecentLogs.TryDequeue(out _);
            }
        }

        public static void AddSystemLog(string level, string message)
        {
            AddLog("SYSTEM", level, message);
        }

        // <summary>

        // Rejestruje check-in scrapera (wywoływane przy każdym zapytaniu o zadanie)

        // </summary>

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

                    if (existing.Status == ScraperLiveStatus.Offline)
                    {
                        existing.Status = ScraperLiveStatus.Idle;
                    }
                    existing.LastCheckIn = DateTime.UtcNow;
                    return existing;
                });

            ScraperStatistics.GetOrAdd(scraperName, _ => new ScraperStats
            {
                ScraperName = scraperName,
                FirstSeenAt = DateTime.UtcNow
            });

            return scraper;
        }

        // <summary>

        // Sprawdza czy scraper może otrzymać nowe zadanie

        // </summary>

        public static bool CanScraperReceiveTask(string scraperName)
        {

            if (CurrentStatus != ScrapingProcessStatus.Running)
                return false;

            if (ScraperStatistics.TryGetValue(scraperName, out var stats))
            {
                if (stats.IsManuallyPaused)
                    return false;
            }

            return true;
        }

        // <summary>

        // Zatrzymuje konkretny scraper (indywidualnie)

        // </summary>

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

        // <summary>

        // Wznawia konkretny scraper

        // </summary>

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

        // <summary>

        // Oznacza scrapery bez aktywności jako offline

        // </summary>

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

        // <summary>

        // Generuje unikalny ID paczki

        // </summary>

        public static string GenerateBatchId()
        {
            var counter = Interlocked.Increment(ref _batchCounter);
            return $"BATCH-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{counter:D4}";
        }

        // <summary>

        // Rejestruje przydzieloną paczkę

        // </summary>

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

            if (ScraperStatistics.TryGetValue(scraperName, out var stats))
            {
                stats.CurrentBatchNumber++;
                stats.LastActivityAt = DateTime.UtcNow;
            }

            if (ActiveScrapers.TryGetValue(scraperName, out var scraper))
            {
                scraper.Status = ScraperLiveStatus.Busy;
                scraper.CurrentBatchId = batchId;
                scraper.CurrentTaskId = taskIds.FirstOrDefault();
            }

            AddLog(scraperName, "INFO", $"Przydzielono paczkę: {taskIds.Count} URLi", batchId);
        }

        // <summary>

        // Oznacza paczkę jako ukończoną

        // </summary>

        public static void CompleteBatch(string batchId, int successCount, int failedCount)
        {
            if (!AssignedBatches.TryGetValue(batchId, out var batch))
                return;

            batch.IsCompleted = true;
            batch.CompletedAt = DateTime.UtcNow;
            batch.ProcessedCount = successCount + failedCount;
            batch.SuccessCount = successCount;
            batch.FailedCount = failedCount;

            if (ScraperStatistics.TryGetValue(batch.ScraperName, out var stats))
            {
                stats.TotalUrlsProcessed += batch.ProcessedCount;
                stats.TotalUrlsSuccess += successCount;
                stats.TotalUrlsFailed += failedCount;
                stats.TotalBatchesCompleted++;
                stats.LastActivityAt = DateTime.UtcNow;

                if (stats.FirstSeenAt.HasValue)
                {
                    var totalMinutes = (DateTime.UtcNow - stats.FirstSeenAt.Value).TotalMinutes;
                    if (totalMinutes > 0)
                    {
                        stats.UrlsPerMinute = Math.Round(stats.TotalUrlsProcessed / totalMinutes, 1);
                    }
                }
            }

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

        // <summary>

        // Znajduje paczki które przekroczyły timeout i zwraca ich TaskIds do ponownego przetworzenia

        // </summary>

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

        // <summary>

        // Sprawdza czy są jakieś aktywne (nieukończone) paczki

        // </summary>

        public static bool HasActiveBatches()
        {
            return AssignedBatches.Values.Any(b => !b.IsCompleted && !b.IsTimedOut);
        }

        // <summary>

        // Pobiera aktywną paczkę dla scrapera (jeśli istnieje)

        // </summary>

        public static AssignedBatch? GetActiveScraperBatch(string scraperName)
        {
            return AssignedBatches.Values
                .FirstOrDefault(b => b.ScraperName == scraperName && !b.IsCompleted && !b.IsTimedOut);
        }

        // <summary>

        // Resetuje cały stan managera (przy starcie nowego procesu scrapowania)

        // </summary>

        public static void ResetForNewProcess()
        {
            CurrentStatus = ScrapingProcessStatus.Running;
            ScrapingStartTime = DateTime.UtcNow;
            ScrapingEndTime = null;

            AssignedBatches.Clear();
            _batchCounter = 0;

            foreach (var stats in ScraperStatistics.Values)
            {
                stats.TotalUrlsProcessed = 0;
                stats.TotalUrlsSuccess = 0;
                stats.TotalUrlsFailed = 0;
                stats.TotalBatchesCompleted = 0;
                stats.TotalBatchesTimedOut = 0;
                stats.CurrentBatchNumber = 0;
                stats.UrlsPerMinute = 0;

            }

            while (RecentLogs.TryDequeue(out _)) { }

            AddSystemLog("INFO", "Rozpoczęto nowy proces scrapowania");
        }

        // <summary>

        // Kończy proces scrapowania

        // </summary>

        public static void FinishProcess()
        {
            CurrentStatus = ScrapingProcessStatus.Idle;
            ScrapingEndTime = DateTime.UtcNow;

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

        // <summary>

        // Pobiera podsumowanie dla frontu

        // </summary>

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

        // <summary>

        // Pobiera szczegółowe dane scraperów dla frontu

        // </summary>

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

                        totalUrlsProcessed = stats?.TotalUrlsProcessed ?? 0,
                        totalUrlsSuccess = stats?.TotalUrlsSuccess ?? 0,
                        totalUrlsFailed = stats?.TotalUrlsFailed ?? 0,
                        successRate = stats?.SuccessRate ?? 0,
                        urlsPerMinute = stats?.UrlsPerMinute ?? 0,
                        batchesCompleted = stats?.TotalBatchesCompleted ?? 0,
                        batchesTimedOut = stats?.TotalBatchesTimedOut ?? 0,
                        currentBatchNumber = stats?.CurrentBatchNumber ?? 0,

                        activeBatchTaskCount = activeBatch?.TaskIds.Count ?? 0,
                        activeBatchAssignedAt = activeBatch?.AssignedAt
                    };
                })
                .Cast<object>()
                .ToList();
        }

        // <summary>

        // Pobiera ostatnie logi

        // </summary>

        public static List<ScraperLogEntry> GetRecentLogs(int count = 50)
        {
            return RecentLogs
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToList();
        }
    }
}

