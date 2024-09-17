using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Xml.Linq;

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
            // Pobieramy tylko produkty, które nie mają przypisanego GoogleUrl i nie są oznaczone jako znalezione
            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.OnGoogle && !string.IsNullOrEmpty(p.Url) && (string.IsNullOrEmpty(p.GoogleUrl)) && (p.FoundOnGoogle == null || p.FoundOnGoogle == true))
                .ToListAsync();

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
                    // Jeżeli produkt ma już przypisany GoogleUrl, ustawiamy FoundOnGoogle na true i pomijamy go
                    if (!string.IsNullOrEmpty(product.GoogleUrl))
                    {
                        product.FoundOnGoogle = true;
                        Console.WriteLine($"Product already has GoogleUrl, skipping: {product.ProductName}");
                        continue; // Pomijamy dalsze przetwarzanie tego produktu
                    }

                    await scraper.InitializeAndSearchAsync(product.ProductNameInStoreForGoogle);

                    var searchUrls = products.Select(p => p.Url).ToList();
                    await scraper.SearchAndNavigateToStoreAsync(store.StoreName, searchUrls);

                    var matchedUrls = await scraper.SearchForMatchingProductUrlsAsync(searchUrls);

                    bool productFound = false;

                    foreach (var (storeUrl, googleProductUrl) in matchedUrls)
                    {
                        var matchedProduct = products.FirstOrDefault(p => p.Url == storeUrl);
                        if (matchedProduct != null && string.IsNullOrEmpty(matchedProduct.GoogleUrl))
                        {
                            matchedProduct.GoogleUrl = googleProductUrl;
                            matchedProduct.FoundOnGoogle = true;
                            Console.WriteLine($"Updated product: {matchedProduct.ProductName}, GoogleUrl: {matchedProduct.GoogleUrl}");

                            _context.Products.Update(matchedProduct);
                            await _context.SaveChangesAsync();
                            productFound = true;
                        }
                    }

                    // Jeżeli produkt nie został znaleziony, ale nie ma GoogleUrl, ustawiamy status na false
                    if (!productFound && string.IsNullOrEmpty(product.GoogleUrl))
                    {
                        product.FoundOnGoogle = false;
                        Console.WriteLine($"Product not found on Google: {product.ProductName}");
                        _context.Products.Update(product);
                        await _context.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing product: {ex.Message}");
                }
            }

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


    [HttpPost]
    public async Task<IActionResult> ValidateGoogleUrls(int storeId)
    {
        // Pobranie produktów z prawidłowym statusem FoundOnGoogle i GoogleUrl
        var products = await _context.Products
            .Where(p => p.StoreId == storeId && p.FoundOnGoogle == true && !string.IsNullOrEmpty(p.GoogleUrl))
            .ToListAsync();

        foreach (var product in products)
        {
            // Sprawdzenie, czy GoogleUrl spełnia wymagany schemat (czy zawiera "shopping/product")
            if (!product.GoogleUrl.Contains("shopping/product"))
            {
                // Jeżeli URL jest nieprawidłowy, aktualizujemy produkt
                product.FoundOnGoogle = false;
                product.GoogleUrl = null;

                _context.Products.Update(product);
            }
        }

        // Zapisanie zmian w bazie danych
        await _context.SaveChangesAsync();

        return RedirectToAction("ProductList", new { storeId });
    }


    [HttpPost]
    public async Task<IActionResult> UpdateProductNamesFromUrl(string xmlUrl)
    {
        if (string.IsNullOrEmpty(xmlUrl))
        {
            return BadRequest("URL pliku XML jest wymagany.");
        }

        try
        {
            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync(xmlUrl);
                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest("Nie udało się pobrać pliku XML.");
                }

                var xmlContent = await response.Content.ReadAsStringAsync();
                var doc = XDocument.Parse(xmlContent);

                var productElements = doc.Descendants("item");

                foreach (var item in productElements)
                {
                    var urlElement = item.Element("link")?.Value.Trim();
                    var nameElement = item.Element("title")?.Value.Trim();

                    if (!string.IsNullOrEmpty(urlElement) && !string.IsNullOrEmpty(nameElement))
                    {
                        var product = await _context.Products
                            .FirstOrDefaultAsync(p => p.Url == urlElement);

                        if (product != null)
                        {
                            product.ProductNameInStoreForGoogle = nameElement;
                            _context.Products.Update(product);
                        }
                    }
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction("ProductList");
        }
        catch (Exception ex)
        {
            return BadRequest($"Wystąpił błąd podczas przetwarzania pliku XML: {ex.Message}");
        }
    }

    [HttpPost]
    public async Task<IActionResult> SetOnGoogleForAll()
    {
        var products = await _context.Products
            .Where(p => !string.IsNullOrEmpty(p.ProductNameInStoreForGoogle))
            .ToListAsync();

        foreach (var product in products)
        {
            product.OnGoogle = true;
            _context.Products.Update(product);
        }

        await _context.SaveChangesAsync();

        return RedirectToAction("ProductList", new { storeId = products.FirstOrDefault()?.StoreId });
    }

    [HttpPost]
    public async Task<IActionResult> RemoveWordFromProductNames(string wordToRemove)
    {
        if (string.IsNullOrEmpty(wordToRemove))
        {
            return BadRequest("Słowo do usunięcia jest wymagane.");
        }

        var products = await _context.Products
            .Where(p => !string.IsNullOrEmpty(p.ProductNameInStoreForGoogle))
            .ToListAsync();

        foreach (var product in products)
        {
            if (product.ProductNameInStoreForGoogle.Contains(wordToRemove, StringComparison.OrdinalIgnoreCase))
            {
                product.ProductNameInStoreForGoogle = product.ProductNameInStoreForGoogle.Replace(wordToRemove, "", StringComparison.OrdinalIgnoreCase).Trim();
                _context.Products.Update(product);
            }
        }

        await _context.SaveChangesAsync();

        return RedirectToAction("ProductList", new { storeId = products.FirstOrDefault()?.StoreId });
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

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") 
        {
            
            var jsonProducts = products.Select(p => new
            {
                p.ProductId,
                p.ProductNameInStoreForGoogle,
                p.Url,
                p.FoundOnGoogle,
                p.GoogleUrl
            }).ToList();

            return Json(jsonProducts);
        }

 
        ViewBag.StoreName = store.StoreName;
        ViewBag.StoreId = storeId;
        return View("~/Views/ManagerPanel/GoogleScraper/GoogleProducts.cshtml", products);
    }

    [HttpPost]
    public async Task<IActionResult> UpdatePendingProducts(int storeId)
    {
       
        var pendingProducts = await _context.Products
            .Where(p => p.StoreId == storeId && !string.IsNullOrEmpty(p.GoogleUrl) && (p.FoundOnGoogle == null || p.FoundOnGoogle == false))
            .ToListAsync();

        foreach (var product in pendingProducts)
        {
          
            product.FoundOnGoogle = true;
            _context.Products.Update(product);
        }

     
        await _context.SaveChangesAsync();

        return Ok();
    }


    [HttpPost]
    public async Task<IActionResult> ResetNotFoundProducts(int storeId)
    {
      
        var notFoundProducts = await _context.Products
            .Where(p => p.StoreId == storeId && p.FoundOnGoogle == false)
            .ToListAsync();

        foreach (var product in notFoundProducts)
        {
  
            product.FoundOnGoogle = null;
            _context.Products.Update(product);
        }

 
        await _context.SaveChangesAsync();

        return Ok();
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