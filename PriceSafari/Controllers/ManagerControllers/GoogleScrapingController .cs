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




        //SCRAPER



        [HttpPost]
        public async Task<IActionResult> StartScraping()
        {
            // Inicjalizacja scrapera
            await _scraper.InitializeAsync();

            // Pobieranie wszystkich produktów do scrapowania
            var scrapingProducts = await _context.GoogleScrapingProducts
                .Where(gsp => !gsp.IsScraped)
                .ToListAsync();

            foreach (var scrapingProduct in scrapingProducts)
            {
                // Zbieranie cen dla danego produktu
                var scrapedPrices = await _scraper.ScrapePricesAsync(scrapingProduct);

                // Zapisanie wyników scrapowania w bazie danych
                _context.PriceData.AddRange(scrapedPrices);

                // Aktualizacja statusu produktu, że został zescrapowany
                scrapingProduct.IsScraped = true;
                _context.GoogleScrapingProducts.Update(scrapingProduct);
            }

            // Zapisanie wyników w bazie danych
            await _context.SaveChangesAsync();

            // Zamykanie przeglądarki
            await _scraper.CloseAsync();

            return RedirectToAction("PreparedProducts");
        }
    }
}
