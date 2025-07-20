using Microsoft.AspNetCore.SignalR;
using PriceSafari.Hubs;
using PriceSafari.ScrapersControllers;

namespace PriceSafari.Services.ConnectionStatus
{
    /// <summary>
    /// Usługa działająca w tle, która monitoruje "zdrowie" scraperów,
    /// oznaczając je jako offline, jeśli nie komunikują się w określonym czasie.
    /// </summary>
    public class ScraperHealthCheckService : IHostedService, IDisposable
    {
        private readonly ILogger<ScraperHealthCheckService> _logger;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private Timer? _timer = null;

        // NOWOŚĆ: Dwa różne progi czasowe dla różnych stanów scrapera.
        private const int DefaultInactivityThresholdSeconds = 40;  // Standardowy czas, po którym scraper jest uznawany za nieaktywny.
        private const int NetworkResetInactivityThresholdSeconds = 120; // Wydłużony czas dla scrapera w trakcie resetu sieci.

        public ScraperHealthCheckService(ILogger<ScraperHealthCheckService> logger, IHubContext<ScrapingHub> hubContext)
        {
            _logger = logger;
            _hubContext = hubContext;
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Scraper Health Check Service is starting.");
            // Uruchamiamy timer, który będzie wywoływał metodę sprawdzającą co 15 sekund.
            _timer = new Timer(CheckScrapersHealth, null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
            return Task.CompletedTask;
        }

        private async void CheckScrapersHealth(object? state)
        {
            var now = DateTime.UtcNow;
            // Pobieramy listę scraperów, które NIE są jeszcze oznaczone jako offline.
            var activeScrapers = AllegroTaskManager.ActiveScrapers.Values
                .Where(s => s.Status != ScraperLiveStatus.Offline)
                .ToList();

            if (!activeScrapers.Any())
            {
                return; // Brak aktywnych scraperów, nic do zrobienia.
            }

            foreach (var scraper in activeScrapers)
            {
                // ZMODYFIKOWANA LOGIKA: Dynamicznie wybierz próg czasowy na podstawie stanu scrapera.
                var timeoutThreshold = scraper.Status == ScraperLiveStatus.ResettingNetwork
                    ? NetworkResetInactivityThresholdSeconds
                    : DefaultInactivityThresholdSeconds;

                // Sprawdź, czy scraper przekroczył swój próg nieaktywności.
                if ((now - scraper.LastCheckIn).TotalSeconds > timeoutThreshold)
                {
                    _logger.LogWarning($"Scraper '{scraper.Name}' timed out (Status: {scraper.Status}, Threshold: {timeoutThreshold}s). Marking as Offline.");

                    var oldStatus = scraper.Status;
                    scraper.Status = ScraperLiveStatus.Offline;

                    // Poinformuj wszystkich klientów (panel admina) o zmianie statusu.
                    // Używamy `_ =` aby nie czekać na zakończenie wysyłania, bo jesteśmy w metodzie `async void`.
                    _ = _hubContext.Clients.All.SendAsync("UpdateScraperStatus", scraper);

                    // ZMODYFIKOWANA LOGIKA: Jeśli scraper był zajęty LUB resetował sieć, musimy zresetować jego zadanie.
                    if ((oldStatus == ScraperLiveStatus.Busy || oldStatus == ScraperLiveStatus.ResettingNetwork) && !string.IsNullOrEmpty(scraper.CurrentTaskUsername))
                    {
                        _logger.LogWarning($"Scraper '{scraper.Name}' went offline while processing task '{scraper.CurrentTaskUsername}'. Resetting task to Pending.");

                        if (AllegroTaskManager.ActiveTasks.TryGetValue(scraper.CurrentTaskUsername, out var abandonedTask))
                        {
                            abandonedTask.Status = ScrapingStatus.Pending; // Wróć zadanie do puli
                            abandonedTask.AssignedScraperName = null;
                            abandonedTask.LastProgressMessage = "Zadanie przerwane - scraper przestał odpowiadać. Oczekuje na nowego scrapera.";

                            // Poinformuj klientów, że zadanie znów jest dostępne.
                            _ = _hubContext.Clients.All.SendAsync("UpdateTaskProgress", scraper.CurrentTaskUsername, abandonedTask);
                        }
                    }
                }
            }
        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Scraper Health Check Service is stopping.");
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}