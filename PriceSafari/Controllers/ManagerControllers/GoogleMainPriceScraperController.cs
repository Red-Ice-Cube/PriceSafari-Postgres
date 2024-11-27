using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Scrapers;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

public class GoogleMainPriceScraperController : Controller
{
    private readonly PriceSafariContext _context;
    private readonly IHubContext<ScrapingHub> _hubContext;

    public GoogleMainPriceScraperController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    [HttpPost]
    public async Task<IActionResult> StartScraping()
    {
        // Get settings from the database
        var settings = await _context.Settings.FirstOrDefaultAsync();
        if (settings == null)
        {
            Console.WriteLine("Settings not found in the database.");
            return BadRequest("Settings not found.");
        }

        // Get all CoOfrClass entries with a non-empty GoogleOfferUrl that haven't been scraped yet
        var coOfrsToScrape = await _context.CoOfrs
            .Where(c => !string.IsNullOrEmpty(c.GoogleOfferUrl) && !c.GoogleIsScraped)
            .ToListAsync();

        if (!coOfrsToScrape.Any())
        {
            Console.WriteLine("No products found to scrape.");
            return NotFound("No products found to scrape.");
        }

        Console.WriteLine($"Found {coOfrsToScrape.Count} products to scrape.");

        if (_hubContext != null)
        {
            await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", 0, coOfrsToScrape.Count, 0, 0);
        }
        else
        {
            Console.WriteLine("Hub context is null.");
        }

        // Variables to track progress
        int totalScraped = 0;
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var serviceScopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();

        // Get semaphore value from settings
        int maxConcurrentScrapers = settings.Semophore;
        var semaphore = new SemaphoreSlim(maxConcurrentScrapers);
        var tasks = new List<Task>();

        var productQueue = new Queue<CoOfrClass>(coOfrsToScrape);

        for (int i = 0; i < maxConcurrentScrapers; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();

                var scraper = new GoogleMainPriceScraper();
                if (scraper == null)
                {
                    Console.WriteLine("Scraper object is null.");
                    semaphore.Release();
                    return;
                }

                await scraper.InitializeAsync(settings);

                while (true)
                {
                    CoOfrClass coOfr = null;

                    lock (productQueue)
                    {
                        if (productQueue.Count > 0)
                        {
                            coOfr = productQueue.Dequeue();
                        }
                    }

                    if (coOfr == null)
                    {
                        break;
                    }

                    try
                    {
                        using (var scope = serviceScopeFactory.CreateScope())
                        {
                            var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                            Console.WriteLine($"Starting scraping for URL: {coOfr.GoogleOfferUrl}");

                            var scrapedPrices = await scraper.ScrapePricesAsync(coOfr.GoogleOfferUrl);

                            if (scrapedPrices.Any())
                            {
                                // Set CoOfrClassId for each entry
                                foreach (var priceHistory in scrapedPrices)
                                {
                                    priceHistory.CoOfrClassId = coOfr.Id;
                                }

                                // Save all offers at once after processing the URL
                                scopedContext.CoOfrPriceHistories.AddRange(scrapedPrices);
                                await scopedContext.SaveChangesAsync();
                                Console.WriteLine($"Saved {scrapedPrices.Count} offers to the database for product {coOfr.GoogleOfferUrl}.");

                                // Update the product status after saving its offers
                                coOfr.GoogleIsScraped = true;
                                coOfr.GooglePricesCount = scrapedPrices.Count;

                                scopedContext.CoOfrs.Update(coOfr);
                                await scopedContext.SaveChangesAsync();
                                Console.WriteLine($"Updated status and offer count for product {coOfr.Id}: {coOfr.GooglePricesCount}.");

                                // Send update via SignalR
                                await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfr.Id, coOfr.GoogleIsScraped, coOfr.GoogleIsRejected, coOfr.GooglePricesCount, "Google");
                            }
                            else
                            {
                                // No prices scraped, mark as rejected
                                coOfr.GoogleIsScraped = true;
                                coOfr.GoogleIsRejected = true;
                                coOfr.GooglePricesCount = 0;

                                scopedContext.CoOfrs.Update(coOfr);
                                await scopedContext.SaveChangesAsync();

                                await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfr.Id, coOfr.GoogleIsScraped, coOfr.GoogleIsRejected, coOfr.GooglePricesCount, "Google");
                            }

                            Interlocked.Increment(ref totalScraped);
                            double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                            await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", totalScraped, coOfrsToScrape.Count, elapsedSeconds, 0);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during scraping product {coOfr.Id}: {ex.Message}");
                    }
                }

                await scraper.CloseAsync();
                semaphore.Release();
            }));
        }

        await Task.WhenAll(tasks);
        Console.WriteLine("All tasks completed.");

        stopwatch.Stop();

        return RedirectToAction("GetUniqueScrapingUrls", "PriceScraping");
    }
}
