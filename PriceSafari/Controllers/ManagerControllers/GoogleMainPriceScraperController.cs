using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models; // Dla Settings, CoOfrClass
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class GoogleMainPriceScraperController : Controller
{
    private readonly PriceSafariContext _context;
    private readonly IHubContext<ScrapingHub> _hubContext;
    private static CancellationTokenSource _googleCancellationTokenSource = new CancellationTokenSource();
    private static readonly object _cancellationTokenLock = new object();
    private static volatile bool _captchaGlobalSignal = false; // Flaga sygnalizująca globalne wykrycie CAPTCHA

    public GoogleMainPriceScraperController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    private void ResetCancellationToken(bool captchaDetected = false)
    {
        lock (_cancellationTokenLock)
        {
            if (captchaDetected)
            {
                if (!_captchaGlobalSignal) // Tylko jeśli to pierwszy sygnał CAPTCHA
                {
                    Console.WriteLine("CAPTCHA DETECTED GLOBALLY! Initiating shutdown of all Google scraping tasks.");
                    _captchaGlobalSignal = true; // Ustaw flagę globalnego wykrycia
                    if (_googleCancellationTokenSource != null && !_googleCancellationTokenSource.IsCancellationRequested)
                    {
                        _googleCancellationTokenSource.Cancel(); // Anuluj bieżący token
                        // Nie tworzymy nowego tokenu, obecna sesja scrapowania jest przerywana
                    }
                }
                return; // Zakończ, jeśli CAPTCHA została zasygnalizowana
            }

            // Normalny reset (np. na początku StartScraping lub po StopScrapingGoogle)
            if (_googleCancellationTokenSource != null)
            {
                if (!_googleCancellationTokenSource.IsCancellationRequested)
                {
                    _googleCancellationTokenSource.Cancel();
                }
                _googleCancellationTokenSource.Dispose();
            }
            _googleCancellationTokenSource = new CancellationTokenSource();
            _captchaGlobalSignal = false; // Zresetuj flagę CAPTCHA przy nowym starcie
            Console.WriteLine("Google CancellationToken has been reset for a new session.");
        }
    }

    [HttpPost]
    public IActionResult StopScrapingGoogle()
    {
        Console.WriteLine("StopScrapingGoogle action called by user.");
        ResetCancellationToken(captchaDetected: false); // Normalny reset, nie z powodu CAPTCHA
        // Tutaj nie ma potrzeby dodatkowego zamykania scraperów,
        // anulowanie tokenu powinno wystarczyć, aby zadania się zakończyły.
        return Ok(new { Message = "Scraping stopped for Google. All tasks will be cancelled upon checking token." });
    }

    [HttpPost]
    public async Task<IActionResult> StartScraping()
    {
        Console.WriteLine("Google StartScraping action called.");
        ResetCancellationToken(captchaDetected: false); // Rozpocznij z nowym, świeżym tokenem i zresetowaną flagą CAPTCHA
        var cancellationToken = _googleCancellationTokenSource.Token;

        var settings = await _context.Settings.FirstOrDefaultAsync(cancellationToken);
        if (settings == null)
        {
            Console.WriteLine("Settings not found in the database.");
            return BadRequest("Settings not found.");
        }

        var coOfrsToScrape = await _context.CoOfrs
            .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
            .ToListAsync(cancellationToken);

        if (!coOfrsToScrape.Any())
        {
            Console.WriteLine("No Google products found to scrape.");
            await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", "No Google products found to scrape.", cancellationToken);
            return Ok(new { Message = "No Google products found to scrape." }); // Zmieniono na Ok dla spójności
        }

        Console.WriteLine($"Found {coOfrsToScrape.Count} Google products to scrape.");
        await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", 0, coOfrsToScrape.Count, 0, 0, cancellationToken);

        int totalScrapedCount = 0;
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var serviceScopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();
        int maxConcurrentScrapers = settings.SemophoreGoogle > 0 ? settings.SemophoreGoogle : 1;
        var semaphore = new SemaphoreSlim(maxConcurrentScrapers, maxConcurrentScrapers);
        var tasks = new List<Task>();
        var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

        // Lista do śledzenia scraperów, aby móc je zamknąć w razie potrzeby
        // Ta lista nie jest bezwzględnie konieczna, jeśli polegamy na CancellationToken
        // i poprawnym zamykaniu scrapera w bloku finally zadania.
        // List<GoogleMainPriceScraper> activeScrapers = new List<GoogleMainPriceScraper>();
        // object activeScrapersLock = new object();


        for (int i = 0; i < maxConcurrentScrapers; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                GoogleMainPriceScraper scraper = null; // Inicjalizuj jako null
                try
                {
                    await semaphore.WaitAsync(cancellationToken); // Czekaj na semafor z uwzględnieniem tokenu
                    if (cancellationToken.IsCancellationRequested) return;

                    scraper = new GoogleMainPriceScraper();
                    // Nie ma potrzeby subskrypcji zdarzenia, jeśli polegamy na wyjątku
                    // scraper.CaptchaDetected += (sender, args) => ResetCancellationToken(captchaDetected: true);

                    await scraper.InitializeAsync(settings);
                    if (cancellationToken.IsCancellationRequested) return;


                    while (true)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            Console.WriteLine($"Task {Task.CurrentId}: Cancellation requested for Google. Stopping processing.");
                            break;
                        }

                        CoOfrClass coOfr = null;
                        lock (productQueue) // Zabezpiecz dostęp do kolejki
                        {
                            if (productQueue.Count > 0)
                            {
                                coOfr = productQueue.Dequeue();
                            }
                        }

                        if (coOfr == null)
                        {
                            Console.WriteLine($"Task {Task.CurrentId}: No more Google products in queue.");
                            break;
                        }

                        try
                        {
                            using (var scope = serviceScopeFactory.CreateScope())
                            {
                                var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                                Console.WriteLine($"Task {Task.CurrentId}: Starting Google scraping for URL: {coOfr.GoogleOfferUrl}");

                                var scrapedPrices = await scraper.ScrapePricesAsync(coOfr.GoogleOfferUrl);
                                if (cancellationToken.IsCancellationRequested) break; // Sprawdź po długiej operacji

                                if (scrapedPrices.Any())
                                {
                                    foreach (var priceHistory in scrapedPrices)
                                    {
                                        priceHistory.CoOfrClassId = coOfr.Id;
                                    }
                                    scopedContext.CoOfrPriceHistories.AddRange(scrapedPrices);
                                    coOfr.GoogleIsScraped = true;
                                    coOfr.GooglePricesCount = scrapedPrices.Count;
                                    coOfr.GoogleIsRejected = false; // Zresetuj flagę odrzucenia, jeśli są ceny
                                }
                                else
                                {
                                    // Jeśli brak cen, ale nie było błędu (np. strona produktu bez ofert)
                                    coOfr.GoogleIsScraped = true;
                                    coOfr.GoogleIsRejected = true; // Można uznać za odrzucony, jeśli brak ofert jest problemem
                                    coOfr.GooglePricesCount = 0;
                                    Console.WriteLine($"Task {Task.CurrentId}: No prices found for Google product {coOfr.Id} ({coOfr.GoogleOfferUrl}).");
                                }
                                scopedContext.CoOfrs.Update(coOfr);
                                await scopedContext.SaveChangesAsync(CancellationToken.None); // Zapisz zmiany dla produktu
                                Console.WriteLine($"Task {Task.CurrentId}: Saved/Updated Google product {coOfr.Id}. Offers: {coOfr.GooglePricesCount}");

                                await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate",
                                    coOfr.Id, coOfr.GoogleIsScraped, coOfr.GoogleIsRejected, coOfr.GooglePricesCount, "Google", cancellationToken);

                                Interlocked.Increment(ref totalScrapedCount);
                                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate",
                                    totalScrapedCount, coOfrsToScrape.Count, elapsedSeconds, 0, cancellationToken);
                            }
                        }
                        catch (CaptchaDetectedException ex)
                        {
                            Console.WriteLine($"Task {Task.CurrentId}: CAPTCHA DETECTED for Google product {coOfr?.Id}: {ex.Message}. Signaling global cancellation.");
                            ResetCancellationToken(captchaDetected: true); // Sygnalizuj globalne zatrzymanie

                            if (coOfr != null) // Oznacz produkt jako odrzucony z powodu CAPTCHA
                            {
                                using (var scope = serviceScopeFactory.CreateScope())
                                {
                                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                                    var productToUpdate = await scopedContext.CoOfrs.FindAsync(coOfr.Id);
                                    if (productToUpdate != null)
                                    {
                                        productToUpdate.GoogleIsScraped = true;
                                        productToUpdate.GoogleIsRejected = true;
                                        scopedContext.CoOfrs.Update(productToUpdate);
                                        await scopedContext.SaveChangesAsync(CancellationToken.None);
                                        await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate",
                                           productToUpdate.Id, productToUpdate.GoogleIsScraped, productToUpdate.GoogleIsRejected, productToUpdate.GooglePricesCount, "Google", cancellationToken);
                                    }
                                }
                            }
                            break; // Przerwij pętlę dla tego zadania
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            Console.WriteLine($"Task {Task.CurrentId}: Google scraping for product {coOfr?.Id} cancelled by token.");
                            break;
                        }
                        catch (Exception ex) // Inne błędy scrapowania pojedynczego produktu
                        {
                            Console.WriteLine($"Task {Task.CurrentId}: Error scraping Google product {coOfr?.Id} ({coOfr?.GoogleOfferUrl}): {ex.Message} - StackTrace: {ex.StackTrace}");
                            if (coOfr != null)
                            {
                                using (var scope = serviceScopeFactory.CreateScope())
                                {
                                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                                    var productToUpdate = await scopedContext.CoOfrs.FindAsync(coOfr.Id);
                                    if (productToUpdate != null)
                                    {
                                        productToUpdate.GoogleIsScraped = true;
                                        productToUpdate.GoogleIsRejected = true;
                                        scopedContext.CoOfrs.Update(productToUpdate);
                                        await scopedContext.SaveChangesAsync(CancellationToken.None);
                                        await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate",
                                           productToUpdate.Id, productToUpdate.GoogleIsScraped, productToUpdate.GoogleIsRejected, productToUpdate.GooglePricesCount, "Google", cancellationToken);
                                    }
                                }
                            }
                            // Rozważ, czy błąd dla jednego produktu powinien przerywać całe zadanie,
                            // czy tylko logować i przechodzić do następnego produktu.
                            // Tutaj kontynuuje (nie ma `break`).
                        }
                    } // Koniec while (true)
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine($"Task {Task.CurrentId} for Google was cancelled before or during initialization.");
                }
                catch (Exception ex) // Krytyczne błędy (np. inicjalizacja scrapera, semafor)
                {
                    Console.WriteLine($"Task {Task.CurrentId}: CRITICAL error in Google scraping task: {ex.Message} - StackTrace: {ex.StackTrace}");
                    // Jeśli błąd wystąpił przed pętlą, inne zadania mogą nadal działać.
                    // Jeśli CAPTCHA jest problemem, ResetCancellationToken(true) powinien być wywołany.
                }
                finally
                {
                    Console.WriteLine($"Task {Task.CurrentId}: Google task finishing. Closing scraper and releasing semaphore.");
                    if (scraper != null)
                    {
                        await scraper.CloseAsync(); // Upewnij się, że scraper jest zamykany
                    }
                    semaphore.Release();
                    Console.WriteLine($"Task {Task.CurrentId}: Google semaphore released. Current count: {semaphore.CurrentCount}");
                }
            }/*, cancellationToken*/)); // Można przekazać token do Task.Run, ale i tak sprawdzamy go wewnątrz
        }

        try
        {
            await Task.WhenAll(tasks);
            Console.WriteLine("All Google scraping tasks have completed or been cancelled.");
        }
        catch (OperationCanceledException) // To się nie powinno zdarzyć, jeśli tokeny są poprawnie obsługiwane wewnątrz zadań
        {
            Console.WriteLine("Main Google scraping operation (Task.WhenAll) was externally canceled. This might indicate an issue.");
        }
        // Nie ma potrzeby łapania AggregateException, jeśli wyjątki są obsługiwane wewnątrz Task.Run

        stopwatch.Stop();
        Console.WriteLine($"Google scraping session finished. Total time: {stopwatch.Elapsed.TotalSeconds}s. Total products processed where scraping was attempted: {totalScrapedCount}.");

        string completionMessage = "Google scraping session completed.";
        if (_captchaGlobalSignal)
        {
            completionMessage = "Google scraping was STOPPED due to CAPTCHA detection.";
        }
        else if (cancellationToken.IsCancellationRequested && !_captchaGlobalSignal) // Anulowano ręcznie
        {
            completionMessage = "Google scraping was manually stopped by the user.";
        }
        await _hubContext.Clients.All.SendAsync("ReceiveGeneralMessage", completionMessage, CancellationToken.None);


        if (cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("Google scraping was cancelled, not redirecting.");
            return Ok(new { Message = completionMessage });
        }

        Console.WriteLine("Google scraping completed without cancellation, redirecting...");
        return RedirectToAction("GetUniqueScrapingUrls", "PriceScraping");
    }
}


//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using System.Diagnostics;


//public class GoogleMainPriceScraperController : Controller
//{
//    private readonly PriceSafariContext _context;
//    private readonly IHubContext<ScrapingHub> _hubContext;
//    private static CancellationTokenSource _googleCancellationTokenSource = new CancellationTokenSource();

//    public GoogleMainPriceScraperController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
//    {
//        _context = context;
//        _hubContext = hubContext;
//    }


//    private void ResetCancellationToken()
//    {
//        if (_googleCancellationTokenSource != null)
//        {
//            _googleCancellationTokenSource.Cancel();
//            _googleCancellationTokenSource.Dispose();
//        }
//        _googleCancellationTokenSource = new CancellationTokenSource();
//    }


//    [HttpPost]
//    public IActionResult StopScrapingGoogle()
//    {

//        ResetCancellationToken();
//        return Ok(new { Message = "Scraping stopped for Google." });
//    }

//    [HttpPost]
//    public async Task<IActionResult> StartScraping()
//    {
//        // Na starcie ustawiamy nowy token
//        ResetCancellationToken();
//        var cancellationToken = _googleCancellationTokenSource.Token;

//        // Get settings from the database
//        var settings = await _context.Settings.FirstOrDefaultAsync();
//        if (settings == null)
//        {
//            Console.WriteLine("Settings not found in the database.");
//            return BadRequest("Settings not found.");
//        }

//        // Get all CoOfrClass entries with a non-empty GoogleOfferUrl that haven't been scraped yet
//        var coOfrsToScrape = await _context.CoOfrs
//            .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
//            .ToListAsync();

//        if (!coOfrsToScrape.Any())
//        {
//            Console.WriteLine("No products found to scrape.");
//            return NotFound("No products found to scrape.");
//        }

//        Console.WriteLine($"Found {coOfrsToScrape.Count} products to scrape.");

//        if (_hubContext != null)
//        {
//            await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", 0, coOfrsToScrape.Count, 0, 0);
//        }
//        else
//        {
//            Console.WriteLine("Hub context is null.");
//        }

//        // Variables to track progress
//        int totalScraped = 0;
//        var stopwatch = new Stopwatch();
//        stopwatch.Start();

//        var serviceScopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();

//        // Get semaphore value from settings
//        int maxConcurrentScrapers = settings.SemophoreGoogle;
//        var semaphore = new SemaphoreSlim(maxConcurrentScrapers);
//        var tasks = new List<Task>();

//        var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

//        for (int i = 0; i < maxConcurrentScrapers; i++)
//        {
//            tasks.Add(Task.Run(async () =>
//            {
//                await semaphore.WaitAsync(cancellationToken);

//                var scraper = new GoogleMainPriceScraper();
//                if (scraper == null)
//                {
//                    Console.WriteLine("Scraper object is null.");
//                    semaphore.Release();
//                    return;
//                }

//                await scraper.InitializeAsync(settings);

//                try
//                {
//                    while (true)
//                    {
//                        // Sprawdzamy, czy nie nastąpiło anulowanie z zewnątrz:
//                        if (cancellationToken.IsCancellationRequested)
//                        {
//                            Console.WriteLine("Scraping (Google) was canceled by user request.");
//                            break;
//                        }

//                        CoOfrClass coOfr = null;

//                        lock (productQueue)
//                        {
//                            if (productQueue.Count > 0)
//                            {
//                                coOfr = productQueue.Dequeue();
//                            }
//                        }

//                        if (coOfr == null)
//                        {
//                            break;
//                        }

//                        try
//                        {
//                            using (var scope = serviceScopeFactory.CreateScope())
//                            {
//                                var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

//                                Console.WriteLine($"Starting scraping for URL: {coOfr.GoogleOfferUrl}");

//                                // Scrape
//                                var scrapedPrices = await scraper.ScrapePricesAsync(coOfr.GoogleOfferUrl);

//                                if (scrapedPrices.Any())
//                                {
//                                    // Set CoOfrClassId for each entry
//                                    foreach (var priceHistory in scrapedPrices)
//                                    {
//                                        // Zakładamy, że "scrapedPrices" jest listą obiektów
//                                        // CoOfrPriceHistoryClass - tak, jak w Twoim kodzie.
//                                        priceHistory.CoOfrClassId = coOfr.Id;
//                                    }

//                                    // Save all offers at once after processing the URL
//                                    scopedContext.CoOfrPriceHistories.AddRange(scrapedPrices);
//                                    await scopedContext.SaveChangesAsync();
//                                    Console.WriteLine($"Saved {scrapedPrices.Count} offers to the database for product {coOfr.GoogleOfferUrl}.");

//                                    // Update the product status after saving its offers
//                                    coOfr.GoogleIsScraped = true;
//                                    coOfr.GooglePricesCount = scrapedPrices.Count;

//                                    scopedContext.CoOfrs.Update(coOfr);
//                                    await scopedContext.SaveChangesAsync();
//                                    Console.WriteLine($"Updated status and offer count for product {coOfr.Id}: {coOfr.GooglePricesCount}.");

//                                    // Send update via SignalR
//                                    await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate",
//                                        coOfr.Id, coOfr.GoogleIsScraped, coOfr.GoogleIsRejected, coOfr.GooglePricesCount, "Google");
//                                }
//                                else
//                                {
//                                    // No prices scraped, mark as rejected
//                                    coOfr.GoogleIsScraped = true;
//                                    coOfr.GoogleIsRejected = true;
//                                    coOfr.GooglePricesCount = 0;

//                                    scopedContext.CoOfrs.Update(coOfr);
//                                    await scopedContext.SaveChangesAsync();

//                                    await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate",
//                                        coOfr.Id, coOfr.GoogleIsScraped, coOfr.GoogleIsRejected, coOfr.GooglePricesCount, "Google");
//                                }

//                                Interlocked.Increment(ref totalScraped);
//                                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
//                                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate",
//                                    totalScraped, coOfrsToScrape.Count, elapsedSeconds, 0);
//                            }
//                        }
//                        catch (Exception ex)
//                        {
//                            Console.WriteLine($"Error during scraping product {coOfr.Id}: {ex.Message}");
//                        }
//                    }
//                }
//                finally
//                {
//                    await scraper.CloseAsync();
//                    semaphore.Release();
//                }
//            }, cancellationToken));
//        }

//        await Task.WhenAll(tasks);
//        Console.WriteLine("All tasks completed.");

//        stopwatch.Stop();

//        return RedirectToAction("GetUniqueScrapingUrls", "PriceScraping");
//    }
//}
