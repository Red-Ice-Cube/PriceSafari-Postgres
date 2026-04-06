using PriceSafari.IntervalPriceChanger.Services;

namespace PriceSafari.IntervalPriceChanger
{
    /// <summary>
    /// Osobny BackgroundService dla interwałowych zmian cenowych.
    /// 
    /// Co 10 minut (na "okrągłych" minutach: 10:00, 10:10, 10:20, ...)
    /// sprawdza, czy jakieś interwały mają aktywny slot i wykonuje zmiany.
    /// 
    /// Wymaga klucza ENV: INTERVAL_EXEC_KEY = "49204718"
    /// </summary>
    public class IntervalPriceBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<IntervalPriceBackgroundService> _logger;

        private const string EXPECTED_KEY = "49204718";
        private const int SLOT_DURATION_MINUTES = 10;

        public IntervalPriceBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<IntervalPriceBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var envKey = Environment.GetEnvironmentVariable("INTERVAL_EXEC_KEY");
            var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "UnknownDevice";

            if (envKey != EXPECTED_KEY)
            {
                _logger.LogInformation(
                    "⏱️ [IntervalBG] Urządzenie '{Device}' nie ma klucza INTERVAL_EXEC_KEY. Serwis wyłączony.",
                    deviceName);
                return;
            }

            _logger.LogInformation(
                "⏱️ [IntervalBG] Urządzenie '{Device}' — serwis interwałowy AKTYWNY.",
                deviceName);

            // Czekaj na najbliższą "okrągłą" 10-minutówkę
            await WaitForNextSlotBoundary(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var executionService = scope.ServiceProvider
                        .GetRequiredService<IntervalPriceExecutionService>();

                    await executionService.ExecutePendingIntervalsAsync(deviceName, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ [IntervalBG] Nieoczekiwany błąd w pętli głównej.");
                }

                await WaitForNextSlotBoundary(stoppingToken);
            }

            _logger.LogInformation("⏱️ [IntervalBG] Serwis zatrzymany.");
        }

        /// <summary>
        /// Czeka do najbliższej granicy 10-minutowej.
        /// Np. 14:23 → czeka do 14:30.
        /// </summary>
        private async Task WaitForNextSlotBoundary(CancellationToken ct)
        {
            var now = DateTime.Now;
            int currentMinute = now.Minute;
            int nextSlotMinute = ((currentMinute / SLOT_DURATION_MINUTES) + 1) * SLOT_DURATION_MINUTES;

            DateTime nextSlot;
            if (nextSlotMinute >= 60)
            {
                nextSlot = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0)
                    .AddHours(1);
            }
            else
            {
                nextSlot = new DateTime(now.Year, now.Month, now.Day, now.Hour, nextSlotMinute, 0);
            }

            var delay = nextSlot - now;
            if (delay.TotalSeconds < 5)
            {
                delay = delay.Add(TimeSpan.FromMinutes(SLOT_DURATION_MINUTES));
            }

            _logger.LogInformation(
                "⏱️ [IntervalBG] Następne sprawdzenie za {Delay:mm\\:ss} (o {NextSlot:HH:mm}).",
                delay, nextSlot);

            await Task.Delay(delay, ct);
        }
    }
}