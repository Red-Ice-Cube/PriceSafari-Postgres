using EFCore.BulkExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Models.ManagerViewModels;
using PriceSafari.Scrapers;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "Admin")]
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
        public async Task<IActionResult> UnpackReports([FromBody] UnpackReportsRequest request)
        {
            if (request == null || request.SelectedReportIds == null || !request.SelectedReportIds.Any())
            {
                return BadRequest("Nie wybrano żadnych raportów.");
            }

            var selectedReportIds = request.SelectedReportIds;
            var ProductIds = request.ProductIds;
            var RegionIds = request.RegionIds;

            // Pobieramy wszystkie regiony jednorazowo
            var allRegions = await _context.Regions.ToDictionaryAsync(r => r.RegionId, r => r.CountryCode);

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
                        // Pobieramy CountryCode dla regionu
                        if (!allRegions.TryGetValue(regionId, out var countryCode))
                        {
                            Console.WriteLine($"Region with ID {regionId} not found.");
                            continue;
                        }

                        // Sprawdzenie, czy już istnieje taki GoogleUrl dla tego samego regionu
                        var existingScrapingProduct = await _context.GoogleScrapingProducts
                            .FirstOrDefaultAsync(gsp => gsp.GoogleUrl == product.GoogleUrl && gsp.RegionId == regionId);

                        if (existingScrapingProduct != null)
                        {
                            // Jeśli już istnieje, dodajemy nowy ProductId do listy, jeśli jeszcze go nie ma
                            if (!existingScrapingProduct.ProductIds.Contains(productId))
                            {
                                existingScrapingProduct.ProductIds.Add(productId);
                                existingScrapingProduct.PriceSafariRaportId = reportId; // Przypisanie raportu
                                existingScrapingProduct.CountryCode = countryCode; // Ustawienie CountryCode
                                _context.GoogleScrapingProducts.Update(existingScrapingProduct);
                            }
                        }
                        else
                        {
                            // Jeśli nie istnieje, tworzymy nowy wpis z przypisanym raportem i CountryCode
                            var newScrapingProduct = new GoogleScrapingProduct
                            {
                                GoogleUrl = product.GoogleUrl,
                                RegionId = regionId,
                                CountryCode = countryCode, // Ustawienie CountryCode
                                ProductIds = new List<int> { productId },
                                PriceSafariRaportId = reportId, // Przypisanie raportu
                                IsScraped = null
                            };

                            _context.GoogleScrapingProducts.Add(newScrapingProduct);
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();

            return Ok("Raporty zostały rozpakowane pomyślnie.");
        }

        public class UnpackReportsRequest
        {
            public List<int> SelectedReportIds { get; set; }
            public Dictionary<int, List<int>> ProductIds { get; set; }
            public Dictionary<int, List<int>> RegionIds { get; set; }
        }


        public async Task<IActionResult> PreparedProducts(int? selectedRegion)
        {
            // Pobieranie produktów do scrapowania z filtrowaniem według regionu, jeśli wybrano
            var scrapingProductsQuery = _context.GoogleScrapingProducts.AsQueryable();

            if (selectedRegion.HasValue)
            {
                scrapingProductsQuery = scrapingProductsQuery.Where(sp => sp.RegionId == selectedRegion.Value);
            }

            var scrapingProducts = await scrapingProductsQuery.ToListAsync();

            // Pobieranie listy regionów do ViewBag z pełnymi informacjami
            ViewBag.Regions = await _context.Regions
                .Select(r => new RegionViewModel
                {
                    RegionId = r.RegionId,
                    Name = r.Name,
                    CountryCode = r.CountryCode
                })
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
            try
            {
            
                await DeleteTableInBatchesAsync("PriceData");
                await DeleteTableInBatchesAsync("GoogleScrapingProducts");
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                throw;
            }

            return RedirectToAction("PreparedProducts");
        }

        private async Task DeleteTableInBatchesAsync(string tableName, int batchSize = 10000) 
        {
            int rowsAffectedThisBatch;
            do
            {

                string sql = $"DELETE TOP ({batchSize}) FROM [{tableName}]";
                rowsAffectedThisBatch = await _context.Database.ExecuteSqlRawAsync(sql);


            } while (rowsAffectedThisBatch == batchSize); 
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
        public async Task<IActionResult> StartScraping(int? selectedRegion, string countryCode)
        {
            // Zawsze pobieramy ustawienia z bazy danych
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                Console.WriteLine("Settings not found in the database.");
                return BadRequest("Settings not found.");
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

            // Pobieramy wartość semafora z ustawień
            int maxConcurrentScrapers = settings.SemophoreGoogle;
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

                                // Przekazujemy region do scrapera
                                var scrapedPrices = await scraper.ScrapePricesAsync(scrapingProduct,  countryCode);

                                if (scrapedPrices.Any())
                                {
                                    // Zapisujemy wszystkie oferty naraz po przetworzeniu URL
                                    scopedContext.PriceData.AddRange(scrapedPrices);
                                    await scopedContext.SaveChangesAsync();
                                    Console.WriteLine($"Zapisano {scrapedPrices.Count} ofert do bazy dla produktu {scrapingProduct.GoogleUrl}.");
                                }

                                // Aktualizujemy status produktu po zapisaniu jego ofert
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


        [HttpPost]
        public async Task<IActionResult> StartScrapingAllRegions()
        {
            // Pobieramy ustawienia z bazy danych
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                Console.WriteLine("Settings not found in the database.");
                return BadRequest("Settings not found.");
            }

            // Pobieramy wszystkie produkty do scrapowania, które nie zostały jeszcze zeskrapowane
            var scrapingProducts = await _context.GoogleScrapingProducts
                .Where(gsp => gsp.IsScraped == null)
                .ToListAsync();

            if (!scrapingProducts.Any())
            {
                Console.WriteLine("No products found to scrape.");
                return NotFound("No products found to scrape.");
            }

            Console.WriteLine($"Znaleziono {scrapingProducts.Count} produktów do scrapowania.");

            if (_hubContext != null)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", 0, scrapingProducts.Count, 0, 0);
            }
            else
            {
                Console.WriteLine("Hub context is null.");
            }

            // Zmienna do śledzenia postępu
            int totalScraped = 0;
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var serviceScopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();

            // Pobieramy wartość semafora z ustawień
            int maxConcurrentScrapers = settings.Semophore;
            var semaphore = new SemaphoreSlim(maxConcurrentScrapers);
            var tasks = new List<Task>();

            var productQueue = new Queue<GoogleScrapingProduct>(scrapingProducts);

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

                                // Przekazujemy CountryCode do scrapera
                                var scrapedPrices = await scraper.ScrapePricesAsync(scrapingProduct, scrapingProduct.CountryCode);

                                if (scrapedPrices.Any())
                                {
                                    // Zapisujemy wszystkie oferty naraz po przetworzeniu URL
                                    scopedContext.PriceData.AddRange(scrapedPrices);
                                    await scopedContext.SaveChangesAsync();
                                    Console.WriteLine($"Zapisano {scrapedPrices.Count} ofert do bazy dla produktu {scrapingProduct.GoogleUrl}.");
                                }

                                // Aktualizujemy status produktu po zapisaniu jego ofert
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
            Console.WriteLine("Wszystkie taski zakończone.");

            stopwatch.Stop();

            return RedirectToAction("PreparedProducts");
        }







        [HttpGet]
        public async Task<IActionResult> ViewReportProducts(int reportId)
        {
            // Pobieramy wszystkie produkty z bazy danych do pamięci
            var scrapingProducts = await _context.GoogleScrapingProducts
                .Where(sp => sp.PriceSafariRaportId == reportId) // Filtrowanie po RaportId
                .ToListAsync();

            if (!scrapingProducts.Any())
            {
                return NotFound("Brak produktów dla tego raportu.");
            }

            // Pobieramy wszystkie produkty z klasy ProductClass na podstawie ProductIds (w pamięci)
            var productIds = scrapingProducts.SelectMany(sp => sp.ProductIds).Distinct().ToList();
            var productClassList = await _context.Products.ToListAsync();

            // Pobieramy wszystkie ceny
            var priceDataList = await _context.PriceData.ToListAsync();

            // Grupa produktów według GoogleUrl, aby zgrupować oferty z różnych regionów
            var groupedProducts = scrapingProducts
                .GroupBy(p => p.GoogleUrl)
                .Select(g => new GroupedProductViewModel
                {
                    GoogleUrl = g.Key,
                    RaportId = g.First().PriceSafariRaportId, // Dodajemy RaportId
                    ProductNames = g.SelectMany(p => p.ProductIds)
                                    .Select(pid => productClassList.FirstOrDefault(pc => pc.ProductId == pid)?.ProductName)
                                    .Where(name => name != null)
                                    .Distinct()
                                    .ToList(),
                    RegionPrices = g.SelectMany(p => priceDataList.Where(pd => pd.ScrapingProductId == p.ScrapingProductId))
                                    .Select(pd => new RegionPriceViewModel
                                    {
                                        RegionId = scrapingProducts.First(sp => sp.ScrapingProductId == pd.ScrapingProductId).RegionId,
                                        Price = pd.Price,                                     
                                        PriceWithDelivery = pd.PriceWithDelivery,                                     
                                        StoreName = pd.StoreName,
                                        OfferUrl = pd.OfferUrl
                                    }).ToList()
                })
                .ToList();

            return View("~/Views/ManagerPanel/GoogleScraping/ViewReportProducts.cshtml", groupedProducts);
        }




        [HttpPost]
        public async Task<IActionResult> SaveReportProducts(int reportId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            Stopwatch stopwatch = new Stopwatch();

            try
            {
                Console.WriteLine("Rozpoczęto przetwarzanie raportu...");
                stopwatch.Start();

                stopwatch.Restart();
                var scrapingProducts = await _context.GoogleScrapingProducts
                    .Include(sp => sp.Products)
                    .Include(sp => sp.PriceData)
                    .Include(sp => sp.Region)
                    .Where(sp => sp.PriceSafariRaportId == reportId)
                    .ToListAsync();
                stopwatch.Stop();
                Console.WriteLine($"Pobranie produktów i powiązań zajęło: {stopwatch.ElapsedMilliseconds} ms");

                if (!scrapingProducts.Any())
                {
                    return NotFound("Brak produktów dla tego raportu.");
                }

                var globalPriceReports = new List<GlobalPriceReport>();

                stopwatch.Restart();
                foreach (var scrapingProduct in scrapingProducts)
                {
                    var region = await _context.Regions.FindAsync(scrapingProduct.RegionId);
                    if (region == null)
                    {
                        Console.WriteLine($"Nie znaleziono regionu dla RegionId: {scrapingProduct.RegionId}");
                        continue;
                    }

                    // Jeśli dla danego produktu jest tylko jedna cena, pomiń go
                    if (scrapingProduct.PriceData == null || scrapingProduct.PriceData.Count <= 1)
                    {
                        Console.WriteLine($"Produkt {scrapingProduct.ScrapingProductId} pominięty - tylko jedna cena.");
                        continue;
                    }

                    foreach (var price in scrapingProduct.PriceData)
                    {
                        foreach (var productId in scrapingProduct.ProductIds)
                        {
                            var product = await _context.Products.FindAsync(productId);
                            if (product == null)
                            {
                                Console.WriteLine($"Nie znaleziono produktu dla ProductId: {productId}");
                                continue;
                            }

                            var calculatedPrice = price.Price * region.CurrencyValue;
                            var calculatedPriceWithDelivery = price.PriceWithDelivery * region.CurrencyValue;

                            var newReport = new GlobalPriceReport
                            {
                                ScrapingProductId = scrapingProduct.ScrapingProductId,
                                ProductId = product.ProductId,
                                Price = price.Price,
                                CalculatedPrice = calculatedPrice,
                                PriceWithDelivery = price.PriceWithDelivery,
                                CalculatedPriceWithDelivery = calculatedPriceWithDelivery,
                                StoreName = price.StoreName,
                                OfferUrl = price.OfferUrl,
                                RegionId = scrapingProduct.RegionId,
                                PriceSafariReportId = reportId
                            };

                            globalPriceReports.Add(newReport);
                        }
                    }
                }


                stopwatch.Stop();
                Console.WriteLine($"Przetwarzanie produktów zajęło: {stopwatch.ElapsedMilliseconds} ms");

                globalPriceReports = globalPriceReports.OrderBy(r => r.CalculatedPrice).ToList();

                var bulkConfig = new BulkConfig
                {
                    CustomDestinationTableName = "heatlead1_SQL_user.GlobalPriceReports",
                    BulkCopyTimeout = 0,
                };

                stopwatch.Restart();
                var batchSize = 1000;
                var totalBatches = (int)Math.Ceiling((double)globalPriceReports.Count / batchSize);

                for (int i = 0; i < globalPriceReports.Count; i += batchSize)
                {
                    var batch = globalPriceReports.Skip(i).Take(batchSize).ToList();
                    stopwatch.Restart();
                    await _context.BulkInsertAsync(batch, bulkConfig);
                    stopwatch.Stop();

                    Console.WriteLine($"Przetworzono partię {i / batchSize + 1} z {totalBatches}. Liczba rekordów: {batch.Count}, Czas zapisania paczki: {stopwatch.ElapsedMilliseconds} ms");
                }

                stopwatch.Stop();
                Console.WriteLine($"Całkowity czas zapisu wszystkich paczek: {stopwatch.ElapsedMilliseconds} ms");

                // Zmiana statusu raportu na "Prepared"
                var report = await _context.PriceSafariReports.FindAsync(reportId);
                if (report != null)
                {
                    report.Prepared = true;
                    report.ReadyDate = DateTime.Now;
                    _context.PriceSafariReports.Update(report);
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                Console.WriteLine("Zakończono przetwarzanie raportu.");

                return RedirectToAction("PreparedProducts");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Błąd podczas zapisywania raportu: {ex.Message}");
                throw;
            }
        }


        [HttpGet]
        public async Task<IActionResult> ViewGlobalPriceReport(int reportId)
        {
            var globalPriceReports = await _context.GlobalPriceReports
                .Include(gpr => gpr.Product)
                .Where(gpr => gpr.PriceSafariReportId == reportId) // Filtrowanie po PriceSafariReportId
                .OrderBy(gpr => gpr.CalculatedPrice)
                .ToListAsync();

            if (!globalPriceReports.Any())
            {
                return NotFound("Brak raportów dla podanego identyfikatora.");
            }

            // Grupowanie raportów na podstawie ProductId i mapowanie do widoku
            var groupedReports = globalPriceReports
                .GroupBy(r => r.ProductId)
                .Select(g => new GroupedGlobalPriceReportViewModel
                {
                    ProductName = g.FirstOrDefault()?.Product?.ProductName,
                    GoogleUrl = g.FirstOrDefault()?.Product?.GoogleUrl,
                    Prices = g.Select(r => new GlobalPriceReportViewModel
                    {
                        CalculatedPrice = r.CalculatedPrice,
                        CalculatedPriceWithDelivery = r.CalculatedPriceWithDelivery,
                        Price = r.Price,
                        PriceWithDelivery = r.PriceWithDelivery,
                        StoreName = r.StoreName,
                        OfferUrl = r.OfferUrl,
                        RegionId = r.RegionId
                    }).ToList()
                })
                .ToList();

            ViewBag.ReportId = reportId; // Przekazanie reportId do widoku

            // Obliczenie liczby ofert w raporcie
            int totalOffers = globalPriceReports.Count;
            ViewBag.TotalOffers = totalOffers; // Przekazanie liczby ofert do widoku

            return View("~/Views/ManagerPanel/GoogleScraping/ViewGlobalPriceReport.cshtml", groupedReports);
        }




        [HttpPost]
        public async Task<IActionResult> TruncateGlobalPriceReports(int reportId)
        {
            try
            {
                // Rozpoczęcie transakcji dla zapewnienia integralności danych
                using (var transaction = await _context.Database.BeginTransactionAsync())
                {
                    // Usunięcie wpisów w GlobalPriceReports powiązanych z danym reportId
                    var reportsToDelete = _context.GlobalPriceReports.Where(gpr => gpr.PriceSafariReportId == reportId);
                    _context.GlobalPriceReports.RemoveRange(reportsToDelete);
                    await _context.SaveChangesAsync();

                    // Usunięcie obiektu PriceSafariReport o danym reportId
                    var priceSafariReportToDelete = await _context.PriceSafariReports.FindAsync(reportId);
                    if (priceSafariReportToDelete != null)
                    {
                        _context.PriceSafariReports.Remove(priceSafariReportToDelete);
                        await _context.SaveChangesAsync();
                    }

                    // Zatwierdzenie transakcji
                    await transaction.CommitAsync();
                }

                TempData["SuccessMessage"] = "Raport oraz powiązane wpisy zostały pomyślnie usunięte.";
                return RedirectToAction("Index"); // Przekierowanie do odpowiedniej akcji, np. lista raportów
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Wystąpił błąd podczas usuwania raportu: {ex.Message}";
                return RedirectToAction("ViewGlobalPriceReport", new { reportId });
            }
        }





        [HttpGet]
        public async Task<IActionResult> LoadUnpreparedReports()
        {
            // Pobieramy raporty, które mają ustawione Prepared na true lub false (wykluczamy null)
            var reports = await _context.PriceSafariReports
                .Where(r => r.Prepared != null) // Wyklucza null
                .ToListAsync();

            if (!reports.Any())
            {
                return NotFound("Brak raportów do wyświetlenia.");
            }

            // Przekazujemy listę raportów do widoku
            return View("~/Views/ManagerPanel/GoogleScraping/LoadUnpreparedReports.cshtml", reports);
        }



    }
}
