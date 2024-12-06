using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Scrapers;

namespace PriceSafari.ScrapersControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class ClientProfileScraperController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        // Statyczne pole przechowujące obecną instancję scrapera
        private static ClientProfileScraper _scraper;
        // Przechowujemy też listę istniejących URLi, aby nie pobierać jej za każdym razem z bazy
        private static List<string> _existingUrls;

        public ClientProfileScraperController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            // Wyświetlamy listę zapisanych w bazie URL
            _existingUrls = await _context.ClientProfiles
                .Select(cp => cp.CeneoProfileUrl)
                .ToListAsync();

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

            // Jeśli scraper jeszcze nie jest zainicjalizowany, tworzymy go
            if (_scraper == null)
            {
                // Pobierz istniejące URL-e z bazy jeśli nie zostały jeszcze pobrane lub jeśli są puste
                if (_existingUrls == null || !_existingUrls.Any())
                {
                    _existingUrls = await _context.ClientProfiles
                        .Select(cp => cp.CeneoProfileUrl)
                        .ToListAsync();
                }

                _scraper = new ClientProfileScraper(_context, _existingUrls);
                await _scraper.InitializeBrowserAsync();
            }

            Console.WriteLine($"Found {_existingUrls.Count} existing URLs in the database.");
            Console.WriteLine($"Rozpoczynam scrapowanie URL-i dla {url}...");

            var foundUrls = await _scraper.ScrapeProfileUrlsAsync(url);

            // Wypiszmy, które URL są nowe, a które już mamy w bazie
            Console.WriteLine("\n=== URL DIFF REPORT ===");
            var alreadyInDb = foundUrls.Intersect(_existingUrls).ToList();
            var newUrls = foundUrls.Except(_existingUrls).ToList();

            Console.WriteLine("Already in DB:");
            foreach (var a in alreadyInDb)
            {
                Console.WriteLine(a);
            }

            Console.WriteLine("\nNew URLs:");
            foreach (var n in newUrls)
            {
                Console.WriteLine(n);
            }

            List<StoreData> processedStores = new List<StoreData>();

            if (newUrls.Any())
            {
                Console.WriteLine("Processing new URLs for emails...");
                processedStores = await _scraper.ProcessNewUrlsAsync(newUrls);
            }
            else
            {
                Console.WriteLine("No new URLs found.");
            }

            // Raport końcowy
            Console.WriteLine("\n=== FINAL REPORT ===");
            Console.WriteLine($"Main scraped URL: {url}");

            int savedCount = 0;
            int skippedCount = 0;
            foreach (var store in processedStores)
            {
                Console.WriteLine("------");
                Console.WriteLine($"Store URL: {store.OriginalUrl}");
                Console.WriteLine($"Email: {store.Email}");
                Console.WriteLine($"Phone: {store.Phone}");
                Console.WriteLine($"Store Name: {store.StoreName}");
                Console.WriteLine($"Products Count: {store.ProductCount}");
            }

            // Zapis do bazy danych przetworzonych sklepów
            var user = await _userManager.GetUserAsync(User);

            foreach (var store in processedStores)
            {
                // Sprawdź czy już istnieje w bazie
                bool urlExists = await _context.ClientProfiles
                    .AnyAsync(cp => cp.CeneoProfileUrl == store.OriginalUrl);

                if (!urlExists)
                {
                    var clientProfile = new ClientProfile
                    {
                        CeneoProfileUrl = store.OriginalUrl,
                        CeneoProfileEmail = store.Email,
                        CeneoProfileTelephone = store.Phone,
                        CeneoProfileName = store.StoreName,
                        CeneoProfileProductCount = store.ProductCount,
                        CreatedByUserId = user.Id,
                        CreationDate = DateTime.Now,
                        Status = ClientStatus.Nowy
                    };

                    _context.ClientProfiles.Add(clientProfile);
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"Saved to DB: {store.OriginalUrl}");
                    savedCount++;

                    // Dodaj nowy URL do listy istniejących, by w kolejnych iteracjach był pomijany
                    _existingUrls.Add(store.OriginalUrl);
                }
                else
                {
                    Console.WriteLine($"Skipped (already in DB): {store.OriginalUrl}");
                    skippedCount++;
                }
            }

            // Podsumowanie
            int totalNewFound = newUrls.Count;
            Console.WriteLine("\n=== SUMMARY ===");
            Console.WriteLine($"Znaleziono {totalNewFound} nowych kontaktów.");
            Console.WriteLine($"Zapisano do bazy NOWYCH KONTAKTÓW: {savedCount}");
        

            // Informacja dla użytkownika, co dalej
            Console.WriteLine("\nAby kontynuować scrapowanie, wprowadź kolejny URL w aplikacji webowej.");
            Console.WriteLine("Aby zakończyć, naciśnij przycisk 'Zatrzymaj scrapera' w aplikacji.");

            // Nie zamykamy przeglądarki, scraper czeka na nowe żądanie lub zatrzymanie
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StopScraper()
        {
            if (_scraper != null)
            {
                await _scraper.CloseBrowserAsync();
                _scraper = null;
                Console.WriteLine("Scraper stopped and browser closed.");
            }
            else
            {
                Console.WriteLine("Scraper is not active.");
            }

            return RedirectToAction("Index");
        }
    }
}
