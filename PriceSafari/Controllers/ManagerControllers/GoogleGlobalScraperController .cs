using EFCore.BulkExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Models.ManagerViewModels;

using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "Admin")]
    public class GoogleScrapingController : Controller
    {
        private readonly PriceSafariContext _context;

        private readonly GoogleGlobalScraper _scraper;
        private readonly IHubContext<ScrapingHub> _hubContext;

        public GoogleScrapingController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
        {
            _context = context;

            _scraper = new GoogleGlobalScraper();
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
                    ReportId = report.ReportId,
                    ReportName = report.ReportName,
                    StoreName = reportStoreDetails,
                    ProductCount = reportProductDetails.Count,
                    Products = reportProductDetails,
                    Regions = reportRegionDetails
                });

            }

            return View("~/Views/ManagerPanel/GoogleGlobalScraper/Prepare.cshtml", reportData);
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

            var allRegions = await _context.Regions.ToDictionaryAsync(r => r.RegionId, r => r.CountryCode);

            foreach (var reportId in selectedReportIds)
            {
                if (!ProductIds.ContainsKey(reportId) || !RegionIds.ContainsKey(reportId))
                {
                    continue;
                }

                var productIdsForReport = ProductIds[reportId];
                var regionIdsForReport = RegionIds[reportId];

                foreach (var productId in productIdsForReport)
                {
                    var product = await _context.Products.FindAsync(productId);
                    if (product == null) continue;

                    foreach (var regionId in regionIdsForReport)
                    {

                        if (!allRegions.TryGetValue(regionId, out var countryCode))
                        {
                            Console.WriteLine($"Region with ID {regionId} not found.");
                            continue;
                        }

                        var existingScrapingProduct = await _context.GoogleScrapingProducts
                            .FirstOrDefaultAsync(gsp => gsp.GoogleUrl == product.GoogleUrl && gsp.RegionId == regionId);

                        if (existingScrapingProduct != null)
                        {

                            if (!existingScrapingProduct.ProductIds.Contains(productId))
                            {
                                existingScrapingProduct.ProductIds.Add(productId);
                                existingScrapingProduct.PriceSafariRaportId = reportId;
                                existingScrapingProduct.CountryCode = countryCode;
                                _context.GoogleScrapingProducts.Update(existingScrapingProduct);
                            }
                        }
                        else
                        {

                            var newScrapingProduct = new GoogleScrapingProduct
                            {
                                GoogleUrl = product.GoogleUrl,
                                RegionId = regionId,
                                CountryCode = countryCode,
                                ProductIds = new List<int> { productId },
                                PriceSafariRaportId = reportId,
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

            var scrapingProductsQuery = _context.GoogleScrapingProducts.AsQueryable();

            if (selectedRegion.HasValue)
            {
                scrapingProductsQuery = scrapingProductsQuery.Where(sp => sp.RegionId == selectedRegion.Value);
            }

            var scrapingProducts = await scrapingProductsQuery.ToListAsync();

            ViewBag.Regions = await _context.Regions
                .Select(r => new RegionViewModel
                {
                    RegionId = r.RegionId,
                    Name = r.Name,
                    CountryCode = r.CountryCode
                })
                .ToListAsync();

            ViewBag.SelectedRegion = selectedRegion;

            return View("~/Views/ManagerPanel/GoogleGlobalScraper/PreparedProducts.cshtml", scrapingProducts);
        }

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
                string sql = $"DELETE FROM \"{tableName}\" WHERE ctid IN (SELECT ctid FROM \"{tableName}\" LIMIT {batchSize})";
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

            var productsToReset = await _context.GoogleScrapingProducts
                                              .Include(p => p.PriceData)
                                              .Where(p => p.OffersCount == 0)
                                              .ToListAsync();

            if (!productsToReset.Any())
            {
                return RedirectToAction("PreparedProducts");
            }

            foreach (var product in productsToReset)
            {

                product.IsScraped = null;

                if (product.PriceData.Any())
                {
                    _context.PriceData.RemoveRange(product.PriceData);
                }

            }

            await _context.SaveChangesAsync();

            return RedirectToAction("PreparedProducts");
        }

        [HttpPost]
        public async Task<IActionResult> StartScraping(int? selectedRegion, string countryCode)
        {

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

                    var scraper = new GoogleGlobalScraper();
                    if (scraper == null)
                    {
                        Console.WriteLine("Scraper object is null.");
                        return;
                    }

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

                                var scrapedPrices = await scraper.ScrapePricesAsync(scrapingProduct, countryCode);

                                if (scrapedPrices.Any())
                                {

                                    scopedContext.PriceData.AddRange(scrapedPrices);
                                    await scopedContext.SaveChangesAsync();
                                    Console.WriteLine($"Zapisano {scrapedPrices.Count} ofert do bazy dla produktu {scrapingProduct.GoogleUrl}.");
                                }

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

            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                Console.WriteLine("Settings not found in the database.");
                return BadRequest("Settings not found.");
            }

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

            int totalScraped = 0;
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var serviceScopeFactory = HttpContext.RequestServices.GetRequiredService<IServiceScopeFactory>();

            int maxConcurrentScrapers = settings.Semophore;
            var semaphore = new SemaphoreSlim(maxConcurrentScrapers);
            var tasks = new List<Task>();

            var productQueue = new Queue<GoogleScrapingProduct>(scrapingProducts);

            for (int i = 0; i < maxConcurrentScrapers; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();

                    var scraper = new GoogleGlobalScraper();
                    if (scraper == null)
                    {
                        Console.WriteLine("Scraper object is null.");
                        return;
                    }

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

                                var scrapedPrices = await scraper.ScrapePricesAsync(scrapingProduct, scrapingProduct.CountryCode);

                                if (scrapedPrices.Any())
                                {

                                    scopedContext.PriceData.AddRange(scrapedPrices);
                                    await scopedContext.SaveChangesAsync();
                                    Console.WriteLine($"Zapisano {scrapedPrices.Count} ofert do bazy dla produktu {scrapingProduct.GoogleUrl}.");
                                }

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

            var scrapingProducts = await _context.GoogleScrapingProducts
                .Where(sp => sp.PriceSafariRaportId == reportId)
                .ToListAsync();

            if (!scrapingProducts.Any())
            {
                return NotFound("Brak produktów dla tego raportu.");
            }

            var productIds = scrapingProducts.SelectMany(sp => sp.ProductIds).Distinct().ToList();
            var productClassList = await _context.Products.ToListAsync();

            var priceDataList = await _context.PriceData.ToListAsync();

            var groupedProducts = scrapingProducts
                .GroupBy(p => p.GoogleUrl)
                .Select(g => new GroupedProductViewModel
                {
                    GoogleUrl = g.Key,
                    RaportId = g.First().PriceSafariRaportId,
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

            return View("~/Views/ManagerPanel/GoogleGlobalScraper/ViewReportProducts.cshtml", groupedProducts);
        }

        [HttpPost]
        public async Task<IActionResult> SaveReportProducts(int reportId)
        {
            try
            {
                var strategy = _context.Database.CreateExecutionStrategy();

                await strategy.ExecuteAsync(async () =>
                {
                    var stopwatch = new Stopwatch();
                    Console.WriteLine("Rozpoczęto przetwarzanie raportu...");
                    stopwatch.Start();

                    var priceSafariReport = await _context.PriceSafariReports
                        .Include(r => r.Store)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.ReportId == reportId);

                    if (priceSafariReport == null || priceSafariReport.Store == null)
                    {
                        throw new InvalidOperationException("Nie znaleziono raportu lub powiązanego z nim sklepu.");
                    }
                    bool useGoogleXmlFeed = priceSafariReport.Store.UseGoogleXMLFeedPrice;
                    string canonicalStoreName = priceSafariReport.Store.StoreName;

                    Region plnRegion = null;
                    if (useGoogleXmlFeed)
                    {
                        plnRegion = await _context.Regions.FindAsync(1);
                        if (plnRegion == null)
                        {
                            throw new InvalidOperationException("Krytyczny błąd: Nie znaleziono w bazie danych regionu o ID = 1 (PLN).");
                        }
                    }

                    var scrapingProducts = await _context.GoogleScrapingProducts
                        .Include(sp => sp.PriceData)
                        .Where(sp => sp.PriceSafariRaportId == reportId)
                        .ToListAsync();

                    if (!scrapingProducts.Any())
                    {
                        throw new InvalidOperationException("Brak produktów dla tego raportu.");
                    }

                    var allProductIds = scrapingProducts.SelectMany(sp => sp.ProductIds).Distinct().ToList();
                    var allProducts = await _context.Products
                        .Where(p => allProductIds.Contains(p.ProductId))
                        .ToDictionaryAsync(p => p.ProductId);

                    var allRegionIds = scrapingProducts.Select(sp => sp.RegionId).Distinct().ToList();
                    var allRegions = await _context.Regions
                        .Where(r => allRegionIds.Contains(r.RegionId))
                        .ToDictionaryAsync(r => r.RegionId);

                    var globalPriceReports = new List<GlobalPriceReport>();
                    stopwatch.Restart();

                    var scrapingGroups = scrapingProducts.GroupBy(sp => string.Join(",", sp.ProductIds.OrderBy(id => id)));

                    foreach (var group in scrapingGroups)
                    {

                        var representativeScrapingProductId = group.First().ScrapingProductId;
                        var productIdsForGroup = group.First().ProductIds;

                        bool ownStoreOfferFoundInScraping = false;
                        if (useGoogleXmlFeed && !string.IsNullOrEmpty(canonicalStoreName))
                        {
                            ownStoreOfferFoundInScraping = group
                                .SelectMany(sp => sp.PriceData)
                                .Any(p => canonicalStoreName.Equals(p.StoreName, StringComparison.OrdinalIgnoreCase));
                        }

                        foreach (var scrapingProductInGroup in group)
                        {
                            if (!allRegions.TryGetValue(scrapingProductInGroup.RegionId, out var region))
                            {
                                Console.WriteLine($"Nie znaleziono regionu dla RegionId: {scrapingProductInGroup.RegionId}");
                                continue;
                            }

                            if (scrapingProductInGroup.PriceData == null) continue;

                            foreach (var price in scrapingProductInGroup.PriceData)
                            {
                                foreach (var productId in productIdsForGroup)
                                {
                                    if (allProducts.TryGetValue(productId, out var product))
                                    {
                                        var newReport = new GlobalPriceReport
                                        {
                                            ScrapingProductId = representativeScrapingProductId,
                                            ProductId = product.ProductId,
                                            Price = price.Price,
                                            CalculatedPrice = price.Price * region.CurrencyValue,
                                            PriceWithDelivery = price.PriceWithDelivery,
                                            CalculatedPriceWithDelivery = price.PriceWithDelivery * region.CurrencyValue,
                                            StoreName = price.StoreName,
                                            OfferUrl = price.OfferUrl,
                                            RegionId = scrapingProductInGroup.RegionId,
                                            PriceSafariReportId = reportId
                                        };
                                        globalPriceReports.Add(newReport);
                                    }
                                }
                            }
                        }

                        if (useGoogleXmlFeed && !ownStoreOfferFoundInScraping && plnRegion != null)
                        {
                            foreach (var productId in productIdsForGroup)
                            {
                                if (allProducts.TryGetValue(productId, out var product) && product.GoogleXMLPrice.HasValue && product.GoogleXMLPrice.Value > 0)
                                {
                                    decimal priceFromFeed = product.GoogleXMLPrice.Value;
                                    decimal deliveryFromFeed = product.GoogleDeliveryXMLPrice ?? 0;
                                    decimal priceWithDeliveryFromFeed = priceFromFeed + deliveryFromFeed;

                                    var newReportFromFeed = new GlobalPriceReport
                                    {
                                        ScrapingProductId = representativeScrapingProductId,
                                        ProductId = product.ProductId,
                                        Price = priceFromFeed,
                                        CalculatedPrice = priceFromFeed * plnRegion.CurrencyValue,
                                        PriceWithDelivery = priceWithDeliveryFromFeed,
                                        CalculatedPriceWithDelivery = priceWithDeliveryFromFeed * plnRegion.CurrencyValue,
                                        StoreName = canonicalStoreName,
                                        OfferUrl = null,
                                        RegionId = plnRegion.RegionId,
                                        PriceSafariReportId = reportId
                                    };
                                    globalPriceReports.Add(newReportFromFeed);
                                }
                            }
                        }
                    }

                    stopwatch.Stop();
                    Console.WriteLine($"Przetwarzanie produktów i tworzenie listy zajęło: {stopwatch.ElapsedMilliseconds} ms");

                    globalPriceReports = globalPriceReports.OrderBy(r => r.CalculatedPrice).ToList();

                    var bulkConfig = new BulkConfig
                    {
                        CustomDestinationTableName = "heatlead1_SQL_user.GlobalPriceReports",
                        BulkCopyTimeout = 0,
                    };

                    var batchSize = 1000;
                    for (int i = 0; i < globalPriceReports.Count; i += batchSize)
                    {
                        var batch = globalPriceReports.Skip(i).Take(batchSize).ToList();
                        await _context.BulkInsertAsync(batch, bulkConfig);
                    }

                    var reportToUpdate = await _context.PriceSafariReports.FindAsync(reportId);
                    if (reportToUpdate != null)
                    {
                        reportToUpdate.Prepared = true;
                        reportToUpdate.ReadyDate = DateTime.Now;
                        _context.PriceSafariReports.Update(reportToUpdate);
                        await _context.SaveChangesAsync();
                    }

                    Console.WriteLine("Zakończono przetwarzanie raportu.");
                });

                return RedirectToAction("PreparedProducts");
            }
            catch (InvalidOperationException ex) when (ex.Message == "Brak produktów dla tego raportu.")
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Wystąpił ostateczny, nieodwracalny błąd podczas zapisu raportu: {ex}");
                return StatusCode(500, "Wystąpił wewnętrzny błąd serwera podczas przetwarzania raportu.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> ViewGlobalPriceReport(int reportId)
        {
            var globalPriceReports = await _context.GlobalPriceReports
                .Include(gpr => gpr.Product)
                .Where(gpr => gpr.PriceSafariReportId == reportId)
                .OrderBy(gpr => gpr.CalculatedPrice)
                .ToListAsync();

            if (!globalPriceReports.Any())
            {
                return NotFound("Brak raportów dla podanego identyfikatora.");
            }

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

            ViewBag.ReportId = reportId;

            int totalOffers = globalPriceReports.Count;
            ViewBag.TotalOffers = totalOffers;

            return View("~/Views/ManagerPanel/GoogleGlobalScraper/ViewGlobalPriceReport.cshtml", groupedReports);
        }

        [HttpPost]
        public async Task<IActionResult> TruncateGlobalPriceReports(int reportId)
        {
            try
            {

                using (var transaction = await _context.Database.BeginTransactionAsync())
                {

                    var reportsToDelete = _context.GlobalPriceReports.Where(gpr => gpr.PriceSafariReportId == reportId);
                    _context.GlobalPriceReports.RemoveRange(reportsToDelete);
                    await _context.SaveChangesAsync();

                    var priceSafariReportToDelete = await _context.PriceSafariReports.FindAsync(reportId);
                    if (priceSafariReportToDelete != null)
                    {
                        _context.PriceSafariReports.Remove(priceSafariReportToDelete);
                        await _context.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();
                }

                TempData["SuccessMessage"] = "Raport oraz powiązane wpisy zostały pomyślnie usunięte.";
                return RedirectToAction("Index");
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

            var reports = await _context.PriceSafariReports
                .Where(r => r.Prepared != null)
                .ToListAsync();

            if (!reports.Any())
            {
                return NotFound("Brak raportów do wyświetlenia.");
            }

            return View("~/Views/ManagerPanel/GoogleGlobalScraper/LoadUnpreparedReports.cshtml", reports);
        }

    }
}