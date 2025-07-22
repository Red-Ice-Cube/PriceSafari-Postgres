using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using Microsoft.AspNetCore.SignalR;

namespace PriceSafari.ScrapersControllers
{
    [ApiController]
    [Route("api/allegro-scrape")]
    public class AllegroScrapeApiController : ControllerBase
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private const string ApiKey = "twoj-super-tajny-klucz-api-123"; // Ten sam klucz co wcześniej

        public AllegroScrapeApiController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // --- Endpoint dla scrapera w Pythonie do pobierania zadań ---
        [HttpGet("get-task")]
        public async Task<IActionResult> GetTask([FromQuery] string scraperName)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            // 1. Zarejestruj lub zaktualizuj status scrapera
            var scraper = AllegroScrapeManager.ActiveScrapers.AddOrUpdate(
                scraperName,
                new HybridScraperClient { Name = scraperName, Status = ScraperLiveStatus.Idle, LastCheckIn = DateTime.UtcNow },
                (key, existing) => {
                    existing.Status = ScraperLiveStatus.Idle;
                    existing.LastCheckIn = DateTime.UtcNow;
                    existing.CurrentTaskId = null;
                    return existing;
                });
            await _hubContext.Clients.All.SendAsync("UpdateDetailScraperStatus", scraper);

            // 2. Sprawdź, czy proces scrapowania jest w ogóle uruchomiony
            if (AllegroScrapeManager.CurrentStatus != ScrapingProcessStatus.Running)
            {
                return Ok(new { message = "Scraping process is paused." });
            }

            // 3. Znajdź następne zadanie w kolejce (pierwszy URL, który nie jest ani zescrapowany, ani odrzucony)
            var offerToScrape = await _context.AllegroOffersToScrape
                .Where(o => !o.IsScraped && !o.IsRejected)
                .OrderBy(o => o.Id) // Bierzemy najstarsze zadania jako pierwsze
                .FirstOrDefaultAsync();

            if (offerToScrape == null)
            {
                return Ok(new { message = "No pending tasks available." });
            }

            // 4. Zwróć zadanie do scrapera w Pythonie
            scraper.Status = ScraperLiveStatus.Busy;
            scraper.CurrentTaskId = offerToScrape.Id;
            await _hubContext.Clients.All.SendAsync("UpdateDetailScraperStatus", scraper);

            return Ok(new { taskId = offerToScrape.Id, url = offerToScrape.AllegroOfferUrl });
        }

        // --- Endpoint dla scrapera do odsyłania wyników ---
        [HttpPost("submit-result")]
        public async Task<IActionResult> SubmitResult([FromBody] ScrapeResultDto result)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var offer = await _context.AllegroOffersToScrape.FindAsync(result.TaskId);
            if (offer == null) return NotFound();

            if (result.Status == "success")
            {
                offer.IsScraped = true;
                offer.IsRejected = false;
                offer.CollectedPricesCount = result.CollectedPricesCount;
            }
            else // "rejected"
            {
                offer.IsScraped = false;
                offer.IsRejected = true;
            }

            await _context.SaveChangesAsync();

            // Wyślij aktualizację do frontendu przez SignalR, aby tabela odświeżyła się na żywo
            await _hubContext.Clients.All.SendAsync("UpdateAllegroOfferRow", offer);

            return Ok(new { message = "Result processed." });
        }
    }

    // DTO (Data Transfer Object) do przesyłania wyników
    public class ScrapeResultDto
    {
        public int TaskId { get; set; }
        public string Status { get; set; } // "success" or "rejected"
        public int CollectedPricesCount { get; set; }
    }
}