using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using System.Security.Claims;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "Member")]
    public class SafariController : Controller
    {
        

        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        public SafariController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }





        [HttpGet]
        public async Task<IActionResult> Chanel()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStores = await _context.UserStores
                .Where(us => us.UserId == userId)
                .Include(us => us.StoreClass)
                .ThenInclude(s => s.ScrapHistories)



                .ToListAsync();

            var stores = userStores.Select(us => us.StoreClass).ToList();

            var storeDetails = stores.Select(store => new ChanelViewModel
            {
                StoreId = store.StoreId,
                StoreName = store.StoreName,
                LogoUrl = store.StoreLogoUrl,
                LastScrapeDate = store.ScrapHistories.OrderByDescending(sh => sh.Date).FirstOrDefault()?.Date,

            }).ToList();

            return View("~/Views/Panel/Safari/Chanel.cshtml", storeDetails);
        }

        [HttpGet]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        public async Task<IActionResult> StoreReports(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (storeId == 0)
            {
                return NotFound("Store ID not provided.");
            }

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound("Store not found.");
            }

         
            var reports = await _context.PriceSafariReports
                .Where(r => r.StoreId == storeId && r.Prepared != null)
                .OrderByDescending(r => r.CreatedDate)
                .ToListAsync();

       
            var allRegions = await _context.Regions.ToListAsync();

         
            var reportCountries = new Dictionary<int, List<string>>();

         
            foreach (var report in reports)
            {
           
                var matchingRegions = allRegions
                    .Where(region => report.RegionIds.Contains(region.RegionId))
                    .ToList();

             
                var countryFlags = matchingRegions.Select(region => region.Name.ToLower()).ToList();
                reportCountries[report.ReportId] = countryFlags;

            }

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreId = storeId;
            ViewBag.ReportCountries = reportCountries;

            return View("~/Views/Panel/Safari/PriceSafariReport.cshtml", reports);
        }


        [HttpGet]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        public async Task<IActionResult> SafariReportAnalysis(int reportId, int? regionId = null)
        {
            if (reportId == 0)
            {
                return NotFound("Nieprawidłowy identyfikator raportu.");
            }

            var report = await _context.PriceSafariReports
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.ReportId == reportId);

            if (report == null)
            {
                return NotFound("Raport nie został znaleziony.");
            }

            var globalPriceReports = await _context.GlobalPriceReports
                .Where(gpr => gpr.PriceSafariReportId == reportId)
                .Include(gpr => gpr.Product)
                .ToListAsync();

            var storeName = report.Store?.StoreName?.ToLower();

            var storeFlags = await _context.Flags
                .Where(f => f.StoreId == report.StoreId)
                .ToListAsync();

            var productFlagsDictionary = storeFlags
                .SelectMany(flag => _context.ProductFlags
                    .Where(pf => pf.FlagId == flag.FlagId)
                    .Select(pf => new { pf.ProductId, pf.FlagId }))
                .GroupBy(pf => pf.ProductId)
                .ToDictionary(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            // Fetch regions
            var regions = await _context.Regions
                .ToDictionaryAsync(r => r.RegionId, r => r.Name);

            // Pass regions and selected regionId to the view
            ViewBag.Regions = regions;
            ViewBag.RegionId = regionId;

            var productPrices = globalPriceReports
                .GroupBy(gpr => gpr.ProductId)
                .Where(group =>
                {
                    if (regionId.HasValue)
                    {
                        // Only include products that have at least one competitor price in the specified region
                        return group.Any(gpr => gpr.RegionId == regionId.Value && gpr.StoreName.ToLower() != storeName);
                    }
                    else
                    {
                        // Include all products
                        return true;
                    }
                })
                .Select(group =>
                {
                    // Fetch your own price from the entire group, regardless of regionId
                    var ourPrice = group.FirstOrDefault(gpr => gpr.StoreName.ToLower() == storeName);

                    IEnumerable<GlobalPriceReport> competitorPrices;

                    if (regionId.HasValue)
                    {
                        // Filter competitor prices by regionId and exclude your store
                        competitorPrices = group.Where(gpr => gpr.RegionId == regionId.Value && gpr.StoreName.ToLower() != storeName);
                    }
                    else
                    {
                        // Include all competitor prices excluding your store
                        competitorPrices = group.Where(gpr => gpr.StoreName.ToLower() != storeName);
                    }

                    var lowestCompetitorPrice = competitorPrices.OrderBy(gpr => gpr.CalculatedPrice).FirstOrDefault();

                    // Get flags for your product or the competitor's product
                    int productId = ourPrice?.ProductId ?? lowestCompetitorPrice?.ProductId ?? 0;
                    productFlagsDictionary.TryGetValue(productId, out var flagIds);

                    var regionName = lowestCompetitorPrice?.RegionId != null && regions.ContainsKey(lowestCompetitorPrice.RegionId)
                        ? regions[lowestCompetitorPrice.RegionId]
                        : "Unknown";

                    var ourRegionName = ourPrice?.RegionId != null && regions.ContainsKey(ourPrice.RegionId)
                        ? regions[ourPrice.RegionId]
                        : "Unknown";

                    return new ProductPriceViewModel
                    {
                        ProductId = productId,
                        ProductName = ourPrice?.Product?.ProductName ?? lowestCompetitorPrice?.Product?.ProductName,
                        GoogleUrl = ourPrice?.Product?.GoogleUrl ?? lowestCompetitorPrice?.Product?.GoogleUrl,
                        Category = ourPrice?.Product?.Category ?? lowestCompetitorPrice?.Product?.Category,
                        // Competitor's lowest price information
                        Price = lowestCompetitorPrice?.Price ?? 0,
                        StoreName = lowestCompetitorPrice?.StoreName ?? "Brak konkurencyjnej ceny",
                        PriceWithDelivery = lowestCompetitorPrice?.PriceWithDelivery ?? 0,
                        CalculatedPrice = lowestCompetitorPrice?.CalculatedPrice ?? 0,
                        CalculatedPriceWithDelivery = lowestCompetitorPrice?.CalculatedPriceWithDelivery ?? 0,
                        // Your own store information
                        MyStoreName = ourPrice?.StoreName,
                        OurCalculatedPrice = ourPrice?.CalculatedPrice ?? 0,
                        OurRegionName = ourRegionName,
                        // Region info
                        RegionId = lowestCompetitorPrice?.RegionId ?? 0,
                        RegionName = regionName,
                        // Flags and product details
                        FlagIds = flagIds ?? new List<int>(),
                        MainUrl = ourPrice?.Product?.MainUrl ?? lowestCompetitorPrice?.Product?.MainUrl,
                        Product = ourPrice?.Product ?? lowestCompetitorPrice?.Product
                    };
                })
                .ToList();

            var viewModel = new SafariReportAnalysisViewModel
            {
                ReportName = report.ReportName,
                CreatedDate = report.CreatedDate,
                StoreName = report.Store?.StoreName,
                ProductPrices = productPrices,
            };

            ViewBag.ReportId = reportId;
            ViewBag.Flags = storeFlags;

            return View("~/Views/Panel/Safari/SafariReportAnalysis.cshtml", viewModel);
        }


        [HttpGet]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        public async Task<IActionResult> ProductPriceDetails(int reportId, int productId, int? regionId = null)
        {
            if (reportId == 0 || productId == 0)
            {
                return NotFound("Nieprawidłowe identyfikatory raportu lub produktu.");
            }

            var productPrices = await _context.GlobalPriceReports
                .Where(gpr => gpr.PriceSafariReportId == reportId && gpr.ProductId == productId)
                .Include(gpr => gpr.Product)
                .Include(gpr => gpr.PriceSafariReport)
                    .ThenInclude(psr => psr.Store)
                .Include(gpr => gpr.Region)
                .OrderBy(gpr => gpr.CalculatedPrice)
                .ToListAsync();

            var product = productPrices.FirstOrDefault()?.Product;

            var report = await _context.PriceSafariReports
                .FirstOrDefaultAsync(rep => rep.ReportId == reportId);

            if (product == null)
            {
                Console.WriteLine("Brak produktu w wynikach zapytania.");
                return NotFound("Brak informacji o produkcie.");
            }

            // Get the selected region name if regionId is provided
            string selectedRegionName = null;
            if (regionId.HasValue)
            {
                var selectedRegion = await _context.Regions.FirstOrDefaultAsync(r => r.RegionId == regionId.Value);
                if (selectedRegion != null)
                {
                    selectedRegionName = selectedRegion.Name;
                }
            }

            // Pass selectedRegionName to the view via ViewBag
            ViewBag.SelectedRegionName = selectedRegionName;

            var myStoreName = productPrices.FirstOrDefault()?.PriceSafariReport?.Store?.StoreName ?? "Unknown Store";

            var viewModel = new ProductPriceDetailsViewModel
            {
                ProductName = product.ProductName ?? "Brak nazwy produktu",
                MyStore = myStoreName,
                ProductImg = product?.MainUrl,
                ReportId = reportId,
                RaportName = report.ReportName,
                GoogleProductUrl = product?.GoogleUrl,
                Prices = productPrices.Select(gpr =>
                {
                    return new PriceDetailsViewModel
                    {
                        StoreName = gpr.StoreName,
                        RegionName = gpr.Region?.Name ?? "Brak regionu",
                        Price = gpr.Price,
                        CalculatedPrice = gpr.CalculatedPrice,
                        PriceWithDelivery = gpr.PriceWithDelivery,
                        CalculatedPriceWithDelivery = gpr.CalculatedPriceWithDelivery,
                        Currency = gpr.Region.Currency,
                        OfferUrl = gpr.OfferUrl ?? "Brak URL oferty"
                    };
                }).ToList()
            };

            return View("~/Views/Panel/Safari/ProductPriceDetails.cshtml", viewModel);
        }




        [HttpGet]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        public async Task<IActionResult> Index(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (storeId == 0)
            {
                return NotFound("Store ID not provided.");
            }

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound("Store not found.");
            }

            // Pobieranie produktów związanych ze sklepem
            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.OnGoogle == true && p.FoundOnGoogle == true)
                .Include(p => p.ProductFlags)
                .ThenInclude(pf => pf.Flag)
                .ToListAsync();

            var reports = await _context.PriceSafariReports
                .Where(r => r.StoreId == storeId && (r.Prepared == null || r.Prepared == false))
                .ToListAsync();

            // Przekazanie danych do widoku
            ViewBag.Reports = reports;
            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreId = storeId;

            return View("~/Views/Panel/Safari/Index.cshtml", products);
        }







        [HttpGet]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        public async Task<IActionResult> CreateReport(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Pobieranie listy regionów
            var regions = await _context.Regions.ToListAsync();
            ViewBag.Regions = regions;
            ViewBag.StoreId = storeId; // Pass StoreId to the view

            return View("~/Views/Panel/Safari/CreateReport.cshtml");
        }


        [HttpPost]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        public async Task<IActionResult> CreateReport(string reportName, List<int> regionIds, int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(reportName) || regionIds == null || regionIds.Count == 0)
            {
                return BadRequest("Nazwa raportu i regiony są wymagane.");
            }

            var report = new PriceSafariReport
            {
                ReportName = reportName,
                RegionIds = regionIds,
                StoreId = storeId,
                CreatedDate = DateTime.Now,
                Prepared = null // Raport nie jest jeszcze przygotowany
            };

            _context.PriceSafariReports.Add(report);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { storeId });
        }

        [HttpPost]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        public async Task<IActionResult> StartReportPreparation(int reportId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var report = await _context.PriceSafariReports.FindAsync(reportId);
            if (report == null)
            {
                return Json(new { success = false, message = "Raport nie istnieje." });
            }

            // Sprawdź, czy istnieje inny raport w przygotowaniu dla tego sklepu
            var existingPreparingReport = await _context.PriceSafariReports
                .FirstOrDefaultAsync(r => r.StoreId == report.StoreId && r.Prepared == false);

            if (existingPreparingReport != null)
            {
                return Json(new { success = false, message = "Inny raport jest w trakcie przygotowania. Nie można zlecić kolejnego." });
            }

            report.Prepared = false; // Ustaw status na "w trakcie przygotowania"
            _context.PriceSafariReports.Update(report);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Raport został zlecony do przygotowania." });
        }


        [HttpPost]
        public async Task<IActionResult> ToggleProductAssignment(int reportId, int productId, bool isAssigned)
        {
           

            if (reportId == 0 || productId == 0)
            {
                return Json(new { success = false, message = "Niepoprawne dane." });
            }

            var report = await _context.PriceSafariReports.FindAsync(reportId);
            if (report == null)
            {
                return Json(new { success = false, message = "Raport nie istnieje." });
            }

            if (isAssigned)
            {
                if (!report.ProductIds.Contains(productId))
                {
                    report.ProductIds.Add(productId);
                }
            }
            else
            {
                report.ProductIds.Remove(productId);
            }

            _context.PriceSafariReports.Update(report);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }




        [HttpPost]
        public async Task<IActionResult> ToggleBatchProductAssignment([FromBody] BatchAssignmentRequest request)
        {


            // Log the incoming request data
            Console.WriteLine($"ToggleBatchProductAssignment called with ReportId: {request.ReportId}, IsAssigned: {request.IsAssigned}, ProductIds: {string.Join(",", request.ProductIds)}");

            if (request.ReportId == 0 || request.ProductIds == null || !request.ProductIds.Any())
            {
                return Json(new { success = false, message = "Niepoprawne dane." });
            }

            var report = await _context.PriceSafariReports.FindAsync(request.ReportId);
            if (report == null)
            {
                return Json(new { success = false, message = "Raport nie istnieje." });
            }

            try
            {
                if (request.IsAssigned)
                {
                    var productsToAdd = request.ProductIds.Except(report.ProductIds).ToList();
                    report.ProductIds.AddRange(productsToAdd);
                }
                else
                {
                    report.ProductIds.RemoveAll(p => request.ProductIds.Contains(p));
                }

                _context.PriceSafariReports.Update(report);
                await _context.SaveChangesAsync();

                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while updating report: {ex.Message}");
                return Json(new { success = false, message = "Wystąpił błąd podczas zapisywania raportu." });
            }
        }

        // Request model
        public class BatchAssignmentRequest
        {
            public int ReportId { get; set; }
            public List<int> ProductIds { get; set; }
            public bool IsAssigned { get; set; }
        }




        [HttpGet]
        public async Task<IActionResult> GetReportProducts(int reportId)
        {
            if (reportId == 0)
            {
                return Json(new { success = false, message = "Niepoprawne dane raportu." });
            }

            var report = await _context.PriceSafariReports.FindAsync(reportId);
            if (report == null)
            {
                return Json(new { success = false, message = "Raport nie istnieje." });
            }

            return Json(new { success = true, productIds = report.ProductIds });
        }



    }
}
