using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;

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

        return View("~/Views/Panel/Competitors/Competitors.cshtml", competitors);
      
    }




    // Metoda wyświetlająca listę scrapowań dla wybranego konkurenta
    public async Task<IActionResult> CompetitorScrapings(string competitorStoreName, int storeId)
    {
        var scrapHistories = await _context.ScrapHistories
            .Where(sh => sh.PriceHistories.Any(ph => ph.StoreName == competitorStoreName))
            .OrderByDescending(sh => sh.Date)
            .ToListAsync();

        ViewBag.CompetitorStoreName = competitorStoreName;
        ViewBag.StoreId = storeId;
     
        return View("~/Views/Panel/Competitors/CompetitorScrapings.cshtml", scrapHistories);
    }

    // Metoda wyświetlająca ceny produktów dla wybranego scrapowania
    public async Task<IActionResult> CompetitorPrices(int scrapHistoryId, string competitorStoreName)
    {
        var prices = await _context.PriceHistories
            .Where(ph => ph.ScrapHistoryId == scrapHistoryId && ph.StoreName == competitorStoreName)
            .Select(ph => new
            {
                ph.Product.ProductName,
                ph.Price,
                ph.Position,
                ph.IsBidding,
                ph.AvailabilityNum,
                ph.ShippingCostNum
            })
            .ToListAsync();

        ViewBag.CompetitorStoreName = competitorStoreName;
        ViewBag.ScrapHistoryId = scrapHistoryId;

        return View("~/Views/Panel/Competitors/CompetitorPrices.cshtml", prices);
    }
}
