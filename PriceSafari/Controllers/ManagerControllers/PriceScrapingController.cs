using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Scrapers;
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
        var products = await _context.Products
            .Include(p => p.Store)
            .Where(p => p.IsScrapable && p.Store.RemainingScrapes > 0)
            .ToListAsync();

        var coOfrs = new List<CoOfrClass>();

        // Podział na dwa zestawy:
        // 1. Produkty, które mają OfferUrl
        var productsWithOffer = products.Where(p => !string.IsNullOrEmpty(p.OfferUrl)).ToList();
        // 2. Produkty, które nie mają OfferUrl
        var productsWithoutOffer = products.Where(p => string.IsNullOrEmpty(p.OfferUrl)).ToList();

        // Grupowanie produktów z OfferUrl po OfferUrl
        var groupsByOfferUrl = productsWithOffer
            .GroupBy(p => p.OfferUrl ?? "")
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kvp in groupsByOfferUrl)
        {
            var offerUrl = kvp.Key;
            var productList = kvp.Value;

            // Znajdź pierwszy niepusty GoogleUrl
            string? chosenGoogleUrl = productList
                .Select(p => p.GoogleUrl)
                .Where(gu => !string.IsNullOrEmpty(gu))
                .FirstOrDefault();

            var coOfr = CreateCoOfrClass(productList, offerUrl, chosenGoogleUrl);
            coOfrs.Add(coOfr);
        }

        // Grupowanie produktów bez OfferUrl po GoogleUrl
        var groupsByGoogleUrlForNoOffer = productsWithoutOffer
            .GroupBy(p => p.GoogleUrl ?? "")
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kvp in groupsByGoogleUrlForNoOffer)
        {
            var googleUrl = kvp.Key;
            var productList = kvp.Value;

            // Tu OfferUrl jest puste (null), a GoogleUrl może być puste lub konkretne
            var coOfr = CreateCoOfrClass(productList, null, string.IsNullOrEmpty(googleUrl) ? null : googleUrl);
            coOfrs.Add(coOfr);
        }

        _context.CoOfrs.RemoveRange(_context.CoOfrs);
        _context.CoOfrs.AddRange(coOfrs);
        await _context.SaveChangesAsync();

        return RedirectToAction("GetUniqueScrapingUrls");
    }

    private CoOfrClass CreateCoOfrClass(List<ProductClass> productList, string? offerUrl, string? googleUrl)
    {
        if (string.IsNullOrEmpty(offerUrl)) offerUrl = null;
        if (string.IsNullOrEmpty(googleUrl)) googleUrl = null;

        var coOfr = new CoOfrClass
        {
            OfferUrl = offerUrl,
            GoogleOfferUrl = googleUrl,
            ProductIds = new List<int>(),
            ProductIdsGoogle = new List<int>(),
            StoreNames = new List<string>(),
            StoreProfiles = new List<string>(),
            IsScraped = false,
            GoogleIsScraped = false,
            IsRejected = false,
            GoogleIsRejected = false,
        };

        foreach (var product in productList)
        {
            // Każdy produkt trafia do ProductIds
            coOfr.ProductIds.Add(product.ProductId);

            // Jeśli mamy wybrany GoogleUrl i produkt go posiada – trafia również do ProductIdsGoogle
            if (!string.IsNullOrEmpty(googleUrl) && product.GoogleUrl == googleUrl)
            {
                coOfr.ProductIdsGoogle.Add(product.ProductId);
            }

            coOfr.StoreNames.Add(product.Store.StoreName);
            coOfr.StoreProfiles.Add(product.Store.StoreProfile);
        }

        return coOfr;
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
    //    bool getCeneoName = settings.GetCeneoName; // Retrieve the setting

    //    // Modify the query to include only CoOfrs with a non-empty OfferUrl
    //    var coOfrs = await _context.CoOfrs
    //        .Where(co => !co.IsScraped && !string.IsNullOrEmpty(co.OfferUrl))
    //        .ToListAsync();

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

    //                    // Generate a unique userDataDir for this instance
    //                    string userDataDir = Path.Combine("user_data", $"instance_{Guid.NewGuid()}");

    //                    // Initialize browser in headless mode
    //                    await captchaScraper.InitializeBrowserAsync(settings, headless: true, userDataDir: userDataDir);

    //                    while (true)
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
    //                            // First declaration of coOfr
    //                            var coOfr = coOfrs.First(co => co.OfferUrl == url);

    //                            using var scope = _serviceProvider.CreateScope();
    //                            var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

    //                            // Pass StoreNames and StoreProfiles to the scraper
    //                            var (prices, log, rejected) = await captchaScraper.HandleCaptchaAndScrapePricesAsync(
    //                                url, getCeneoName, coOfr.StoreNames, coOfr.StoreProfiles, userDataDir);
    //                            Console.WriteLine(log);

    //                            // Log captcha detection
    //                            if (log.Contains("Captcha detected"))
    //                            {
    //                                Console.WriteLine($"Captcha encountered for URL: {url}");
    //                            }
    //                            else
    //                            {
    //                                Console.WriteLine($"Captcha not encountered or solved for URL: {url}");
    //                            }

    //                            if (prices.Count > 0)
    //                            {
    //                                // Existing code to process prices
    //                                var priceHistories = prices.Select(priceData => new CoOfrPriceHistoryClass
    //                                {
    //                                    CoOfrClassId = coOfr.Id,
    //                                    StoreName = priceData.storeName,
    //                                    Price = priceData.price,
    //                                    ShippingCostNum = priceData.shippingCostNum,
    //                                    AvailabilityNum = priceData.availabilityNum,
    //                                    IsBidding = priceData.isBidding,
    //                                    Position = priceData.position,
    //                                    ExportedName = priceData.ceneoName
    //                                }).ToList();

    //                                await scopedContext.CoOfrPriceHistories.AddRangeAsync(priceHistories);
    //                                coOfr.IsScraped = true;
    //                                coOfr.PricesCount = priceHistories.Count;
    //                                coOfr.IsRejected = false;
    //                                scopedContext.CoOfrs.Update(coOfr);
    //                                await scopedContext.SaveChangesAsync();

    //                                // Update progress
    //                                Interlocked.Add(ref totalPrices, priceHistories.Count);
    //                            }
    //                            else
    //                            {
    //                                // Handle the case where no prices were found
    //                                coOfr.IsScraped = true;
    //                                coOfr.IsRejected = true;
    //                                coOfr.PricesCount = 0;
    //                                scopedContext.CoOfrs.Update(coOfr);
    //                                await scopedContext.SaveChangesAsync();

    //                                Console.WriteLine($"No prices found for URL: {url}. Marked as rejected.");

    //                                // Update progress
    //                                Interlocked.Increment(ref rejectedCount);
    //                            }

    //                            // Common to both cases
    //                            Interlocked.Increment(ref scrapedCount);
    //                            await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfr.OfferUrl, coOfr.IsScraped, coOfr.IsRejected, coOfr.PricesCount);
    //                            await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", scrapedCount, coOfrs.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);

    //                            if (cancellationToken.IsCancellationRequested)
    //                            {
    //                                Console.WriteLine("Scraping canceled.");
    //                                break;
    //                            }
    //                        }
    //                        catch (Exception ex)
    //                        {
    //                            var log = $"Error scraping URL: {url}. Exception: {ex.Message}";
    //                            Console.WriteLine(log);
    //                        }
    //                    }

    //                    // Close the browser and delete the userDataDir
    //                    await captchaScraper.CloseBrowserAsync();

    //                    // Clean up userDataDir
    //                    try
    //                    {
    //                        if (Directory.Exists(userDataDir))
    //                        {
    //                            Directory.Delete(userDataDir, true);
    //                        }
    //                    }
    //                    catch (Exception ex)
    //                    {
    //                        Console.WriteLine($"Error deleting userDataDir {userDataDir}: {ex.Message}");
    //                    }
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
        bool getCeneoName = settings.GetCeneoName; // Retrieve the setting

        // Modify the query to include only CoOfrs with a non-empty OfferUrl
        var coOfrs = await _context.CoOfrs
            .Where(co => !co.IsScraped && !string.IsNullOrEmpty(co.OfferUrl))
            .ToListAsync();

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

                        await captchaScraper.InitializeBrowserAsync(settings);

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
                                // First declaration of coOfr
                                var coOfr = coOfrs.First(co => co.OfferUrl == url);

                                using var scope = _serviceProvider.CreateScope();
                                var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                // Pass StoreNames and StoreProfiles to the scraper
                                var (prices, log, rejected) = await captchaScraper.HandleCaptchaAndScrapePricesAsync(
                                    url, getCeneoName, coOfr.StoreNames, coOfr.StoreProfiles);
                                Console.WriteLine(log);
                                if (prices.Count > 0)
                                {
                                    // Existing code to process prices
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
                                    coOfr.PricesCount = priceHistories.Count;
                                    coOfr.IsRejected = false;
                                    scopedContext.CoOfrs.Update(coOfr);
                                    await scopedContext.SaveChangesAsync();

                                    // Update progress
                                    Interlocked.Add(ref totalPrices, priceHistories.Count);
                                }
                                else
                                {
                                    // Handle the case where no prices were found
                                    coOfr.IsScraped = true;
                                    coOfr.IsRejected = true;
                                    coOfr.PricesCount = 0;
                                    scopedContext.CoOfrs.Update(coOfr);
                                    await scopedContext.SaveChangesAsync();

                                    Console.WriteLine($"No prices found for URL: {url}. Marked as rejected.");

                                    // Update progress
                                    Interlocked.Increment(ref rejectedCount);
                                }

                                // Common to both cases
                                Interlocked.Increment(ref scrapedCount);
                                await _hubContext.Clients.All.SendAsync("ReceiveScrapingUpdate", coOfr.OfferUrl, coOfr.IsScraped, coOfr.IsRejected, coOfr.PricesCount);
                                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", scrapedCount, coOfrs.Count, stopwatch.Elapsed.TotalSeconds, rejectedCount);


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