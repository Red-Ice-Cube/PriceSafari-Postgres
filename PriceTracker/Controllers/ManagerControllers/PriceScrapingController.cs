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
    private readonly IHttpClientFactory _httpClientFactory;

    public PriceScrapingController(PriceTrackerContext context, IHubContext<ScrapingHub> hubContext, IServiceProvider serviceProvider, HttpClient httpClient, IHttpClientFactory httpClientFactory)
    {
        _context = context;
        _hubContext = hubContext;
        _serviceProvider = serviceProvider;
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
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
        var semaphore = new SemaphoreSlim(4);

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


    [HttpPost]
    public async Task<IActionResult> GroupAndSaveUniqueUrls()
    {
        var uniqueUrls = await _context.Products
            .Where(p => p.IsScrapable && !p.IsRejected)
            .GroupBy(p => p.OfferUrl)
            .Select(g => new CoOfrClass
            {
                OfferUrl = g.Key,
                ProductIds = g.Select(p => p.ProductId).ToList()
            })
            .ToListAsync();

        _context.CoOfrs.RemoveRange(_context.CoOfrs);  
        _context.CoOfrs.AddRange(uniqueUrls); 
        await _context.SaveChangesAsync();

        return RedirectToAction("GetUniqueScrapingUrls");
    }

    [HttpGet]
    public async Task<IActionResult> GetUniqueScrapingUrls()
    {
        var uniqueUrls = await _context.CoOfrs.ToListAsync();
        var coOfrPriceHistories = await _context.CoOfrPriceHistories.ToListAsync();

        var scrapedUrlsCount = coOfrPriceHistories.Select(ph => ph.CoOfrClassId).Distinct().Count();
        ViewBag.ScrapedUrlsCount = scrapedUrlsCount;
        ViewBag.TotalUrlsCount = uniqueUrls.Count;

        return View("~/Views/ManagerPanel/Store/GetUniqueScrapingUrls.cshtml", uniqueUrls);
    }


    [HttpPost]
    public async Task<IActionResult> ClearCoOfrPriceHistories()
    {
        _context.CoOfrPriceHistories.RemoveRange(_context.CoOfrPriceHistories);
        await _context.SaveChangesAsync();
        return RedirectToAction("GetUniqueScrapingUrls");
    }



    [HttpPost]
    public async Task<IActionResult> StartScrapingByCoOfrUrls()
    {
        var coOfrs = await _context.CoOfrs.ToListAsync();
        var scrapedCoOfrIds = await _context.CoOfrPriceHistories
            .Select(ph => ph.CoOfrClassId)
            .Distinct()
            .ToListAsync();

        var urls = coOfrs
            .Where(co => !scrapedCoOfrIds.Contains(co.Id))
            .Select(co => co.OfferUrl)
            .ToList();

        if (urls == null || !urls.Any())
        {
            Console.WriteLine("No URLs found to scrape.");
            return NotFound("No URLs found to scrape.");
        }

        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(1);
        int totalPrices = 0;
        int scrapedCount = 0;
        int rejectedCount = 0;
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        foreach (var coOfr in coOfrs.Where(co => !scrapedCoOfrIds.Contains(co.Id)))
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
                        (prices, log, rejected) = await scraper.GetProductPricesAsync(coOfr.OfferUrl, ++tryCount);
                        Console.WriteLine(log);

                        if (rejected.Count == 0)
                            break;

                    } while (tryCount < 2);

                    var priceHistories = prices.Select(priceData => new CoOfrPriceHistoryClass
                    {
                        CoOfrClassId = coOfr.Id,
                        StoreName = priceData.storeName,
                        Price = priceData.price,
                        ShippingCostNum = priceData.shippingCostNum,
                        AvailabilityNum = priceData.availabilityNum,
                        IsBidding = priceData.isBidding,
                        Position = priceData.position
                    }).ToList();

                    await scopedContext.CoOfrPriceHistories.AddRangeAsync(priceHistories);
                    await scopedContext.SaveChangesAsync();

                    Interlocked.Add(ref totalPrices, priceHistories.Count);
                    Interlocked.Increment(ref scrapedCount);
                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", scrapedCount, coOfrs.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
                }
                catch (Exception ex)
                {
                    var log = $"Error scraping URL: {coOfr.OfferUrl}. Exception: {ex.Message}";
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

        return Ok(new { Message = "Scraping completed.", TotalPrices = totalPrices, RejectedCount = rejectedCount });
    }


    [HttpPost]
    public async Task<IActionResult> StartScrapingWithCaptchaHandling()
    {
        var coOfrs = await _context.CoOfrs.ToListAsync();
        var scrapedCoOfrIds = await _context.CoOfrPriceHistories
            .Select(ph => ph.CoOfrClassId)
            .Distinct()
            .ToListAsync();

        var urls = coOfrs
            .Where(co => !scrapedCoOfrIds.Contains(co.Id))
            .Select(co => co.OfferUrl)
            .ToList();

        if (!urls.Any())
        {
            Console.WriteLine("No URLs found to scrape.");
            return NotFound("No URLs found to scrape.");
        }

        var scraper = new Scraper(_httpClientFactory.CreateClient());
        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(1);
        int totalPrices = 0;
        int scrapedCount = 0;
        int rejectedCount = 0;
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        await scraper.InitializeBrowserAsync();

        foreach (var coOfr in coOfrs.Where(co => !scrapedCoOfrIds.Contains(co.Id)))
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceTrackerContext>();

                    var (prices, log, rejected) = await scraper.HandleCaptchaAndScrapePricesAsync(coOfr.OfferUrl);
                    Console.WriteLine(log);

                    var priceHistories = prices.Select(priceData => new CoOfrPriceHistoryClass
                    {
                        CoOfrClassId = coOfr.Id,
                        StoreName = priceData.storeName,
                        Price = priceData.price,
                        ShippingCostNum = priceData.shippingCostNum,
                        AvailabilityNum = priceData.availabilityNum,
                        IsBidding = priceData.isBidding,
                        Position = priceData.position
                    }).ToList();

                    await scopedContext.CoOfrPriceHistories.AddRangeAsync(priceHistories);
                    await scopedContext.SaveChangesAsync();

                    Interlocked.Add(ref totalPrices, priceHistories.Count);
                    Interlocked.Increment(ref scrapedCount);
                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", scrapedCount, coOfrs.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
                }
                catch (Exception ex)
                {
                    var log = $"Error scraping URL: {coOfr.OfferUrl}. Exception: {ex.Message}";
                    Console.WriteLine(log);
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);

        await scraper.CloseBrowserAsync();

        stopwatch.Stop();

        return Ok(new { Message = "Scraping completed.", TotalPrices = totalPrices, RejectedCount = rejectedCount });
    }



    [HttpGet]
    public async Task<IActionResult> GetStoreProductsWithCoOfrIds(int storeId)
    {
        var store = await _context.Stores.FindAsync(storeId);
        if (store == null)
        {
            return NotFound();
        }

        var products = await _context.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

        var coOfrClasses = await _context.CoOfrs.ToListAsync();

        var productCoOfrViewModels = products.Select(product => new ProductCoOfrViewModel
        {
            ProductId = product.ProductId,
            ProductName = product.ProductName,
            Category = product.Category,
            OfferUrl = product.OfferUrl,
            CoOfrId = coOfrClasses.FirstOrDefault(co => co.ProductIds.Contains(product.ProductId))?.Id
        }).ToList();

        var viewModel = new StoreProductsViewModel
        {
            StoreName = store.StoreName,
            Products = productCoOfrViewModels
        };

        ViewBag.StoreId = storeId; 

        return View("~/Views/ManagerPanel/Store/GetStoreProductsWithCoOfrIds.cshtml", viewModel);
    }


    [HttpPost]
    public async Task<IActionResult> MapCoOfrToPriceHistory(int storeId)
    {
        var store = await _context.Stores.FindAsync(storeId);
        if (store == null)
        {
            return NotFound();
        }

        var products = await _context.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

        var coOfrClasses = await _context.CoOfrs.ToListAsync();
        var coOfrPriceHistories = await _context.CoOfrPriceHistories.ToListAsync();

        var scrapHistory = new ScrapHistoryClass
        {
            Date = DateTime.Now,
            StoreId = storeId,
            ProductCount = products.Count,
            PriceCount = 0,
            Store = store
        };

        var priceHistories = new List<PriceHistoryClass>();

        foreach (var product in products)
        {
            var coOfrId = coOfrClasses.FirstOrDefault(co => co.ProductIds.Contains(product.ProductId))?.Id;
            if (coOfrId != null)
            {
                var coOfrPriceHistory = coOfrPriceHistories.Where(ph => ph.CoOfrClassId == coOfrId).ToList();

                foreach (var coOfrPrice in coOfrPriceHistory)
                {
                    var priceHistory = new PriceHistoryClass
                    {
                        ProductId = product.ProductId,
                        StoreName = coOfrPrice.StoreName,
                        Price = coOfrPrice.Price,
                        IsBidding = coOfrPrice.IsBidding,
                        Position = coOfrPrice.Position,
                        ShippingCostNum = coOfrPrice.ShippingCostNum,
                        AvailabilityNum = coOfrPrice.AvailabilityNum,
                        ScrapHistory = scrapHistory
                    };

                    priceHistories.Add(priceHistory);
                }
            }
        }

        scrapHistory.PriceCount = priceHistories.Count;
        _context.ScrapHistories.Add(scrapHistory);
        _context.PriceHistories.AddRange(priceHistories);

        await _context.SaveChangesAsync();

        return RedirectToAction("GetStoreProductsWithCoOfrIds", new { storeId });
    }




}

