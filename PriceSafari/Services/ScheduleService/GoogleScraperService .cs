using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Services.ControlNetwork;

namespace PriceSafari.Services.ScheduleService
{
    public class GoogleScraperService
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly INetworkControlService _networkControlService;

        // do synchronizacji i limitowania automatycznych resetów
        private const int MAX_CONSECUTIVE_CAPTCHA_RESETS = 3;

        public GoogleScraperService(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            IServiceScopeFactory scopeFactory,
            INetworkControlService networkControlService)
        {
            _context = context;
            _hubContext = hubContext;
            _scopeFactory = scopeFactory;
            _networkControlService = networkControlService;
        }

        public enum GoogleScrapingResult
        {
            Success,
            SettingsNotFound,
            NoProductsToScrape,
            Error,
            CaptchaDetected
        }

        public record GoogleScrapingDto(
            GoogleScrapingResult Result,
            int TotalScraped,
            int TotalRejected,
            int CaptchaResets,
            string? Message
        );

        public async Task<GoogleScrapingDto> StartScraping()
        {
            // 1. Pobranie ustawień
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                Console.WriteLine("Settings not found in the database.");
                return new GoogleScrapingDto(
                    GoogleScrapingResult.SettingsNotFound,
                    0, 0, 0,
                    "Settings not found."
                );
            }

            // 2. Lista produktów do scrapowania
            var coOfrsToScrape = await _context.CoOfrs
                .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
                .ToListAsync();

            if (!coOfrsToScrape.Any())
            {
                Console.WriteLine("No products found to scrape.");
                return new GoogleScrapingDto(
                    GoogleScrapingResult.NoProductsToScrape,
                    0, 0, 0,
                    "No products to scrape."
                );
            }

            Console.WriteLine($"Found {coOfrsToScrape.Count} products to scrape (Google).");

            // 3. Sygnal Start
            await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", 0, coOfrsToScrape.Count, 0, 0);

            // 4. Inicjalizacja liczników
            int totalScraped = 0;
            int totalRejected = 0;
            int captchaResetCount = 0;
            var stopwatch = Stopwatch.StartNew();

            // 5. Pętla próby scrapowania z retry po CAPTCHA
            for (int attempt = 0; attempt <= MAX_CONSECUTIVE_CAPTCHA_RESETS; attempt++)
            {
                // wykonaj run ze wspólną logiką, zwracając czy wykryto CAPTCHA
                bool captchaDetected = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(
                    coOfrsToScrape,
                    settings,
                    stopwatch,
                    (scrapedDelta, rejectedDelta) =>
                    {
                        Interlocked.Add(ref totalScraped, scrapedDelta);
                        Interlocked.Add(ref totalRejected, rejectedDelta);
                    });

                if (!captchaDetected)
                {
                    // zakończono bez CAPTCHA
                    return new GoogleScrapingDto(
                        GoogleScrapingResult.Success,
                        totalScraped,
                        totalRejected,
                        captchaResetCount,
                        $"Scraping done: {totalScraped} scraped, {totalRejected} rejected, {captchaResetCount} resets."
                    );
                }

                // jeżeli wykryto CAPTCHA i przekroczono próby
                if (attempt == MAX_CONSECUTIVE_CAPTCHA_RESETS)
                {
                    captchaResetCount++;
                    return new GoogleScrapingDto(
                        GoogleScrapingResult.CaptchaDetected,
                        totalScraped,
                        totalRejected,
                        captchaResetCount,
                        $"CAPTCHA still after {captchaResetCount} resets – manual intervention required."
                    );
                }

                // inny przypadek: CAPTCHA wykryto, ale można jeszcze próbować
                captchaResetCount++;

                // 6. Reset sieci i Delay przed kolejną próbą
                bool resetOk = false;
                try
                {
                    resetOk = await _networkControlService.TriggerNetworkDisableAndResetAsync();
                }
                catch (Exception ex)
                {
                    await _hubContext.Clients.All.SendAsync(
                        "ReceiveGeneralMessage",
                        $"Google: błąd podczas resetu sieci (próba {attempt + 1}): {ex.Message}",
                        CancellationToken.None);
                }

                await _hubContext.Clients.All.SendAsync(
                    "ReceiveGeneralMessage",
                    resetOk
                        ? $"Google: network reset succeeded, retrying scraping (#{attempt + 1})..."
                        : $"Google: network reset FAILED on attempt #{attempt + 1}, retrying anyway...",
                    CancellationToken.None);

                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            // tu nie powinno nigdy wejść
            return new GoogleScrapingDto(
                GoogleScrapingResult.Error,
                totalScraped,
                totalRejected,
                captchaResetCount,
                "Unexpected exit from scraping loop."
            );
        }

        private async Task<bool> PerformScrapingLogicInternalAsyncWithCaptchaFlag(
            List<CoOfrClass> coOfrsToScrape,
            Settings settings,
            Stopwatch stopwatch,
            Action<int, int> progressCallback)
        {
            bool captchaDetectedInThisRun = false;

            int scrapedCountThisRun = 0;
            int rejectedCountThisRun = 0;

            int maxConcurrent = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
            var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
            var tasks = new List<Task>();
            var queue = new Queue<CoOfrClass>(coOfrsToScrape);

            for (int i = 0; i < maxConcurrent; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    var scraper = new GoogleMainPriceScraper();
                    await scraper.InitializeAsync(settings);
                    try
                    {
                        while (true)
                        {
                            CoOfrClass item = null;
                            lock (queue)
                                if (queue.Count > 0)
                                    item = queue.Dequeue();
                            if (item == null)
                                break;

                            try
                            {
                                using var scope = _scopeFactory.CreateScope();
                                var db = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                var prices = await scraper.ScrapePricesAsync(item.GoogleOfferUrl);
                                if (prices.Any())
                                {
                                    foreach (var p in prices)
                                        p.CoOfrClassId = item.Id;
                                    db.CoOfrPriceHistories.AddRange(prices);
                                    item.GoogleIsScraped = true;
                                    item.GooglePricesCount = prices.Count;

                                    scrapedCountThisRun++;
                                    progressCallback(1, 0);
                                }
                                else
                                {
                                    item.GoogleIsScraped = true;
                                    item.GoogleIsRejected = true;
                                    item.GooglePricesCount = 0;

                                    rejectedCountThisRun++;
                                    progressCallback(0, 1);
                                }

                                db.CoOfrs.Update(item);
                                await db.SaveChangesAsync();

                                double elapsed = stopwatch.Elapsed.TotalSeconds;
                                await _hubContext.Clients.All.SendAsync(
                                    "ReceiveProgressUpdate",
                                    scrapedCountThisRun + rejectedCountThisRun,
                                    coOfrsToScrape.Count,
                                    elapsed,
                                    rejectedCountThisRun);
                            }
                            catch (CaptchaDetectedException)
                            {
                                captchaDetectedInThisRun = true;
                                break;
                            }
                            catch
                            {
                                // log or ignore
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

            await Task.WhenAll(tasks);
            return captchaDetectedInThisRun;
        }
    }
}





//using PriceSafari.Data;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using System.Diagnostics;
//using PriceSafari.Hubs;

//namespace PriceSafari.Services.ScheduleService
//{
//    public class GoogleScraperService
//    {
//        private readonly PriceSafariContext _context;
//        private readonly IHubContext<ScrapingHub> _hubContext;
//        private readonly IServiceScopeFactory _scopeFactory;

//        public GoogleScraperService(
//            PriceSafariContext context,
//            IHubContext<ScrapingHub> hubContext,
//            IServiceScopeFactory scopeFactory)
//        {
//            _context = context;
//            _hubContext = hubContext;
//            _scopeFactory = scopeFactory;
//        }

//        // Ewentualny enum na wyniki
//        public enum GoogleScrapingResult
//        {
//            Success,
//            SettingsNotFound,
//            NoProductsToScrape,
//            Error
//        }

//        public record GoogleScrapingDto(
//             GoogleScrapingResult Result,
//             int TotalScraped,
//             int TotalRejected,
//             string? Message
//         );



//        public async Task<GoogleScrapingDto> StartScraping()
//        {
//            // 1. Pobranie ustawień z bazy
//            var settings = await _context.Settings.FirstOrDefaultAsync();
//            if (settings == null)
//            {
//                Console.WriteLine("Settings not found in the database.");
//                return new GoogleScrapingDto(
//                    GoogleScrapingResult.SettingsNotFound,
//                    0,
//                    0,
//                    "Settings not found."
//                );
//            }

//            // 2. Pobranie CoOfrClass do scrapowania
//            var coOfrsToScrape = await _context.CoOfrs
//                .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
//                .ToListAsync();

//            if (!coOfrsToScrape.Any())
//            {
//                Console.WriteLine("No products found to scrape.");
//                return new GoogleScrapingDto(
//                    GoogleScrapingResult.NoProductsToScrape,
//                    0,
//                    0,
//                    "No products found to scrape."
//                );
//            }

//            Console.WriteLine($"Found {coOfrsToScrape.Count} products to scrape (Google).");

//            // 3. Komunikat startowy (SignalR)
//            if (_hubContext != null)
//            {
//                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", 0, coOfrsToScrape.Count, 0, 0);
//            }
//            else
//            {
//                Console.WriteLine("Hub context is null.");
//            }

//            // 4. Przygotowanie zmiennych do śledzenia postępu
//            int totalScraped = 0;
//            int totalRejected = 0; // << Dodajemy licznik odrzuconych
//            var stopwatch = new Stopwatch();
//            stopwatch.Start();

//            // 5. Semafor
//            int maxConcurrentScrapers = settings.SemophoreGoogle;
//            var semaphore = new SemaphoreSlim(maxConcurrentScrapers);
//            var tasks = new List<Task>();
//            var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

//            // 6. Uruchomienie wątków
//            for (int i = 0; i < maxConcurrentScrapers; i++)
//            {
//                tasks.Add(Task.Run(async () =>
//                {
//                    await semaphore.WaitAsync();

//                    var scraper = new GoogleMainPriceScraper(); // klasa Selenium/Puppeteer
//                    await scraper.InitializeAsync(settings);

//                    try
//                    {
//                        while (true)
//                        {
//                            CoOfrClass coOfr = null;

//                            // Zdejmujemy z kolejki
//                            lock (productQueue)
//                            {
//                                if (productQueue.Count > 0)
//                                {
//                                    coOfr = productQueue.Dequeue();
//                                }
//                            }

//                            if (coOfr == null)
//                                break; // Koniec kolejki

//                            try
//                            {
//                                // Scope dla EF
//                                using (var scope = _scopeFactory.CreateScope())
//                                {
//                                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

//                                    Console.WriteLine($"Starting scraping for GoogleOfferUrl: {coOfr.GoogleOfferUrl}");

//                                    // Scrapowanie
//                                    var scrapedPrices = await scraper.ScrapePricesAsync(coOfr.GoogleOfferUrl);

//                                    if (scrapedPrices.Any())
//                                    {
//                                        foreach (var priceHistory in scrapedPrices)
//                                        {
//                                            priceHistory.CoOfrClassId = coOfr.Id;
//                                        }

//                                        // Zapis do bazy
//                                        scopedContext.CoOfrPriceHistories.AddRange(scrapedPrices);
//                                        await scopedContext.SaveChangesAsync();

//                                        Console.WriteLine($"Saved {scrapedPrices.Count} offers for {coOfr.GoogleOfferUrl}.");

//                                        // Aktualizacja coOfr
//                                        coOfr.GoogleIsScraped = true;
//                                        coOfr.GooglePricesCount = scrapedPrices.Count;

//                                        scopedContext.CoOfrs.Update(coOfr);
//                                        await scopedContext.SaveChangesAsync();

//                                        Console.WriteLine($"Updated {coOfr.Id}: {coOfr.GooglePricesCount} offers.");

//                                        // Komunikat SignalR
//                                        await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate",
//                                            coOfr.Id, coOfr.GoogleIsScraped, coOfr.GoogleIsRejected, coOfr.GooglePricesCount, "Google");

//                                        Interlocked.Increment(ref totalScraped);
//                                    }
//                                    else
//                                    {
//                                        // Brak ofert - odrzucamy
//                                        coOfr.GoogleIsScraped = true;
//                                        coOfr.GoogleIsRejected = true;
//                                        coOfr.GooglePricesCount = 0;

//                                        scopedContext.CoOfrs.Update(coOfr);
//                                        await scopedContext.SaveChangesAsync();

//                                        await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate",
//                                            coOfr.Id, coOfr.GoogleIsScraped, coOfr.GoogleIsRejected, coOfr.GooglePricesCount, "Google");

//                                        // Zwiększamy licznik odrzuconych
//                                        Interlocked.Increment(ref totalRejected);
//                                    }

//                                    // Informacja o postępie
//                                    double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
//                                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate",
//                                        (totalScraped + totalRejected), // łączna liczba 'przetworzonych' (scraped + rejected)
//                                        coOfrsToScrape.Count,
//                                        elapsedSeconds,
//                                        totalRejected
//                                    );
//                                }
//                            }
//                            catch (Exception ex)
//                            {
//                                Console.WriteLine($"Error scraping product {coOfr.Id}: {ex.Message}");
//                            }
//                        }
//                    }
//                    finally
//                    {
//                        await scraper.CloseAsync();
//                        semaphore.Release();
//                    }
//                }));
//            }

//            // 7. Czekamy aż wszystkie wątki skończą
//            await Task.WhenAll(tasks);
//            stopwatch.Stop();

//            Console.WriteLine("All tasks completed (Google scraping).");

//            return new GoogleScrapingDto(
//                GoogleScrapingResult.Success,
//                totalScraped,
//                totalRejected,
//                $"All tasks completed. Scraped {totalScraped} offers, Rejected: {totalRejected}."
//            );
//        }


//    }
//}
