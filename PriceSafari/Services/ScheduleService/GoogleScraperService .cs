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
        private const int MAX_CONSECUTIVE_CAPTCHA_RESETS = 10;

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


        // W PriceSafari.Services.ScheduleService.GoogleScraperService.cs

        public async Task<GoogleScrapingDto> StartScraping()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync(); // Rozważ użycie _scopeFactory, jeśli _context ma krótki cykl życia
            if (settings == null)
            {
                Console.WriteLine("Settings not found in the database."); // Użyj ILogger
                return new GoogleScrapingDto(
                    GoogleScrapingResult.SettingsNotFound,
                    0, 0, 0,
                    "Settings not found."
                );
            }

            int totalScrapedOverall = 0;
            int totalRejectedOverall = 0;
            int captchaResetCount = 0;
            var stopwatch = Stopwatch.StartNew();

            for (int attempt = 0; attempt <= MAX_CONSECUTIVE_CAPTCHA_RESETS; attempt++)
            {
                List<CoOfrClass> coOfrsToScrapeThisAttempt;
                using (var scope = _scopeFactory.CreateScope())
                {
                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                    coOfrsToScrapeThisAttempt = await scopedContext.CoOfrs
                        .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
                        .ToListAsync();
                }

                if (!coOfrsToScrapeThisAttempt.Any())
                {
                    // Użyj ILogger zamiast Console.WriteLine
                    Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: No products left to scrape for Google.");
                    if (attempt == 0)
                    {
                        return new GoogleScrapingDto(
                            GoogleScrapingResult.NoProductsToScrape,
                            totalScrapedOverall, totalRejectedOverall, captchaResetCount,
                            "No Google products to scrape initially."
                        );
                    }
                    else
                    {
                        return new GoogleScrapingDto(
                            GoogleScrapingResult.Success,
                            totalScrapedOverall, totalRejectedOverall, captchaResetCount,
                            "All Google products scraped successfully after retries."
                        );
                    }
                }

                // Użyj ILogger
                Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: Found {coOfrsToScrapeThisAttempt.Count} products to scrape.");
                // Rozważ, czy ten ProgressUpdate jest potrzebny tutaj, czy wystarczą te z PerformScrapingLogicInternalAsyncWithCaptchaFlag
                // oraz ReceiveGeneralMessage
                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Rozpoczynam próbę {attempt + 1} z {coOfrsToScrapeThisAttempt.Count} produktami.", CancellationToken.None);


                // ----- POCZĄTEK POPRAWKI -----
                // Usunięto pierwsze, nadmiarowe wywołanie. Pozostaje tylko jedno, poprawne wywołanie:
                bool captchaDetectedActual = await PerformScrapingLogicInternalAsyncWithCaptchaFlag(
                    coOfrsToScrapeThisAttempt,
                    settings,
                    stopwatch,
                    (deltaScraped, deltaRejected) => // Callback aktualizujący liczniki globalne
                    {
                        Interlocked.Add(ref totalScrapedOverall, deltaScraped);
                        Interlocked.Add(ref totalRejectedOverall, deltaRejected);
                    }
                );
                // ----- KONIEC POPRAWKI -----

                if (!captchaDetectedActual)
                {
                    // Użyj ILogger
                    Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: No CAPTCHA. Overall scraped: {totalScrapedOverall}, rejected: {totalRejectedOverall}.");
                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Scrapowanie zakończone pomyślnie w próbie {attempt + 1}. Całkowicie zebrano: {totalScrapedOverall}, odrzucono: {totalRejectedOverall}.", CancellationToken.None);
                    return new GoogleScrapingDto(
                        GoogleScrapingResult.Success,
                        totalScrapedOverall,
                        totalRejectedOverall,
                        captchaResetCount, // Liczba resetów, które wystąpiły PRZED tym udanym przebiegiem
                        $"Google: Scrapowanie zakończone. Zebrano: {totalScrapedOverall}, odrzucono: {totalRejectedOverall}. Resetów CAPTCHA: {captchaResetCount}."
                    );
                }

                // Wykryto CAPTCHA w tym przebiegu
                captchaResetCount++;
                // Użyj ILogger
                Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: CAPTCHA detected. Reset count: {captchaResetCount}.");

                if (attempt == MAX_CONSECUTIVE_CAPTCHA_RESETS) // Sprawdź, czy to była ostatnia dozwolona próba
                {
                    // Użyj ILogger
                    Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: CAPTCHA persisted after max ({captchaResetCount}) resets.");
                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: CAPTCHA utrzymuje się po {captchaResetCount} próbach resetu (maksimum). Wymagana ręczna interwencja. Zebrano: {totalScrapedOverall}, odrzucono: {totalRejectedOverall}.", CancellationToken.None);
                    return new GoogleScrapingDto(
                        GoogleScrapingResult.CaptchaDetected,
                        totalScrapedOverall,
                        totalRejectedOverall,
                        captchaResetCount,
                        $"Google: CAPTCHA utrzymuje się po {captchaResetCount} próbach resetu. Wymagana ręczna interwencja. W tej sesji zebrano: {totalScrapedOverall}, Odrzucono: {totalRejectedOverall}."
                    );
                }

                // Logika resetu sieci, jeśli wykryto CAPTCHA i dozwolone są kolejne próby
                // Użyj ILogger
                Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: Attempting network reset ({captchaResetCount}/{MAX_CONSECUTIVE_CAPTCHA_RESETS}).");
                await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Wykryto CAPTCHA (Próba {attempt + 1}, Reset {captchaResetCount}/{MAX_CONSECUTIVE_CAPTCHA_RESETS}). Próba resetu sieci...", CancellationToken.None);
                bool resetOk = false;
                try
                {
                    resetOk = await _networkControlService.TriggerNetworkDisableAndResetAsync();
                }
                catch (Exception ex)
                {
                    // Użyj ILogger
                    Console.WriteLine($"[GoogleService] Attempt {attempt + 1}: Error during network reset ({captchaResetCount}): {ex.Message}");
                    await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", $"Google: Błąd podczas resetu sieci (Próba {attempt + 1}, Reset {captchaResetCount}): {ex.Message}", CancellationToken.None);
                }

                await _hubContext.Clients.All.SendAsync(
                    "ReceiveGeneralMessage",
                    resetOk
                        ? $"Google: Reset sieci udany (Próba {attempt + 1}, Reset {captchaResetCount}). Ponawiam scrapowanie (następna próba: {attempt + 2})..."
                        : $"Google: Reset sieci NIEUDANY (Próba {attempt + 1}, Reset {captchaResetCount}). Mimo to ponawiam scrapowanie (następna próba: {attempt + 2})...",
                    CancellationToken.None);

                await Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None); // Rozważ przekazanie CancellationToken z `RunGoogleAsync` jeśli scrapowanie ma być anulowalne
            }

            // Ten punkt jest osiągany, jeśli pętla zakończy wszystkie iteracje (MAX_CONSECUTIVE_CAPTCHA_RESETS + 1)
            // Oznacza to, że CAPTCHA wystąpiła w ostatniej dozwolonej próbie,
            // a warunek `if (attempt == MAX_CONSECUTIVE_CAPTCHA_RESETS)` wewnątrz pętli już obsłużył ten przypadek i zwrócił wynik.
            // Ten return jest więc awaryjny.
            // Użyj ILogger
            Console.WriteLine($"[GoogleService] Exited loop unexpectedly after {captchaResetCount} resets. Overall scraped: {totalScrapedOverall}, rejected: {totalRejectedOverall}.");
            return new GoogleScrapingDto(
                GoogleScrapingResult.Error, // Lub CaptchaDetected, jeśli captchaResetCount > 0
                totalScrapedOverall,
                totalRejectedOverall,
                captchaResetCount,
                captchaResetCount > 0 ? $"Google: Scrapowanie zatrzymane po {captchaResetCount} resetach CAPTCHA i wyczerpaniu prób." : "Google: Nieoczekiwane zakończenie pętli scrapowania."
            );
        }

        private async Task<bool> PerformScrapingLogicInternalAsyncWithCaptchaFlag(
            List<CoOfrClass> coOfrsForThisRun, // Zmieniono nazwę dla jasności
            Settings settings,
            Stopwatch overallStopwatch,
            Action<int, int> accumulateProgressCallback) // Nazwa callbacku zmieniona dla jasności
        {
            bool captchaDetectedInThisRun = false;
        
            int processedInThisSpecificRun = 0;
            int rejectedInThisSpecificRun = 0;

            int maxConcurrent = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
            var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
            var tasks = new List<Task>();
            // Użyj przekazanej listy dla tego uruchomienia
            var queue = new Queue<CoOfrClass>(coOfrsForThisRun);


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
                            if (captchaDetectedInThisRun) break;

                            CoOfrClass item = null;
                            lock (queue)
                            {
                                if (queue.Count > 0)
                                    item = queue.Dequeue();
                            }
                            if (item == null) break;

                            try
                            {
                                using var scope = _scopeFactory.CreateScope();
                                var db = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                var prices = await scraper.ScrapePricesAsync(item.GoogleOfferUrl);

                                var trackedItem = await db.CoOfrs.FindAsync(item.Id);
                                if (trackedItem == null)
                                {
                                    Interlocked.Increment(ref processedInThisSpecificRun);
                                    // Log error or decide how to handle
                                    continue;
                                }

                                if (prices.Any())
                                {
                                    foreach (var p in prices) p.CoOfrClassId = trackedItem.Id;
                                    db.CoOfrPriceHistories.AddRange(prices);
                                    trackedItem.GoogleIsScraped = true;
                                    trackedItem.GoogleIsRejected = false;
                                    trackedItem.GooglePricesCount = prices.Count;
                                    accumulateProgressCallback(1, 0); // Aktualizuj liczniki globalne
                                }
                                else
                                {
                                    trackedItem.GoogleIsScraped = true;
                                    trackedItem.GoogleIsRejected = true;
                                    trackedItem.GooglePricesCount = 0;
                                    accumulateProgressCallback(0, 1); // Aktualizuj liczniki globalne
                                    Interlocked.Increment(ref rejectedInThisSpecificRun);
                                }
                                // db.CoOfrs.Update(trackedItem); // Niepotrzebne jeśli FindAsync śledzi
                                await db.SaveChangesAsync();
                                Interlocked.Increment(ref processedInThisSpecificRun);


                                double elapsed = overallStopwatch.Elapsed.TotalSeconds;
                             
                                await _hubContext.Clients.All.SendAsync(
                                    "ReceiveProgressUpdate",
                                    processedInThisSpecificRun,
                                    coOfrsForThisRun.Count,
                                    elapsed,
                                    rejectedInThisSpecificRun // Odrzucone tylko w tym przebiegu
                                );
                             
                            }
                            catch (CaptchaDetectedException)
                            {
                                captchaDetectedInThisRun = true;
                                break;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Google: Error scraping item {item?.Id} in service: {ex.Message}"); // Lub ILogger
                                                                                                                       
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
