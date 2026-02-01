using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace PriceSafari.Services.GoogleScraping
{
    /// <summary>
    /// Singleton przechowujący stan scrapowania Google.
    /// Używany przez GoogleScraperApiController (dla Python API) oraz GoogleScraperService (dla harmonogramu).
    /// </summary>
    public class GoogleScrapingStateService
    {
        // === ZARZĄDZANIE SCRAPERAMI ===
        private readonly ConcurrentDictionary<string, ScraperStatus> _activeScrapers = new();
        private readonly ConcurrentDictionary<int, string> _tasksInProgress = new();
        private readonly object _stateLock = new();

        // === STAN SESJI ===
        private bool _isScrapingEnabled = false;
        private DateTime? _scrapingStartedAt = null;
        private int _totalProcessedInSession = 0;
        private int _totalRejectedInSession = 0;
        private int _totalTasksInSession = 0;
        private int _lastSignalRUpdateAt = 0;
        private readonly Stopwatch _sessionStopwatch = new();

        // === KONFIGURACJA ===
        public const int BATCH_SIZE = 100;
        public const int SCRAPER_TIMEOUT_SECONDS = 120;
        public const int SIGNALR_UPDATE_INTERVAL = 100;

        // === EVENTY ===
        public event Action? OnScrapingCompleted;
        public event Action<int, int, double, int>? OnProgressUpdate; // processed, total, elapsed, rejected

        #region Properties

        public bool IsScrapingEnabled
        {
            get { lock (_stateLock) return _isScrapingEnabled; }
        }

        public DateTime? ScrapingStartedAt
        {
            get { lock (_stateLock) return _scrapingStartedAt; }
        }

        public int TotalProcessedInSession => _totalProcessedInSession;
        public int TotalRejectedInSession => _totalRejectedInSession;
        public int TotalTasksInSession => _totalTasksInSession;
        public int TasksInProgressCount => _tasksInProgress.Count;

        public double ElapsedSeconds => _sessionStopwatch.IsRunning
            ? _sessionStopwatch.Elapsed.TotalSeconds
            : 0;

        public TimeSpan ElapsedTime => _sessionStopwatch.Elapsed;

        public ConcurrentDictionary<string, ScraperStatus> ActiveScrapers => _activeScrapers;
        public ConcurrentDictionary<int, string> TasksInProgress => _tasksInProgress;
        public object StateLock => _stateLock;

        #endregion

        #region Zarządzanie stanem scrapowania

        /// <summary>
        /// Uruchamia scrapowanie
        /// </summary>
        public bool TryStartScraping(int totalTasks)
        {
            lock (_stateLock)
            {
                if (_isScrapingEnabled)
                    return false; // Już działa

                _isScrapingEnabled = true;
                _scrapingStartedAt = DateTime.UtcNow;

                // Reset statystyk sesji
                _totalProcessedInSession = 0;
                _totalRejectedInSession = 0;
                _totalTasksInSession = totalTasks;
                _lastSignalRUpdateAt = 0;
                _tasksInProgress.Clear();

                // Start stopera
                _sessionStopwatch.Restart();

                return true;
            }
        }

        /// <summary>
        /// Zatrzymuje scrapowanie
        /// </summary>
        public (int processed, int rejected, double elapsedSeconds) StopScraping()
        {
            lock (_stateLock)
            {
                _isScrapingEnabled = false;
                _scrapingStartedAt = null;
                _sessionStopwatch.Stop();
                _tasksInProgress.Clear();

                return (_totalProcessedInSession, _totalRejectedInSession, _sessionStopwatch.Elapsed.TotalSeconds);
            }
        }

        /// <summary>
        /// Oznacza scrapowanie jako zakończone (wszystkie zadania przetworzone)
        /// </summary>
        public void MarkAsCompleted()
        {
            lock (_stateLock)
            {
                _isScrapingEnabled = false;
                _sessionStopwatch.Stop();
            }

            OnScrapingCompleted?.Invoke();
        }

        #endregion

        #region Aktualizacja statystyk

        /// <summary>
        /// Dodaje przetworzone wyniki
        /// </summary>
        public void AddProcessedResults(int successCount, int rejectedCount)
        {
            Interlocked.Add(ref _totalProcessedInSession, successCount + rejectedCount);
            Interlocked.Add(ref _totalRejectedInSession, rejectedCount);
        }

        /// <summary>
        /// Sprawdza czy należy wysłać update SignalR (co SIGNALR_UPDATE_INTERVAL wyników)
        /// </summary>
        public bool ShouldSendProgressUpdate(out int currentProcessed)
        {
            currentProcessed = _totalProcessedInSession;
            int lastUpdate = _lastSignalRUpdateAt;

            if (currentProcessed - lastUpdate >= SIGNALR_UPDATE_INTERVAL)
            {
                // Atomowo aktualizuj _lastSignalRUpdateAt
                if (Interlocked.CompareExchange(ref _lastSignalRUpdateAt, currentProcessed, lastUpdate) == lastUpdate)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Wywołuje event postępu
        /// </summary>
        public void RaiseProgressUpdate()
        {
            OnProgressUpdate?.Invoke(
                _totalProcessedInSession,
                _totalTasksInSession,
                _sessionStopwatch.Elapsed.TotalSeconds,
                _totalRejectedInSession
            );
        }

        #endregion

        #region Zarządzanie scraperami

        /// <summary>
        /// Aktualizuje heartbeat scrapera
        /// </summary>
        public void UpdateScraperHeartbeat(string scraperName)
        {
            _activeScrapers.AddOrUpdate(scraperName,
                new ScraperStatus
                {
                    ScraperName = scraperName,
                    LastHeartbeat = DateTime.UtcNow,
                    IsWorking = false,
                    TasksProcessed = 0,
                    TasksInProgress = 0
                },
                (key, existing) =>
                {
                    existing.LastHeartbeat = DateTime.UtcNow;
                    return existing;
                });
        }

        /// <summary>
        /// Oznacza scrapera jako pracującego
        /// </summary>
        public void SetScraperWorking(string scraperName, int tasksCount)
        {
            if (_activeScrapers.TryGetValue(scraperName, out var status))
            {
                status.IsWorking = true;
                status.TasksInProgress = tasksCount;
            }
        }

        /// <summary>
        /// Aktualizuje statystyki scrapera po przetworzeniu
        /// </summary>
        public void UpdateScraperStats(string scraperName, int processedCount)
        {
            if (_activeScrapers.TryGetValue(scraperName, out var status))
            {
                status.TasksProcessed += processedCount;
                status.IsWorking = false;
            }
        }

        /// <summary>
        /// Usuwa martwe scrapery (bez heartbeat przez SCRAPER_TIMEOUT_SECONDS)
        /// </summary>
        public void CleanupDeadScrapers()
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-SCRAPER_TIMEOUT_SECONDS);
            var deadScrapers = _activeScrapers
                .Where(kvp => kvp.Value.LastHeartbeat < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var name in deadScrapers)
            {
                _activeScrapers.TryRemove(name, out _);

                // Zwolnij osieroce tasks
                var orphanedTasks = _tasksInProgress
                    .Where(kvp => kvp.Value == name)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var taskId in orphanedTasks)
                {
                    _tasksInProgress.TryRemove(taskId, out _);
                }
            }
        }

        /// <summary>
        /// Zwraca listę aktywnych scraperów
        /// </summary>
        public List<object> GetActiveScrapersList()
        {
            return _activeScrapers.Values
                .Select(s => new
                {
                    s.ScraperName,
                    s.LastHeartbeat,
                    s.IsWorking,
                    s.TasksProcessed,
                    s.TasksInProgress,
                    SecondsAgo = (int)(DateTime.UtcNow - s.LastHeartbeat).TotalSeconds
                })
                .OrderBy(s => s.ScraperName)
                .Cast<object>()
                .ToList();
        }

        #endregion

        #region Zarządzanie zadaniami

        /// <summary>
        /// Rezerwuje zadania dla scrapera
        /// </summary>
        public void ReserveTasks(List<int> taskIds, string scraperName)
        {
            foreach (var taskId in taskIds)
            {
                _tasksInProgress.TryAdd(taskId, scraperName);
            }
        }

        /// <summary>
        /// Zwalnia zadania po przetworzeniu
        /// </summary>
        public void ReleaseTasks(List<int> taskIds)
        {
            foreach (var taskId in taskIds)
            {
                _tasksInProgress.TryRemove(taskId, out _);
            }
        }

        /// <summary>
        /// Zwraca zbiór ID zadań w trakcie przetwarzania
        /// </summary>
        public HashSet<int> GetTasksInProgressIds()
        {
            return _tasksInProgress.Keys.ToHashSet();
        }

        #endregion

        #region Pomocnicze

        /// <summary>
        /// Pobiera pełny status scrapowania
        /// </summary>
        public ScrapingStatusDto GetFullStatus(int totalTasks, int completedTasks, int rejectedTasks)
        {
            var activeScrapersList = GetActiveScrapersList();

            return new ScrapingStatusDto
            {
                IsEnabled = _isScrapingEnabled,
                StartedAt = _scrapingStartedAt,
                TotalTasks = totalTasks,
                CompletedTasks = completedTasks,
                RejectedTasks = rejectedTasks,
                InProgressTasks = _tasksInProgress.Count,
                RemainingTasks = totalTasks - completedTasks,
                ProgressPercent = totalTasks > 0 ? Math.Round((double)completedTasks / totalTasks * 100, 1) : 0,
                ActiveScrapers = activeScrapersList,
                ActiveScrapersCount = activeScrapersList.Count,
                SessionProcessed = _totalProcessedInSession,
                SessionRejected = _totalRejectedInSession,
                SessionElapsedSeconds = ElapsedSeconds
            };
        }

        #endregion
    }

    #region DTOs

    public class ScraperStatus
    {
        public string ScraperName { get; set; } = "";
        public DateTime LastHeartbeat { get; set; }
        public bool IsWorking { get; set; }
        public int TasksProcessed { get; set; }
        public int TasksInProgress { get; set; }
    }

    public class ScrapingStatusDto
    {
        public bool IsEnabled { get; set; }
        public DateTime? StartedAt { get; set; }
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int RejectedTasks { get; set; }
        public int InProgressTasks { get; set; }
        public int RemainingTasks { get; set; }
        public double ProgressPercent { get; set; }
        public List<object> ActiveScrapers { get; set; } = new();
        public int ActiveScrapersCount { get; set; }
        public int SessionProcessed { get; set; }
        public int SessionRejected { get; set; }
        public double SessionElapsedSeconds { get; set; }
    }

    #endregion
}