using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Hubs;
using PriceTracker.Models;
using PriceTracker.Services;
using System.Diagnostics;
using System.Net.Http;

[Authorize(Roles = "Admin")]
public class PriceScrapingController : Controller
{
    private readonly PriceTrackerContext _context;
    private readonly IHubContext<ScrapingHub> _hubContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpClient _httpClient;

    public PriceScrapingController(PriceTrackerContext context, IHubContext<ScrapingHub> hubContext, IServiceProvider serviceProvider, HttpClient httpClient)
    {
        _context = context;
        _hubContext = hubContext;
        _serviceProvider = serviceProvider;
        _httpClient = httpClient;
    }


    [HttpPost]
    public async Task<IActionResult> StartScraping(int storeId)
    {
        var store = await _context.Stores.FindAsync(storeId);
        if (store == null)
        {
            Console.WriteLine("Store not found.");
            return NotFound("Store not found.");
        }

        var products = await _context.Products
          .Where(p => p.StoreId == storeId && p.IsScrapable && !p.IsRejected)
          .ToListAsync();

        if (products == null || !products.Any())
        {
            Console.WriteLine("No products found to scrape.");
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
        int rejectedCount = 0;
        var rejectedProducts = new List<(string Reason, string Url)>();
        var actualRejectedProducts = new List<ProductClass>();
        var stopwatch = new Stopwatch();
        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(8);

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

                    var scraper = new Scraper(_httpClient);
                    var tryCount = 0;
                    var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
                    List<(string Reason, string Url)> rejected = new List<(string Reason, string Url)>();
                    string log = "";

                    do
                    {
                        (prices, log, rejected) = await scraper.GetProductPricesAsync(product.OfferUrl, ++tryCount);
                        Console.WriteLine(log);

                        if (rejected.Count == 0)
                            break;

                    } while (tryCount < 2);

                    lock (rejectedProducts)
                    {
                        rejectedProducts.AddRange(rejected);
                    }

                    var ourStorePrices = prices.Where(p => p.storeName.ToLower() == store.StoreName.ToLower()).ToList();
                    if (ourStorePrices.Count == 0 || ourStorePrices.Count == prices.Count)
                    {
                        lock (actualRejectedProducts)
                        {
                            actualRejectedProducts.Add(product);
                        }
                        Interlocked.Increment(ref rejectedCount);
                        await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", Interlocked.Increment(ref scrapedCount), products.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
                        return;
                    }

                    foreach (var priceData in prices)
                    {
                        var priceHistory = new PriceHistoryClass
                        {
                            ProductId = product.ProductId,
                            StoreName = priceData.storeName,
                            Price = priceData.price,
                            ScrapHistoryId = scrapHistory.Id,
                            ShippingCostNum = priceData.shippingCostNum,
                            AvailabilityNum = priceData.availabilityNum,
                            IsBidding = priceData.isBidding,
                            Position = priceData.position
                        };

                        scopedContext.PriceHistories.Add(priceHistory);
                    }

                    await scopedContext.SaveChangesAsync();
                    Interlocked.Increment(ref totalPrices);
                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", Interlocked.Increment(ref scrapedCount), products.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
                }
                catch (Exception ex)
                {
                    var log = $"Error scraping URL: {product.OfferUrl}. Exception: {ex.Message}";
                    Console.WriteLine(log);
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

        foreach (var product in actualRejectedProducts)
        {
            product.IsRejected = true;
        }
        await _context.SaveChangesAsync();

        Console.WriteLine("Summary of rejected products:");
        foreach (var product in actualRejectedProducts)
        {
            Console.WriteLine($"URL: {product.OfferUrl}");
        }

        return RedirectToAction("ProductList", "Store", new { storeId });
    }


    [HttpGet]
    public async Task<IActionResult> StartScrapingGet(int storeId)
    {
        return await StartScraping(storeId);
    }
}

