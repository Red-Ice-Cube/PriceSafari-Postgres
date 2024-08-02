using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

public class GoogleScraperController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View("~/Views/ManagerPanel/GoogleScraper/Index.cshtml");
    }

    [HttpPost]
    public async Task<IActionResult> StartScraping(string title, string storeName)
    {
        var scraper = new GoogleScraper();
        await scraper.InitializeBrowserAsync();
        await scraper.InitializeAndSearchAsync(title);
        await scraper.SearchStoreNameAsync(storeName);
        await scraper.SearchUrlAndReviewsWithFallbackAsync();

        // Perform matching after scraping
        scraper.MatchReviews();

        await scraper.CloseBrowserAsync();

        return Content("Scraping completed. Check the console for output.");
    }
}
