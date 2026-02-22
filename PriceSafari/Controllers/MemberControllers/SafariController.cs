using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using PriceSafari.Attributes;
using PriceSafari.Data;
using PriceSafari.Enums;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using PriceSafari.ViewModels;
using System.Security.Claims;
using System.Threading;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "Member")]
    [RequireUserAccess(UserAccessRequirement.ViewSafari)]
    public class SafariController : Controller
    {

        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly IHubContext<ReportProgressHub> _hubContext;

        public SafariController(PriceSafariContext context, UserManager<PriceSafariUser> userManager, IHubContext<ReportProgressHub> hubContext)
        {
            _context = context;
            _userManager = userManager;
            _hubContext = hubContext;
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
            var lastActionTime = HttpContext.Session.GetString("LastActionTime");
            if (!string.IsNullOrEmpty(lastActionTime))
            {
                var lastActionDateTime = DateTime.Parse(lastActionTime);
                if ((DateTime.Now - lastActionDateTime).TotalSeconds < 2)
                {
                    return BadRequest("Please wait before clicking again.");
                }
            }

            HttpContext.Session.SetString("LastActionTime", DateTime.Now.ToString());

            int totalSteps = 8;
            int currentStep = 0;

            async Task UpdateProgress(string message, int additionalProgressSteps = 1)
            {
                currentStep += additionalProgressSteps;
                int progress = (currentStep * 100) / totalSteps;
                Console.WriteLine($"Sending progress: {progress}% - {message}");
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", message, progress);
            }

            if (reportId == 0)
            {
                return NotFound("Nieprawidłowy identyfikator raportu.");
            }

            var report = await _context.PriceSafariReports
                .AsNoTracking()
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.ReportId == reportId);

            if (report == null)
            {
                return NotFound("Raport nie został znaleziony.");
            }

            await UpdateProgress("Wczytywanie raportu...");

            var globalPriceReports = await _context.GlobalPriceReports
                .AsNoTracking()
                .Where(gpr => gpr.PriceSafariReportId == reportId)
                .Select(gpr => new
                {
                    gpr.ProductId,
                    gpr.PriceSafariReportId,
                    gpr.Price,
                    gpr.PriceWithDelivery,
                    gpr.CalculatedPrice,
                    gpr.CalculatedPriceWithDelivery,
                    gpr.StoreName,
                    gpr.RegionId,
                    gpr.OfferUrl,

                    ProductName = gpr.Product.ProductName,
                    Ean = gpr.Product.Ean,
                    GoogleUrl = gpr.Product.GoogleUrl,
                    MarginPrice = gpr.Product.MarginPrice,
                    MainUrl = gpr.Product.MainUrl
                })
                .ToListAsync();
            await UpdateProgress("Ładowanie produktów ...");

            var storeFlags = await _context.Flags
                .AsNoTracking()
                .Where(f => f.StoreId == report.StoreId && !f.IsMarketplace)
                .ToListAsync();
            await UpdateProgress("Ładowanie flag...");

            var priceValues = await _context.PriceValues
               .AsNoTracking()
               .Where(pv => pv.StoreId == report.StoreId)
               .Select(pv => new { pv.SetSafariPrice1, pv.SetSafariPrice2, pv.UsePriceDiffSafari })
               .FirstOrDefaultAsync();

            if (priceValues == null)
            {
                priceValues = new { SetSafariPrice1 = 2.00m, SetSafariPrice2 = 2.00m, UsePriceDiffSafari = true };
            }
            await UpdateProgress("Ładowanie cen...");

            await UpdateProgress("Ładowanie powiązań produktów z flagami...");

            var relevantFlagIds = storeFlags.Select(f => f.FlagId).ToHashSet();

            var productFlagsDictionary = await _context.ProductFlags
                .AsNoTracking()

                .Where(pf => pf.ProductId.HasValue && relevantFlagIds.Contains(pf.FlagId))
                .GroupBy(pf => pf.ProductId.Value)

                .ToDictionaryAsync(
                    g => g.Key,
                    g => g.Select(pf => pf.FlagId).ToList()
                );

            await UpdateProgress("Ładowanie regionów...");
            var regions = await _context.Regions
                .AsNoTracking()
                .ToDictionaryAsync(r => r.RegionId, r => r.Name);

            ViewBag.Regions = regions;
            ViewBag.RegionId = regionId;

            var productPrices = globalPriceReports
     .GroupBy(gpr => gpr.ProductId)
     .Where(group =>
     {
         // Ten warunek filtruje całe grupy (czy produkt w ogóle ma być pokazany)
         if (regionId.HasValue)
         {
             return group.Any(gpr => gpr.RegionId == regionId.Value && gpr.StoreName.ToLower() != report.Store.StoreName.ToLower());
         }
         else
         {
             return true;
         }
     })
     .Select(group =>
     {
         // --- POPRAWKA TUTAJ ---
         // Jeśli wybrano region, zliczamy tylko oferty z tego regionu.
         // Jeśli nie wybrano regionu (null), zliczamy wszystkie oferty w grupie.
         int offerCount = regionId.HasValue
             ? group.Count(gpr => gpr.RegionId == regionId.Value)
             : group.Count();
         // ----------------------

         var ourPrice = group.FirstOrDefault(gpr => gpr.StoreName.ToLower() == report.Store.StoreName.ToLower());
         var firstInGroup = group.FirstOrDefault();

         IEnumerable<dynamic> competitorPrices;

         if (regionId.HasValue)
         {
             competitorPrices = group.Where(gpr => gpr.RegionId == regionId.Value && gpr.StoreName.ToLower() != report.Store.StoreName.ToLower());
         }
         else
         {
             competitorPrices = group.Where(gpr => gpr.StoreName.ToLower() != report.Store.StoreName.ToLower());
         }

         var lowestCompetitorPrice = competitorPrices.OrderBy(gpr => gpr.CalculatedPrice).FirstOrDefault();

         int productId = ourPrice?.ProductId ?? lowestCompetitorPrice?.ProductId ?? firstInGroup?.ProductId ?? 0;
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
             ProductName = ourPrice?.ProductName ?? firstInGroup?.ProductName,
             GoogleUrl = ourPrice?.GoogleUrl ?? firstInGroup?.GoogleUrl,
             MarginPrice = ourPrice?.MarginPrice ?? firstInGroup?.MarginPrice,
             MainUrl = ourPrice?.MainUrl ?? firstInGroup?.MainUrl,
             Ean = ourPrice?.Ean ?? firstInGroup?.Ean,
             Price = lowestCompetitorPrice?.Price ?? 0,
             StoreName = lowestCompetitorPrice?.StoreName ?? "Brak konkurencyjnej ceny",
             PriceWithDelivery = lowestCompetitorPrice?.PriceWithDelivery ?? 0,
             CalculatedPrice = lowestCompetitorPrice?.CalculatedPrice ?? 0,
             CalculatedPriceWithDelivery = lowestCompetitorPrice?.CalculatedPriceWithDelivery ?? 0,
             MyStoreName = ourPrice?.StoreName,
             OurCalculatedPrice = ourPrice?.CalculatedPrice ?? 0,
             OurRegionName = ourRegionName,
             RegionId = lowestCompetitorPrice?.RegionId ?? 0,
             RegionName = regionName,
             FlagIds = flagIds ?? new List<int>(),
             OfferCount = offerCount // Przypisujemy obliczoną wyżej wartość
         };
     })
     .ToList();

            await UpdateProgress("Przetwarzanie...", additionalProgressSteps: 1);

            var viewModel = new SafariReportAnalysisViewModel
            {
                ReportName = report.ReportName,
                CreatedDate = report.CreatedDate,
                StoreName = report.Store?.StoreName,
                StoreId = report.Store.StoreId,
                StoreLogo = report.Store?.StoreLogoUrl,
                ProductPrices = productPrices,
                SetSafariPrice1 = priceValues.SetSafariPrice1,
                SetSafariPrice2 = priceValues.SetSafariPrice2,
                UsePriceDiffSafari = priceValues.UsePriceDiffSafari
            };

            ViewBag.ReportId = reportId;
            ViewBag.Flags = storeFlags;

            await UpdateProgress("Finalizacja...");

            return View("~/Views/Panel/Safari/SafariReportAnalysis.cshtml", viewModel);
        }

        [HttpPost]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        public async Task<IActionResult> SaveSafariPriceValues([FromBody] SafariPriceValuesViewModel model)
        {
            if (model == null || model.StoreId <= 0)
            {
                return BadRequest("Invalid store ID or price values.");
            }

            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == model.StoreId)
                .FirstOrDefaultAsync();

            if (priceValues == null)
            {
                priceValues = new PriceValueClass
                {
                    StoreId = model.StoreId,
                    SetSafariPrice1 = model.SetSafariPrice1,
                    SetSafariPrice2 = model.SetSafariPrice2,
                    UsePriceDiffSafari = model.UsePriceDiffSafari

                };
                _context.PriceValues.Add(priceValues);
            }
            else
            {
                priceValues.SetSafariPrice1 = model.SetSafariPrice1;
                priceValues.SetSafariPrice2 = model.SetSafariPrice2;
                priceValues.UsePriceDiffSafari = model.UsePriceDiffSafari;
                _context.PriceValues.Update(priceValues);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Price values updated successfully." });
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

            string selectedRegionName = null;
            if (regionId.HasValue)
            {
                var selectedRegion = await _context.Regions.FirstOrDefaultAsync(r => r.RegionId == regionId.Value);
                if (selectedRegion != null)
                {
                    selectedRegionName = selectedRegion.Name;
                }
            }

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
                        PriceId = gpr.ReportId,
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

        [HttpPost]
        public async Task<IActionResult> DeletePrice([FromBody] DeletePriceModel model)
        {

            if (model == null || model.PriceId <= 0)
            {
                return BadRequest("Nieprawidłowe ID ceny.");
            }

            var priceReport = await _context.GlobalPriceReports
                .Include(p => p.PriceSafariReport)
                .FirstOrDefaultAsync(p => p.ReportId == model.PriceId);

            if (priceReport == null)
            {
                return NotFound($"Cena o ID {model.PriceId} nie istnieje.");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var hasAccess = await _context.UserStores
                .AnyAsync(us => us.UserId == userId && us.StoreId == priceReport.PriceSafariReport.StoreId);

            if (!hasAccess)
            {
                return Forbid("Brak dostępu do tego sklepu.");
            }

            _context.GlobalPriceReports.Remove(priceReport);
            await _context.SaveChangesAsync();

            return Ok($"Cena z ID {model.PriceId} została usunięta.");
        }

        public class DeletePriceModel
        {
            public int PriceId { get; set; }
        }

        [HttpGet]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        [RequireUserAccess(UserAccessRequirement.CreateSafari)]
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

            var reports = await _context.PriceSafariReports
                .Where(r => r.StoreId == storeId && (r.Prepared == null || r.Prepared == false))
                .ToListAsync();

            ViewBag.Reports = reports;
            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreLogo = store.StoreLogoUrl;
            ViewBag.StoreId = storeId;

            return View("~/Views/Panel/Safari/Index.cshtml");
        }

        [HttpGet]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        [RequireUserAccess(UserAccessRequirement.CreateSafari)]
        public async Task<IActionResult> GetProducts(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (storeId == 0)
            {
                return Json(new { success = false, message = "Store ID not provided." });
            }

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return Json(new { success = false, message = "Store not found." });
            }

            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.OnGoogle == true && p.FoundOnGoogle == true)
                .Include(p => p.ProductFlags)
                    .ThenInclude(pf => pf.Flag)
                .ToListAsync();

            var productsData = products.Select(p => new
            {
                productId = p.ProductId,
                productImg = p.MainUrl,
                productNameInStoreForGoogle = p.ProductName,
                ean = p.Ean,
                url = p.Url,
                googleUrl = p.GoogleUrl,
                foundOnGoogle = p.FoundOnGoogle,
                flags = (p.ProductFlags ?? Enumerable.Empty<ProductFlag>()).Select(pf => new
                {
                    Name = pf.Flag.FlagName,
                    Color = pf.Flag.FlagColor
                }).ToList()
            }).ToList();

            return Json(new { success = true, products = productsData });
        }

        [HttpGet]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        [RequireUserAccess(UserAccessRequirement.CreateSafari)]
        public async Task<IActionResult> CreateReport(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var regions = await _context.Regions.ToListAsync();
            ViewBag.Regions = regions;
            ViewBag.StoreId = storeId;

            return View("~/Views/Panel/Safari/CreateReport.cshtml");
        }

        [HttpPost]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        [RequireUserAccess(UserAccessRequirement.CreateSafari)]
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
                Prepared = null
            };

            _context.PriceSafariReports.Add(report);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index", new { storeId });
        }

        [HttpPost]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        [RequireUserAccess(UserAccessRequirement.CreateSafari)]
        public async Task<IActionResult> StartReportPreparation(int reportId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var report = await _context.PriceSafariReports.FindAsync(reportId);
            if (report == null)
            {
                return Json(new { success = false, message = "Raport nie istnieje." });
            }

            var existingPreparingReport = await _context.PriceSafariReports
                .FirstOrDefaultAsync(r => r.StoreId == report.StoreId && r.Prepared == false);

            if (existingPreparingReport != null)
            {
                return Json(new { success = false, message = "Inny raport jest w trakcie przygotowania. Nie można zlecić kolejnego." });
            }

            report.Prepared = false;
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

        public class BatchAssignmentRequest
        {
            public int ReportId { get; set; }
            public List<int> ProductIds { get; set; }
            public bool IsAssigned { get; set; }
        }

        [HttpGet]
        [RequireUserAccess(UserAccessRequirement.CreateSafari)]
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

            var allRegions = await _context.Regions.ToListAsync();

            var reportRegionIds = report.RegionIds ?? new List<int>();

            var regions = allRegions
                .Where(r => reportRegionIds.Contains(r.RegionId))
                .Select(r => new
                {
                    regionId = r.RegionId,
                    name = r.Name,
                    countryCode = r.CountryCode
                })
                .ToList();

            var productIds = report.ProductIds ?? new List<int>();

            return Json(new
            {
                success = true,
                productIds = productIds,
                regions = regions
            });
        }


        [HttpGet]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        public async Task<IActionResult> ExportToExcel(int reportId, int? regionId = null)
        {
            if (reportId == 0) return NotFound("Nieprawidłowy identyfikator raportu.");

            // 1. Pobranie raportu i danych sklepu
            var report = await _context.PriceSafariReports
                .AsNoTracking()
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.ReportId == reportId);

            if (report == null) return NotFound("Raport nie został znaleziony.");

            var myStoreNameLower = report.Store?.StoreName?.ToLower().Trim() ?? "";

            // 2. Pobranie danych GlobalPriceReports wraz z danymi Produktu i Regionu
            var globalPriceReports = await _context.GlobalPriceReports
                .AsNoTracking()
                .Include(gpr => gpr.Product)
                .Include(gpr => gpr.Region)
                .Where(gpr => gpr.PriceSafariReportId == reportId)
                .ToListAsync();

            if (!globalPriceReports.Any()) return Content("Brak danych do eksportu dla tego raportu.");

            // 3. Grupowanie i obliczanie rankingu (analogicznie do logiki z widoku)
            var groupedData = globalPriceReports
                .GroupBy(gpr => new { gpr.Product.ProductId, gpr.Product.ProductName, gpr.Product.Ean, gpr.Product.ExternalId })
                .Where(group =>
                {
                    // Jeśli wybrano region, interesują nas tylko produkty, w których jest konkurencja z tego regionu
                    if (regionId.HasValue)
                        return group.Any(gpr => gpr.RegionId == regionId.Value && (gpr.StoreName ?? "").ToLower().Trim() != myStoreNameLower);
                    return true;
                })
                .Select(g =>
                {
                    var allOffers = g.Select(x => new
                    {
                        Store = x.StoreName ?? "Nieznany",
                        Country = x.Region?.Name ?? "Brak",
                        Currency = x.Region?.Currency ?? "N/A",
                        OriginalPrice = x.Price,
                        CalculatedPrice = x.CalculatedPrice,
                        Url = x.OfferUrl ?? x.Product?.GoogleUrl ?? "Brak URL",
                        IsMe = x.StoreName != null && x.StoreName.ToLower().Trim() == myStoreNameLower,
                        RegionId = x.RegionId
                    }).ToList();

                    var myOffer = allOffers.FirstOrDefault(x => x.IsMe);
                    var competitors = allOffers.Where(x => !x.IsMe);

                    // Filtrowanie konkurencji jeśli wybrano konkretny region
                    if (regionId.HasValue)
                    {
                        competitors = competitors.Where(x => x.RegionId == regionId.Value);
                    }

                    var sortedCompetitors = competitors.OrderBy(x => x.CalculatedPrice).ToList();
                    decimal minMarketPrice = allOffers.Any() ? allOffers.Min(x => x.CalculatedPrice) : 0;

                    string positionString = "-";
                    int statusColorCode = 0;
                    decimal? diffToLowest = null;

                    if (myOffer != null)
                    {
                        int cheaperCount = allOffers.Count(x => x.CalculatedPrice < myOffer.CalculatedPrice);
                        int myRank = cheaperCount + 1;
                        int totalOffers = allOffers.Count;
                        positionString = $"{myRank} z {totalOffers}";

                        if (sortedCompetitors.Any())
                        {
                            decimal lowestCompetitor = sortedCompetitors.First().CalculatedPrice;
                            diffToLowest = myOffer.CalculatedPrice - lowestCompetitor;
                        }

                        if (allOffers.Any() && myOffer.CalculatedPrice == minMarketPrice)
                        {
                            int othersWithSamePrice = allOffers.Count(x => x.CalculatedPrice == minMarketPrice && !x.IsMe);
                            statusColorCode = othersWithSamePrice == 0 ? 1 : 2;
                        }
                        else
                        {
                            statusColorCode = 3;
                        }
                    }

                    return new
                    {
                        Product = g.Key,
                        MyOffer = myOffer,
                        DiffToLowest = diffToLowest,
                        Position = positionString,
                        ColorCode = statusColorCode,
                        Competitors = sortedCompetitors
                    };
                })
                .OrderBy(x => x.Product.ProductName)
                .ToList();

            // 4. Tworzenie Excela (NPOI)
            using (var workbook = new XSSFWorkbook())
            {
                var sheet = workbook.CreateSheet("Eksport Safari");

                // Style NPOI
                var headerStyle = workbook.CreateCellStyle();
                var headerFont = workbook.CreateFont();
                headerFont.IsBold = true;
                headerStyle.SetFont(headerFont);

                var currencyStyle = workbook.CreateCellStyle();
                currencyStyle.DataFormat = workbook.CreateDataFormat().GetFormat("#,##0.00");

                var styleGreen = workbook.CreateCellStyle();
                styleGreen.CloneStyleFrom(currencyStyle);
                styleGreen.FillForegroundColor = IndexedColors.LightGreen.Index;
                styleGreen.FillPattern = FillPattern.SolidForeground;

                var styleLightGreen = workbook.CreateCellStyle();
                styleLightGreen.CloneStyleFrom(currencyStyle);
                styleLightGreen.FillForegroundColor = IndexedColors.LemonChiffon.Index;
                styleLightGreen.FillPattern = FillPattern.SolidForeground;

                var styleRed = workbook.CreateCellStyle();
                styleRed.CloneStyleFrom(currencyStyle);
                styleRed.FillForegroundColor = IndexedColors.Rose.Index;
                styleRed.FillPattern = FillPattern.SolidForeground;

                var headerRow = sheet.CreateRow(0);
                int colIndex = 0;

                // Stałe kolumny naszego sklepu
                string[] staticHeaders = {
            "ID Produktu", "Nazwa Produktu", "EAN",
            "Moja Cena (Przeliczona)", "Moja Cena (Org)", "Moja Waluta", "Mój Kraj",
            "Pozycja", "Różnica (Przeliczona)"
        };

                foreach (var h in staticHeaders) headerRow.CreateCell(colIndex++).SetCellValue(h);

                // Dynamiczne generowanie kolumn dla ofert konkurencji (max żeby nie rozbić excela to 60)
                int maxCompetitors = groupedData.Any() ? groupedData.Max(x => x.Competitors.Count) : 0;
                

                for (int i = 1; i <= maxCompetitors; i++)
                {
                    headerRow.CreateCell(colIndex++).SetCellValue($"Sklep {i}");
                    headerRow.CreateCell(colIndex++).SetCellValue($"Kraj {i}");
                    headerRow.CreateCell(colIndex++).SetCellValue($"Waluta {i}");
                    headerRow.CreateCell(colIndex++).SetCellValue($"Cena Org. {i}");
                    headerRow.CreateCell(colIndex++).SetCellValue($"Cena Przelicz. {i}");
                    headerRow.CreateCell(colIndex++).SetCellValue($"URL {i}");
                }

                for (int i = 0; i < colIndex; i++) headerRow.GetCell(i).CellStyle = headerStyle;

                // Wypełnianie wierszy
                int rowIndex = 1;
                foreach (var item in groupedData)
                {
                    var row = sheet.CreateRow(rowIndex++);
                    colIndex = 0;

                    row.CreateCell(colIndex++).SetCellValue(item.Product.ExternalId?.ToString() ?? "");
                    row.CreateCell(colIndex++).SetCellValue(item.Product.ProductName ?? "");
                    row.CreateCell(colIndex++).SetCellValue(item.Product.Ean ?? "");

                    var cellMyCalcPrice = row.CreateCell(colIndex++);
                    var cellMyOrgPrice = row.CreateCell(colIndex++);
                    var cellMyCurrency = row.CreateCell(colIndex++);
                    var cellMyCountry = row.CreateCell(colIndex++);

                    if (item.MyOffer != null)
                    {
                        cellMyCalcPrice.SetCellValue((double)item.MyOffer.CalculatedPrice);
                        cellMyOrgPrice.SetCellValue((double)item.MyOffer.OriginalPrice);
                        cellMyCurrency.SetCellValue(item.MyOffer.Currency);
                        cellMyCountry.SetCellValue(item.MyOffer.Country);

                        if (item.ColorCode == 1) cellMyCalcPrice.CellStyle = styleGreen;
                        else if (item.ColorCode == 2) cellMyCalcPrice.CellStyle = styleLightGreen;
                        else if (item.ColorCode == 3) cellMyCalcPrice.CellStyle = styleRed;
                        else cellMyCalcPrice.CellStyle = currencyStyle;

                        cellMyOrgPrice.CellStyle = currencyStyle;
                    }
                    else
                    {
                        cellMyCalcPrice.SetCellValue("-");
                        cellMyOrgPrice.SetCellValue("-");
                        cellMyCurrency.SetCellValue("-");
                        cellMyCountry.SetCellValue("-");
                    }

                    row.CreateCell(colIndex++).SetCellValue(item.Position);

                    var cellDiff = row.CreateCell(colIndex++);
                    if (item.DiffToLowest.HasValue)
                    {
                        cellDiff.SetCellValue((double)item.DiffToLowest.Value);
                        cellDiff.CellStyle = currencyStyle;
                    }
                    else
                    {
                        cellDiff.SetCellValue("");
                    }

                    // Uzupełnianie ofert konkurencji w pętli dla każdego dynamicznego bloku
                    for (int i = 0; i < maxCompetitors; i++)
                    {
                        if (i < item.Competitors.Count)
                        {
                            var comp = item.Competitors[i];
                            row.CreateCell(colIndex++).SetCellValue(comp.Store);
                            row.CreateCell(colIndex++).SetCellValue(comp.Country);
                            row.CreateCell(colIndex++).SetCellValue(comp.Currency);

                            var cellOrgPrice = row.CreateCell(colIndex++);
                            cellOrgPrice.SetCellValue((double)comp.OriginalPrice);
                            cellOrgPrice.CellStyle = currencyStyle;

                            var cellCalcPrice = row.CreateCell(colIndex++);
                            cellCalcPrice.SetCellValue((double)comp.CalculatedPrice);
                            cellCalcPrice.CellStyle = currencyStyle;

                            row.CreateCell(colIndex++).SetCellValue(comp.Url);
                        }
                        else
                        {
                            // Zostawiamy puste komórki jeśli brakuje tylu konkurentów u tego produktu
                            colIndex += 6;
                        }
                    }
                }

                // Auto-sizing kluczowych kolumn (dla płynniejszego działania nie formatujemy wszystkich)
                sheet.AutoSizeColumn(0);
                sheet.AutoSizeColumn(1);
                sheet.AutoSizeColumn(2);

                using (var stream = new MemoryStream())
                {
                    workbook.Write(stream);
                    var content = stream.ToArray();
                    var fileName = $"Safari_Raport_{report.ReportName}_{DateTime.Now:yyyy-MM-dd_HHmm}.xlsx";
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
        }

    }
}