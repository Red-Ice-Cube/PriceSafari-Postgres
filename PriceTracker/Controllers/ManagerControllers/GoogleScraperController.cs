using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Hubs;
using System.Net.Http;
using System.Threading.Tasks;

public class GoogleScraperController : Controller

        

{


    private readonly PriceTrackerContext _context;


    public GoogleScraperController(PriceTrackerContext context)
    {
        _context = context;
       
    }


    [HttpGet]
    public IActionResult Index()
    {
        return View("~/Views/ManagerPanel/GoogleScraper/Index.cshtml");
    }

    [HttpPost]
    public async Task<IActionResult> StartScraping(string title, string storeName, string targetUrl)
    {
        var scraper = new GoogleScraper();
        await scraper.InitializeBrowserAsync();
        await scraper.InitializeAndSearchAsync(title);
        await scraper.SearchStoreNameAsync(storeName);
        await scraper.SearchUrlAndReviewsWithFallbackAsync();

        scraper.MatchReviews();

        await scraper.OpenAndScrapeMatchedOffersAsync(targetUrl, storeName);  

        await scraper.CloseBrowserAsync();

        return Content("Scraping completed. Check the console for output.");
    }

    [HttpPost]
    public async Task<IActionResult> StartScrapingForProducts(int storeId)
    {
        var store = await _context.Stores.FindAsync(storeId);
        if (store == null)
        {
            return NotFound();
        }
        var products = await _context.Products
              .Where(p => p.StoreId == storeId && p.OnGoogle && (string.IsNullOrEmpty(p.GoogleUrl)))
              .ToListAsync();

        foreach (var product in products)
        {
            var scraper = new GoogleScraper();
            await scraper.InitializeBrowserAsync();
            await scraper.InitializeAndSearchAsync(product.ProductNameInStoreForGoogle);
            await scraper.SearchStoreNameAsync(store.StoreName);
            await scraper.SearchUrlAndReviewsWithFallbackAsync();

            scraper.MatchReviews();

            await scraper.OpenAndScrapeMatchedOffersAsync(product.Url, store.StoreName);  // Dodanie storeName

            // Pobierz znaleziony URL i zaktualizuj produkt
            product.GoogleUrl = scraper.GetScrapedGoogleUrl();

            await scraper.CloseBrowserAsync();
        }

        await _context.SaveChangesAsync();

        return Content("Scraping completed for all products. Check the database for updated Google URLs.");
    }




    [HttpGet]
    public async Task<IActionResult> ProductList(int storeId)
    {
        var store = await _context.Stores.FindAsync(storeId);
        if (store == null)
        {
            return NotFound();
        }

        var products = await _context.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

        ViewBag.StoreName = store.StoreName;
        ViewBag.StoreId = storeId;
        return View("~/Views/ManagerPanel/GoogleScraper/ProductList.cshtml", products);
    }





    [HttpGet]
    public async Task<IActionResult> GoogleProducts(int storeId)
    {
        var store = await _context.Stores.FindAsync(storeId);
        if (store == null)
        {
            return NotFound();
        }

        var products = await _context.Products
            .Where(p => p.StoreId == storeId && p.OnGoogle)
            .ToListAsync();

        ViewBag.StoreName = store.StoreName;
        ViewBag.StoreName = store.StoreId;
        ViewBag.StoreId = storeId;
        return View("~/Views/ManagerPanel/GoogleScraper/GoogleProducts.cshtml", products);
    }






    [HttpPost]
    public async Task<IActionResult> ToggleGoogleStatus(int productId)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
        {
            return NotFound();
        }

        product.OnGoogle = !product.OnGoogle;
        await _context.SaveChangesAsync();

        return RedirectToAction("ProductList", new { storeId = product.StoreId });
    }


    [HttpPost]
    public async Task<IActionResult> UpdateProductNameInStoreForGoogle(int productId, string productNameInStoreForGoogle)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
        {
            return NotFound();
        }

        product.ProductNameInStoreForGoogle = productNameInStoreForGoogle;
        await _context.SaveChangesAsync();

        return RedirectToAction("ProductList", new { storeId = product.StoreId });
    }
}
