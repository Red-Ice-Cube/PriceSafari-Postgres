using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NPOI.OOXML.XSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
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
            if (reportId == 0)
                return NotFound("Nieprawidłowy identyfikator raportu.");

            var report = await _context.PriceSafariReports
                .AsNoTracking()
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.ReportId == reportId);

            if (report == null) return NotFound("Raport nie został znaleziony.");

            var regions = await _context.Regions
                .AsNoTracking()
                .OrderBy(r => r.Name)
                .ToDictionaryAsync(r => r.RegionId, r => r.Name);

            var flags = await _context.Flags
                .AsNoTracking()
                .Where(f => f.StoreId == report.StoreId && !f.IsMarketplace)
                .Select(f => new FlagViewModel
                {
                    FlagId = f.FlagId,
                    FlagName = f.FlagName,
                    FlagColor = f.FlagColor,
                    IsMarketplace = f.IsMarketplace
                })
                .ToListAsync();

            var pv = await _context.PriceValues
                .AsNoTracking()
                .Where(p => p.StoreId == report.StoreId)
                .Select(p => new
                {
                    p.SetSafariPrice1,
                    p.SetSafariPrice2,
                    p.UsePriceDiffSafari,
                    p.IdentifierForSimulation
                })
                .FirstOrDefaultAsync();

            ViewBag.ReportId = reportId;
            ViewBag.RegionId = regionId;
            ViewBag.Regions = regions;
            ViewBag.Flags = flags;
            ViewBag.ReportName = report.ReportName;
            ViewBag.CreatedDate = report.CreatedDate;
            ViewBag.StoreId = report.StoreId;
            ViewBag.StoreName = report.Store?.StoreName ?? "";
            ViewBag.StoreLogo = report.Store?.StoreLogoUrl;
            ViewBag.SetSafariPrice1 = pv?.SetSafariPrice1 ?? 2.00m;
            ViewBag.SetSafariPrice2 = pv?.SetSafariPrice2 ?? 2.00m;
            ViewBag.UsePriceDiffSafari = pv?.UsePriceDiffSafari ?? true;
            ViewBag.IdentifierForSimulation = pv?.IdentifierForSimulation ?? "EAN";

            return View("~/Views/Panel/Safari/SafariReportAnalysis.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetSafariReportData(int reportId, int? regionId = null)
        {
            if (reportId == 0) return Json(new { error = "Invalid report ID" });

            var report = await _context.PriceSafariReports
                .AsNoTracking()
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.ReportId == reportId);

            if (report == null) return Json(new { error = "Report not found" });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var hasAccess = await _context.UserStores
                .AnyAsync(us => us.UserId == userId && us.StoreId == report.StoreId);
            if (!hasAccess) return Json(new { error = "No access" });

            var myStoreName = report.Store?.StoreName ?? "";
            var myStoreNameLower = myStoreName.ToLower().Trim();

            var regions = await _context.Regions
                .AsNoTracking()
                .ToDictionaryAsync(
                    r => r.RegionId,
                    r => new { r.Name, r.Currency, r.CountryCode });

            var rawOffers = await _context.GlobalPriceReports
                .AsNoTracking()
                .Where(gpr => gpr.PriceSafariReportId == reportId)
                .Select(gpr => new SafariOfferDto
                {
                    ProductId = gpr.ProductId,
                    Price = gpr.Price,
                    CalculatedPrice = gpr.CalculatedPrice,
                    PriceWithDelivery = gpr.PriceWithDelivery,
                    CalculatedPriceWithDelivery = gpr.CalculatedPriceWithDelivery,
                    StoreName = gpr.StoreName,
                    RegionId = gpr.RegionId,
                    OfferUrl = gpr.OfferUrl,
                    ProductName = gpr.Product.ProductName,
                    Ean = gpr.Product.Ean,
                    ExternalId = gpr.Product.ExternalId,
                    ProducerCode = gpr.Product.ProducerCode,
                    Producer = gpr.Product.Producer,
                    GoogleUrl = gpr.Product.GoogleUrl,
                    MarginPrice = gpr.Product.MarginPrice,
                    MainUrl = gpr.Product.MainUrl
                })
                .ToListAsync();

            var productIds = rawOffers.Select(o => o.ProductId).Distinct().ToList();

            var productFlagsDict = await _context.ProductFlags
                .AsNoTracking()
                .Where(pf => pf.ProductId.HasValue && productIds.Contains(pf.ProductId.Value))
                .GroupBy(pf => pf.ProductId.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            var pv = await _context.PriceValues
                .AsNoTracking()
                .Where(p => p.StoreId == report.StoreId)
                .Select(p => new
                {
                    p.SetSafariPrice1,
                    p.SetSafariPrice2,
                    p.UsePriceDiffSafari,
                    p.IdentifierForSimulation
                })
                .FirstOrDefaultAsync();

            var products = rawOffers
                .GroupBy(o => o.ProductId)
                .Select(g =>
                {
                    var groupList = g.ToList();
                    var first = groupList.First();

                    var myOffer = groupList
                        .FirstOrDefault(o => (o.StoreName ?? "").ToLower().Trim() == myStoreNameLower);

                    IEnumerable<SafariOfferDto> competitorPool = groupList
                        .Where(o => (o.StoreName ?? "").ToLower().Trim() != myStoreNameLower);

                    if (regionId.HasValue)
                        competitorPool = competitorPool.Where(o => o.RegionId == regionId.Value);

                    var competitorList = competitorPool.ToList();

                    if (regionId.HasValue && !competitorList.Any())
                    {
                        if (myOffer == null) return null;
                    }

                    var bestCompetitor = competitorList
                        .OrderBy(c => c.CalculatedPrice)
                        .ThenBy(c => c.StoreName)
                        .FirstOrDefault();

                    int offerCount = regionId.HasValue
                        ? groupList.Count(o => o.RegionId == regionId.Value)
                        : groupList.Count;

                    // unique countries this product appears in
                    var countriesSet = groupList
                        .Where(o => (o.StoreName ?? "").ToLower().Trim() != myStoreNameLower)
                        .Select(o => o.RegionId)
                        .Distinct()
                        .ToList();
                    int countriesCount = countriesSet.Count;

                    decimal? priceDifference = null;
                    decimal? percentageDifference = null;
                    if (myOffer != null && bestCompetitor != null && bestCompetitor.CalculatedPrice > 0)
                    {
                        priceDifference = Math.Round(myOffer.CalculatedPrice - bestCompetitor.CalculatedPrice, 2);
                        percentageDifference = Math.Round(
                            (myOffer.CalculatedPrice - bestCompetitor.CalculatedPrice) / bestCompetitor.CalculatedPrice * 100, 2);
                    }

                    decimal? marginAmount = null;
                    decimal? marginPercentage = null;
                    if (first.MarginPrice.HasValue && myOffer != null)
                    {
                        marginAmount = myOffer.CalculatedPrice - first.MarginPrice.Value;
                        if (first.MarginPrice.Value != 0)
                            marginPercentage = Math.Round(marginAmount.Value / first.MarginPrice.Value * 100, 2);
                    }

                    // [NOWE] Różnica konkurencji vs cena zakupu - krytyczny insight!
                    decimal? bestVsPurchaseDiff = null;
                    decimal? bestVsPurchasePerc = null;
                    bool competitorBelowCost = false;
                    if (first.MarginPrice.HasValue && bestCompetitor != null)
                    {
                        bestVsPurchaseDiff = Math.Round(bestCompetitor.CalculatedPrice - first.MarginPrice.Value, 2);
                        if (first.MarginPrice.Value != 0)
                            bestVsPurchasePerc = Math.Round(bestVsPurchaseDiff.Value / first.MarginPrice.Value * 100, 2);
                        competitorBelowCost = bestCompetitor.CalculatedPrice < first.MarginPrice.Value;
                    }

                    var rankingScope = regionId.HasValue
                        ? groupList.Where(o => o.RegionId == regionId.Value).ToList()
                        : groupList;

                    int myRank = 0;
                    int totalRankOffers = rankingScope.Count;
                    string positionString = "N/A";
                    if (myOffer != null && totalRankOffers > 0)
                    {
                        int cheaperCount = rankingScope.Count(o => o.CalculatedPrice < myOffer.CalculatedPrice);
                        myRank = cheaperCount + 1;
                        positionString = $"{myRank}/{totalRankOffers}";
                    }

                    var offersList = groupList.Select(o => new
                    {
                        storeName = o.StoreName ?? "Nieznany",
                        regionId = o.RegionId,
                        regionName = regions.ContainsKey(o.RegionId) ? regions[o.RegionId].Name : "Brak",
                        countryCode = regions.ContainsKey(o.RegionId) ? regions[o.RegionId].CountryCode : "",
                        currency = regions.ContainsKey(o.RegionId) ? regions[o.RegionId].Currency : "",
                        originalPrice = o.Price,
                        calculatedPrice = o.CalculatedPrice,
                        offerUrl = o.OfferUrl,
                        isMe = (o.StoreName ?? "").ToLower().Trim() == myStoreNameLower,
                        belowCost = first.MarginPrice.HasValue && o.CalculatedPrice < first.MarginPrice.Value
                            && (o.StoreName ?? "").ToLower().Trim() != myStoreNameLower
                    }).ToList();

                    // Liczba ofert poniżej naszej ceny zakupu
                    int offersBelowCost = first.MarginPrice.HasValue
                        ? competitorList.Count(o => o.CalculatedPrice < first.MarginPrice.Value)
                        : 0;

                    productFlagsDict.TryGetValue(g.Key, out var flagIds);

                    return new
                    {
                        productId = g.Key,
                        productName = first.ProductName,
                        ean = first.Ean,
                        externalId = first.ExternalId,
                        producerCode = first.ProducerCode,
                        producer = first.Producer,
                        mainUrl = first.MainUrl,
                        googleUrl = first.GoogleUrl,
                        marginPrice = first.MarginPrice,
                        marginAmount,
                        marginPercentage,

                        myStoreName,
                        myOriginalPrice = myOffer?.Price,
                        myCalculatedPrice = myOffer?.CalculatedPrice,
                        myRegionId = myOffer?.RegionId,
                        myRegionName = (myOffer != null && regions.ContainsKey(myOffer.RegionId))
                            ? regions[myOffer.RegionId].Name : "Polska",
                        myCurrency = (myOffer != null && regions.ContainsKey(myOffer.RegionId))
                            ? regions[myOffer.RegionId].Currency : "PLN",

                        bestStoreName = bestCompetitor?.StoreName,
                        bestOriginalPrice = bestCompetitor?.Price,
                        bestCalculatedPrice = bestCompetitor?.CalculatedPrice,
                        bestRegionId = bestCompetitor?.RegionId,
                        bestRegionName = (bestCompetitor != null && regions.ContainsKey(bestCompetitor.RegionId))
                            ? regions[bestCompetitor.RegionId].Name : null,
                        bestCurrency = (bestCompetitor != null && regions.ContainsKey(bestCompetitor.RegionId))
                            ? regions[bestCompetitor.RegionId].Currency : null,
                        bestOfferUrl = bestCompetitor?.OfferUrl,

                        offerCount,
                        countriesCount,
                        myRank,
                        totalRankOffers,
                        myPosition = positionString,

                        priceDifference,
                        percentageDifference,
                        bestVsPurchaseDiff,
                        bestVsPurchasePerc,
                        competitorBelowCost,
                        offersBelowCost,

                        flagIds = flagIds ?? new List<int>(),
                        allOffers = offersList
                    };
                })
                .Where(p => p != null)
                .ToList();

            return Json(new
            {
                myStoreName,
                products,
                productCount = products.Count,
                setSafariPrice1 = pv?.SetSafariPrice1 ?? 2.00m,
                setSafariPrice2 = pv?.SetSafariPrice2 ?? 2.00m,
                usePriceDiffSafari = pv?.UsePriceDiffSafari ?? true,
                identifierForSimulation = pv?.IdentifierForSimulation ?? "EAN",
                regions = regions.Select(r => new
                {
                    id = r.Key,
                    name = r.Value.Name,
                    currency = r.Value.Currency,
                    countryCode = r.Value.CountryCode
                })
            });
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


        // =====================================================================
        // EKSPORT EXCEL — multi-sheet, BEZ AutoSizeColumn (chroni przed SkiaSharp)
        //
        // Zawartość:
        //  1. Podsumowanie               - kluczowe metryki raportu
        //  2. Przegląd produktów         - tabela master z naszą pozycją
        //  3. Według krajów              - dwie sekcje: surowe + top5/outlier-filtered
        //  4. Najtańsze kraje per produkt - gdzie statystycznie kupić najtaniej
        //  5. Top konkurenci             - sklepy obecne w największej liczbie produktów
        //  6. Wszystkie oferty           - raw dump z czytelnym kolorowaniem vs nasza cena
        //
        // Filtry statystyczne:
        //  - OUTLIER_THRESHOLD = 0.5 → odrzucamy ceny >50% odchyłki od mediany krajowej
        //  - TOP_N_PER_COUNTRY = 5  → bierzemy max 5 najtańszych ofert per (produkt, kraj)
        // =====================================================================

        private const decimal OUTLIER_THRESHOLD = 0.5m;
        private const int TOP_N_PER_COUNTRY = 5;

        [HttpGet]
        [ServiceFilter(typeof(AuthorizeStoreAccessAttribute))]
        public async Task<IActionResult> ExportToExcel(int reportId, int? regionId = null)
        {
            if (reportId == 0) return NotFound("Nieprawidłowy identyfikator raportu.");

            var report = await _context.PriceSafariReports
                .AsNoTracking()
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.ReportId == reportId);

            if (report == null) return NotFound("Raport nie został znaleziony.");

            var myStoreNameLower = report.Store?.StoreName?.ToLower().Trim() ?? "";
            var myStoreName = report.Store?.StoreName ?? "";

            var regions = await _context.Regions
                .AsNoTracking()
                .ToDictionaryAsync(r => r.RegionId, r => new { r.Name, r.Currency, r.CountryCode });

            var globalReports = await _context.GlobalPriceReports
                .AsNoTracking()
                .Include(gpr => gpr.Product)
                .Include(gpr => gpr.Region)
                .Where(gpr => gpr.PriceSafariReportId == reportId)
                .ToListAsync();

            if (!globalReports.Any())
                return Content("Brak danych do eksportu dla tego raportu.");

            string regionFilterName = null;
            if (regionId.HasValue && regions.TryGetValue(regionId.Value, out var rinfo))
                regionFilterName = rinfo.Name;

            using var wb = new XSSFWorkbook();
            var styles = CreateExportStyles(wb);

            // Grupuj po produkcie - to nasze podstawowe dane robocze
            var productGroups = globalReports
                .GroupBy(gpr => new
                {
                    gpr.Product.ProductId,
                    gpr.Product.ProductName,
                    gpr.Product.Ean,
                    gpr.Product.ExternalId,
                    gpr.Product.ProducerCode,
                    gpr.Product.Producer,
                    gpr.Product.MarginPrice
                })
                .Select(g =>
                {
                    var allOffers = g.Select(x => new OfferRow
                    {
                        Store = x.StoreName ?? "Nieznany",
                        Country = x.Region?.Name ?? "Brak",
                        Currency = x.Region?.Currency ?? "N/A",
                        OriginalPrice = x.Price,
                        CalculatedPrice = x.CalculatedPrice,
                        Url = x.OfferUrl ?? "",
                        IsMe = (x.StoreName ?? "").ToLower().Trim() == myStoreNameLower,
                        RegionId = x.RegionId
                    }).ToList();

                    var myOffer = allOffers.FirstOrDefault(x => x.IsMe);
                    IEnumerable<OfferRow> competitors = allOffers.Where(x => !x.IsMe);

                    if (regionId.HasValue)
                        competitors = competitors.Where(x => x.RegionId == regionId.Value);

                    var sortedCompetitors = competitors.OrderBy(x => x.CalculatedPrice).ToList();
                    var bestComp = sortedCompetitors.FirstOrDefault();

                    decimal? diffPLN = null, diffPct = null;
                    if (myOffer != null && bestComp != null)
                    {
                        diffPLN = Math.Round(myOffer.CalculatedPrice - bestComp.CalculatedPrice, 2);
                        if (bestComp.CalculatedPrice != 0)
                            diffPct = Math.Round(diffPLN.Value / bestComp.CalculatedPrice * 100, 2);
                    }

                    decimal? marginAmt = null, marginPct = null;
                    if (g.Key.MarginPrice.HasValue && myOffer != null)
                    {
                        marginAmt = myOffer.CalculatedPrice - g.Key.MarginPrice.Value;
                        if (g.Key.MarginPrice.Value != 0)
                            marginPct = Math.Round(marginAmt.Value / g.Key.MarginPrice.Value * 100, 2);
                    }

                    string position = "-";
                    int rank = 0, totalOffers = 0;
                    if (myOffer != null)
                    {
                        var rankingScope = allOffers.AsEnumerable();
                        if (regionId.HasValue)
                            rankingScope = rankingScope.Where(x => x.RegionId == regionId.Value);

                        var rl = rankingScope.ToList();
                        totalOffers = rl.Count;
                        if (totalOffers > 0)
                        {
                            int cheaper = rl.Count(x => x.CalculatedPrice < myOffer.CalculatedPrice);
                            rank = cheaper + 1;
                            position = $"{rank}/{totalOffers}";
                        }
                    }

                    int colorCode = 0; // 0 = brak naszej oferty, 1 = unikalnie najtańszy, 2 = najtańszy ex-aequo, 3 = drożej
                    if (myOffer != null)
                    {
                        if (sortedCompetitors.Count == 0) colorCode = 1;
                        else
                        {
                            var minComp = sortedCompetitors.Min(x => x.CalculatedPrice);
                            if (myOffer.CalculatedPrice < minComp) colorCode = 1;
                            else if (myOffer.CalculatedPrice == minComp) colorCode = 2;
                            else colorCode = 3;
                        }
                    }

                    return new ProductGroup
                    {
                        ProductId = g.Key.ProductId,
                        ProductName = g.Key.ProductName,
                        Ean = g.Key.Ean,
                        ExternalId = g.Key.ExternalId,
                        ProducerCode = g.Key.ProducerCode,
                        Producer = g.Key.Producer,
                        MarginPrice = g.Key.MarginPrice,
                        AllOffers = allOffers,
                        MyOffer = myOffer,
                        Competitors = sortedCompetitors,
                        BestComp = bestComp,
                        DiffPLN = diffPLN,
                        DiffPct = diffPct,
                        MarginAmt = marginAmt,
                        MarginPct = marginPct,
                        Position = position,
                        Rank = rank,
                        TotalOffers = totalOffers,
                        ColorCode = colorCode
                    };
                })
                .Where(x => x.MyOffer != null || x.Competitors.Any())
                .OrderBy(x => x.ProductName)
                .ToList();

            // ================ ARKUSZ 1: Podsumowanie ================
            BuildSummarySheet(wb, styles, report, myStoreName, regionFilterName, productGroups);

            // ================ ARKUSZ 2: Przegląd produktów ================
            BuildProductOverviewSheet(wb, styles, productGroups);

            // ================ ARKUSZ 3: Według krajów (2 sekcje) ================
            BuildByCountrySheet(wb, styles, globalReports, productGroups, myStoreNameLower);

            // ================ ARKUSZ 4: Najtańsze kraje per produkt (NOWE) ================
            BuildBestCountriesPerProductSheet(wb, styles, productGroups);

            // ================ ARKUSZ 5: Top konkurenci ================
            BuildTopCompetitorsSheet(wb, styles, productGroups);

            // ================ ARKUSZ 6: Wszystkie oferty ================
            BuildAllOffersSheet(wb, styles, productGroups);

            using var stream = new MemoryStream();
            wb.Write(stream);
            var content = stream.ToArray();
            var fileName = $"Safari_Raport_{report.ReportName}_{DateTime.Now:yyyy-MM-dd_HHmm}.xlsx";
            return File(content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // =====================================================================
        // ARKUSZ 1 — PODSUMOWANIE
        // =====================================================================
        private void BuildSummarySheet(
            XSSFWorkbook wb,
            ExportStyles styles,
            PriceSafariReport report,
            string myStoreName,
            string regionFilterName,
            List<ProductGroup> productGroups)
        {
            var sheet = wb.CreateSheet("Podsumowanie");

            int totalProducts = productGroups.Count;
            int withMyOffer = productGroups.Count(p => p.MyOffer != null);
            int weAreCheapest = productGroups.Count(p => p.ColorCode == 1);
            int weAreCheapestEx = productGroups.Count(p => p.ColorCode == 2);
            int weAreExpensive = productGroups.Count(p => p.ColorCode == 3);
            int totalOffers = productGroups.Sum(p => p.AllOffers.Count);
            int countriesCovered = productGroups.SelectMany(p => p.AllOffers).Select(o => o.Country).Distinct().Count();
            int storesTotal = productGroups.SelectMany(p => p.AllOffers.Where(o => !o.IsMe)).Select(o => o.Store).Distinct().Count();

            int r = 0;
            var titleRow = sheet.CreateRow(r++);
            titleRow.HeightInPoints = 28;
            var titleCell = titleRow.CreateCell(0);
            titleCell.SetCellValue($"Raport Safari — {myStoreName}");
            titleCell.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 3));

            r++;
            var infoRow = sheet.CreateRow(r++);
            SetHeaderCell(infoRow, 0, "Nazwa raportu", styles.ProductBlockSubHeader);
            SetCell(infoRow, 1, report.ReportName ?? "", styles.Default);
            var dateRow = sheet.CreateRow(r++);
            SetHeaderCell(dateRow, 0, "Data utworzenia", styles.ProductBlockSubHeader);
            SetCell(dateRow, 1, report.CreatedDate.ToString("dd.MM.yyyy HH:mm"), styles.Default);
            var sNameRow = sheet.CreateRow(r++);
            SetHeaderCell(sNameRow, 0, "Sklep", styles.ProductBlockSubHeader);
            SetCell(sNameRow, 1, myStoreName, styles.Default);
            var regRow = sheet.CreateRow(r++);
            SetHeaderCell(regRow, 0, "Filtr regionu", styles.ProductBlockSubHeader);
            SetCell(regRow, 1, regionFilterName ?? "Wszystkie", styles.Default);

            r++;
            var statsHeader = sheet.CreateRow(r++);
            statsHeader.HeightInPoints = 22;
            var sh = statsHeader.CreateCell(0);
            sh.SetCellValue("Statystyki ogólne");
            sh.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 0, 3));

            void AddStat(string label, string value, ICellStyle valueStyle = null)
            {
                var row = sheet.CreateRow(r++);
                SetHeaderCell(row, 0, label, styles.ProductBlockSubHeader);
                SetCell(row, 1, value, valueStyle ?? styles.Default);
            }

            AddStat("Liczba produktów w raporcie", totalProducts.ToString());
            AddStat("Produkty z naszą ofertą", $"{withMyOffer} / {totalProducts}");
            AddStat("Łączna liczba ofert", totalOffers.ToString());
            AddStat("Liczba krajów w danych", countriesCovered.ToString());
            AddStat("Liczba unikalnych sklepów konkurencji", storesTotal.ToString());

            r++;
            var posHeader = sheet.CreateRow(r++);
            posHeader.HeightInPoints = 22;
            var ph = posHeader.CreateCell(0);
            ph.SetCellValue("Pozycja cenowa Twojego sklepu");
            ph.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 0, 3));

            AddStat("Jesteśmy najtańsi (samodzielnie)", $"{weAreCheapest} produktów", styles.PriceGreen);
            AddStat("Jesteśmy najtańsi (ex-aequo)", $"{weAreCheapestEx} produktów", styles.PriceLightGreen);
            AddStat("Jesteśmy drożsi", $"{weAreExpensive} produktów", styles.PriceRed);

            sheet.SetColumnWidth(0, 45 * 256);
            sheet.SetColumnWidth(1, 30 * 256);
            sheet.SetColumnWidth(2, 18 * 256);
            sheet.SetColumnWidth(3, 18 * 256);
        }

        // =====================================================================
        // ARKUSZ 2 — PRZEGLĄD PRODUKTÓW
        // =====================================================================
        private void BuildProductOverviewSheet(XSSFWorkbook wb, ExportStyles styles, List<ProductGroup> productGroups)
        {
            var sheet = wb.CreateSheet("Przegląd produktów");

            int r = 0;
            var hr = sheet.CreateRow(r++);
            hr.HeightInPoints = 28;

            string[] headers = {
                "ID", "EAN", "SKU", "Marka", "Nazwa produktu",
                "Cena zakupu",
                "Moja cena (PLN)", "Moja waluta", "Mój kraj",
                "Pozycja",
                "Najtańsza konkurencja (PLN)", "Najtańsza waluta",
                "Najtańszy sklep", "Kraj najt. konkurenta",
                "Różnica vs konkurenta (PLN)", "Różnica vs konkurenta (%)",
                "Marża PLN", "Marża %",
                "Łącznie ofert"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var c = hr.CreateCell(i);
                c.SetCellValue(headers[i]);
                c.CellStyle = styles.HeaderDark;
            }

            foreach (var item in productGroups)
            {
                var row = sheet.CreateRow(r++);
                int col = 0;

                SetCell(row, col++, item.ExternalId?.ToString() ?? "", styles.Default);
                SetCell(row, col++, item.Ean ?? "", styles.Default);
                SetCell(row, col++, item.ProducerCode ?? "", styles.Default);
                SetCell(row, col++, item.Producer ?? "", styles.Default);
                SetCell(row, col++, item.ProductName ?? "", styles.Default);

                SetDecimalCell(row.CreateCell(col++), item.MarginPrice, styles.Currency);

                var myCalcCell = row.CreateCell(col++);
                if (item.MyOffer != null)
                {
                    myCalcCell.SetCellValue((double)item.MyOffer.CalculatedPrice);
                    myCalcCell.CellStyle = item.ColorCode switch
                    {
                        1 => styles.PriceGreen,
                        2 => styles.PriceLightGreen,
                        3 => styles.PriceRed,
                        _ => styles.Currency
                    };
                }
                else
                {
                    myCalcCell.SetCellValue("-");
                    myCalcCell.CellStyle = styles.NoDataCell;
                }

                SetCell(row, col++, item.MyOffer?.Currency ?? "-", styles.Default);
                SetCell(row, col++, item.MyOffer?.Country ?? "-", styles.Default);
                SetCell(row, col++, item.Position, styles.Default);

                SetDecimalCell(row.CreateCell(col++), item.BestComp?.CalculatedPrice, styles.Currency);
                SetCell(row, col++, item.BestComp?.Currency ?? "-", styles.Default);
                SetCell(row, col++, item.BestComp?.Store ?? "-", styles.Default);
                SetCell(row, col++, item.BestComp?.Country ?? "-", styles.Default);

                SetDecimalCell(row.CreateCell(col++), item.DiffPLN, styles.Currency);
                var diffPctCell = row.CreateCell(col++);
                if (item.DiffPct.HasValue)
                {
                    diffPctCell.SetCellValue((double)item.DiffPct.Value);
                    diffPctCell.CellStyle = item.DiffPct.Value > 0 ? styles.PercentRed : styles.PercentGreen;
                }
                else { diffPctCell.SetCellValue("-"); diffPctCell.CellStyle = styles.NoDataCell; }

                SetDecimalCell(row.CreateCell(col++), item.MarginAmt, styles.Currency);
                var marPctCell = row.CreateCell(col++);
                if (item.MarginPct.HasValue)
                {
                    marPctCell.SetCellValue((double)item.MarginPct.Value);
                    marPctCell.CellStyle = item.MarginPct.Value >= 0 ? styles.PercentGreen : styles.PercentRed;
                }
                else { marPctCell.SetCellValue("-"); marPctCell.CellStyle = styles.NoDataCell; }

                var totC = row.CreateCell(col++);
                totC.SetCellValue(item.TotalOffers);
                totC.CellStyle = styles.Default;
            }

            int[] widths = { 12, 14, 14, 18, 50, 14, 14, 10, 14, 12, 18, 12, 22, 16, 18, 18, 14, 12, 12 };
            for (int i = 0; i < widths.Length; i++)
                sheet.SetColumnWidth(i, widths[i] * 256);

            sheet.CreateFreezePane(5, 1);
            if (r > 1)
                sheet.SetAutoFilter(new CellRangeAddress(0, r - 1, 0, headers.Length - 1));
        }

        // =====================================================================
        // ARKUSZ 3 — WEDŁUG KRAJÓW (2 sekcje)
        //   Sekcja A: wszystkie oferty konkurencji
        //   Sekcja B: tylko top-5 najtańszych ofert per (produkt, kraj),
        //            po wykluczeniu cen odbiegających od mediany krajowej o >50%
        // =====================================================================
        private void BuildByCountrySheet(
            XSSFWorkbook wb,
            ExportStyles styles,
            List<GlobalPriceReport> globalReports,
            List<ProductGroup> productGroups,
            string myStoreNameLower)
        {
            var sheet = wb.CreateSheet("Według krajów");

            // === Sekcja A: surowe dane ===
            var allCompetitorOffers = globalReports
                .Where(g => (g.StoreName ?? "").ToLower().Trim() != myStoreNameLower)
                .Select(g => new
                {
                    ProductId = g.ProductId,
                    Country = g.Region?.Name ?? "Brak",
                    StoreName = g.StoreName ?? "Nieznany",
                    CalculatedPrice = g.CalculatedPrice
                })
                .ToList();

            var byCountryAll = allCompetitorOffers
                .GroupBy(o => o.Country)
                .Select(g => ComputeCountryStats(
                    g.Key,
                    g.Select(x => (x.ProductId, x.StoreName, x.CalculatedPrice)),
                    productGroups))
                .OrderByDescending(x => x.TotalOffers)
                .ToList();

            // === Sekcja B: top-5 per (produkt, kraj) z filtrem outlierów ===
            var representativeOffers = productGroups
                .SelectMany(pg => pg.Competitors
                    .GroupBy(c => c.Country)
                    .SelectMany(g =>
                    {
                        var inCountry = g.ToList();
                        var filtered = FilterPriceOutliers(inCountry, x => x.CalculatedPrice, OUTLIER_THRESHOLD);
                        return filtered
                            .OrderBy(x => x.CalculatedPrice)
                            .Take(TOP_N_PER_COUNTRY)
                            .Select(x => new
                            {
                                ProductId = pg.ProductId,
                                Country = x.Country,
                                StoreName = x.Store,
                                CalculatedPrice = x.CalculatedPrice
                            });
                    }))
                .ToList();

            var byCountryTop5 = representativeOffers
                .GroupBy(o => o.Country)
                .Select(g => ComputeCountryStats(
                    g.Key,
                    g.Select(x => (x.ProductId, x.StoreName, x.CalculatedPrice)),
                    productGroups))
                .OrderByDescending(x => x.TotalOffers)
                .ToList();

            // === Render ===
            int r = 0;

            // Sekcja A header
            var titleA = sheet.CreateRow(r++);
            titleA.HeightInPoints = 24;
            var tcA = titleA.CreateCell(0);
            tcA.SetCellValue("Sekcja A: Wszystkie oferty konkurencji według krajów");
            tcA.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 0, 7));

            r++;
            r = WriteCountryStatsTable(sheet, styles, byCountryAll, r);

            r += 2;

            // Sekcja B header
            var titleB = sheet.CreateRow(r++);
            titleB.HeightInPoints = 24;
            var tcB = titleB.CreateCell(0);
            tcB.SetCellValue($"Sekcja B: Tylko top {TOP_N_PER_COUNTRY} najtańszych ofert per (produkt, kraj) — z filtracją outlierów");
            tcB.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 0, 7));

            var descRow = sheet.CreateRow(r++);
            descRow.HeightInPoints = 42;
            var desc = descRow.CreateCell(0);
            desc.SetCellValue(
                $"Statystyki tej sekcji liczone są wyłącznie z max {TOP_N_PER_COUNTRY} najtańszych ofert konkurencji per (produkt, kraj), " +
                $"po odrzuceniu cen odbiegających o ponad {(int)(OUTLIER_THRESHOLD * 100)}% od mediany krajowej dla danego produktu. " +
                "Dzięki temu pojedyncze, absurdnie tanie lub drogie oferty nie zaburzają obrazu konkurencji w danym kraju.");
            var wrap = wb.CreateCellStyle(); wrap.WrapText = true; wrap.VerticalAlignment = VerticalAlignment.Top;
            desc.CellStyle = wrap;
            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 0, 7));

            r++;
            WriteCountryStatsTable(sheet, styles, byCountryTop5, r);

            int[] widths = { 24, 14, 22, 22, 16, 16, 18, 24 };
            for (int i = 0; i < widths.Length; i++) sheet.SetColumnWidth(i, widths[i] * 256);
        }

        private CountryStat ComputeCountryStats(
            string country,
            IEnumerable<(int ProductId, string StoreName, decimal CalculatedPrice)> offers,
            List<ProductGroup> productGroups)
        {
            var list = offers.ToList();
            int totalOffers = list.Count;
            int productCount = list.Select(o => o.ProductId).Distinct().Count();
            int storeCount = list.Select(o => o.StoreName).Distinct().Count();

            var pgById = productGroups.ToDictionary(p => p.ProductId);

            var matched = list
                .Where(o => pgById.ContainsKey(o.ProductId)
                            && pgById[o.ProductId].MyOffer != null
                            && pgById[o.ProductId].MyOffer.CalculatedPrice > 0)
                .Select(o => new
                {
                    Offer = o,
                    MyPrice = pgById[o.ProductId].MyOffer.CalculatedPrice
                })
                .ToList();

            int cheaperThanUs = matched.Count(x => x.Offer.CalculatedPrice < x.MyPrice);
            int moreExpensive = matched.Count(x => x.Offer.CalculatedPrice > x.MyPrice);

            decimal avgPrice = list.Any() ? Math.Round(list.Average(x => x.CalculatedPrice), 2) : 0;
            decimal? avgDiffPct = matched.Any()
                ? Math.Round(matched.Average(x => (x.Offer.CalculatedPrice - x.MyPrice) / x.MyPrice * 100), 2)
                : (decimal?)null;

            return new CountryStat
            {
                Country = country,
                TotalOffers = totalOffers,
                ProductCount = productCount,
                StoreCount = storeCount,
                CheaperThanUs = cheaperThanUs,
                MoreExpensive = moreExpensive,
                AvgPrice = avgPrice,
                AvgDiffPct = avgDiffPct
            };
        }

        private int WriteCountryStatsTable(ISheet sheet, ExportStyles styles, List<CountryStat> rows, int startRow)
        {
            int r = startRow;
            var hr = sheet.CreateRow(r++);
            string[] headers = {
                "Kraj", "Łącznie ofert", "Produktów (unikatowych)", "Sklepów (unikatowych)",
                "Tańszych od nas", "Droższych od nas",
                "Średnia cena (PLN)", "Średnia różnica vs nas (%)"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = hr.CreateCell(i);
                c.SetCellValue(headers[i]);
                c.CellStyle = styles.HeaderDark;
            }

            foreach (var c in rows)
            {
                var row = sheet.CreateRow(r++);
                int col = 0;
                SetCell(row, col++, c.Country, styles.Default);
                row.CreateCell(col++).SetCellValue(c.TotalOffers);
                row.CreateCell(col++).SetCellValue(c.ProductCount);
                row.CreateCell(col++).SetCellValue(c.StoreCount);

                var cheaperCell = row.CreateCell(col++);
                cheaperCell.SetCellValue(c.CheaperThanUs);
                cheaperCell.CellStyle = c.CheaperThanUs > 0 ? styles.CellRedBg : styles.Default;

                var moreCell = row.CreateCell(col++);
                moreCell.SetCellValue(c.MoreExpensive);
                moreCell.CellStyle = c.MoreExpensive > 0 ? styles.CellGreenBg : styles.Default;

                var avgCell = row.CreateCell(col++);
                avgCell.SetCellValue((double)c.AvgPrice);
                avgCell.CellStyle = styles.Currency;

                var diffCell = row.CreateCell(col++);
                if (c.AvgDiffPct.HasValue)
                {
                    diffCell.SetCellValue((double)c.AvgDiffPct.Value);
                    diffCell.CellStyle = c.AvgDiffPct.Value < 0 ? styles.PercentRed : styles.PercentGreen;
                }
                else { diffCell.SetCellValue("-"); diffCell.CellStyle = styles.NoDataCell; }
            }

            return r;
        }

        // =====================================================================
        // ARKUSZ 4 — NAJTAŃSZE KRAJE PER PRODUKT (NOWY)
        //   Dla każdego produktu pokazuje top 5 krajów z najniższą średnią ceną
        //   liczoną z max 5 najtańszych ofert per kraj (po filtrze outlierów >50%
        //   od mediany krajowej dla danego produktu).
        // =====================================================================
        private void BuildBestCountriesPerProductSheet(XSSFWorkbook wb, ExportStyles styles, List<ProductGroup> productGroups)
        {
            var sheet = wb.CreateSheet("Najtańsze kraje per produkt");

            int r = 0;
            var titleRow = sheet.CreateRow(r++);
            titleRow.HeightInPoints = 24;
            var titleC = titleRow.CreateCell(0);
            titleC.SetCellValue("Gdzie statystycznie najtaniej kupić produkt — top 5 krajów");
            titleC.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 18));

            var descRow = sheet.CreateRow(r++);
            descRow.HeightInPoints = 48;
            var desc = descRow.CreateCell(0);
            desc.SetCellValue(
                $"Dla każdego produktu pokazujemy top 5 krajów z najniższą średnią ceną. " +
                $"Średnia liczona z max {TOP_N_PER_COUNTRY} najtańszych ofert per kraj, po odrzuceniu cen odbiegających " +
                $"o ponad {(int)(OUTLIER_THRESHOLD * 100)}% od mediany krajowej dla danego produktu. " +
                "Kraj #1 to ten, w którym produkt jest statystycznie najtańszy w Europie.");
            var wrap = wb.CreateCellStyle(); wrap.WrapText = true; wrap.VerticalAlignment = VerticalAlignment.Top;
            desc.CellStyle = wrap;
            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 0, 18));

            r++;
            var hr = sheet.CreateRow(r++);
            hr.HeightInPoints = 32;

            var headers = new List<string> { "EAN", "Nazwa produktu", "Marka", "Moja cena (PLN)" };
            for (int i = 1; i <= 5; i++)
            {
                headers.Add($"#{i} Kraj");
                headers.Add($"#{i} Średnia top{TOP_N_PER_COUNTRY} (PLN)");
                headers.Add($"#{i} Liczba ofert");
            }

            for (int i = 0; i < headers.Count; i++)
            {
                var c = hr.CreateCell(i);
                c.SetCellValue(headers[i]);
                c.CellStyle = styles.HeaderDark;
            }

            int dataStartRow = r;

            foreach (var pg in productGroups)
            {
                if (!pg.Competitors.Any()) continue;

                var byCountry = pg.Competitors
                    .GroupBy(c => c.Country)
                    .Select(g =>
                    {
                        var allInCountry = g.ToList();
                        var filtered = FilterPriceOutliers(allInCountry, x => x.CalculatedPrice, OUTLIER_THRESHOLD);
                        var top = filtered.OrderBy(x => x.CalculatedPrice).Take(TOP_N_PER_COUNTRY).ToList();

                        if (!top.Any()) return null;

                        return new BestCountryEntry
                        {
                            Country = g.Key,
                            AvgPrice = Math.Round(top.Average(x => x.CalculatedPrice), 2),
                            OffersCount = top.Count
                        };
                    })
                    .Where(x => x != null)
                    .OrderBy(x => x.AvgPrice)
                    .Take(5)
                    .ToList();

                if (!byCountry.Any()) continue;

                var row = sheet.CreateRow(r++);
                int col = 0;
                SetCell(row, col++, pg.Ean ?? "", styles.Default);
                SetCell(row, col++, pg.ProductName ?? "", styles.Default);
                SetCell(row, col++, pg.Producer ?? "", styles.Default);
                SetDecimalCell(row.CreateCell(col++), pg.MyOffer?.CalculatedPrice, styles.BaselineCurrency);

                for (int i = 0; i < 5; i++)
                {
                    if (i < byCountry.Count)
                    {
                        var item = byCountry[i];
                        // Czy kraj #i ma cenę niższą od naszej? Pokoloruj średnią
                        ICellStyle priceStyle;
                        if (pg.MyOffer != null)
                        {
                            if (item.AvgPrice < pg.MyOffer.CalculatedPrice) priceStyle = styles.CellRedBg;
                            else priceStyle = styles.CellGreenBg;
                        }
                        else priceStyle = styles.Currency;

                        // Pierwsza pozycja - dodatkowo highlight kraju
                        var countryStyle = i == 0 ? styles.CellGreenBgBold : styles.Default;

                        SetCell(row, col++, item.Country, countryStyle);
                        var pc = row.CreateCell(col++);
                        pc.SetCellValue((double)item.AvgPrice);
                        // Średnia: oryginalnie kolor zależny od porównania, ale dla #1 nadpisz najjaśniejszym zielonym
                        if (i == 0) pc.CellStyle = styles.PriceGreen;
                        else
                        {
                            var combined = wb.CreateCellStyle();
                            combined.CloneStyleFrom(priceStyle);
                            combined.DataFormat = wb.CreateDataFormat().GetFormat("#,##0.00");
                            pc.CellStyle = combined;
                        }
                        row.CreateCell(col++).SetCellValue(item.OffersCount);
                    }
                    else
                    {
                        SetCell(row, col++, "-", styles.NoDataCell);
                        SetCell(row, col++, "-", styles.NoDataCell);
                        SetCell(row, col++, "-", styles.NoDataCell);
                    }
                }
            }

            int[] widths = { 14, 40, 16, 14, 18, 16, 12, 18, 16, 12, 18, 16, 12, 18, 16, 12, 18, 16, 12 };
            for (int i = 0; i < widths.Length; i++) sheet.SetColumnWidth(i, widths[i] * 256);
            sheet.CreateFreezePane(3, dataStartRow);
            if (r > dataStartRow)
                sheet.SetAutoFilter(new CellRangeAddress(dataStartRow - 1, r - 1, 0, headers.Count - 1));
        }

        // =====================================================================
        // ARKUSZ 5 — TOP KONKURENCI
        // =====================================================================
        private void BuildTopCompetitorsSheet(XSSFWorkbook wb, ExportStyles styles, List<ProductGroup> productGroups)
        {
            var sheet = wb.CreateSheet("Top konkurenci");

            var byCompetitor = productGroups
                .SelectMany(pg => pg.Competitors.Select(c => new { pg, Comp = c }))
                .GroupBy(x => x.Comp.Store)
                .Select(g =>
                {
                    var entries = g.ToList();
                    int totalProducts = entries.Select(e => e.pg.ProductId).Distinct().Count();
                    int cheaperThanUs = entries.Count(e =>
                        e.pg.MyOffer != null && e.Comp.CalculatedPrice < e.pg.MyOffer.CalculatedPrice);

                    var withDiff = entries
                        .Where(e => e.pg.MyOffer != null && e.pg.MyOffer.CalculatedPrice > 0)
                        .ToList();
                    decimal? avgDiffPct = withDiff.Any()
                        ? Math.Round(withDiff.Average(e =>
                            (e.Comp.CalculatedPrice - e.pg.MyOffer.CalculatedPrice) / e.pg.MyOffer.CalculatedPrice * 100), 2)
                        : (decimal?)null;

                    var countries = entries.Select(e => e.Comp.Country).Distinct().ToList();

                    return new
                    {
                        Store = g.Key,
                        Countries = string.Join(", ", countries),
                        CountriesCount = countries.Count,
                        TotalProducts = totalProducts,
                        CheaperThanUs = cheaperThanUs,
                        AvgDiffPct = avgDiffPct
                    };
                })
                .OrderByDescending(x => x.TotalProducts)
                .Take(100)
                .ToList();

            int r = 0;
            var titleRow = sheet.CreateRow(r++);
            titleRow.HeightInPoints = 24;
            var titleC = titleRow.CreateCell(0);
            titleC.SetCellValue("Top 100 konkurentów (sklepy obecne w największej liczbie produktów)");
            titleC.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 5));

            r++;
            var hr = sheet.CreateRow(r++);
            string[] headers = {
                "Sklep", "Kraje", "Liczba krajów",
                "Wspólne produkty", "Tańszy od nas",
                "Średnia różnica vs nas (%)"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = hr.CreateCell(i);
                c.SetCellValue(headers[i]);
                c.CellStyle = styles.HeaderDark;
            }

            foreach (var c in byCompetitor)
            {
                var row = sheet.CreateRow(r++);
                int col = 0;
                SetCell(row, col++, c.Store, styles.Default);
                SetCell(row, col++, c.Countries, styles.Default);
                row.CreateCell(col++).SetCellValue(c.CountriesCount);
                row.CreateCell(col++).SetCellValue(c.TotalProducts);

                var cheaperCell = row.CreateCell(col++);
                cheaperCell.SetCellValue(c.CheaperThanUs);
                cheaperCell.CellStyle = c.CheaperThanUs > 0 ? styles.CellRedBg : styles.Default;

                var diffCell = row.CreateCell(col++);
                if (c.AvgDiffPct.HasValue)
                {
                    diffCell.SetCellValue((double)c.AvgDiffPct.Value);
                    diffCell.CellStyle = c.AvgDiffPct.Value < 0 ? styles.PercentRed : styles.PercentGreen;
                }
                else { diffCell.SetCellValue("-"); diffCell.CellStyle = styles.NoDataCell; }
            }

            int[] widths = { 28, 40, 14, 18, 16, 24 };
            for (int i = 0; i < widths.Length; i++) sheet.SetColumnWidth(i, widths[i] * 256);
            sheet.CreateFreezePane(1, 2);
        }

        // =====================================================================
        // ARKUSZ 6 — WSZYSTKIE OFERTY
        //   Bez "kod kraju" (dane wewnętrzne).
        //   Bez "Poniżej MAP".
        //   Czytelne kolory:
        //     niebieski   = nasza oferta
        //     czerwony bg = konkurent TAŃSZY od nas (zagrożenie)
        //     zielony bg  = konkurent DROŻSZY od nas (przewaga)
        //   Dodano kolumnę "Diff vs nasza (%)".
        // =====================================================================
        private void BuildAllOffersSheet(XSSFWorkbook wb, ExportStyles styles, List<ProductGroup> productGroups)
        {
            var sheet = wb.CreateSheet("Wszystkie oferty");

            int r = 0;

            // Legenda
            var legendRow = sheet.CreateRow(r++);
            legendRow.HeightInPoints = 22;
            var lc = legendRow.CreateCell(0);
            lc.SetCellValue("Legenda kolorów ceny PLN: niebieski = nasza oferta | czerwony = konkurent tańszy od nas | zielony = konkurent droższy od nas");
            var legendStyle = wb.CreateCellStyle();
            var lf = wb.CreateFont(); lf.IsItalic = true;
            legendStyle.SetFont(lf);
            lc.CellStyle = legendStyle;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 10));

            var hr = sheet.CreateRow(r++);
            string[] headers = {
                "EAN", "Nazwa produktu", "Marka",
                "Cena zakupu",
                "Sklep", "Kraj",
                "Cena oryginalna", "Waluta",
                "Cena (PLN)",
                "Diff vs nasza (%)",
                "Nasza?",
                "URL oferty"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = hr.CreateCell(i);
                c.SetCellValue(headers[i]);
                c.CellStyle = styles.HeaderDark;
            }

            foreach (var pg in productGroups)
            {
                foreach (var off in pg.AllOffers.OrderByDescending(o => o.IsMe).ThenBy(o => o.CalculatedPrice))
                {
                    var row = sheet.CreateRow(r++);
                    int col = 0;
                    SetCell(row, col++, pg.Ean ?? "", styles.Default);
                    SetCell(row, col++, pg.ProductName ?? "", styles.Default);
                    SetCell(row, col++, pg.Producer ?? "", styles.Default);

                    var mc = row.CreateCell(col++);
                    if (pg.MarginPrice.HasValue)
                    {
                        mc.SetCellValue((double)pg.MarginPrice.Value);
                        mc.CellStyle = styles.Currency;
                    }
                    else { mc.SetCellValue("-"); mc.CellStyle = styles.NoDataCell; }

                    SetCell(row, col++, off.Store, styles.Default);
                    SetCell(row, col++, off.Country, styles.Default);

                    var op = row.CreateCell(col++);
                    op.SetCellValue((double)off.OriginalPrice);
                    op.CellStyle = styles.Currency;

                    SetCell(row, col++, off.Currency, styles.Default);

                    // Cena PLN — kolorowanie wg porównania z naszą ceną
                    var calcC = row.CreateCell(col++);
                    calcC.SetCellValue((double)off.CalculatedPrice);
                    if (off.IsMe)
                    {
                        calcC.CellStyle = styles.BaselineCurrency;
                    }
                    else if (pg.MyOffer != null)
                    {
                        if (off.CalculatedPrice < pg.MyOffer.CalculatedPrice)
                            calcC.CellStyle = styles.CompetitorCheaperCurrency; // czerwony
                        else if (off.CalculatedPrice > pg.MyOffer.CalculatedPrice)
                            calcC.CellStyle = styles.CompetitorMoreExpensiveCurrency; // zielony
                        else
                            calcC.CellStyle = styles.Currency;
                    }
                    else
                    {
                        calcC.CellStyle = styles.Currency;
                    }

                    // Diff vs nasza (%)
                    var diffCell = row.CreateCell(col++);
                    if (!off.IsMe && pg.MyOffer != null && pg.MyOffer.CalculatedPrice > 0)
                    {
                        decimal diff = Math.Round(
                            (off.CalculatedPrice - pg.MyOffer.CalculatedPrice) / pg.MyOffer.CalculatedPrice * 100, 2);
                        diffCell.SetCellValue((double)diff);
                        diffCell.CellStyle = diff < 0 ? styles.PercentRed
                                            : (diff > 0 ? styles.PercentGreen : styles.Percent);
                    }
                    else
                    {
                        diffCell.SetCellValue("-");
                        diffCell.CellStyle = off.IsMe ? styles.BaselineCell : styles.NoDataCell;
                    }

                    SetCell(row, col++, off.IsMe ? "TAK" : "Nie",
                        off.IsMe ? styles.BaselineCell : styles.Default);

                    SetCell(row, col++, off.Url ?? "", styles.Default);
                }
            }

            int[] widths = { 14, 45, 16, 14, 22, 14, 14, 10, 14, 16, 10, 50 };
            for (int i = 0; i < widths.Length; i++) sheet.SetColumnWidth(i, widths[i] * 256);
            sheet.CreateFreezePane(2, 2);
            if (r > 2)
                sheet.SetAutoFilter(new CellRangeAddress(1, r - 1, 0, headers.Length - 1));
        }

        // =====================================================================
        // FILTR OUTLIERÓW — odrzuca ceny odbiegające od mediany o > pctThreshold
        //   pctThreshold = 0.5  oznacza odrzucenie cen < 50% mediany lub > 150% mediany.
        //   Używamy mediany (a nie średniej), bo mediana jest odporna na outliery.
        //   Dla zbioru ≤ 2 elementów filtracja nie ma sensu — zwracamy oryginał.
        // =====================================================================
        private static List<T> FilterPriceOutliers<T>(
            IEnumerable<T> items,
            Func<T, decimal> priceSelector,
            decimal pctThreshold)
        {
            var list = items.ToList();
            if (list.Count <= 2) return list;

            var sortedPrices = list.Select(priceSelector).OrderBy(p => p).ToList();
            decimal median;
            int n = sortedPrices.Count;
            if (n % 2 == 0)
                median = (sortedPrices[n / 2 - 1] + sortedPrices[n / 2]) / 2m;
            else
                median = sortedPrices[n / 2];

            if (median <= 0) return list;

            decimal lower = median * (1m - pctThreshold);
            decimal upper = median * (1m + pctThreshold);

            return list.Where(item =>
            {
                var price = priceSelector(item);
                return price >= lower && price <= upper;
            }).ToList();
        }

        // =====================================================================
        // KLASY POMOCNICZE
        // =====================================================================
        private class SafariOfferDto
        {
            public int ProductId { get; set; }
            public decimal Price { get; set; }
            public decimal? PriceWithDelivery { get; set; }
            public decimal CalculatedPrice { get; set; }
            public decimal? CalculatedPriceWithDelivery { get; set; }
            public string StoreName { get; set; }
            public int RegionId { get; set; }
            public string OfferUrl { get; set; }
            public string ProductName { get; set; }
            public string Ean { get; set; }
            public int? ExternalId { get; set; }
            public string ProducerCode { get; set; }
            public string Producer { get; set; }
            public string GoogleUrl { get; set; }
            public decimal? MarginPrice { get; set; }
            public string MainUrl { get; set; }
        }

        private class OfferRow
        {
            public string Store { get; set; }
            public string Country { get; set; }
            public string Currency { get; set; }
            public decimal OriginalPrice { get; set; }
            public decimal CalculatedPrice { get; set; }
            public string Url { get; set; }
            public bool IsMe { get; set; }
            public int RegionId { get; set; }
        }

        private class ProductGroup
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string Ean { get; set; }
            public int? ExternalId { get; set; }
            public string ProducerCode { get; set; }
            public string Producer { get; set; }
            public decimal? MarginPrice { get; set; }
            public List<OfferRow> AllOffers { get; set; }
            public OfferRow MyOffer { get; set; }
            public List<OfferRow> Competitors { get; set; }
            public OfferRow BestComp { get; set; }
            public decimal? DiffPLN { get; set; }
            public decimal? DiffPct { get; set; }
            public decimal? MarginAmt { get; set; }
            public decimal? MarginPct { get; set; }
            public string Position { get; set; }
            public int Rank { get; set; }
            public int TotalOffers { get; set; }
            public int ColorCode { get; set; }
        }

        private class CountryStat
        {
            public string Country { get; set; }
            public int TotalOffers { get; set; }
            public int ProductCount { get; set; }
            public int StoreCount { get; set; }
            public int CheaperThanUs { get; set; }
            public int MoreExpensive { get; set; }
            public decimal AvgPrice { get; set; }
            public decimal? AvgDiffPct { get; set; }
        }

        private class BestCountryEntry
        {
            public string Country { get; set; }
            public decimal AvgPrice { get; set; }
            public int OffersCount { get; set; }
        }

        private class ExportStyles
        {
            public ICellStyle Default { get; set; }
            public ICellStyle Header { get; set; }
            public ICellStyle HeaderDark { get; set; }
            public ICellStyle Currency { get; set; }
            public ICellStyle Percent { get; set; }
            public ICellStyle PercentRed { get; set; }
            public ICellStyle PercentGreen { get; set; }
            public ICellStyle PriceGreen { get; set; }
            public ICellStyle PriceLightGreen { get; set; }
            public ICellStyle PriceRed { get; set; }
            public ICellStyle CellRedBg { get; set; }
            public ICellStyle CellGreenBg { get; set; }
            public ICellStyle CellGreenBgBold { get; set; }
            public ICellStyle BaselineCell { get; set; }
            public ICellStyle BaselineCurrency { get; set; }
            public ICellStyle NoDataCell { get; set; }
            public ICellStyle ProductBlockSubHeader { get; set; }
            public ICellStyle CompetitorCheaperCurrency { get; set; }
            public ICellStyle CompetitorMoreExpensiveCurrency { get; set; }
        }

        private ExportStyles CreateExportStyles(XSSFWorkbook wb)
        {
            var s = new ExportStyles();
            var df = wb.CreateDataFormat();

            s.Default = wb.CreateCellStyle();

            var hf = wb.CreateFont(); hf.IsBold = true;
            s.Header = wb.CreateCellStyle(); s.Header.SetFont(hf);

            s.HeaderDark = CreateColoredStyle(wb, new byte[] { 26, 39, 68 }, true, IndexedColors.White.Index);

            s.Currency = wb.CreateCellStyle();
            s.Currency.DataFormat = df.GetFormat("#,##0.00");

            s.Percent = wb.CreateCellStyle();
            s.Percent.DataFormat = df.GetFormat("0.00");

            s.PercentRed = wb.CreateCellStyle(); s.PercentRed.DataFormat = df.GetFormat("0.00");
            var redFont = wb.CreateFont(); redFont.Color = IndexedColors.Red.Index; redFont.IsBold = true;
            s.PercentRed.SetFont(redFont);

            s.PercentGreen = wb.CreateCellStyle(); s.PercentGreen.DataFormat = df.GetFormat("0.00");
            var greenFont = wb.CreateFont(); greenFont.Color = IndexedColors.Green.Index; greenFont.IsBold = true;
            s.PercentGreen.SetFont(greenFont);

            s.PriceGreen = wb.CreateCellStyle(); s.PriceGreen.CloneStyleFrom(s.Currency);
            s.PriceGreen.FillForegroundColor = IndexedColors.LightGreen.Index;
            s.PriceGreen.FillPattern = FillPattern.SolidForeground;

            s.PriceLightGreen = wb.CreateCellStyle(); s.PriceLightGreen.CloneStyleFrom(s.Currency);
            s.PriceLightGreen.FillForegroundColor = IndexedColors.LemonChiffon.Index;
            s.PriceLightGreen.FillPattern = FillPattern.SolidForeground;

            s.PriceRed = wb.CreateCellStyle(); s.PriceRed.CloneStyleFrom(s.Currency);
            s.PriceRed.FillForegroundColor = IndexedColors.Rose.Index;
            s.PriceRed.FillPattern = FillPattern.SolidForeground;

            s.CellRedBg = CreateColoredStyle(wb, new byte[] { 248, 215, 218 }, false, 0);
            s.CellGreenBg = CreateColoredStyle(wb, new byte[] { 212, 237, 218 }, false, 0);
            s.CellGreenBgBold = CreateColoredStyle(wb, new byte[] { 195, 230, 203 }, true, 0);

            s.BaselineCell = CreateColoredStyle(wb, new byte[] { 220, 228, 240 }, true, 0);
            s.BaselineCurrency = CreateColoredStyle(wb, new byte[] { 220, 228, 240 }, true, 0, s.Currency);
            s.NoDataCell = CreateColoredStyle(wb, new byte[] { 235, 235, 235 }, false, 0);

            s.ProductBlockSubHeader = CreateColoredStyle(wb, new byte[] { 222, 228, 240 }, true, 0);

            // Konkurent vs nas — czytelne kolory
            s.CompetitorCheaperCurrency = CreateColoredStyle(wb, new byte[] { 248, 215, 218 }, false, 0, s.Currency);
            s.CompetitorMoreExpensiveCurrency = CreateColoredStyle(wb, new byte[] { 212, 237, 218 }, false, 0, s.Currency);

            return s;
        }

        private ICellStyle CreateColoredStyle(XSSFWorkbook wb, byte[] rgb, bool bold, short fontColorIndex, ICellStyle cloneFrom = null)
        {
            var style = (XSSFCellStyle)wb.CreateCellStyle();
            if (cloneFrom != null) style.CloneStyleFrom(cloneFrom);

            var colorMap = new DefaultIndexedColorMap();
            style.SetFillForegroundColor(new XSSFColor(rgb, colorMap));
            style.FillPattern = FillPattern.SolidForeground;

            if (bold || fontColorIndex > 0)
            {
                var font = wb.CreateFont();
                if (bold) font.IsBold = true;
                if (fontColorIndex > 0) font.Color = fontColorIndex;
                style.SetFont(font);
            }
            return style;
        }

        private static void SetCell(IRow row, int col, string value, ICellStyle style)
        {
            var c = row.CreateCell(col);
            c.SetCellValue(value);
            c.CellStyle = style;
        }

        private static void SetHeaderCell(IRow row, int col, string value, ICellStyle style)
        {
            var c = row.CreateCell(col);
            c.SetCellValue(value);
            c.CellStyle = style;
        }

        private static void SetDecimalCell(ICell cell, decimal? value, ICellStyle style)
        {
            if (value.HasValue)
            {
                cell.SetCellValue((double)value.Value);
                cell.CellStyle = style;
            }
            else
            {
                cell.SetCellValue("-");
            }
        }



    }
}