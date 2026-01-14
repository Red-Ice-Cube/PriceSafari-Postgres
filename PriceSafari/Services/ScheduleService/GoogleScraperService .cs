using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Services.ControlNetwork;

namespace PriceSafari.Services.ScheduleService
{
    public class GoogleScraperService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly INetworkControlService _networkControlService;
        private readonly ILogger<GoogleScraperService> _logger;

        private const int MAX_NETWORK_RESETS = 3;

        public GoogleScraperService(
            IServiceScopeFactory scopeFactory,
            IHubContext<ScrapingHub> hubContext,
            INetworkControlService networkControlService,
            ILogger<GoogleScraperService> logger)
        {
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
            _networkControlService = networkControlService;
            _logger = logger;
        }

        public enum GoogleScrapingResult
        {
            Success,
            SettingsNotFound,
            NoProductsToScrape,
            Error,
            PersistentBlock
        }

        public record GoogleScrapingDto(
            GoogleScrapingResult Result,
            int TotalScraped,
            int TotalRejected,
            int NetworkResets,
            int TotalUrlsToScrape,
            string? Message
        );

        public async Task<GoogleScrapingDto> StartScraping()
        {
            int totalScrapedOverall = 0;
            int totalRejectedOverall = 0;
            int networkResetCount = 0;

            int totalUrlsToScrape = 0;
            var stopwatch = Stopwatch.StartNew();

            for (int attempt = 0; attempt <= MAX_NETWORK_RESETS; attempt++)
            {

                (bool success, int scrapedThisRun, int rejectedThisRun, int urlsInRun) result;

                try
                {
                    result = await PerformSingleScrapingRun(stopwatch);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GoogleService] A critical error occurred during a scraping run.");

                    return new GoogleScrapingDto(GoogleScrapingResult.Error, totalScrapedOverall, totalRejectedOverall, networkResetCount, totalUrlsToScrape, $"A critical error occurred: {ex.Message}");
                }

                if (attempt == 0)
                {
                    totalUrlsToScrape = result.urlsInRun;
                }

                totalScrapedOverall += result.scrapedThisRun;
                totalRejectedOverall += result.rejectedThisRun;

                if (result.success)
                {
                    _logger.LogInformation($"[GoogleService] Scraping run successful. Overall scraped: {totalScrapedOverall}, rejected: {totalRejectedOverall}.");
                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Scrapowanie zakończone. Zebrano: {totalScrapedOverall}, odrzucono: {totalRejectedOverall}.", CancellationToken.None);

                    return new GoogleScrapingDto(GoogleScrapingResult.Success, totalScrapedOverall, totalRejectedOverall, networkResetCount, totalUrlsToScrape, "Scraping completed successfully.");
                }

                networkResetCount++;
                _logger.LogWarning($"[GoogleService] Persistent block detected on attempt {attempt + 1}. Reset count: {networkResetCount}.");

                if (attempt == MAX_NETWORK_RESETS)
                {
                    _logger.LogError($"[GoogleService] Persistent block remained after max ({networkResetCount}) resets.");
                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Blokada utrzymuje się po {networkResetCount} próbach resetu. Wymagana ręczna interwencja.", CancellationToken.None);

                    return new GoogleScrapingDto(GoogleScrapingResult.PersistentBlock, totalScrapedOverall, totalRejectedOverall, networkResetCount, totalUrlsToScrape, $"Persistent block after {networkResetCount} resets. Manual intervention required.");
                }

                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Wykryto blokadę (Reset {networkResetCount}/{MAX_NETWORK_RESETS}). Próba resetu sieci...", CancellationToken.None);

                try
                {
                    await _networkControlService.TriggerNetworkDisableAndResetAsync();
                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "Google: Reset sieci udany. Ponawiam scrapowanie za 10 sekund...", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GoogleService] Error during network reset.");
                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Błąd podczas resetu sieci: {ex.Message}", CancellationToken.None);
                }

                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            return new GoogleScrapingDto(GoogleScrapingResult.Error, totalScrapedOverall, totalRejectedOverall, networkResetCount, totalUrlsToScrape, "Unexpected end of scraping process.");
        }

        private async Task<(bool success, int scraped, int rejected, int totalUrls)> PerformSingleScrapingRun(Stopwatch stopwatch)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
            var settings = await dbContext.Settings.FirstOrDefaultAsync();

            if (settings == null)
            {
                _logger.LogError("[GoogleService] Settings not found in the database.");
                throw new InvalidOperationException("Settings not found.");
            }

            var coOfrsToScrape = await dbContext.CoOfrs
           .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
           // Nie musimy tu nic zmieniać w zapytaniu, bo 'GoogleCid' jest w głównej tabeli CoOfrs, 
           // ale upewnij się, że scraper go dostaje.
           .ToListAsync();

            int totalUrls = coOfrsToScrape.Count;

            if (!coOfrsToScrape.Any())
            {

                return (success: true, scraped: 0, rejected: 0, totalUrls: 0);
            }

            _logger.LogInformation($"[GoogleService] Starting a new run with {totalUrls} products.");

            int scrapedThisRun = 0;
            int rejectedThisRun = 0;
            int persistentFailures = 0;

            int maxConcurrent = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
            var semaphore = new SemaphoreSlim(maxConcurrent);
            var tasks = new List<Task>();
            var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

            for (int i = 0; i < maxConcurrent; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var scraper = new GoogleMainPriceScraper();

                        while (true)
                        {
                            CoOfrClass item = null;
                            lock (productQueue)
                            {
                                if (productQueue.Count > 0) item = productQueue.Dequeue();
                            }
                            if (item == null) break;

                            try
                            {
                                using var productScope = _scopeFactory.CreateScope();
                                var db = productScope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                var prices = await scraper.ScrapePricesAsync(item);

                                var trackedItem = await db.CoOfrs.FindAsync(item.Id);
                                if (trackedItem == null) continue;

                                if (prices != null && prices.Any())
                                {
                                    foreach (var p in prices)
                                    {
                                        p.CoOfrClassId = trackedItem.Id;
                                        p.GoogleCid = trackedItem.GoogleCid; // DODATKOWE ZABEZPIECZENIE
                                    }
                                    db.CoOfrPriceHistories.AddRange(prices);
                                    trackedItem.GoogleIsScraped = true;
                                    trackedItem.GoogleIsRejected = false;
                                    trackedItem.GooglePricesCount = prices.Count;
                                    Interlocked.Increment(ref scrapedThisRun);
                                }
                                else
                                {
                                    trackedItem.GoogleIsScraped = true;
                                    trackedItem.GoogleIsRejected = true;
                                    trackedItem.GooglePricesCount = 0;
                                    Interlocked.Increment(ref rejectedThisRun);

                                    if (prices == null) Interlocked.Increment(ref persistentFailures);
                                }

                                await db.SaveChangesAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"[GoogleService] Error scraping item {item?.Id}.");
                                Interlocked.Increment(ref rejectedThisRun);
                                Interlocked.Increment(ref persistentFailures);
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            _logger.LogInformation($"[GoogleService] Run finished. Scraped: {scrapedThisRun}, Rejected: {rejectedThisRun}, Failures: {persistentFailures}");

            bool wasSuccess = persistentFailures <= (coOfrsToScrape.Count / 2);

            return (success: wasSuccess, scraped: scrapedThisRun, rejected: rejectedThisRun, totalUrls: totalUrls);
        }
    }
}