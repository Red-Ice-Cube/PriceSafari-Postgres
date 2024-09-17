using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.Controllers
{
    public class GoogleScrapingController : Controller
    {
        private readonly PriceSafariContext _context;

        public GoogleScrapingController(PriceSafariContext context)
        {
            _context = context;
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
    }
}
