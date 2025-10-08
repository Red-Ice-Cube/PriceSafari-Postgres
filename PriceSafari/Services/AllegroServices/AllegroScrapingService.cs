using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.ScrapersControllers;

namespace PriceSafari.Services.AllegroServices
{
    public class AllegroScrapingService
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<AllegroScrapingService> _logger;

        public AllegroScrapingService(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            ILogger<AllegroScrapingService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task<(bool success, string message)> StartScrapingProcessAsync()
        {
            _logger.LogInformation("Próba uruchomienia procesu scrapowania Allegro...");

            var anyActiveScrapers = AllegroScrapeManager.ActiveScrapers.Values.Any(s => s.Status != ScraperLiveStatus.Offline);
            if (!anyActiveScrapers)
            {
                const string errorMsg = "Nie można uruchomić procesu. Żaden scraper nie jest aktywny (online).";
                _logger.LogWarning(errorMsg);
                return (false, errorMsg);
            }

            var hasUrlsToScrape = await _context.AllegroOffersToScrape.AnyAsync(o => !o.IsScraped && !o.IsRejected);
            if (!hasUrlsToScrape)
            {
                const string infoMsg = "Brak oczekujących URL-i do scrapowania. Proces nie został uruchomiony.";
                _logger.LogInformation(infoMsg);
                return (true, infoMsg);
            }

            var orphanedTasks = await _context.AllegroOffersToScrape
                .Where(o => o.IsProcessing)
                .ToListAsync();

            if (orphanedTasks.Any())
            {
                foreach (var task in orphanedTasks)
                {
                    task.IsProcessing = false;
                }
                await _context.SaveChangesAsync();
                _logger.LogInformation("Zresetowano stan dla {Count} zawieszonych zadań.", orphanedTasks.Count);
            }

            AllegroScrapeManager.CurrentStatus = ScrapingProcessStatus.Running;
            AllegroScrapeManager.ScrapingStartTime = DateTime.UtcNow;

            await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", new
            {
                status = "Running",
                startTime = AllegroScrapeManager.ScrapingStartTime,
                endTime = (DateTime?)null
            });

            const string successMsg = "Proces scrapowania ofert Allegro został pomyślnie uruchomiony.";
            _logger.LogInformation(successMsg);
            return (true, successMsg);
        }
    }
}