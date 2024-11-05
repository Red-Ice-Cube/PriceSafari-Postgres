using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Scrapers;
using PriceSafari.Services;
using System.Collections.Concurrent;
using System.Diagnostics;


[Authorize(Roles = "Admin")]
public class PriceScrapingController : Controller
{
    private readonly PriceSafariContext _context;
    private readonly IHubContext<ScrapingHub> _hubContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private static CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
    private readonly StoreProcessingService _storeProcessingService;

    public PriceScrapingController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext, IServiceProvider serviceProvider, HttpClient httpClient, IHttpClientFactory httpClientFactory, StoreProcessingService storeProcessingService)
    {
        _context = context;
        _hubContext = hubContext;
        _serviceProvider = serviceProvider;
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _storeProcessingService = storeProcessingService;
    }


    [HttpPost]
    public async Task<IActionResult> GroupAndSaveUniqueUrls()
    {
        var uniqueUrls = await _context.Products
            .Include(p => p.Store)
            .Where(p => p.IsScrapable && p.Store.RemainingScrapes > 0)
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

    //    int captchaSpeed = settings.Semophore;
    //    bool getCeneoName = settings.GetCeneoName; // Pobieramy wartość ustawienia

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

    //                    await captchaScraper.InitializeBrowserAsync(settings);

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

    //                            // Otrzymujemy dane z scraper'a, w tym ceny oraz opcjonalnie nazwę produktu z Ceneo
    //                            var (prices, log, rejected) = await captchaScraper.HandleCaptchaAndScrapePricesAsync(url, getCeneoName);
    //                            Console.WriteLine(log);

    //                            if (prices.Count > 0)
    //                            {
    //                                var coOfr = coOfrs.First(co => co.OfferUrl == url);

    //                                // Tworzenie listy wpisów do historii cenowej, w której uwzględniamy nazwę z Ceneo, jeśli dostępna
    //                                var priceHistories = prices.Select(priceData => new CoOfrPriceHistoryClass
    //                                {
    //                                    CoOfrClassId = coOfr.Id,
    //                                    StoreName = priceData.storeName,
    //                                    Price = priceData.price,
    //                                    ShippingCostNum = priceData.shippingCostNum,
    //                                    AvailabilityNum = priceData.availabilityNum,
    //                                    IsBidding = priceData.isBidding,
    //                                    Position = priceData.position,
    //                                    ExportedName = priceData.ceneoName // Nazwa produktu z Ceneo dla każdej oferty
    //                                }).ToList();

    //                                await scopedContext.CoOfrPriceHistories.AddRangeAsync(priceHistories);
    //                                coOfr.IsScraped = true;
    //                                coOfr.ScrapingMethod = "Pupeeteer";
    //                                coOfr.PricesCount = priceHistories.Count;
    //                                coOfr.IsRejected = (priceHistories.Count == 0);
    //                                scopedContext.CoOfrs.Update(coOfr);
    //                                await scopedContext.SaveChangesAsync();

    //                                await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfr.OfferUrl, coOfr.IsScraped, coOfr.IsRejected, coOfr.ScrapingMethod, coOfr.PricesCount);

    //                                Interlocked.Add(ref totalPrices, priceHistories.Count);
    //                                Interlocked.Increment(ref scrapedCount);
    //                                if (coOfr.IsRejected)
    //                                {
    //                                    Interlocked.Increment(ref rejectedCount);
    //                                }
    //                                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", scrapedCount, coOfrs.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
    //                            }

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

        int captchaSpeed = settings.Semophore;
        bool getCeneoName = settings.GetCeneoName;

        // Fetch unsraped offers and map them for quick access
        var coOfrs = await _context.CoOfrs.Where(co => !co.IsScraped).ToListAsync();
        var urlCoOfrDict = coOfrs.ToDictionary(co => co.OfferUrl);

        var urls = coOfrs.Select(co => co.OfferUrl).ToList();
        var urlQueue = new ConcurrentQueue<string>(urls);

        if (!urls.Any())
        {
            Console.WriteLine("No URLs found to scrape.");
            return NotFound("No URLs found to scrape.");
        }

        int totalPrices = 0;
        int scrapedCount = 0;
        int rejectedCount = 0;
        var stopwatch = new Stopwatch();
        stopwatch.Start();

        var tasks = new List<Task>();

        for (int i = 0; i < captchaSpeed; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var httpClient = _httpClientFactory.CreateClient();
                var captchaScraper = new CaptchaScraper(httpClient);
                await captchaScraper.InitializeBrowserAsync(settings);

                try
                {
                    while (urlQueue.TryDequeue(out var url))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            Console.WriteLine("Scraping canceled.");
                            break;
                        }

                        try
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                            // Get data from the scraper, including prices and optionally the Ceneo product name
                            var (prices, log, rejected) = await captchaScraper.HandleCaptchaAndScrapePricesAsync(url, getCeneoName);
                            Console.WriteLine(log);

                            var coOfr = urlCoOfrDict[url];

                            if (prices.Count > 0)
                            {
                                // Create a list of price history entries, including the Ceneo product name if available
                                var priceHistories = prices.Select(priceData => new CoOfrPriceHistoryClass
                                {
                                    CoOfrClassId = coOfr.Id,
                                    StoreName = priceData.storeName,
                                    Price = priceData.price,
                                    ShippingCostNum = priceData.shippingCostNum,
                                    AvailabilityNum = priceData.availabilityNum,
                                    IsBidding = priceData.isBidding,
                                    Position = priceData.position,
                                    ExportedName = priceData.ceneoName
                                }).ToList();

                                await scopedContext.CoOfrPriceHistories.AddRangeAsync(priceHistories);
                                coOfr.IsScraped = true;
                                coOfr.ScrapingMethod = "Puppeteer";
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
                            else
                            {
                                // Handle cases where no prices were scraped
                                coOfr.IsScraped = true;
                                coOfr.IsRejected = true;
                                coOfr.ScrapingMethod = "Puppeteer";
                                scopedContext.CoOfrs.Update(coOfr);
                                await scopedContext.SaveChangesAsync();

                                Interlocked.Increment(ref scrapedCount);
                                Interlocked.Increment(ref rejectedCount);
                                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", scrapedCount, coOfrs.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);
                            }
                        }
                        catch (Exception ex)
                        {
                            var log = $"Error scraping URL: {url}. Exception: {ex.Message}";
                            Console.WriteLine(log);
                        }
                    }
                }
                finally
                {
                    await captchaScraper.CloseBrowserAsync();
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
        await _storeProcessingService.ProcessStoreAsync(storeId);

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