//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using PriceTracker.Data;
//using PriceTracker.Hubs;
//using PriceTracker.Models;
//using PriceTracker.Services;
//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;

//public class PriceScrapingController : Controller
//{
//    private readonly PriceTrackerContext _context;
//    private readonly IHubContext<ScrapingHub> _hubContext;
//    private readonly IServiceProvider _serviceProvider;

//    public PriceScrapingController(PriceTrackerContext context, IHubContext<ScrapingHub> hubContext, IServiceProvider serviceProvider)
//    {
//        _context = context;
//        _hubContext = hubContext;
//        _serviceProvider = serviceProvider;
//    }

//    [HttpPost]
//    public async Task<IActionResult> StartScraping(int storeId)
//    {
//        var store = await _context.Stores.FindAsync(storeId);
//        if (store == null)
//        {
//            Console.WriteLine("Store not found.");
//            return NotFound("Store not found.");
//        }

//        var products = await _context.Products.Where(p => p.StoreId == storeId).ToListAsync();
//        if (products == null || !products.Any())
//        {
//            Console.WriteLine("No products found to scrape.");
//            return NotFound("No products found to scrape.");
//        }

//        var scrapHistory = new ScrapHistoryClass
//        {
//            Date = DateTime.Now,
//            ProductCount = products.Count,
//            PriceCount = 0,
//            StoreId = storeId
//        };
//        _context.ScrapHistories.Add(scrapHistory);
//        await _context.SaveChangesAsync();

//        int scrapedCount = 0;
//        int totalPrices = 0;
//        int rejectedCount = 0;
//        var stopwatch = new Stopwatch();
//        var tasks = new List<Task>();
//        var semaphore = new SemaphoreSlim(5);

//        stopwatch.Start();

//        foreach (var product in products)
//        {
//            tasks.Add(Task.Run(async () =>
//            {
//                await semaphore.WaitAsync();
//                try
//                {
//                    using var scope = _serviceProvider.CreateScope();
//                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceTrackerContext>();

//                    var puppeteerScraper = new PuppeteerScraper();
//                    var (prices, log) = await puppeteerScraper.GetProductPricesAsync(product.OfferUrl);
//                    Console.WriteLine(log);

//                    var ourStorePrices = prices.Where(p => p.storeName.ToLower() == store.StoreName.ToLower()).ToList();
//                    if (ourStorePrices.Count == 0 || ourStorePrices.Count == prices.Count)
//                    {
//                        Interlocked.Increment(ref rejectedCount);
//                        await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", Interlocked.Increment(ref scrapedCount), products.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
//                        return;
//                    }

//                    foreach (var priceData in prices)
//                    {
//                        var priceHistory = new PriceHistoryClass
//                        {
//                            ProductId = product.ProductId,
//                            StoreName = priceData.storeName,
//                            Price = priceData.price,
//                            ScrapHistoryId = scrapHistory.Id,
//                            ShippingCostNum = priceData.shippingCostNum,
//                            AvailabilityNum = priceData.availabilityNum
//                        };

//                        scopedContext.PriceHistories.Add(priceHistory);
//                    }

//                    await scopedContext.SaveChangesAsync();
//                    Interlocked.Increment(ref totalPrices);
//                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", Interlocked.Increment(ref scrapedCount), products.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
//                }
//                catch (Exception ex)
//                {
//                    var log = $"Error scraping URL: {product.OfferUrl}. Exception: {ex.Message}";
//                    Console.WriteLine(log);
//                }
//                finally
//                {
//                    semaphore.Release();
//                }
//            }));
//        }

//        await Task.WhenAll(tasks);

//        stopwatch.Stop();

//        scrapHistory.PriceCount = totalPrices;
//        _context.ScrapHistories.Update(scrapHistory);
//        await _context.SaveChangesAsync();

//        return RedirectToAction("ProductList", "Store", new { storeId = storeId });
//    }

//    [HttpGet]
//    public async Task<IActionResult> StartScrapingGet(int storeId)
//    {
//        return await StartScraping(storeId);
//    }
//}
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Hubs;
using PriceTracker.Models;
using PriceTracker.Services;
using System.Diagnostics;

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

        var products = await _context.Products.Where(p => p.StoreId == storeId).ToListAsync();
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
        var stopwatch = new Stopwatch();
        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(2);

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
                    var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum)>();
                    List<(string Reason, string Url)> rejected = new List<(string Reason, string Url)>();
                    string log = "";

                    do
                    {
                        (prices, log, rejected) = await scraper.GetProductPricesAsync(product.OfferUrl, ++tryCount);
                        Console.WriteLine(log);

                        if (rejected.Count == 0)
                            break;

                    } while (tryCount < 3);

                    lock (rejectedProducts)
                    {
                        rejectedProducts.AddRange(rejected);
                    }

                    var ourStorePrices = prices.Where(p => p.storeName.ToLower() == store.StoreName.ToLower()).ToList();
                    if (ourStorePrices.Count == 0 || ourStorePrices.Count == prices.Count)
                    {
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
                            AvailabilityNum = priceData.availabilityNum
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

        // Log rejected products
        Console.WriteLine("Summary of rejected products:");
        foreach (var rejected in rejectedProducts)
        {
            Console.WriteLine($"URL: {rejected.Url}, Reason: {rejected.Reason}");
        }

        return RedirectToAction("ProductList", "Store", new { storeId = storeId });
    }

    [HttpGet]
    public async Task<IActionResult> StartScrapingGet(int storeId)
    {
        return await StartScraping(storeId);
    }
}





//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using PriceTracker.Data;
//using PriceTracker.Hubs;
//using PriceTracker.Models;
//using PriceTracker.Services;
//using System.Diagnostics;

//public class PriceScrapingController : Controller
//{
//    private readonly PriceTrackerContext _context;
//    private readonly Scraper _scraper;
//    private readonly IHubContext<ScrapingHub> _hubContext;
//    private readonly IServiceProvider _serviceProvider;

//    public PriceScrapingController(PriceTrackerContext context, Scraper scraper, IHubContext<ScrapingHub> hubContext, IServiceProvider serviceProvider)
//    {
//        _context = context;
//        _scraper = scraper;
//        _hubContext = hubContext;
//        _serviceProvider = serviceProvider;
//    }

//    [HttpPost]
//    public async Task<IActionResult> StartScraping(int storeId)
//    {
//        var store = await _context.Stores.FindAsync(storeId);
//        if (store == null) return NotFound("Store not found.");

//        var products = await _context.Products.Where(p => p.StoreId == storeId).ToListAsync();
//        if (products == null || !products.Any())
//        {
//            return NotFound("No products found to scrape.");
//        }

//        var scrapHistory = new ScrapHistoryClass
//        {
//            Date = DateTime.Now,
//            ProductCount = products.Count,
//            PriceCount = 0,
//            StoreId = storeId
//        };
//        _context.ScrapHistories.Add(scrapHistory);
//        await _context.SaveChangesAsync();

//        int scrapedCount = 0;
//        int totalPrices = 0;
//        int rejectedCount = 0; // Licznik odrzuconych produktów
//        var stopwatch = new Stopwatch();
//        var tasks = new List<Task>();
//        var semaphore = new SemaphoreSlim(50);

//        stopwatch.Start();

//        foreach (var product in products)
//        {
//            tasks.Add(Task.Run(async () =>
//            {
//                await semaphore.WaitAsync();
//                try
//                {
//                    using var scope = _serviceProvider.CreateScope();
//                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceTrackerContext>();

//                    var (prices, log) = await _scraper.GetProductPricesAsync(product.OfferUrl);


//                    var ourStorePrices = prices.Where(p => p.storeName.ToLower() == store.StoreName.ToLower()).ToList();
//                    if (ourStorePrices.Count == 0 || ourStorePrices.Count == prices.Count)
//                    {

//                        Interlocked.Increment(ref rejectedCount);
//                        await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", Interlocked.Increment(ref scrapedCount), products.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
//                        return;
//                    }

//                    foreach (var priceData in prices)
//                    {
//                        var priceHistory = new PriceHistoryClass
//                        {
//                            ProductId = product.ProductId,
//                            StoreName = priceData.storeName,
//                            Price = priceData.price,
//                            ScrapHistoryId = scrapHistory.Id,
//                            ShippingCostNum = priceData.shippingCostNum,
//                            AvailabilityNum = priceData.availabilityNum
//                        };

//                        scopedContext.PriceHistories.Add(priceHistory);
//                    }

//                    await scopedContext.SaveChangesAsync();
//                    Interlocked.Increment(ref totalPrices);
//                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", Interlocked.Increment(ref scrapedCount), products.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
//                }
//                catch (Exception ex)
//                {
//                    var log = $"Error scraping URL: {product.OfferUrl}. Exception: {ex.Message}";
//                }
//                finally
//                {
//                    semaphore.Release();
//                }
//            }));
//        }

//        await Task.WhenAll(tasks);

//        stopwatch.Stop();

//        scrapHistory.PriceCount = totalPrices;
//        _context.ScrapHistories.Update(scrapHistory);
//        await _context.SaveChangesAsync();

//        return RedirectToAction("ProductList", "Store", new { storeId = storeId });
//    }

//    [HttpGet]
//    public async Task<IActionResult> StartScrapingGet(int storeId)
//    {
//        return await StartScraping(storeId);
//    }
//}
