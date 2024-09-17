using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Services;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.Controllers
{
    public class GoogleScrapingController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly GooglePriceScraper _scraper;

        public GoogleScrapingController(PriceSafariContext context)
        {
            _context = context;
            _scraper = new GooglePriceScraper();
        }

        // Akcja przygotowania produktów do scrapowania
        public IActionResult Prepare()
        {
            var productsToScrape = _context.Products
                .Where(p => p.FoundOnGoogle == true && !string.IsNullOrEmpty(p.GoogleUrl))
                .ToList();

            var regions = _context.Regions.ToList();
            ViewBag.Regions = regions;

            return View("~/Views/ManagerPanel/GoogleScraping/Prepare.cshtml", productsToScrape);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(int regionId)
        {
            if (regionId <= 0)
            {
                return BadRequest("Invalid region selected.");
            }

            // Pobieranie produktów do scrapowania
            var products = await _context.Products
                .Where(p => p.FoundOnGoogle == true && !string.IsNullOrEmpty(p.GoogleUrl))
                .ToListAsync();

            foreach (var product in products)
            {
                // Sprawdzenie, czy już istnieje taki GoogleUrl dla tego samego regionu
                var existingScrapingProduct = await _context.GoogleScrapingProducts
                    .FirstOrDefaultAsync(gsp => gsp.GoogleUrl == product.GoogleUrl && gsp.RegionId == regionId);

                if (existingScrapingProduct != null)
                {
                    // Jeśli już istnieje, dodajemy nowy ProductId do listy, jeśli jeszcze go nie ma
                    if (!existingScrapingProduct.ProductIds.Contains(product.ProductId))
                    {
                        existingScrapingProduct.ProductIds.Add(product.ProductId);
                        _context.GoogleScrapingProducts.Update(existingScrapingProduct);
                    }
                }
                else
                {
                    // Jeśli nie istnieje, tworzymy nowy wpis
                    var newScrapingProduct = new GoogleScrapingProduct
                    {
                        GoogleUrl = product.GoogleUrl,
                        RegionId = regionId,
                        ProductIds = new List<int> { product.ProductId },
                        IsScraped = false
                    };

                    _context.GoogleScrapingProducts.Add(newScrapingProduct);
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("PreparedProducts");
        }




        // GET: Scraping/PreparedProducts
        public async Task<IActionResult> PreparedProducts()
        {
            var scrapingProducts = await _context.GoogleScrapingProducts.ToListAsync();
            return View("~/Views/ManagerPanel/GoogleScraping/PreparedProducts.cshtml", scrapingProducts);
        }

        // POST: Scraping/ClearPreparedProducts
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearPreparedProducts()
        {
            _context.GoogleScrapingProducts.RemoveRange(_context.GoogleScrapingProducts);
            await _context.SaveChangesAsync();
            return RedirectToAction("PreparedProducts");
        }


        [HttpPost]
        public async Task<IActionResult> ResetScrapingStatus(int productId)
        {
            var product = await _context.GoogleScrapingProducts.FindAsync(productId);
            if (product != null)
            {
                product.IsScraped = null;
                _context.GoogleScrapingProducts.Update(product);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("PreparedProducts");
        }

        [HttpPost]
        public async Task<IActionResult> ResetAllScrapingStatuses()
        {
            var products = await _context.GoogleScrapingProducts.ToListAsync();
            foreach (var product in products)
            {
                product.IsScraped = null;
                _context.GoogleScrapingProducts.Update(product);
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("PreparedProducts");
        }



        //SCRAPER
        [HttpPost]
        public async Task<IActionResult> StartScraping(Settings settings)
        {
            // Inicjalizacja przeglądarki
            await _scraper.InitializeAsync(settings);
            Console.WriteLine("Przeglądarka zainicjalizowana.");

            // Pobranie produktów do scrapowania
            var scrapingProducts = await _context.GoogleScrapingProducts
                .Where(gsp => gsp.IsScraped == null)
                .ToListAsync();

            Console.WriteLine($"Znaleziono {scrapingProducts.Count} produktów do scrapowania.");

            foreach (var scrapingProduct in scrapingProducts)
            {
                try
                {
                    // Scrapowanie danych cen
                    Console.WriteLine($"Rozpoczęcie scrapowania dla URL: {scrapingProduct.GoogleUrl}");
                    var scrapedPrices = await _scraper.ScrapePricesAsync(scrapingProduct);

                    // Zapis danych w bazie
                    _context.PriceData.AddRange(scrapedPrices);
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"Zapisano {scrapedPrices.Count} ofert do bazy.");

                    // Aktualizacja statusu produktu
                    scrapingProduct.IsScraped = true;
                }
                catch (Exception ex)
                {
                    // W przypadku błędu ustawiamy IsScraped na false
                    scrapingProduct.IsScraped = false;
                    Console.WriteLine($"Błąd podczas scrapowania produktu {scrapingProduct.ScrapingProductId}: {ex.Message}");
                }

                _context.GoogleScrapingProducts.Update(scrapingProduct);
                await _context.SaveChangesAsync(); // Zapis aktualizacji statusu i liczby ofert
                Console.WriteLine($"Zaktualizowano status i liczbę ofert dla produktu {scrapingProduct.ScrapingProductId}.");
            }

            await _scraper.CloseAsync();
            Console.WriteLine("Przeglądarka zamknięta.");

            return RedirectToAction("PreparedProducts");
        }


    }
}
