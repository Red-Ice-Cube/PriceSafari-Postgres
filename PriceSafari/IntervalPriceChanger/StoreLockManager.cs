using System.Collections.Concurrent;

namespace PriceSafari.IntervalPriceChanger
{
    /// <summary>
    /// Globalny, statyczny lock per sklep.
    /// 
    /// Zapobiega jednoczesnemu odpytywaniu API Allegro (lub sklepu)
    /// przez główny automat i system interwałowy.
    /// 
    /// ZASADA: Główny automat ma ZAWSZE pierwszeństwo.
    /// - Główny automat → AcquireAsync() — czeka na lock (z timeoutem)
    /// - Interwał → TryAcquire() — natychmiast, jeśli zajęty → pomija sklep
    /// 
    /// Użycie:
    ///   using var storeLock = await StoreLockManager.AcquireAsync(storeId);
    ///   if (storeLock == null) { /* timeout — pomiń */ }
    /// </summary>
    public static class StoreLockManager
    {
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

        /// <summary>
        /// Asynchronicznie czeka na lock (dla głównego automatu).
        /// Zwraca null jeśli nie uda się zdobyć w timeout.
        /// </summary>
        public static async Task<StoreLock?> AcquireAsync(int storeId, TimeSpan? timeout = null)
        {
            var semaphore = _locks.GetOrAdd(storeId, _ => new SemaphoreSlim(1, 1));
            var actualTimeout = timeout ?? TimeSpan.FromMinutes(10);

            bool acquired = await semaphore.WaitAsync(actualTimeout);
            return acquired ? new StoreLock(storeId, semaphore) : null;
        }

        /// <summary>
        /// Próba natychmiastowego zdobycia locka (dla interwału — bez czekania).
        /// Zwraca null jeśli sklep jest zajęty.
        /// </summary>
        public static StoreLock? TryAcquire(int storeId)
        {
            var semaphore = _locks.GetOrAdd(storeId, _ => new SemaphoreSlim(1, 1));
            bool acquired = semaphore.Wait(0);
            return acquired ? new StoreLock(storeId, semaphore) : null;
        }

        /// <summary>
        /// Sprawdza czy sklep jest aktualnie zablokowany.
        /// </summary>
        public static bool IsLocked(int storeId)
        {
            return _locks.TryGetValue(storeId, out var semaphore) && semaphore.CurrentCount == 0;
        }
    }

    public sealed class StoreLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        internal StoreLock(int storeId, SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _semaphore.Release();
            }
        }
    }
}