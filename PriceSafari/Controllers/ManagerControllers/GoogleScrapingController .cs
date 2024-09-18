using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Services;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.Controllers
{
    public class GoogleScrapingController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly GooglePriceScraper _scraper;
        private readonly IHubContext<ScrapingHub> _hubContext;

        public GoogleScrapingController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
        {
            _context = context;
            _scraper = new GooglePriceScraper();
            _hubContext = hubContext;
        }

        public async Task<IActionResult> Prepare()
        {
           
            var reportsToPrepare = await _context.PriceSafariReports
                .Where(r => r.Prepared == false)
                .ToListAsync();

          
            var allProducts = await _context.Products.ToListAsync();

          
            var allRegions = await _context.Regions.ToListAsync();

            
            var allStores = await _context.Stores.ToListAsync();

         
            var productsById = allProducts.ToDictionary(p => p.ProductId);

           
            var regionsById = allRegions.ToDictionary(r => r.RegionId);

         
            var storesById = allStores.ToDictionary(s => s.StoreId);

         
            var reportData = new List<object>();

            foreach (var report in reportsToPrepare)
            {
               
                var reportProductDetails = report.ProductIds
                    .Where(id => productsById.ContainsKey(id))
                    .Select(id => new
                    {
                        ProductId = id,
                        ProductName = productsById[id].ProductName,
                        GoogleUrl = productsById[id].GoogleUrl
                    })
                    .ToList();

             
                var reportRegionDetails = report.RegionIds
                    .Where(id => regionsById.ContainsKey(id))
                    .Select(id => new
                    {
                        RegionId = id,
                        RegionName = regionsById[id].Name
                    })
                    .ToList();

            
                var reportStoreDetails = storesById.ContainsKey(report.StoreId)
                    ? storesById[report.StoreId].StoreName
                    : "Brak sklepu";


                reportData.Add(new
                {
                    ReportId = report.ReportId, // Dodanie ReportId
                    ReportName = report.ReportName,
                    StoreName = reportStoreDetails,
                    ProductCount = reportProductDetails.Count,
                    Products = reportProductDetails,
                    Regions = reportRegionDetails
                });

            }


            return View("~/Views/ManagerPanel/GoogleScraping/Prepare.cshtml", reportData);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnpackReports(List<int> selectedReportIds, Dictionary<int, List<int>> ProductIds, Dictionary<int, List<int>> RegionIds)
        {
            if (selectedReportIds == null || !selectedReportIds.Any())
            {
                return BadRequest("Nie wybrano żadnych raportów.");
            }

            // Przetwarzanie wybranych raportów
            foreach (var reportId in selectedReportIds)
            {
                if (!ProductIds.ContainsKey(reportId) || !RegionIds.ContainsKey(reportId))
                {
                    continue; // Pomiń raport, jeśli brakuje ID produktów lub regionów
                }

                var productIdsForReport = ProductIds[reportId];
                var regionIdsForReport = RegionIds[reportId];

                foreach (var productId in productIdsForReport)
                {
                    var product = await _context.Products.FindAsync(productId);
                    if (product == null) continue;

                    foreach (var regionId in regionIdsForReport)
                    {
                        // Sprawdzenie, czy już istnieje taki GoogleUrl dla tego samego regionu
                        var existingScrapingProduct = await _context.GoogleScrapingProducts
                            .FirstOrDefaultAsync(gsp => gsp.GoogleUrl == product.GoogleUrl && gsp.RegionId == regionId);

                        if (existingScrapingProduct != null)
                        {
                            // Jeśli już istnieje, dodajemy nowy ProductId do listy, jeśli jeszcze go nie ma
                            if (!existingScrapingProduct.ProductIds.Contains(productId))
                            {
                                existingScrapingProduct.ProductIds.Add(productId);
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
                                ProductIds = new List<int> { productId },
                                IsScraped = null
                            };

                            _context.GoogleScrapingProducts.Add(newScrapingProduct);
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("PreparedProducts");
        }



        // GET: Scraping/PreparedProducts
        public async Task<IActionResult> PreparedProducts(int? selectedRegion)
        {
            // Pobieranie produktów do scrapowania z filtrowaniem według regionu, jeśli wybrano
            var scrapingProductsQuery = _context.GoogleScrapingProducts.AsQueryable();

            if (selectedRegion.HasValue)
            {
                scrapingProductsQuery = scrapingProductsQuery.Where(sp => sp.RegionId == selectedRegion.Value);
            }

            var scrapingProducts = await scrapingProductsQuery.ToListAsync();

            // Pobieranie listy regionów do ViewBag
            ViewBag.Regions = await _context.Regions
                .Select(r => new { r.RegionId, r.Name })
                .ToListAsync();

            // Przekazujemy również wybrany region do widoku, aby zachować wybór
            ViewBag.SelectedRegion = selectedRegion;

            // Renderowanie odpowiedniego widoku
            return View("~/Views/ManagerPanel/GoogleScraping/PreparedProducts.cshtml", scrapingProducts);
        }


        // POST: Scraping/ClearPreparedProducts
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearPreparedProducts()
        {
            
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM GoogleScrapingProducts");

      
            await _context.Database.ExecuteSqlRawAsync("DELETE FROM PriceData");

           
            return RedirectToAction("PreparedProducts");
        }


        [HttpPost]
        public async Task<IActionResult> ResetScrapingStatus(int productId)
        {
            var product = await _context.GoogleScrapingProducts.FindAsync(productId);
            if (product != null)
            {
                product.IsScraped = null;

            
                var relatedPrices = _context.PriceData.Where(pd => pd.ScrapingProductId == productId);
                _context.PriceData.RemoveRange(relatedPrices);

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

           
                var relatedPrices = _context.PriceData.Where(pd => pd.ScrapingProductId == product.ScrapingProductId);
                _context.PriceData.RemoveRange(relatedPrices);

                _context.GoogleScrapingProducts.Update(product);
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("PreparedProducts");
        }


        [HttpPost]
        public async Task<IActionResult> StartScraping(Settings settings, int? selectedRegion)
        {
            if (settings == null)
            {
                Console.WriteLine("Settings object is null.");
                return BadRequest("Settings cannot be null.");
            }

            var scrapingProductsQuery = _context.GoogleScrapingProducts
                .Where(gsp => gsp.IsScraped == null);

            if (selectedRegion.HasValue)
            {
                scrapingProductsQuery = scrapingProductsQuery.Where(gsp => gsp.RegionId == selectedRegion.Value);
            }

            var scrapingProducts = await scrapingProductsQuery.ToListAsync();

            if (!scrapingProducts.Any())
            {
                Console.WriteLine("No products found to scrape.");
                return NotFound("No products found to scrape.");
            }

            Console.WriteLine($"Znaleziono {scrapingProducts.Count} produktów do scrapowania w regionie {selectedRegion}.");

            
            if (_hubContext != null)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", 0, scrapingProducts.Count, 0, 0);
            }
            else
            {
                Console.WriteLine("Hub context is null.");
            }

            int maxConcurrentScrapers = settings.Semophore;
            var semaphore = new SemaphoreSlim(maxConcurrentScrapers);
            var tasks = new List<Task>();

            var productQueue = new Queue<GoogleScrapingProduct>(scrapingProducts);
            var serviceScopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();

            int totalScraped = 0;
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            for (int i = 0; i < maxConcurrentScrapers; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();

                    var scraper = new GooglePriceScraper();
                    if (scraper == null)
                    {
                        Console.WriteLine("Scraper object is null.");
                        return;
                    }

                    await scraper.InitializeAsync(settings);

                    while (true)
                    {
                        GoogleScrapingProduct scrapingProduct = null;

                        lock (productQueue)
                        {
                            if (productQueue.Count > 0)
                            {
                                scrapingProduct = productQueue.Dequeue();
                            }
                        }

                        if (scrapingProduct == null)
                        {
                            break;
                        }

                        try
                        {
                            using (var scope = serviceScopeFactory.CreateScope())
                            {
                                var scopedContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                                Console.WriteLine($"Rozpoczęcie scrapowania dla URL: {scrapingProduct.GoogleUrl}");
                                var scrapedPrices = await scraper.ScrapePricesAsync(scrapingProduct);

                                scopedContext.PriceData.AddRange(scrapedPrices);
                                await scopedContext.SaveChangesAsync();
                                Console.WriteLine($"Zapisano {scrapedPrices.Count} ofert do bazy.");

                                scrapingProduct.IsScraped = true;
                                scrapingProduct.OffersCount = scrapedPrices.Count;

                                scopedContext.GoogleScrapingProducts.Update(scrapingProduct);
                                await scopedContext.SaveChangesAsync();
                                Console.WriteLine($"Zaktualizowano status i liczbę ofert dla produktu {scrapingProduct.ScrapingProductId}: {scrapingProduct.OffersCount}.");

                                
                                Interlocked.Increment(ref totalScraped);
                                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", totalScraped, scrapingProducts.Count, elapsedSeconds, 0);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Błąd podczas scrapowania produktu {scrapingProduct.ScrapingProductId}: {ex.Message}");
                        }
                    }

                    await scraper.CloseAsync();
                    semaphore.Release();
                }));
            }

            await Task.WhenAll(tasks);
            stopwatch.Stop();
            Console.WriteLine("Wszystkie taski zakończone.");

            return RedirectToAction("PreparedProducts");
        }






    }
}
