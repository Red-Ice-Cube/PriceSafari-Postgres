using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Hubs;
using PriceTracker.Models;
using PriceTracker.Services;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

public class PriceScrapingController : Controller
{
    private readonly PriceTrackerContext _context;
    private readonly Scraper _scraper;
    private readonly IHubContext<ScrapingHub> _hubContext;
    private readonly IServiceProvider _serviceProvider;

    public PriceScrapingController(PriceTrackerContext context, Scraper scraper, IHubContext<ScrapingHub> hubContext, IServiceProvider serviceProvider)
    {
        _context = context;
        _scraper = scraper;
        _hubContext = hubContext;
        _serviceProvider = serviceProvider;
    }

    [HttpPost]
    public async Task<IActionResult> StartScraping(int storeId)
    {
        var store = await _context.Stores.FindAsync(storeId);
        if (store == null) return NotFound("Store not found.");

        var products = await _context.Products.Where(p => p.StoreId == storeId).ToListAsync();
        if (products == null || !products.Any())
        {
            return NotFound("No products found to scrape.");
        }

        var scrapHistory = new ScrapHistoryClass
        {
            Date = DateTime.Now,
            ProductCount = products.Count,
            PriceCount = 0,
            StoreId = storeId
        };
        _context.ScrapHistories.Add(scrapHistory);
        await _context.SaveChangesAsync();

        int scrapedCount = 0;
        int totalPrices = 0;
        var stopwatch = new Stopwatch();
        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(50);

        stopwatch.Start();

        foreach (var product in products)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceTrackerContext>();

                    var (prices, log) = await _scraper.GetProductPricesAsync(product.OfferUrl);

                    foreach (var priceData in prices)
                    {
                        var priceHistory = new PriceHistoryClass
                        {
                            ProductId = product.ProductId,
                            StoreName = priceData.storeName,
                            Date = DateTime.Now,
                            Price = priceData.price,
                            OfferUrl = product.OfferUrl,
                            ScrapHistoryId = scrapHistory.Id,
                            ShippingCost = priceData.shippingCost,
                            ShippingCostNum = priceData.shippingCostNum,
                            Availability = priceData.availability,
                            AvailabilityNum = priceData.availabilityNum
                        };

                        scopedContext.PriceHistories.Add(priceHistory);
                    }

                    await scopedContext.SaveChangesAsync();
                    Interlocked.Increment(ref scrapedCount);
                    Interlocked.Add(ref totalPrices, prices.Count);
                    var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", scrapedCount, products.Count, elapsedSeconds);
                }
                catch (Exception ex)
                {
                    var log = $"Error scraping URL: {product.OfferUrl}. Exception: {ex.Message}";
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);

        stopwatch.Stop();

        scrapHistory.PriceCount = totalPrices;
        _context.ScrapHistories.Update(scrapHistory);
        await _context.SaveChangesAsync();

        return RedirectToAction("ProductList", "Store", new { storeId = storeId });
    }

    [HttpGet]
    public async Task<IActionResult> StartScrapingGet(int storeId)
    {
        return await StartScraping(storeId);
    }
}


