using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;


[Authorize(Roles = "Admin")]
public class GoogleScraperController : Controller

{
    private readonly PriceSafariContext _context;

    public GoogleScraperController(PriceSafariContext context)
    {
        _context = context;
    }
    [HttpPost]
    public async Task<IActionResult> StartScrapingForProducts(int storeId)
    {
        var store = await _context.Stores.FindAsync(storeId);
        if (store == null)
        {
            return NotFound();
        }

        var scraper = new GoogleScraper();
        await scraper.InitializeBrowserAsync();

        bool moreProductsToProcess = true;

        while (moreProductsToProcess)
        {
            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.OnGoogle && !string.IsNullOrEmpty(p.Url) && string.IsNullOrEmpty(p.GoogleUrl))
                .ToListAsync();

            // Wyświetlenie listy URL, które będą przetwarzane
            Console.WriteLine("Product URLs to be processed:");
            foreach (var product in products)
            {
                Console.WriteLine($"Product: {product.ProductName}, URL: {product.Url}");
            }

            if (!products.Any())
            {
                moreProductsToProcess = false;
                Console.WriteLine("No more products to process.");
                break;
            }

            foreach (var product in products)
            {
                try
                {
                    await scraper.InitializeAndSearchAsync(product.ProductNameInStoreForGoogle);

                    // Najpierw nawiguj do sklepu
                    var searchUrls = new List<string> { product.Url };
                    await scraper.SearchAndNavigateToStoreAsync(store.StoreName, searchUrls);

                    // Wyniki dopasowania URL i aktualizacja produktu
                    var matchedUrls = await scraper.SearchForMatchingProductUrlsAsync(searchUrls);
                    foreach (var (storeUrl, googleProductUrl) in matchedUrls)
                    {
                        var matchedProduct = products.FirstOrDefault(p => p.Url == storeUrl);
                        if (matchedProduct != null && string.IsNullOrEmpty(matchedProduct.GoogleUrl))  // Aktualizacja tylko jeśli GoogleUrl jest puste
                        {
                            matchedProduct.GoogleUrl = googleProductUrl;
                            Console.WriteLine($"Updated product: {matchedProduct.ProductName}, GoogleUrl: {matchedProduct.GoogleUrl}");

                            _context.Products.Update(matchedProduct);
                            await _context.SaveChangesAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing product: {ex.Message}");
                }
            }

            // Odśwież listę produktów, aby uwzględnić tylko te, które jeszcze nie mają GoogleUrl
            products = await _context.Products
                .Where(p => p.StoreId == storeId && p.OnGoogle && !string.IsNullOrEmpty(p.Url) && string.IsNullOrEmpty(p.GoogleUrl))
                .ToListAsync();

            if (!products.Any())
            {
                moreProductsToProcess = false;
                Console.WriteLine("No more products left to process.");
            }
        }

        await scraper.CloseBrowserAsync();
        return Content("Scraping completed.");
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