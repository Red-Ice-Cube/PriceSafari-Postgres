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

[Authorize(Roles = "Admin, Member, Manager")]
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

        var latestScrap = await _context.ScrapHistories
            .Where(sh => sh.StoreId == storeId)
            .OrderByDescending(sh => sh.Date)
            .Select(sh => new { sh.Id, sh.Date })
            .FirstOrDefaultAsync();

        if (latestScrap == null)
        {
            return Content("Brak danych o cenach.");
        }

        // Pobranie cen dla naszego sklepu
        var myPrices = await _context.PriceHistories
            .Where(ph => ph.ScrapHistoryId == latestScrap.Id && ph.StoreName.ToLower() == storeName.ToLower())
            .Select(ph => new { ph.ProductId, ph.Price })
            .ToListAsync();

        // Pobranie cen konkurentów
        var competitorPrices = await _context.PriceHistories
            .Where(ph => ph.ScrapHistoryId == latestScrap.Id && ph.StoreName.ToLower() != storeName.ToLower())
            .ToListAsync();

        // Grupowanie po konkurentach
        var competitors = competitorPrices
            .GroupBy(ph => ph.StoreName)
            .Select(g => new
            {
                StoreName = g.Key,
                CommonProductsCount = g.Count(),
                SamePriceCount = g.Count(ph => myPrices.Any(mp => mp.ProductId == ph.ProductId && mp.Price == ph.Price)),
                HigherPriceCount = g.Count(ph => myPrices.Any(mp => mp.ProductId == ph.ProductId && mp.Price < ph.Price)),
                LowerPriceCount = g.Count(ph => myPrices.Any(mp => mp.ProductId == ph.ProductId && mp.Price > ph.Price))
            })
            .OrderByDescending(c => c.CommonProductsCount)
            .ToList();

        ViewBag.StoreName = storeName;
        ViewBag.StoreId = storeId;
        return View("~/Views/Panel/Competitors/Competitors.cshtml", competitors);
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
        Console.WriteLine($"Received request for competitor prices with parameters: storeId={request.StoreId}, competitorStoreName={request.CompetitorStoreName}, scrapHistoryId={request.ScrapHistoryId}");

        var storeId = request.StoreId;

        if (!await UserHasAccessToStore(storeId))
        {
            return Content("Nie ma takiego sklepu");
        }

        var storeName = await _context.Stores
            .Where(s => s.StoreId == request.StoreId)
            .Select(s => s.StoreName)
            .FirstOrDefaultAsync();

        // Pobierz nasze ceny
        var ourPrices = await _context.PriceHistories
            .Where(ph => ph.Product.StoreId == request.StoreId && ph.StoreName == storeName && ph.ScrapHistoryId == request.ScrapHistoryId)
            .Select(ph => new
            {
                ph.ProductId,
                ph.Price,
                StoreName = ph.StoreName,
                ProductName = ph.Product.ProductName,
                OfferUrl = ph.Product.OfferUrl
            })
            .ToListAsync();

        // Pobierz ceny konkurenta
        var competitorPrices = await _context.PriceHistories
            .Where(ph => ph.ScrapHistoryId == request.ScrapHistoryId && ph.StoreName == request.CompetitorStoreName)
            .Select(ph => new
            {
                ph.ProductId,
                ph.Product.ProductName,
                ph.Product.OfferUrl,
                ph.Price,
                ph.ScrapHistoryId
            })
            .ToListAsync();

        // Połącz ceny tylko tych produktów, dla których mamy zarówno naszą cenę, jak i cenę konkurenta
        var combinedPrices = ourPrices
            .Select(op => {
                var competitorPrice = competitorPrices.FirstOrDefault(cp => cp.ProductId == op.ProductId);
                if (competitorPrice != null)
                {
                    return new
                    {
                        op.ProductId,
                        ProductName = competitorPrice.ProductName,
                        OfferUrl = competitorPrice.OfferUrl,
                        Price = competitorPrice.Price,
                        ScrapHistoryId = request.ScrapHistoryId,
                        OurPrice = op.Price,
                        PriceData = 3  // Oznaczenie, że mamy kompletne dane
                    };
                }
                return null;
            })
            .Where(result => result != null)
            .ToList();

        Console.WriteLine($"Found {combinedPrices.Count} prices for competitor {request.CompetitorStoreName} in scrap history {request.ScrapHistoryId}");

        foreach (var price in combinedPrices)
        {
            Console.WriteLine($"ProductId: {price.ProductId}, ProductName: {price.ProductName}, OfferUrl: {price.OfferUrl}, Price: {price.Price}, OurPrice: {price.OurPrice}, ScrapHistoryId: {price.ScrapHistoryId}, PriceData: {price.PriceData}");
        }

        return Json(combinedPrices);
    }




}
