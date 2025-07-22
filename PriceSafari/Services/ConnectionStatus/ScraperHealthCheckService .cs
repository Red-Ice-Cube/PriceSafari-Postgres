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

        private const int DefaultInactivityThresholdSeconds = 40;
        private const int NetworkResetInactivityThresholdSeconds = 120;

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

            var activeScrapers = AllegroTaskManager.ActiveScrapers.Values
                .Where(s => s.Status != ScraperLiveStatus.Offline)
                .ToList();

            if (!activeScrapers.Any())
            {
                return;
            }

            foreach (var scraper in activeScrapers)
            {

                var timeoutThreshold = scraper.Status == ScraperLiveStatus.ResettingNetwork
                    ? NetworkResetInactivityThresholdSeconds
                    : DefaultInactivityThresholdSeconds;

                if ((now - scraper.LastCheckIn).TotalSeconds > timeoutThreshold)
                {
                    _logger.LogWarning($"Scraper '{scraper.Name}' timed out (Status: {scraper.Status}, Threshold: {timeoutThreshold}s). Marking as Offline.");

                    var oldStatus = scraper.Status;
                    scraper.Status = ScraperLiveStatus.Offline;

                    _ = _hubContext.Clients.All.SendAsync("UpdateScraperStatus", scraper);

                    if ((oldStatus == ScraperLiveStatus.Busy || oldStatus == ScraperLiveStatus.ResettingNetwork) && !string.IsNullOrEmpty(scraper.CurrentTaskUsername))
                    {
                        _logger.LogWarning($"Scraper '{scraper.Name}' went offline while processing task '{scraper.CurrentTaskUsername}'. Resetting task to Pending.");

                        if (AllegroTaskManager.ActiveTasks.TryGetValue(scraper.CurrentTaskUsername, out var abandonedTask))
                        {
                            abandonedTask.Status = ScrapingStatus.Pending;
                            abandonedTask.AssignedScraperName = null;
                            abandonedTask.LastProgressMessage = "Zadanie przerwane - scraper przestał odpowiadać. Oczekuje na nowego scrapera.";

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