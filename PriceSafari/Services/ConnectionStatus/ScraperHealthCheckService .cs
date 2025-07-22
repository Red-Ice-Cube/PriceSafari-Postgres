using Microsoft.AspNetCore.SignalR;
using PriceSafari.Hubs;
using PriceSafari.ScrapersControllers;

namespace PriceSafari.Services.ConnectionStatus
{
    public class ScraperHealthCheckService : IHostedService, IDisposable
    {
        private readonly ILogger<ScraperHealthCheckService> _logger;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private Timer? _timer = null;

        // Ustawiamy próg na 45 sekund, zgodnie z twoją prośbą
        private const int DefaultInactivityThresholdSeconds = 45;
        private const int NetworkResetInactivityThresholdSeconds = 120; // To zostaje dla scrapera #1

        public ScraperHealthCheckService(ILogger<ScraperHealthCheckService> logger, IHubContext<ScrapingHub> hubContext)
        {
            _logger = logger;
            _hubContext = hubContext;
        }

        public Task StartAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Scraper Health Check Service is starting.");
            _timer = new Timer(CheckScrapersHealth, null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
            return Task.CompletedTask;
        }

        private async void CheckScrapersHealth(object? state)
        {
            var now = DateTime.UtcNow;

            // --- Sprawdzanie Scraperów #1 (Gatherers) ---
            var gathererScrapers = AllegroTaskManager.ActiveScrapers.Values
                .Where(s => s.Status != ScraperLiveStatus.Offline).ToList();

            foreach (var scraper in gathererScrapers)
            {
                var timeoutThreshold = scraper.Status == ScraperLiveStatus.ResettingNetwork
                    ? NetworkResetInactivityThresholdSeconds
                    : DefaultInactivityThresholdSeconds;

                if ((now - scraper.LastCheckIn).TotalSeconds > timeoutThreshold)
                {
                    _logger.LogWarning($"Scraper ZBIERAJĄCY '{scraper.Name}' przekroczył limit czasu. Oznaczam jako Offline.");
                    var oldStatus = scraper.Status;
                    scraper.Status = ScraperLiveStatus.Offline;
                    _ = _hubContext.Clients.All.SendAsync("UpdateScraperStatus", scraper);

                    if ((oldStatus == ScraperLiveStatus.Busy || oldStatus == ScraperLiveStatus.ResettingNetwork) && !string.IsNullOrEmpty(scraper.CurrentTaskUsername))
                    {
                        if (AllegroTaskManager.ActiveTasks.TryGetValue(scraper.CurrentTaskUsername, out var abandonedTask))
                        {
                            abandonedTask.Status = ScrapingStatus.Pending;
                            abandonedTask.AssignedScraperName = null;
                            abandonedTask.LastProgressMessage = "Zadanie przerwane - scraper przestał odpowiadać.";
                            _ = _hubContext.Clients.All.SendAsync("UpdateTaskProgress", scraper.CurrentTaskUsername, abandonedTask);
                        }
                    }
                }
            }

            // --- NOWA SEKCJA: Sprawdzanie Scraperów #2 (Detail Scrapers) ---
            var detailScrapers = AllegroScrapeManager.ActiveScrapers.Values
                .Where(s => s.Status != ScraperLiveStatus.Offline).ToList();

            foreach (var scraper in detailScrapers)
            {
                // Dla tego typu scrapera używamy jednego, stałego progu
                if ((now - scraper.LastCheckIn).TotalSeconds > DefaultInactivityThresholdSeconds)
                {
                    _logger.LogWarning($"Scraper OFERTOWY '{scraper.Name}' przekroczył limit czasu. Oznaczam jako Offline.");
                    scraper.Status = ScraperLiveStatus.Offline;

                    // Używamy dedykowanego eventu dla tego typu scrapera
                    _ = _hubContext.Clients.All.SendAsync("UpdateDetailScraperStatus", scraper);
                }
            }
        }

        // Metody StopAsync i Dispose bez zmian
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