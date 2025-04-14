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

        var googleMiG = store.GoogleMiG;
        if (string.IsNullOrEmpty(googleMiG))
        {
            return BadRequest("GoogleMiG is not set for this store.");
        }

        var scraper = new GoogleScraper();
        await scraper.InitializeBrowserAsync();

        // Pętla, która będzie działać, dopóki są produkty do przetworzenia
        while (true)
        {
            // Pobierz produkty, które są OnGoogle, mają niepusty URL i jeszcze nie były przetworzone (FoundOnGoogle == null)
            var productsToProcess = await _context.Products
                .Where(p => p.StoreId == storeId
                         && p.OnGoogle
                         && !string.IsNullOrEmpty(p.Url)
                         && p.FoundOnGoogle == null)
                .ToListAsync();

            if (!productsToProcess.Any())
            {
                Console.WriteLine("No products left to process.");
                break; // Kończymy pętlę, gdy nie ma już produktów do przetworzenia
            }

            // Wybieramy losowy produkt z aktualnej listy do przetwarzania
            Random random = new Random();
            var productToProcess = productsToProcess[random.Next(productsToProcess.Count)];

            // Pobieramy pełną listę produktów (wszystkich URL-i) dla porównania
            var allProducts = await _context.Products
                .Where(p => p.StoreId == storeId && p.OnGoogle && !string.IsNullOrEmpty(p.Url))
                .ToListAsync();

            // Tworzymy słownik dla szybkiego dostępu po URL
            var productDict = allProducts.ToDictionary(p => p.Url, p => p);
            var allProductUrls = productDict.Keys.ToList();

            try
            {
                // Wyszukujemy produkt na Google używając nazwy produktu
                await scraper.InitializeAndSearchAsync(productToProcess.ProductNameInStoreForGoogle, googleMiG);

                // Pobieramy listę dopasowanych URL-i
                var matchedUrls = await scraper.SearchForMatchingProductUrlsAsync(allProductUrls);

                // Aktualizujemy produkty, które zostały znalezione
                foreach (var (matchedStoreUrl, googleProductUrl) in matchedUrls)
                {
                    if (productDict.TryGetValue(matchedStoreUrl, out var matchedProduct))
                    {
                        // Bez względu na poprzedni status, ustawiamy FoundOnGoogle na true i zapisujemy URL
                        matchedProduct.GoogleUrl = googleProductUrl;
                        matchedProduct.FoundOnGoogle = true;
                        Console.WriteLine($"Updated product: {matchedProduct.ProductName}, GoogleUrl: {matchedProduct.GoogleUrl}");

                        _context.Products.Update(matchedProduct);
                        await _context.SaveChangesAsync();
                    }
                }

                // Jeśli przetwarzany produkt nie został znaleziony, ustawiamy jego status na false
                if (!matchedUrls.Any(m => m.storeUrl == productToProcess.Url))
                {
                    productToProcess.FoundOnGoogle = false;
                    Console.WriteLine($"Product not found on Google: {productToProcess.ProductName}");
                    _context.Products.Update(productToProcess);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing product: {ex.Message}");
            }
        }

        await scraper.CloseBrowserAsync();
        return Content("Scraping completed for all products.");
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
    public async Task<IActionResult> SetOnGoogleForAll()
    {
        // Pobieramy produkty, które mają wypełnione pole ProductNameInStoreForGoogle
        var products = await _context.Products
            .Where(p => !string.IsNullOrEmpty(p.ProductNameInStoreForGoogle))
            .ToListAsync();

        foreach (var product in products)
        {
            // Jeśli pole Url nie jest puste, ustawiamy OnGoogle na true
            if (!string.IsNullOrEmpty(product.Url))
            {
                product.OnGoogle = true;
            }
            // Jeśli pole Url jest puste, ustawiamy OnGoogle na false
            else
            {
                product.OnGoogle = false;
            }

            // Aktualizujemy produkt w bazie danych
            _context.Products.Update(product);
        }

        // Zapisujemy zmiany
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


    [HttpPost]
    public async Task<IActionResult> ResetIncorrectGoogleStatuses(int storeId)
    {
        // Znajdź produkty, które mają FoundOnGoogle = true, mają GoogleUrl, ale ProductUrl jest null
        var productsToReset = await _context.Products
            .Where(p => p.StoreId == storeId && p.FoundOnGoogle == true && !string.IsNullOrEmpty(p.GoogleUrl) && string.IsNullOrEmpty(p.Url))
            .ToListAsync();

        foreach (var product in productsToReset)
        {
            // Ustawiamy FoundOnGoogle na null i usuwamy GoogleUrl
            product.FoundOnGoogle = null;
            product.GoogleUrl = null;
            _context.Products.Update(product);
        }

        // Zapisanie zmian
        await _context.SaveChangesAsync();

        return Ok(); // Możesz zwrócić inne odpowiedzi w zależności od tego, co chcesz
    }

    [HttpPost]
    public async Task<IActionResult> ClearGoogleUrls(int storeId)
    {
        var products = await _context.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

        foreach (var product in products)
        {
            product.GoogleUrl = null;
            product.FoundOnGoogle = null; // Opcjonalnie możesz zresetować także ten status
            _context.Products.Update(product);
        }

        await _context.SaveChangesAsync();

        return Ok(); // Możesz zwrócić inną odpowiedź w zależności od potrzeb
    }
}