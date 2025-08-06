using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGet.Packaging;
using PriceSafari.Data;
using PriceSafari.Models;
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

    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var userStores = await _context.UserStores
            .Where(us => us.UserId == userId)
            .Include(us => us.StoreClass)
            .ThenInclude(s => s.ScrapHistories)

            .ToListAsync();

        var stores = userStores.Select(us => us.StoreClass).ToList();

        return View("~/Views/Panel/Competitors/Index.cshtml", stores);
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

        // Pobieramy ID produktów naszego sklepu
        var myDistinctProductIds = await _context.PriceHistories
            .Where(ph => ph.ScrapHistoryId == latestScrap.Id && ph.StoreName.ToLower() == storeName.ToLower())
            .Select(ph => ph.ProductId)
            .Distinct()
            .ToListAsync();

        ViewBag.StoreName = storeName;
        ViewBag.StoreId = storeId;

        // PUNKT DECYZYJNY: Sprawdzamy, czy nasz sklep ma jakiekolwiek oferty
        if (!myDistinctProductIds.Any())
        {
            // NOWA LOGIKA: Nasz sklep nie został znaleziony. Zliczamy wszystkie oferty.
            ViewBag.AnalysisType = "fullScan"; // Flaga dla widoku, informująca o trybie analizy

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

                    // Obliczamy opis źródła danych używając metody pomocniczej
                    string dataSourceDescription = GetDataSourceDescription(
                        entriesForStore.Any(e => e.IsGoogle),
                        entriesForStore.Any(e => !e.IsGoogle)
                    );

                    // Tworzymy obiekt pasujący do widoku.
                    // CommonProductsCount przechowuje teraz łączną liczbę ofert.
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
                .Where(c => c.CommonProductsCount >= 10) // Stosujemy ten sam filtr co w oryginalnej logice
                .OrderByDescending(c => c.CommonProductsCount)
                .ToList<object>(); // Konwertujemy na listę obiektów, aby pasowała do typu zwracanego przez widok

            return View("~/Views/Panel/Competitors/Competitors.cshtml", storeData);
        }
        else
        {
            // ORYGINALNA LOGIKA: Nasz sklep został znaleziony.
            ViewBag.AnalysisType = "commonProducts";

            // Pobieramy wpisy naszego sklepu (potrzebne do porównania cen)
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
                            higherPriceCountResult++; // Nasza cena jest niższa = ich cena jest wyższa
                        }
                        if (ourOffersForProduct.Any(op => competitorOffersForProduct.Any(cp => op.Price > cp.Price)))
                        {
                            lowerPriceCountResult++; // Nasza cena jest wyższa = ich cena jest niższa
                        }
                    }

                    // Używamy metody pomocniczej do wyznaczenia źródła
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

    // Metoda pomocnicza, aby nie powtarzać kodu
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

        // Ten krok pozostaje bez zmian - najpierw próbujemy znaleźć nasze oferty.
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

        // PUNKT DECYZYJNY: Sprawdzamy, czy znaleziono oferty naszego sklepu.
        if (!ourPriceEntries.Any())
        {
            // NOWA LOGIKA: Nasz sklep nie został znaleziony. Pokazujemy wszystkie produkty konkurenta.

            // 1. Pobieramy oferty konkurenta, ale tym razem z danymi produktu.
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

            // 2. Grupujemy i filtrujemy, aby uzyskać jednoznaczną cenę dla każdego produktu konkurenta.
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
                .Where(p => p.CompetitorPrice.HasValue) // Bierzemy tylko te produkty, gdzie cena konkurenta jest jasna
                .ToList();

            // 3. Tworzymy listę wynikową, ustawiając 'ourPrice' na null.
            var combinedPrices = competitorProducts.Select(p => new
            {
                productId = p.ProductId,
                productName = p.ProductName,
                productMainUrl = p.ProductMainUrl,
                price = p.CompetitorPrice, // Cena konkurenta
                ourPrice = (decimal?)null, // Nasza cena jest nieznana
                scrapHistoryId = request.ScrapHistoryId
            }).ToList();

            return Json(combinedPrices);
        }
        else
        {
            // ORYGINALNA LOGIKA: Nasz sklep został znaleziony, więc porównujemy ceny.

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