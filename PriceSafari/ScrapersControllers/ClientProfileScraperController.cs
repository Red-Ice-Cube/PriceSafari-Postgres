using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Scrapers;

namespace PriceSafari.ScrapersControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class ClientProfileScraperController : Controller
    {
        private readonly PriceSafariContext _context;

        public ClientProfileScraperController(PriceSafariContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View("~/Views/ManagerPanel/ClientProfiles/ClientScraper.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Scrape(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                ModelState.AddModelError("", "URL nie może być pusty.");
                return RedirectToAction("Index");
            }

            var scraper = new ClientProfileScraper(_context);

            try
            {
                await scraper.InitializeBrowserAsync();

                // Pobierz istniejące URL-e z bazy danych
                var existingUrls = await _context.ClientProfiles
                    .Select(cp => cp.CeneoProfileUrl)
                    .ToListAsync();

                Console.WriteLine($"Found {existingUrls.Count} existing URLs in the database.");

                // Rozpocznij scrapowanie URL-i (teraz ta metoda zwróci listę znalezionych URLi)
                Console.WriteLine($"Rozpoczynam scrapowanie URL-i dla {url}...");
                var foundUrls = await scraper.ScrapeProfileUrlsAsync(url, existingUrls);

                // Przetwórz nowe URL-e
                var newUrls = foundUrls.Except(existingUrls).ToList();
                if (newUrls.Any())
                {
                    Console.WriteLine("Processing new URLs for emails...");
                    await scraper.ProcessNewUrlsAsync(newUrls);
                }

                await scraper.CloseBrowserAsync();

                TempData["SuccessMessage"] = "Scraping completed. Check console for details.";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Wystąpił błąd podczas scrapowania: {ex.Message}";
                return RedirectToAction("Index");
            }
        }





    }
}
