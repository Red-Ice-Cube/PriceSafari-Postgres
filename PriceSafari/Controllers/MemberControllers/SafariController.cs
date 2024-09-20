using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using System.Security.Claims;

namespace PriceSafari.Controllers
{
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
        public async Task<IActionResult> StoreReports(int storeId)
        {
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
                .ToListAsync();

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreId = storeId;

            return View("~/Views/Panel/Safari/PriceSafariReport.cshtml", reports);
        }

        [HttpGet]
        public async Task<IActionResult> SafariReportAnalysis(int reportId)
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
                .OrderBy(gpr => gpr.CalculatedPrice) 
                .ToListAsync();

           
            var storeName = report.Store?.StoreName?.ToLower();

            
            var productPrices = globalPriceReports
                .GroupBy(gpr => gpr.ProductId)
                .Select(group =>
                {
                    var lowestPrice = group.OrderBy(gpr => gpr.CalculatedPrice).FirstOrDefault(); 
                    var ourPrice = group.FirstOrDefault(gpr => gpr.StoreName.ToLower() == storeName); 

                    return new ProductPriceViewModel
                    {
                        ProductId = lowestPrice.ProductId,
                        ProductName = lowestPrice?.Product?.ProductName,
                        GoogleUrl = lowestPrice?.Product?.GoogleUrl,
                        Price = lowestPrice?.Price ?? 0,
                        PriceWithDelivery = lowestPrice?.PriceWithDelivery ?? 0,
                        CalculatedPrice = lowestPrice?.CalculatedPrice ?? 0,
                        CalculatedPriceWithDelivery = lowestPrice?.CalculatedPriceWithDelivery ?? 0,
                        StoreName = ourPrice?.StoreName,                    
                        RegionId = lowestPrice?.RegionId ?? 0,
                        OurCalculatedPrice = ourPrice?.CalculatedPrice ?? 0 
                    };
                })
                .ToList();

            // Tworzymy widok modelu na podstawie pobranych danych
            var viewModel = new SafariReportAnalysisViewModel
            {
                ReportName = report.ReportName,
                CreatedDate = report.CreatedDate,
                StoreName = report.Store?.StoreName,  
                ProductPrices = productPrices
            };

      
            ViewBag.ReportId = reportId;

        
            return View("~/Views/Panel/Safari/SafariReportAnalysis.cshtml", viewModel);
        }




        [HttpGet]
        public async Task<IActionResult> ProductPriceDetails(int reportId, int productId)
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

           
            var viewModel = new ProductPriceDetailsViewModel
            {
                ProductName = product.ProductName ?? "Brak nazwy produktu",
                MyStore = product.Store.StoreName,
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
        public async Task<IActionResult> Index(int storeId)
        {
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

            // Pobieranie raportów związanych z danym sklepem
            var reports = await _context.PriceSafariReports
                .Where(r => r.StoreId == storeId)
                .ToListAsync();

            // Przekazanie danych do widoku
            ViewBag.Reports = reports;
            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreId = storeId;


            return View("~/Views/Panel/Safari/Index.cshtml", products);
        }








        [HttpGet]
        public async Task<IActionResult> CreateReport(int storeId)
        {
            // Pobieranie listy regionów
            var regions = await _context.Regions.ToListAsync();
            ViewBag.Regions = regions;
            ViewBag.StoreId = storeId; // Pass StoreId to the view

            return View("~/Views/Panel/Safari/CreateReport.cshtml");
        }


        [HttpPost]
        public async Task<IActionResult> CreateReport(string reportName, List<int> regionIds, int storeId)
        {
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
        public async Task<IActionResult> StartReportPreparation(int reportId)
        {
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
