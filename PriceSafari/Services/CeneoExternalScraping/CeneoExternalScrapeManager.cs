using System.Collections.Concurrent;

namespace PriceSafari.Services.CeneoExternalScraping
{
    public enum CeneoExternalScrapingProcessStatus
    {
        Idle,
        Running,
        Paused
    }

    public enum CeneoExternalScraperLiveStatus
    {
        Idle,
        Busy,
        Offline,
        Stopped,
        SolvingCaptcha
    }

    public class CeneoExternalScraperClient
    {
        public string Name { get; set; } = string.Empty;
        public CeneoExternalScraperLiveStatus Status { get; set; } = CeneoExternalScraperLiveStatus.Offline;
        public DateTime LastCheckIn { get; set; } = DateTime.MinValue;
        public int? CurrentTaskId { get; set; }
        public string? CurrentBatchId { get; set; }
        public string? CurrentIpAddress { get; set; }
        public int CaptchaCount { get; set; } = 0;
    }

    public class CeneoExternalAssignedBatch
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
        public int RejectedCount { get; set; }
    }

    public class CeneoExternalScraperStats
    {
        public string ScraperName { get; set; } = string.Empty;
        public int TotalUrlsProcessed { get; set; }
        public int TotalUrlsSuccess { get; set; }
        public int TotalUrlsFailed { get; set; }
        public int TotalUrlsRejected { get; set; }
        public int TotalBatchesCompleted { get; set; }
        public int TotalBatchesTimedOut { get; set; }
        public int CurrentBatchNumber { get; set; }
        public DateTime? LastActivityAt { get; set; }
        public DateTime? FirstSeenAt { get; set; }
        public bool IsManuallyPaused { get; set; }
        public int TotalPricesCollected { get; set; }
        public int CaptchaCount { get; set; }

        public double SuccessRate => TotalUrlsProcessed > 0
            ? Math.Round((double)TotalUrlsSuccess / TotalUrlsProcessed * 100, 1)
            : 0;

        public double UrlsPerMinute { get; set; }
    }

    public class CeneoExternalScraperLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string ScraperName { get; set; } = string.Empty;
        public string Level { get; set; } = "INFO";
        public string Message { get; set; } = string.Empty;
        public string? BatchId { get; set; }
    }

    public static class CeneoExternalScrapeManager
    {
        public const int BatchSize = 100;
        public const int BatchTimeoutSeconds = 420;
        public const int ScraperOfflineThresholdSeconds = 180;
        public const int MaxLogEntries = 300;

        public static CeneoExternalScrapingProcessStatus CurrentStatus { get; set; } = CeneoExternalScrapingProcessStatus.Idle;
        public static DateTime? ScrapingStartTime { get; set; }
        public static DateTime? ScrapingEndTime { get; set; }

        public static ConcurrentDictionary<string, CeneoExternalScraperClient> ActiveScrapers { get; } = new();
        public static ConcurrentDictionary<string, CeneoExternalAssignedBatch> AssignedBatches { get; } = new();
        public static ConcurrentDictionary<string, CeneoExternalScraperStats> ScraperStatistics { get; } = new();
        public static ConcurrentQueue<CeneoExternalScraperLogEntry> RecentLogs { get; } = new();

        public static readonly object BatchAssignmentLock = new();

        private static int _batchCounter = 0;
        private static Timer? _cleanupTimer;

        public static void AddLog(string scraperName, string level, string message, string? batchId = null)
        {
            var entry = new CeneoExternalScraperLogEntry
            {
                Timestamp = DateTime.UtcNow,
                ScraperName = scraperName,
                Level = level,
                Message = message,
                BatchId = batchId
            };

            RecentLogs.Enqueue(entry);
            while (RecentLogs.Count > MaxLogEntries) RecentLogs.TryDequeue(out _);
        }

        public static void AddSystemLog(string level, string message) => AddLog("SYSTEM", level, message);

        public static CeneoExternalScraperClient RegisterScraperCheckIn(string scraperName, string? ipAddress = null)
        {
            var scraper = ActiveScrapers.AddOrUpdate(
                scraperName,
                new CeneoExternalScraperClient
                {
                    Name = scraperName,
                    Status = CeneoExternalScraperLiveStatus.Idle,
                    LastCheckIn = DateTime.UtcNow,
                    CurrentIpAddress = ipAddress
                },
                (key, existing) =>
                {
                    if (existing.Status == CeneoExternalScraperLiveStatus.Offline)
                    {
                        existing.Status = CeneoExternalScraperLiveStatus.Idle;
                        AddLog(scraperName, "INFO", $"Scraper wrócił online (IP: {ipAddress ?? "unknown"})");
                    }
                    existing.LastCheckIn = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(ipAddress)) existing.CurrentIpAddress = ipAddress;
                    return existing;
                });

            ScraperStatistics.GetOrAdd(scraperName, _ => new CeneoExternalScraperStats
            {
                ScraperName = scraperName,
                FirstSeenAt = DateTime.UtcNow
            });

            return scraper;
        }

        public static bool CanScraperReceiveTask(string scraperName)
        {
            if (CurrentStatus != CeneoExternalScrapingProcessStatus.Running) return false;
            if (ScraperStatistics.TryGetValue(scraperName, out var stats) && stats.IsManuallyPaused) return false;
            return true;
        }

        public static void PauseScraper(string scraperName)
        {
            if (ScraperStatistics.TryGetValue(scraperName, out var stats)) stats.IsManuallyPaused = true;
            if (ActiveScrapers.TryGetValue(scraperName, out var scraper)) scraper.Status = CeneoExternalScraperLiveStatus.Stopped;
            AddLog(scraperName, "WARNING", "Scraper zatrzymany ręcznie (hibernacja)");
        }

        public static void ResumeScraper(string scraperName)
        {
            if (ScraperStatistics.TryGetValue(scraperName, out var stats)) stats.IsManuallyPaused = false;
            if (ActiveScrapers.TryGetValue(scraperName, out var scraper)) scraper.Status = CeneoExternalScraperLiveStatus.Idle;
            AddLog(scraperName, "INFO", "Scraper wznowiony");
        }

        public static void MarkScraperSolvingCaptcha(string scraperName, string? reason = null)
        {
            if (ActiveScrapers.TryGetValue(scraperName, out var scraper))
            {
                scraper.Status = CeneoExternalScraperLiveStatus.SolvingCaptcha;
                scraper.CaptchaCount++;
            }
            if (ScraperStatistics.TryGetValue(scraperName, out var stats))
                stats.CaptchaCount++;

            AddLog(scraperName, "WARNING", $"🧩 Rozwiązywanie CAPTCHA: {reason ?? "wykryto blokadę"}");
        }

        public static void MarkScraperCaptchaSolved(string scraperName)
        {
            if (ActiveScrapers.TryGetValue(scraperName, out var scraper))
            {
                scraper.Status = CeneoExternalScraperLiveStatus.Idle;
            }
            AddLog(scraperName, "SUCCESS", "✅ CAPTCHA rozwiązana - powrót do scrapowania");
        }

        public static List<string> MarkInactiveScrapersAsOffline()
        {
            var markedOffline = new List<string>();
            var threshold = DateTime.UtcNow.AddSeconds(-ScraperOfflineThresholdSeconds);

            foreach (var kvp in ActiveScrapers)
            {
                if (kvp.Value.LastCheckIn < threshold &&
                    kvp.Value.Status != CeneoExternalScraperLiveStatus.Offline &&
                    kvp.Value.Status != CeneoExternalScraperLiveStatus.Stopped)
                {
                    kvp.Value.Status = CeneoExternalScraperLiveStatus.Offline;
                    markedOffline.Add(kvp.Key);
                    AddLog(kvp.Key, "WARNING", $"Scraper OFFLINE (brak kontaktu > {ScraperOfflineThresholdSeconds}s)");
                }
            }
            return markedOffline;
        }

        public static string GenerateBatchId()
        {
            var counter = Interlocked.Increment(ref _batchCounter);
            return $"CENEO-EXT-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{counter:D4}";
        }

        public static HashSet<int> GetAllActiveTaskIds()
        {
            var activeIds = new HashSet<int>();
            lock (BatchAssignmentLock)
            {
                foreach (var batch in AssignedBatches.Values)
                {
                    if (!batch.IsCompleted && !batch.IsTimedOut)
                    {
                        foreach (var taskId in batch.TaskIds) activeIds.Add(taskId);
                    }
                }
            }
            return activeIds;
        }

        public static void RegisterAssignedBatch(string batchId, string scraperName, List<int> taskIds)
        {
            var batch = new CeneoExternalAssignedBatch
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
                scraper.Status = CeneoExternalScraperLiveStatus.Busy;
                scraper.CurrentBatchId = batchId;
                scraper.CurrentTaskId = taskIds.FirstOrDefault();
            }
            AddLog(scraperName, "INFO", $"Przydzielono paczkę: {taskIds.Count} URLi", batchId);
        }

        public static void CompleteBatch(string batchId, int successCount, int failedCount, int rejectedCount, int pricesCollected)
        {
            if (!AssignedBatches.TryGetValue(batchId, out var batch)) return;

            batch.IsCompleted = true;
            batch.CompletedAt = DateTime.UtcNow;
            batch.ProcessedCount = successCount + failedCount + rejectedCount;
            batch.SuccessCount = successCount;
            batch.FailedCount = failedCount;
            batch.RejectedCount = rejectedCount;

            if (ScraperStatistics.TryGetValue(batch.ScraperName, out var stats))
            {
                stats.TotalUrlsProcessed += batch.ProcessedCount;
                stats.TotalUrlsSuccess += successCount;
                stats.TotalUrlsFailed += failedCount;
                stats.TotalUrlsRejected += rejectedCount;
                stats.TotalBatchesCompleted++;
                stats.TotalPricesCollected += pricesCollected;
                stats.LastActivityAt = DateTime.UtcNow;

                if (stats.FirstSeenAt.HasValue)
                {
                    var totalMinutes = (DateTime.UtcNow - stats.FirstSeenAt.Value).TotalMinutes;
                    if (totalMinutes > 0.01)
                        stats.UrlsPerMinute = Math.Round(stats.TotalUrlsProcessed / totalMinutes, 1);
                }
            }

            if (ActiveScrapers.TryGetValue(batch.ScraperName, out var scraper))
            {
                scraper.Status = CeneoExternalScraperLiveStatus.Idle;
                scraper.CurrentBatchId = null;
                scraper.CurrentTaskId = null;
            }

            var duration = batch.CompletedAt.Value - batch.AssignedAt;
            AddLog(batch.ScraperName, "SUCCESS",
                $"Paczka OK: {successCount} sukces, {rejectedCount} odrz., {failedCount} błędy, {pricesCollected} cen ({duration.TotalSeconds:F0}s)",
                batchId);
        }

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
                        stats.TotalBatchesTimedOut++;

                    timedOut.Add((batch.BatchId, batch.ScraperName, batch.TaskIds));
                    AddLog(batch.ScraperName, "ERROR",
                        $"TIMEOUT! Paczka przekroczyła {BatchTimeoutSeconds}s - URLe wracają do puli", batch.BatchId);
                }
            }
            return timedOut;
        }

        public static bool HasActiveBatches() =>
            AssignedBatches.Values.Any(b => !b.IsCompleted && !b.IsTimedOut);

        public static CeneoExternalAssignedBatch? GetActiveScraperBatch(string scraperName) =>
            AssignedBatches.Values.FirstOrDefault(b => b.ScraperName == scraperName && !b.IsCompleted && !b.IsTimedOut);

        private static void OnCleanupTimerTick(object? state)
        {
            try
            {
                if (CurrentStatus != CeneoExternalScrapingProcessStatus.Running) return;
                FindAndMarkTimedOutBatches();
                MarkInactiveScrapersAsOffline();
            }
            catch (Exception ex)
            {
                AddSystemLog("ERROR", $"Błąd Timera: {ex.Message}");
            }
        }

        public static void ResetForNewProcess()
        {
            CurrentStatus = CeneoExternalScrapingProcessStatus.Running;
            ScrapingStartTime = DateTime.UtcNow;
            ScrapingEndTime = null;
            AssignedBatches.Clear();
            _batchCounter = 0;

            foreach (var stats in ScraperStatistics.Values)
            {
                stats.TotalUrlsProcessed = 0;
                stats.TotalUrlsSuccess = 0;
                stats.TotalUrlsFailed = 0;
                stats.TotalUrlsRejected = 0;
                stats.TotalBatchesCompleted = 0;
                stats.TotalBatchesTimedOut = 0;
                stats.TotalPricesCollected = 0;
                stats.CurrentBatchNumber = 0;
                stats.UrlsPerMinute = 0;
                stats.CaptchaCount = 0;
                stats.FirstSeenAt = DateTime.UtcNow;
                stats.LastActivityAt = DateTime.UtcNow;
            }

            foreach (var scraper in ActiveScrapers.Values)
            {
                scraper.CaptchaCount = 0;
                if (scraper.Status == CeneoExternalScraperLiveStatus.Stopped)
                    scraper.Status = CeneoExternalScraperLiveStatus.Idle;
            }

            while (RecentLogs.TryDequeue(out _)) { }

            _cleanupTimer?.Dispose();
            _cleanupTimer = new Timer(OnCleanupTimerTick, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            AddSystemLog("INFO", "🚀 Rozpoczęto nowy proces scrapowania Ceneo External (Timer czyszczący włączony)");
        }

        public static void FinishProcess()
        {
            CurrentStatus = CeneoExternalScrapingProcessStatus.Idle;
            ScrapingEndTime = DateTime.UtcNow;

            _cleanupTimer?.Dispose();
            _cleanupTimer = null;

            foreach (var scraper in ActiveScrapers.Values)
            {
                if (scraper.Status == CeneoExternalScraperLiveStatus.Busy)
                    scraper.Status = CeneoExternalScraperLiveStatus.Idle;
                scraper.CurrentBatchId = null;
                scraper.CurrentTaskId = null;
            }

            AddSystemLog("SUCCESS", "✅ Proces scrapowania Ceneo External zakończony (Timer zatrzymany)");
        }

        public static object GetDashboardSummary()
        {
            var onlineScrapers = ActiveScrapers.Values.Where(s => s.Status != CeneoExternalScraperLiveStatus.Offline).ToList();
            var activeBatches = AssignedBatches.Values.Where(b => !b.IsCompleted && !b.IsTimedOut).ToList();

            var totals = ScraperStatistics.Values.Aggregate(
                new { Success = 0, Failed = 0, Rejected = 0, Prices = 0, Captchas = 0 },
                (acc, s) => new
                {
                    Success = acc.Success + s.TotalUrlsSuccess,
                    Failed = acc.Failed + s.TotalUrlsFailed,
                    Rejected = acc.Rejected + s.TotalUrlsRejected,
                    Prices = acc.Prices + s.TotalPricesCollected,
                    Captchas = acc.Captchas + s.CaptchaCount
                });

            return new
            {
                status = CurrentStatus.ToString(),
                startTime = ScrapingStartTime,
                endTime = ScrapingEndTime,
                scrapersOnline = onlineScrapers.Count,
                scrapersBusy = onlineScrapers.Count(s => s.Status == CeneoExternalScraperLiveStatus.Busy),
                scrapersSolvingCaptcha = onlineScrapers.Count(s => s.Status == CeneoExternalScraperLiveStatus.SolvingCaptcha),
                activeBatchesCount = activeBatches.Count,
                totalBatchesProcessed = AssignedBatches.Values.Count(b => b.IsCompleted),
                totalBatchesTimedOut = AssignedBatches.Values.Count(b => b.IsTimedOut),
                totalUrlsSuccess = totals.Success,
                totalUrlsFailed = totals.Failed,
                totalUrlsRejected = totals.Rejected,
                totalPricesCollected = totals.Prices,
                totalCaptchaEvents = totals.Captchas
            };
        }

        public static List<object> GetScrapersDetails()
        {
            return ActiveScrapers.Values.OrderBy(s => s.Name).Select(scraper =>
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
                    currentIpAddress = scraper.CurrentIpAddress,
                    isManuallyPaused = stats?.IsManuallyPaused ?? false,
                    totalUrlsProcessed = stats?.TotalUrlsProcessed ?? 0,
                    totalUrlsSuccess = stats?.TotalUrlsSuccess ?? 0,
                    totalUrlsFailed = stats?.TotalUrlsFailed ?? 0,
                    totalUrlsRejected = stats?.TotalUrlsRejected ?? 0,
                    totalPricesCollected = stats?.TotalPricesCollected ?? 0,
                    successRate = stats?.SuccessRate ?? 0,
                    urlsPerMinute = stats?.UrlsPerMinute ?? 0,
                    batchesCompleted = stats?.TotalBatchesCompleted ?? 0,
                    batchesTimedOut = stats?.TotalBatchesTimedOut ?? 0,
                    currentBatchNumber = stats?.CurrentBatchNumber ?? 0,
                    captchaCount = stats?.CaptchaCount ?? 0,
                    activeBatchTaskCount = activeBatch?.TaskIds.Count ?? 0,
                    activeBatchAssignedAt = activeBatch?.AssignedAt
                };
            }).Cast<object>().ToList();
        }

        public static List<CeneoExternalScraperLogEntry> GetRecentLogs(int count = 100) =>
            RecentLogs.OrderByDescending(l => l.Timestamp).Take(count).ToList();
    }
}