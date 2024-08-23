using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Services;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;

[Authorize(Roles = "Admin")]
public class PriceScrapingController : Controller
{
    private readonly PriceSafariContext _context;
    private readonly IHubContext<ScrapingHub> _hubContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private static CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

    public PriceScrapingController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext, IServiceProvider serviceProvider, HttpClient httpClient, IHttpClientFactory httpClientFactory)
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
        var settings = await _context.Settings.FirstOrDefaultAsync();
        if (settings == null)
        {
            Console.WriteLine("Settings not found.");
            return NotFound("Settings not found.");
        }

        int scrapSemaphoreSlim = settings.ScrapSemaphoreSlim;

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
        var semaphore = new SemaphoreSlim(scrapSemaphoreSlim);

        stopwatch.Start();

        foreach (var product in products)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

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
            .Where(p => p.IsScrapable)
            .GroupBy(p => p.OfferUrl)
            .Select(g => new CoOfrClass
            {
                OfferUrl = g.Key,
                ProductIds = g.Select(p => p.ProductId).ToList(),
                IsScraped = false
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
        var scrapedUrlsCount = uniqueUrls.Count(u => u.IsScraped);
        ViewBag.ScrapedUrlsCount = scrapedUrlsCount;
        ViewBag.TotalUrlsCount = uniqueUrls.Count;

        return View("~/Views/ManagerPanel/Store/GetUniqueScrapingUrls.cshtml", uniqueUrls);
    }

    [HttpPost]
    public async Task<IActionResult> ClearCoOfrPriceHistories()
    {
        await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE CoOfrPriceHistories");

        await _context.Database.ExecuteSqlRawAsync("UPDATE CoOfrs SET IsScraped = 0, ScrapingMethod = NULL, PricesCount = 0");

        return RedirectToAction("GetUniqueScrapingUrls");
    }

    private void ResetCancellationToken()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    [HttpPost]
    public IActionResult StopScraping()
    {
        ResetCancellationToken();
        return Ok(new { Message = "Scraping stopped." });
    }

    [HttpPost]
    public async Task<IActionResult> StartScrapingByCoOfrUrls()
    {
        ResetCancellationToken();
        var cancellationToken = _cancellationTokenSource.Token;

        var coOfrs = await _context.CoOfrs.Where(co => !co.IsScraped).ToListAsync();

        if (!coOfrs.Any())
        {
            Console.WriteLine("No URLs found to scrape.");
            return NotFound("No URLs found to scrape.");
        }

        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(4);
        int totalPrices = 0;
        int scrapedCount = 0;
        int rejectedCount = 0;
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        foreach (var coOfr in coOfrs)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

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

                        cancellationToken.ThrowIfCancellationRequested();
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

                    await scopedContext.CoOfrPriceHistories.AddRangeAsync(priceHistories, cancellationToken);
                    await scopedContext.SaveChangesAsync(cancellationToken);

                    coOfr.IsScraped = true;
                    coOfr.ScrapingMethod = "Http";
                    coOfr.PricesCount = priceHistories.Count;
                    coOfr.IsRejected = (priceHistories.Count == 0);
                    scopedContext.CoOfrs.Update(coOfr);
                    await scopedContext.SaveChangesAsync(cancellationToken);

                    await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfr.OfferUrl, coOfr.IsScraped, coOfr.IsRejected, coOfr.ScrapingMethod, coOfr.PricesCount);

                    Interlocked.Add(ref totalPrices, priceHistories.Count);
                    Interlocked.Increment(ref scrapedCount);
                    if (coOfr.IsRejected)
                    {
                        Interlocked.Increment(ref rejectedCount);
                    }
                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", scrapedCount, coOfrs.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);

                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Scraping canceled.");
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
            }, cancellationToken));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Scraping was canceled.");
        }

        stopwatch.Stop();

        return Ok(new { Message = "Scraping completed.", TotalPrices = totalPrices, RejectedCount = rejectedCount });
    }


    [HttpPost]
    public async Task<IActionResult> StartScrapingWithCaptchaHandling()
    {
        ResetCancellationToken();
        var cancellationToken = _cancellationTokenSource.Token;

        var settings = await _context.Settings.FirstOrDefaultAsync();
        if (settings == null)
        {
            Console.WriteLine("Settings not found.");
            return NotFound("Settings not found.");
        }

        int captchaSpeed = settings.CaptchaSpeed;

        var coOfrs = await _context.CoOfrs.Where(co => !co.IsScraped).ToListAsync();
        var urls = coOfrs.Select(co => co.OfferUrl).ToList();
        var urlQueue = new Queue<string>(urls);

        if (!urls.Any())
        {
            Console.WriteLine("No URLs found to scrape.");
            return NotFound("No URLs found to scrape.");
        }

        var tasks = new List<Task>();
        int totalPrices = 0;
        int scrapedCount = 0;
        int rejectedCount = 0;
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        using (var semaphore = new SemaphoreSlim(captchaSpeed))
        {
            for (int i = 0; i < captchaSpeed; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken);

                    try
                    {
                        var httpClient = _httpClientFactory.CreateClient();
                        var captchaScraper = new CaptchaScraper(httpClient);

                        await captchaScraper.InitializeBrowserAsync();

                        while (urlQueue.Count > 0)
                        {
                            string url;
                            lock (urlQueue)
                            {
                                if (urlQueue.Count == 0)
                                {
                                    break;
                                }
                                url = urlQueue.Dequeue();
                            }

                            try
                            {
                                using var scope = _serviceProvider.CreateScope();
                                var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                var (prices, log, rejected) = await captchaScraper.HandleCaptchaAndScrapePricesAsync(url);
                                Console.WriteLine(log);

                                if (prices.Count > 0)
                                {
                                    var coOfr = coOfrs.First(co => co.OfferUrl == url);
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
                                    coOfr.IsScraped = true;
                                    coOfr.ScrapingMethod = "HandleCaptcha";
                                    coOfr.PricesCount = priceHistories.Count;
                                    coOfr.IsRejected = (priceHistories.Count == 0);
                                    scopedContext.CoOfrs.Update(coOfr);
                                    await scopedContext.SaveChangesAsync();

                                    await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfr.OfferUrl, coOfr.IsScraped, coOfr.IsRejected, coOfr.ScrapingMethod, coOfr.PricesCount);

                                    Interlocked.Add(ref totalPrices, priceHistories.Count);
                                    Interlocked.Increment(ref scrapedCount);
                                    if (coOfr.IsRejected)
                                    {
                                        Interlocked.Increment(ref rejectedCount);
                                    }
                                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", scrapedCount, coOfrs.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
                                }

                                if (cancellationToken.IsCancellationRequested)
                                {
                                    Console.WriteLine("Scraping canceled.");
                                    await captchaScraper.CloseBrowserAsync();
                                    return;
                                }
                            }
                            catch (Exception ex)
                            {
                                var log = $"Error scraping URL: {url}. Exception: {ex.Message}";
                                Console.WriteLine(log);
                            }
                        }

                        await captchaScraper.CloseBrowserAsync();
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Scraping was canceled.");
            }
        }

        stopwatch.Stop();

        return Ok(new { Message = "Scraping completed.", TotalPrices = totalPrices, RejectedCount = rejectedCount });
    }



    //[HttpPost]
    //public async Task<IActionResult> StartScrapingWithCaptchaHandling()
    //{
    //    ResetCancellationToken();
    //    var cancellationToken = _cancellationTokenSource.Token;

    //    var settings = await _context.Settings.FirstOrDefaultAsync();
    //    if (settings == null)
    //    {
    //        Console.WriteLine("Settings not found.");
    //        return NotFound("Settings not found.");
    //    }

    //    int captchaSpeed = settings.CaptchaSpeed;

    //    var coOfrs = await _context.CoOfrs.Where(co => !co.IsScraped).ToListAsync();
    //    var urls = coOfrs.Select(co => co.OfferUrl).ToList();
    //    var urlQueue = new Queue<string>(urls);

    //    if (!urls.Any())
    //    {
    //        Console.WriteLine("No URLs found to scrape.");
    //        return NotFound("No URLs found to scrape.");
    //    }

    //    var tasks = new List<Task>();
    //    int totalPrices = 0;
    //    int scrapedCount = 0;
    //    int rejectedCount = 0;
    //    var stopwatch = new Stopwatch();
    //    stopwatch.Start();

    //    using (var semaphore = new SemaphoreSlim(captchaSpeed))
    //    {
    //        for (int i = 0; i < captchaSpeed; i++)
    //        {
    //            tasks.Add(Task.Run(async () =>
    //            {
    //                await semaphore.WaitAsync(cancellationToken);

    //                try
    //                {
    //                    var httpClient = _httpClientFactory.CreateClient();
    //                    var captchaScraper = new CaptchaScraper(httpClient);

    //                    await captchaScraper.InitializeBrowserAsync();

    //                    while (urlQueue.Count > 0)
    //                    {
    //                        string url;
    //                        lock (urlQueue)
    //                        {
    //                            if (urlQueue.Count == 0)
    //                            {
    //                                break;
    //                            }
    //                            url = urlQueue.Dequeue();
    //                        }

    //                        try
    //                        {
    //                            using var scope = _serviceProvider.CreateScope();
    //                            var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

    //                            var (prices, log, rejected) = await captchaScraper.HandleCaptchaAndScrapePricesAsync(url);
    //                            Console.WriteLine(log);

    //                            var coOfr = coOfrs.First(co => co.OfferUrl == url);
    //                            var priceHistories = prices.Select(priceData => new CoOfrPriceHistoryClass
    //                            {
    //                                CoOfrClassId = coOfr.Id,
    //                                StoreName = priceData.storeName,
    //                                Price = priceData.price,
    //                                ShippingCostNum = priceData.shippingCostNum,
    //                                AvailabilityNum = priceData.availabilityNum,
    //                                IsBidding = priceData.isBidding,
    //                                Position = priceData.position
    //                            }).ToList();

    //                            await scopedContext.CoOfrPriceHistories.AddRangeAsync(priceHistories);
    //                            await scopedContext.SaveChangesAsync();

    //                            coOfr.IsScraped = true;
    //                            coOfr.ScrapingMethod = "HandleCaptcha";
    //                            coOfr.PricesCount = priceHistories.Count;
    //                            coOfr.IsRejected = (priceHistories.Count == 0);
    //                            scopedContext.CoOfrs.Update(coOfr);
    //                            await scopedContext.SaveChangesAsync();

    //                            await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfr.OfferUrl, coOfr.IsScraped, coOfr.IsRejected, coOfr.ScrapingMethod, coOfr.PricesCount);

    //                            Interlocked.Add(ref totalPrices, priceHistories.Count);
    //                            Interlocked.Increment(ref scrapedCount);
    //                            if (coOfr.IsRejected)
    //                            {
    //                                Interlocked.Increment(ref rejectedCount);
    //                            }
    //                            await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", scrapedCount, coOfrs.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);

    //                            if (cancellationToken.IsCancellationRequested)
    //                            {
    //                                Console.WriteLine("Scraping canceled.");
    //                                await captchaScraper.CloseBrowserAsync();
    //                                return;
    //                            }
    //                        }
    //                        catch (Exception ex)
    //                        {
    //                            var log = $"Error scraping URL: {url}. Exception: {ex.Message}";
    //                            Console.WriteLine(log);
    //                        }
    //                    }

    //                    await captchaScraper.CloseBrowserAsync();
    //                }
    //                finally
    //                {
    //                    semaphore.Release();
    //                }
    //            }, cancellationToken));
    //        }

    //        try
    //        {
    //            await Task.WhenAll(tasks);
    //        }
    //        catch (OperationCanceledException)
    //        {
    //            Console.WriteLine("Scraping was canceled.");
    //        }
    //    }

    //    stopwatch.Stop();

    //    return Ok(new { Message = "Scraping completed.", TotalPrices = totalPrices, RejectedCount = rejectedCount });
    //}


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
        var rejectedProducts = new List<ProductClass>(); // Lista do przechowywania odrzuconych produktów

        // Użycie równoległego przetwarzania
        await Task.Run(() => Parallel.ForEach(products, product =>
        {
            var coOfrId = coOfrClasses.FirstOrDefault(co => co.ProductIds.Contains(product.ProductId))?.Id;
            if (coOfrId != null)
            {
                var coOfrPriceHistory = coOfrPriceHistories.Where(ph => ph.CoOfrClassId == coOfrId).ToList();

                bool hasStorePrice = false;

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

                    lock (priceHistories)
                    {
                        priceHistories.Add(priceHistory);
                    }

                    // Sprawdzamy, czy cena jest przypisana do naszego sklepu
                    if (string.Equals(coOfrPrice.StoreName, store.StoreName, StringComparison.OrdinalIgnoreCase))
                    {
                        hasStorePrice = true;
                    }
                }

             
                if (!hasStorePrice)
                {
                    lock (_context)
                    {
                        product.IsRejected = true;
                        _context.SaveChanges();
                    }

                    lock (rejectedProducts)
                    {
                        rejectedProducts.Add(product); 
                    }
                }
            }
        }));

        scrapHistory.PriceCount = priceHistories.Count;
        _context.ScrapHistories.Add(scrapHistory);
        _context.PriceHistories.AddRange(priceHistories);

        await _context.SaveChangesAsync();

        // Logowanie odrzuconych produktów
        foreach (var rejectedProduct in rejectedProducts)
        {
            var relatedPrices = coOfrPriceHistories
                .Where(ph => ph.CoOfrClassId == coOfrClasses.FirstOrDefault(co => co.ProductIds.Contains(rejectedProduct.ProductId))?.Id)
                .Select(ph => new { ph.StoreName, ph.Price })
                .ToList();

            Console.WriteLine($"Produkt odrzucony: {rejectedProduct.ProductName}");
            Console.WriteLine($"Sklep użyty do porównania: {store.StoreName}");
            Console.WriteLine("Ceny z innych sklepów:");

            foreach (var price in relatedPrices)
            {
                Console.WriteLine($"- Sklep: {price.StoreName}, Cena: {price.Price}");
            }
        }

        return RedirectToAction("GetStoreProductsWithCoOfrIds", new { storeId });
    }


    [HttpPost]
    public async Task<IActionResult> ClearRejectedAndScrapedProducts()
    {
        var productsToReset = await _context.CoOfrs
            .Where(co => co.IsScraped && co.IsRejected)
            .ToListAsync();

        if (productsToReset.Any())
        {
            foreach (var product in productsToReset)
            {
                product.IsScraped = false;
                product.IsRejected = false;
            }

            _context.CoOfrs.UpdateRange(productsToReset);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction("GetUniqueScrapingUrls");
    }
}