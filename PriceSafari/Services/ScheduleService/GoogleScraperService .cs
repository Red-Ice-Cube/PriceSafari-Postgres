using PriceSafari.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using PriceSafari.Hubs;

namespace PriceSafari.Services.ScheduleService
{
    public class GoogleScraperService
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;

        public GoogleScraperService(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            IServiceScopeFactory scopeFactory)
        {
            _context = context;
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;
        }

        // Ewentualny enum na wyniki
        public enum GoogleScrapingResult
        {
            Success,
            SettingsNotFound,
            NoProductsToScrape,
            Error
        }

        public record GoogleScrapingDto(
             GoogleScrapingResult Result,
             int TotalScraped,
             int TotalRejected,
             string? Message
         );



        public async Task<GoogleScrapingDto> StartScraping()
        {
            // 1. Pobranie ustawień z bazy
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                Console.WriteLine("Settings not found in the database.");
                return new GoogleScrapingDto(
                    GoogleScrapingResult.SettingsNotFound,
                    0,
                    0,
                    "Settings not found."
                );
            }

            // 2. Pobranie CoOfrClass do scrapowania
            var coOfrsToScrape = await _context.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
                .ToListAsync();

            if (!coOfrsToScrape.Any())
            {
                Console.WriteLine("No products found to scrape.");
                return new GoogleScrapingDto(
                    GoogleScrapingResult.NoProductsToScrape,
                    0,
                    0,
                    "No products found to scrape."
                );
            }

            Console.WriteLine($"Found {coOfrsToScrape.Count} products to scrape (Google).");

            // 3. Komunikat startowy (SignalR)
            if (_hubContext != null)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", 0, coOfrsToScrape.Count, 0, 0);
            }
            else
            {
                Console.WriteLine("Hub context is null.");
            }

            // 4. Przygotowanie zmiennych do śledzenia postępu
            int totalScraped = 0;
            int totalRejected = 0; // << Dodajemy licznik odrzuconych
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            // 5. Semafor
            int maxConcurrentScrapers = settings.SemophoreGoogle;
            var semaphore = new SemaphoreSlim(maxConcurrentScrapers);
            var tasks = new List<Task>();
            var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

            // 6. Uruchomienie wątków
            for (int i = 0; i < maxConcurrentScrapers; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();

                    var scraper = new GoogleMainPriceScraper(); // klasa Selenium/Puppeteer
                    await scraper.InitializeAsync(settings);

                    try
                    {
                        while (true)
                        {
                            CoOfrClass coOfr = null;

                            // Zdejmujemy z kolejki
                            lock (productQueue)
                            {
                                if (productQueue.Count > 0)
                                {
                                    coOfr = productQueue.Dequeue();
                                }
                            }

                            if (coOfr == null)
                                break; // Koniec kolejki

                            try
                            {
                                // Scope dla EF
                                using (var scope = _scopeFactory.CreateScope())
                                {
                                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                    Console.WriteLine($"Starting scraping for GoogleOfferUrl: {coOfr.GoogleOfferUrl}");

                                    // Scrapowanie
                                    var scrapedPrices = await scraper.ScrapePricesAsync(coOfr.GoogleOfferUrl);

                                    if (scrapedPrices.Any())
                                    {
                                        foreach (var priceHistory in scrapedPrices)
                                        {
                                            priceHistory.CoOfrClassId = coOfr.Id;
                                        }

                                        // Zapis do bazy
                                        scopedContext.CoOfrPriceHistories.AddRange(scrapedPrices);
                                        await scopedContext.SaveChangesAsync();

                                        Console.WriteLine($"Saved {scrapedPrices.Count} offers for {coOfr.GoogleOfferUrl}.");

                                        // Aktualizacja coOfr
                                        coOfr.GoogleIsScraped = true;
                                        coOfr.GooglePricesCount = scrapedPrices.Count;

                                        scopedContext.CoOfrs.Update(coOfr);
                                        await scopedContext.SaveChangesAsync();

                                        Console.WriteLine($"Updated {coOfr.Id}: {coOfr.GooglePricesCount} offers.");

                                        // Komunikat SignalR
                                        await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate",
                                            coOfr.Id, coOfr.GoogleIsScraped, coOfr.GoogleIsRejected, coOfr.GooglePricesCount, "Google");

                                        Interlocked.Increment(ref totalScraped);
                                    }
                                    else
                                    {
                                        // Brak ofert - odrzucamy
                                        coOfr.GoogleIsScraped = true;
                                        coOfr.GoogleIsRejected = true;
                                        coOfr.GooglePricesCount = 0;

                                        scopedContext.CoOfrs.Update(coOfr);
                                        await scopedContext.SaveChangesAsync();

                                        await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate",
                                            coOfr.Id, coOfr.GoogleIsScraped, coOfr.GoogleIsRejected, coOfr.GooglePricesCount, "Google");

                                        // Zwiększamy licznik odrzuconych
                                        Interlocked.Increment(ref totalRejected);
                                    }

                                    // Informacja o postępie
                                    double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate",
                                        (totalScraped + totalRejected), // łączna liczba 'przetworzonych' (scraped + rejected)
                                        coOfrsToScrape.Count,
                                        elapsedSeconds,
                                        totalRejected
                                    );
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error scraping product {coOfr.Id}: {ex.Message}");
                            }
                        }
                    }
                    finally
                    {
                        await scraper.CloseAsync();
                        semaphore.Release();
                    }
                }));
            }

            // 7. Czekamy aż wszystkie wątki skończą
            await Task.WhenAll(tasks);
            stopwatch.Stop();

            Console.WriteLine("All tasks completed (Google scraping).");

            return new GoogleScrapingDto(
                GoogleScrapingResult.Success,
                totalScraped,
                totalRejected,
                $"All tasks completed. Scraped {totalScraped} offers, Rejected: {totalRejected}."
            );
        }


    }
}
