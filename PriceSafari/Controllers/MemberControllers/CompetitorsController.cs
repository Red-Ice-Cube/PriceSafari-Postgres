using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGet.Packaging;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

[Authorize(Roles = "Member")]
public class CompetitorsController : Controller
{
    private readonly PriceSafariContext _context;
    private readonly UserManager<PriceSafariUser> _userManager;

    public CompetitorsController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private async Task<bool> UserHasAccessToStore(int storeId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId);
        var isAdminOrManager = await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "Manager");

        if (!isAdminOrManager)
        {
            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
            return hasAccess;
        }

        return true;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var userStoresQuery = _context.UserStores
            .Where(us => us.UserId == userId)
            .Include(us => us.StoreClass)
            .Select(us => us.StoreClass);

        var stores = await userStoresQuery.ToListAsync();

        var model = stores.Select(store => new ChanelViewModel
        {
            StoreId = store.StoreId,
            StoreName = store.StoreName,
            LogoUrl = store.StoreLogoUrl,

            OnCeneo = store.OnCeneo,
            OnGoogle = store.OnGoogle,
            OnAllegro = store.OnAllegro
        }).ToList();

        return View("~/Views/Panel/Competitors/Index.cshtml", model);
    }

    public async Task<IActionResult> Competitors(int storeId)
    {
        if (!await UserHasAccessToStore(storeId))
        {
            return Content("Nie ma takiego sklepu");
        }

        var storeName = await _context.Stores
            .Where(s => s.StoreId == storeId)
            .Select(s => s.StoreName)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(storeName))
        {
            return Content("Nie można zidentyfikować nazwy sklepu.");
        }

        var latestScrap = await _context.ScrapHistories
            .Where(sh => sh.StoreId == storeId)
            .OrderByDescending(sh => sh.Date)
            .Select(sh => new { sh.Id, sh.Date })
            .FirstOrDefaultAsync();

        if (latestScrap == null)
        {
            return Content("Brak danych o cenach.");
        }

        var myDistinctProductIds = await _context.PriceHistories
            .Where(ph => ph.ScrapHistoryId == latestScrap.Id && ph.StoreName.ToLower() == storeName.ToLower())
            .Select(ph => ph.ProductId)
            .Distinct()
            .ToListAsync();

        ViewBag.StoreName = storeName;
        ViewBag.StoreId = storeId;
        ViewBag.DetailAction = "CompetitorPrices";
        if (!myDistinctProductIds.Any())
        {

            ViewBag.AnalysisType = "fullScan";

            var allPriceEntries = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id)
                .Select(ph => new { ph.StoreName, ph.IsGoogle })
                .ToListAsync();

            var storeData = allPriceEntries
                .GroupBy(ph => ph.StoreName)
                .Select(g =>
                {
                    var competitorStoreName = g.Key;
                    var entriesForStore = g.ToList();

                    string dataSourceDescription = GetDataSourceDescription(
                        entriesForStore.Any(e => e.IsGoogle),
                        entriesForStore.Any(e => !e.IsGoogle)
                    );

                    return new
                    {
                        StoreName = competitorStoreName,
                        CommonProductsCount = entriesForStore.Count,
                        SamePriceCount = 0,
                        HigherPriceCount = 0,
                        LowerPriceCount = 0,
                        DataSourceDescription = dataSourceDescription
                    };
                })
                .Where(c => c.CommonProductsCount >= 10)

                .OrderByDescending(c => c.CommonProductsCount)
                .ToList<object>();

            return View("~/Views/Panel/Competitors/Competitors.cshtml", storeData);
        }
        else
        {

            ViewBag.AnalysisType = "commonProducts";

            var myPriceEntries = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id && ph.StoreName.ToLower() == storeName.ToLower())
                .Select(ph => new { ph.ProductId, ph.Price })
                .ToListAsync();

            var myDistinctProductIdsHashSet = myDistinctProductIds.ToHashSet();

            var allCompetitorPriceEntries = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id && ph.StoreName.ToLower() != storeName.ToLower())
                .Select(ph => new { ph.StoreName, ph.ProductId, ph.Price, ph.IsGoogle })
                .ToListAsync();

            var competitors = allCompetitorPriceEntries
                .GroupBy(ph => ph.StoreName)
                .Select(g =>
                {
                    var competitorStoreName = g.Key;
                    var competitorEntriesForStore = g.ToList();

                    var competitorDistinctProductIdsForStore = competitorEntriesForStore.Select(p => p.ProductId).Distinct().ToHashSet();
                    var actualCommonProductIds = myDistinctProductIdsHashSet.Intersect(competitorDistinctProductIdsForStore).ToList();

                    int commonProductsCountResult = actualCommonProductIds.Count();
                    int samePriceCountResult = 0;
                    int higherPriceCountResult = 0;
                    int lowerPriceCountResult = 0;

                    foreach (var productId in actualCommonProductIds)
                    {
                        var ourOffersForProduct = myPriceEntries.Where(p => p.ProductId == productId).ToList();
                        var competitorOffersForProduct = competitorEntriesForStore.Where(p => p.ProductId == productId).ToList();

                        if (ourOffersForProduct.Any(op => competitorOffersForProduct.Any(cp => op.Price == cp.Price)))
                        {
                            samePriceCountResult++;
                        }
                        if (ourOffersForProduct.Any(op => competitorOffersForProduct.Any(cp => op.Price < cp.Price)))
                        {
                            higherPriceCountResult++;

                        }
                        if (ourOffersForProduct.Any(op => competitorOffersForProduct.Any(cp => op.Price > cp.Price)))
                        {
                            lowerPriceCountResult++;

                        }
                    }

                    string dataSourceDescription = GetDataSourceDescription(
                        competitorEntriesForStore.Any(e => e.IsGoogle),
                        competitorEntriesForStore.Any(e => !e.IsGoogle)
                    );

                    return new
                    {
                        StoreName = competitorStoreName,
                        CommonProductsCount = commonProductsCountResult,
                        SamePriceCount = samePriceCountResult,
                        HigherPriceCount = higherPriceCountResult,
                        LowerPriceCount = lowerPriceCountResult,
                        DataSourceDescription = dataSourceDescription
                    };
                })
                .Where(c => c.CommonProductsCount >= 1)
                .OrderByDescending(c => c.CommonProductsCount)
                .ToList<object>();

            return View("~/Views/Panel/Competitors/Competitors.cshtml", competitors);
        }
    }

    public async Task<IActionResult> AllegroCompetitors(int storeId)
    {
        if (!await UserHasAccessToStore(storeId)) return Content("Brak dostępu.");

        var store = await _context.Stores
            .Where(s => s.StoreId == storeId)
            .Select(s => new { s.StoreName, s.StoreNameAllegro })
            .FirstOrDefaultAsync();

        if (store == null || string.IsNullOrEmpty(store.StoreNameAllegro)) return Content("Brak konfiguracji Allegro.");

        var latestScrap = await _context.AllegroScrapeHistories
            .Where(sh => sh.StoreId == storeId)
            .OrderByDescending(sh => sh.Date)
            .Select(sh => new { sh.Id })
            .FirstOrDefaultAsync();

        if (latestScrap == null)
        {
            ViewBag.StoreName = store.StoreName;
            return View("~/Views/Panel/Competitors/Competitors.cshtml", new List<object>());
        }

        var allPrices = await _context.AllegroPriceHistories
            .Include(ph => ph.AllegroProduct)

            .Where(ph => ph.AllegroScrapeHistoryId == latestScrap.Id)
            .Select(ph => new
            {
                ph.AllegroProductId,
                ph.SellerName,
                ph.Price,
                IdOnAllegro = ph.AllegroProduct.IdOnAllegro

            })
            .ToListAsync();

        var myPrices = allPrices
            .Where(p => p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
            .Where(p => !string.IsNullOrEmpty(p.IdOnAllegro))

            .GroupBy(p => p.IdOnAllegro)
            .ToDictionary(g => g.Key, g => g.Min(x => x.Price));

        var competitorsData = allPrices
            .Where(p => !p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.SellerName)
            .Select(g =>
            {
                var competitorName = g.Key;

                var uniqueCompetitorProducts = g
                    .Where(o => !string.IsNullOrEmpty(o.IdOnAllegro))
                    .GroupBy(o => o.IdOnAllegro)
                    .Select(pg => new
                    {
                        CatalogId = pg.Key,

                        Price = pg.Min(x => x.Price)

                    })
                    .ToList();

                int common = 0, same = 0, iHaveLower = 0, iHaveHigher = 0;

                foreach (var competitorProduct in uniqueCompetitorProducts)
                {

                    if (myPrices.TryGetValue(competitorProduct.CatalogId, out decimal myPrice))
                    {
                        common++;

                        if (myPrice < competitorProduct.Price) iHaveLower++;

                        else if (myPrice > competitorProduct.Price) iHaveHigher++;

                        else same++;
                    }
                }

                return new
                {
                    StoreName = competitorName,
                    CommonProductsCount = common,
                    SamePriceCount = same,
                    HigherPriceCount = iHaveLower,
                    LowerPriceCount = iHaveHigher,
                    DataSourceDescription = "Allegro"
                };
            })
            .Where(x => x.CommonProductsCount > 0)
            .OrderByDescending(x => x.CommonProductsCount)
            .ToList<object>();

        ViewBag.StoreName = store.StoreName;
        ViewBag.StoreId = storeId;

        ViewBag.DetailAction = "AllegroCompetitorPrices";

        return View("~/Views/Panel/Competitors/Competitors.cshtml", competitorsData);
    }

    private string GetDataSourceDescription(bool fromGoogle, bool fromCeneo)
    {
        if (fromGoogle && fromCeneo) return "Google & Ceneo";
        if (fromGoogle) return "Google";
        if (fromCeneo) return "Ceneo";
        return "Nieznane źródło";
    }

    public async Task<IActionResult> CompetitorPrices(int storeId, string competitorStoreName)
    {

        if (!await UserHasAccessToStore(storeId))
        {
            return Content("Nie ma takiego sklepu");
        }

        var storeName = await _context.Stores
            .Where(s => s.StoreId == storeId)
            .Select(s => s.StoreName)
            .FirstOrDefaultAsync();

        ViewBag.CompetitorStoreName = competitorStoreName;
        ViewBag.StoreId = storeId;
        ViewBag.StoreName = storeName;
        return View("~/Views/Panel/Competitors/CompetitorPrices.cshtml");
    }

    public async Task<IActionResult> AllegroCompetitorPrices(int storeId, string competitorStoreName)
    {
        if (!await UserHasAccessToStore(storeId)) return Content("Brak dostępu.");

        var store = await _context.Stores
            .Where(s => s.StoreId == storeId)
            .Select(s => new { s.StoreName, s.StoreNameAllegro })
            .FirstOrDefaultAsync();

        ViewBag.CompetitorStoreName = competitorStoreName;
        ViewBag.StoreId = storeId;
        ViewBag.StoreName = store.StoreName;

        ViewBag.ScrapHistoryUrl = Url.Action("GetAllegroScrapHistoryIds", "Competitors");
        ViewBag.PricesUrl = Url.Action("GetAllegroCompetitorPrices", "Competitors");

        return View("~/Views/Panel/Competitors/CompetitorPrices.cshtml");
    }

    [HttpGet]
    public async Task<IActionResult> GetAllegroScrapHistoryIds(int storeId)
    {
        if (!await UserHasAccessToStore(storeId)) return Forbid();

        var history = await _context.AllegroScrapeHistories
            .Where(sh => sh.StoreId == storeId)
            .OrderByDescending(sh => sh.Date)
            .Select(sh => new { id = sh.Id, date = sh.Date })

            .ToListAsync();

        return Json(history);
    }
    [HttpPost]
    public async Task<IActionResult> GetAllegroCompetitorPrices([FromBody] GetCompetitorPricesRequest request)
    {
        if (!await UserHasAccessToStore(request.StoreId)) return Forbid();

        var store = await _context.Stores
            .Where(s => s.StoreId == request.StoreId)
            .Select(s => new { s.StoreNameAllegro })
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(store?.StoreNameAllegro)) return NotFound(new { message = "Brak konfiguracji Allegro." });

        var allPrices = await _context.AllegroPriceHistories
            .Include(ph => ph.AllegroProduct)
            .Where(ph => ph.AllegroScrapeHistoryId == request.ScrapHistoryId)
            .Select(ph => new
            {
                ph.AllegroProductId,
                ph.SellerName,
                ph.Price,

                IdOnAllegro = ph.AllegroProduct.IdOnAllegro,
                ProductName = ph.AllegroProduct.AllegroProductName,
               
            })
            .ToListAsync();

        var myPricesByCatalogId = allPrices
            .Where(p => p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
            .Where(p => !string.IsNullOrEmpty(p.IdOnAllegro))

            .GroupBy(p => p.IdOnAllegro)
            .ToDictionary(
                g => g.Key,
                g => g.Min(x => x.Price)
            );

        var competitorOffers = allPrices
            .Where(p => p.SellerName.Equals(request.CompetitorStoreName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var result = competitorOffers
            .Where(p => !string.IsNullOrEmpty(p.IdOnAllegro))

            .GroupBy(p => p.IdOnAllegro)
            .Select(g =>
            {
                var catalogId = g.Key;
                var competitorBestPrice = g.Min(x => x.Price);

                var productInfo = g.First();

                decimal? myPrice = myPricesByCatalogId.TryGetValue(catalogId, out var p) ? p : (decimal?)null;

                return new
                {

                    productId = productInfo.AllegroProductId,
                    productName = productInfo.ProductName,
             
                    price = competitorBestPrice,
                    ourPrice = myPrice,
                    scrapHistoryId = request.ScrapHistoryId
                };
            })
            .Where(x => x.ourPrice.HasValue)

            .ToList();

        return Json(result);
    }

    public async Task<IActionResult> GetScrapHistoryIds(int storeId)
    {

        if (!await UserHasAccessToStore(storeId))
        {
            return Content("Nie ma takiego sklepu");
        }

        var scrapHistoryIds = await _context.ScrapHistories
            .Where(sh => sh.StoreId == storeId)
            .OrderByDescending(sh => sh.Date)
            .Select(sh => new { sh.Id, sh.Date })
            .ToListAsync();

        return Json(scrapHistoryIds);
    }
    public class GetCompetitorPricesRequest
    {
        public int StoreId { get; set; }
        public string CompetitorStoreName { get; set; }
        public int ScrapHistoryId { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> GetCompetitorPrices([FromBody] GetCompetitorPricesRequest request)
    {
        if (!await UserHasAccessToStore(request.StoreId))
        {
            return Forbid();
        }

        var storeName = await _context.Stores
            .Where(s => s.StoreId == request.StoreId)
            .Select(s => s.StoreName)
            .FirstOrDefaultAsync();

        if (string.IsNullOrEmpty(storeName))
        {
            return NotFound(new { message = "Nie znaleziono sklepu." });
        }

        var ourPriceEntries = await _context.PriceHistories
            .Where(ph => ph.Product.StoreId == request.StoreId &&
                         ph.StoreName.ToLower() == storeName.ToLower() &&
                         ph.ScrapHistoryId == request.ScrapHistoryId)
            .Select(ph => new
            {
                ph.ProductId,
                ph.Price,
                ph.Product.ProductName,
                ph.Product.MainUrl
            })
            .ToListAsync();

        if (!ourPriceEntries.Any())
        {

            var competitorEntriesWithProductData = await _context.PriceHistories
                .Where(ph => ph.StoreName.ToLower() == request.CompetitorStoreName.ToLower() &&
                             ph.ScrapHistoryId == request.ScrapHistoryId)
                .Select(ph => new
                {
                    ph.ProductId,
                    ph.Price,
                    ph.Product.ProductName,
                    ph.Product.MainUrl
                })
                .ToListAsync();

            var competitorProducts = competitorEntriesWithProductData
                .GroupBy(p => p.ProductId)
                .Select(g =>
                {
                    var distinctPrices = g.Select(p => p.Price).Distinct().ToList();
                    return new
                    {
                        ProductId = g.Key,
                        ProductName = g.First().ProductName,
                        ProductMainUrl = g.First().MainUrl,
                        CompetitorPrice = distinctPrices.Count == 1 ? distinctPrices.First() : (decimal?)null
                    };
                })
                .Where(p => p.CompetitorPrice.HasValue)

                .ToList();

            var combinedPrices = competitorProducts.Select(p => new
            {
                productId = p.ProductId,
                productName = p.ProductName,
                productMainUrl = p.ProductMainUrl,
                price = p.CompetitorPrice,

                ourPrice = (decimal?)null,

                scrapHistoryId = request.ScrapHistoryId
            }).ToList();

            return Json(combinedPrices);
        }
        else
        {

            var ourRepresentativePrices = ourPriceEntries
                .GroupBy(p => p.ProductId)
                .Select(g =>
                {
                    var distinctPrices = g.Select(p => p.Price).Distinct().ToList();
                    return new
                    {
                        ProductId = g.Key,
                        ProductName = g.First().ProductName,
                        ProductMainUrl = g.First().MainUrl,
                        OurPrice = distinctPrices.Count == 1 ? distinctPrices.First() : (decimal?)null
                    };
                })
                .Where(p => p.OurPrice.HasValue)
                .ToDictionary(p => p.ProductId);

            var competitorPriceEntries = await _context.PriceHistories
                .Where(ph => ph.StoreName.ToLower() == request.CompetitorStoreName.ToLower() &&
                             ph.ScrapHistoryId == request.ScrapHistoryId)
                .Select(ph => new { ph.ProductId, ph.Price })
                .ToListAsync();

            var competitorRepresentativePrices = competitorPriceEntries
                .GroupBy(p => p.ProductId)
                .Select(g =>
                {
                    var distinctPrices = g.Select(p => p.Price).Distinct().ToList();
                    return new
                    {
                        ProductId = g.Key,
                        CompetitorPrice = distinctPrices.Count == 1 ? distinctPrices.First() : (decimal?)null
                    };
                })
                .Where(p => p.CompetitorPrice.HasValue)
                .ToDictionary(p => p.ProductId);

            var combinedPrices = new List<object>();

            foreach (var productId in ourRepresentativePrices.Keys)
            {
                if (competitorRepresentativePrices.TryGetValue(productId, out var competitorProductPriceInfo))
                {
                    var ourProductPriceInfo = ourRepresentativePrices[productId];
                    combinedPrices.Add(new
                    {
                        productId = ourProductPriceInfo.ProductId,
                        productName = ourProductPriceInfo.ProductName,
                        productMainUrl = ourProductPriceInfo.ProductMainUrl,
                        price = competitorProductPriceInfo.CompetitorPrice,
                        ourPrice = ourProductPriceInfo.OurPrice,
                        scrapHistoryId = request.ScrapHistoryId
                    });
                }
            }

            return Json(combinedPrices);
        }
    }

}