using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Scrapers;
using PriceSafari.Services.ControlXY;
using PriceSafari.Services.ScheduleService;
using PuppeteerSharp;
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
    private readonly ControlXYService _controlXYService;
    private readonly CeneoScraperService _ceneoScraperService;

    public PriceScrapingController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext, IServiceProvider serviceProvider, HttpClient httpClient, IHttpClientFactory httpClientFactory, StoreProcessingService storeProcessingService, ControlXYService controlXYService, CeneoScraperService ceneoScraperService)
    {
        _context = context;
        _hubContext = hubContext;
        _serviceProvider = serviceProvider;
        _httpClient = httpClient;
        _httpClientFactory = httpClientFactory;
        _storeProcessingService = storeProcessingService;
        _controlXYService = controlXYService;
        _ceneoScraperService = ceneoScraperService;
    }

    [HttpPost]
    public async Task<IActionResult> GroupAndSaveUniqueUrls()
    {
        var products = await _context.Products
            .Include(p => p.Store)
            .Where(p => p.IsScrapable && p.Store.RemainingDays > 0)
            .ToListAsync();

        var coOfrs = new List<CoOfrClass>();

        var productsWithOffer = products.Where(p => !string.IsNullOrEmpty(p.OfferUrl)).ToList();
        var productsWithoutOffer = products.Where(p => string.IsNullOrEmpty(p.OfferUrl)).ToList();

        var groupsByOfferUrl = productsWithOffer
            .GroupBy(p => p.OfferUrl ?? "")
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kvp in groupsByOfferUrl)
        {
            var offerUrl = kvp.Key;
            var productList = kvp.Value;

            string? chosenGoogleUrl = productList
                .Select(p => p.GoogleUrl)
                .Where(gu => !string.IsNullOrEmpty(gu))
                .FirstOrDefault();

            var coOfr = CreateCoOfrClass(productList, offerUrl, chosenGoogleUrl);
            coOfrs.Add(coOfr);
        }

        var groupsByGoogleUrlForNoOffer = productsWithoutOffer
            .GroupBy(p => p.GoogleUrl ?? "")
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kvp in groupsByGoogleUrlForNoOffer)
        {
            var googleUrl = kvp.Key;
            var productList = kvp.Value;

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

            coOfr.ProductIds.Add(product.ProductId);

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
    public async Task<IActionResult> ClearCoOfrPriceHistoriesCeneo()
    {

        var productsToReset = await _context.CoOfrs.ToListAsync();

        if (productsToReset.Any())
        {

            var productIds = productsToReset.Select(co => co.Id).ToList();

            var allHistories = await _context.CoOfrPriceHistories.ToListAsync();

            var historiesToRemove = allHistories
                .Where(ph => productIds.Contains(ph.CoOfrClassId) && ph.ExportedName != null)
                .ToList();

            if (historiesToRemove.Any())
            {
                _context.CoOfrPriceHistories.RemoveRange(historiesToRemove);
            }

            foreach (var product in productsToReset)
            {
                product.IsScraped = false;
                product.IsRejected = false;
                product.PricesCount = 0;
            }

            _context.CoOfrs.UpdateRange(productsToReset);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction("GetUniqueScrapingUrls");
    }

    [HttpPost]
    public async Task<IActionResult> ClearCoOfrPriceHistoriesGoogle()
    {

        var productsToReset = await _context.CoOfrs
            .Where(co => co.GoogleIsScraped)
            .ToListAsync();

        if (productsToReset.Any())
        {

            var productIds = productsToReset.Select(co => co.Id).ToList();

            var allHistories = await _context.CoOfrPriceHistories.ToListAsync();

            var historiesToRemove = allHistories
                .Where(ph => productIds.Contains(ph.CoOfrClassId) && ph.GoogleStoreName != null)
                .ToList();

            if (historiesToRemove.Any())
            {
                _context.CoOfrPriceHistories.RemoveRange(historiesToRemove);
            }

            foreach (var product in productsToReset)
            {
                product.GoogleIsScraped = false;
                product.GoogleIsRejected = false;
                product.GooglePricesCount = 0;
            }

            _context.CoOfrs.UpdateRange(productsToReset);
            await _context.SaveChangesAsync();
        }

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
    public async Task<IActionResult> StartScrapingWithCaptchaHandling()
    {
        ResetCancellationToken();
        var cancellationToken = _cancellationTokenSource.Token;

        try
        {

            var result = await _ceneoScraperService.StartScrapingWithCaptchaHandlingAsync(cancellationToken);

            switch (result.Result)
            {
                case CeneoScraperService.CeneoScrapingResult.Success:
                    return Ok(new
                    {
                        Message = result.Message,
                        ScrapedCount = result.ScrapedCount,
                        RejectedCount = result.RejectedCount
                    });

                case CeneoScraperService.CeneoScrapingResult.NoUrlsFound:
                    return NotFound(new { Message = result.Message });

                case CeneoScraperService.CeneoScrapingResult.SettingsNotFound:
                    return NotFound(new { Message = result.Message });

                case CeneoScraperService.CeneoScrapingResult.Error:
                    return StatusCode(500, new
                    {
                        Message = result.Message,
                        ScrapedCount = result.ScrapedCount,
                        RejectedCount = result.RejectedCount
                    });

                default:
                    return BadRequest("Unknown scraping result.");
            }

        }
        catch (OperationCanceledException)
        {

            return Ok(new { Message = "Scraping was canceled by the user." });
        }
        catch (Exception ex)
        {

            Console.WriteLine($"An unexpected error occurred in the controller: {ex.Message}");
            return StatusCode(500, new { Message = "An unexpected server error occurred." });
        }
    }

    public class CaptchaSessionData
    {
        public CookieParam[] Cookies { get; set; }

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
    public async Task<IActionResult> ClearRejectedAndScrapedProductsCeneo()
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

    [HttpPost]
    public async Task<IActionResult> ClearRejectedAndScrapedProductsGoogle()
    {

        var productsToReset = await _context.CoOfrs
            .Where(co => co.GoogleIsScraped && co.GoogleIsRejected)
            .ToListAsync();

        if (productsToReset.Any())
        {
            foreach (var product in productsToReset)
            {
                product.GoogleIsScraped = false;
                product.GoogleIsRejected = false;
            }

            _context.CoOfrs.UpdateRange(productsToReset);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction("GetUniqueScrapingUrls");
    }

}