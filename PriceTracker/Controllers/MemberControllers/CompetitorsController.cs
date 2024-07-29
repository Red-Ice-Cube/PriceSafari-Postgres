using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using System.Linq;
using System.Threading.Tasks;

public class CompetitorsController : Controller
{
    private readonly PriceTrackerContext _context;

    public CompetitorsController(PriceTrackerContext context)
    {
        _context = context;
    }

   
    public async Task<IActionResult> Index()
    {
        var stores = await _context.Stores.ToListAsync();
        return View("~/Views/Panel/Competitors/Index.cshtml", stores);
    }



    public async Task<IActionResult> Competitors(int storeId)
    {
        var storeName = await _context.Stores
            .Where(s => s.StoreId == storeId)
            .Select(s => s.StoreName)
            .FirstOrDefaultAsync();

        var competitors = await _context.PriceHistories
            .Where(ph => ph.Product.StoreId == storeId)
            .Select(ph => new { ph.StoreName, ph.ProductId })
            .Distinct()
            .GroupBy(ph => ph.StoreName)
            .Select(g => new
            {
                StoreName = g.Key,
                CommonProductsCount = g.Count()
            })
            .Where(c => c.StoreName != storeName)
            .OrderByDescending(c => c.CommonProductsCount)
            .ToListAsync();

        ViewBag.StoreName = storeName;
        ViewBag.StoreId = storeId;
        return View("~/Views/Panel/Competitors/Competitors.cshtml", competitors);
    }


    public async Task<IActionResult> CompetitorPrices(int storeId, string competitorStoreName)
    {
        ViewBag.CompetitorStoreName = competitorStoreName;
        ViewBag.StoreId = storeId;
        return View("~/Views/Panel/Competitors/CompetitorPrices.cshtml");
    }

    public async Task<IActionResult> GetScrapHistoryIds(int storeId)
    {
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

        var prices = await _context.PriceHistories
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

        Console.WriteLine($"Found {prices.Count} prices for competitor {request.CompetitorStoreName} in scrap history {request.ScrapHistoryId}");

        foreach (var price in prices)
        {
            Console.WriteLine($"ProductId: {price.ProductId}, ProductName: {price.ProductName}, OfferUrl: {price.OfferUrl}, Price: {price.Price}, ScrapHistoryId: {price.ScrapHistoryId}");
        }

        return Json(prices);
    }







}
