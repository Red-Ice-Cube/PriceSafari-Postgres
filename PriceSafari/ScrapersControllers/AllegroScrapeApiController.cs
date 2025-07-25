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
        private const string ApiKey = "2764udhnJUDI8392j83jfi2ijdo1949rncowp89i3rnfiiui1203kfnf9030rfpPkUjHyHt";
        private const int BatchSize = 200; 

        public AllegroScrapeApiController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet("get-task")]
        public async Task<IActionResult> GetTaskBatch([FromQuery] string scraperName)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            // Rejestracja scrapera (bez zmian)
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

            if (AllegroScrapeManager.CurrentStatus != ScrapingProcessStatus.Running)
            {
                return Ok(new { message = "Scraping process is paused." });
            }

            // Pobierz paczkę 100 URL-i, które nie są ani zescrapowane, ani odrzucone, ani w trakcie przetwarzania
            var offersToScrape = await _context.AllegroOffersToScrape
                .Where(o => !o.IsScraped && !o.IsRejected && !o.IsProcessing)
                .OrderBy(o => o.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (!offersToScrape.Any())
            {
                return Ok(new { message = "No pending tasks available." });
            }

            // Oznacz pobrane zadania jako "w trakcie przetwarzania", aby inny scraper ich nie pobrał
            foreach (var offer in offersToScrape)
            {
                offer.IsProcessing = true;
            }
            await _context.SaveChangesAsync();

            scraper.Status = ScraperLiveStatus.Busy;
            // Zamiast jednego ID, możemy wysłać informację o liczbie zadań
            await _hubContext.Clients.All.SendAsync("UpdateDetailScraperStatus", scraper);

            // Zwróć paczkę zadań do Pythona
            var tasksForPython = offersToScrape.Select(o => new { taskId = o.Id, url = o.AllegroOfferUrl });
            return Ok(tasksForPython);
        }

        [HttpPost("submit-batch-results")]
        public async Task<IActionResult> SubmitBatchResults([FromBody] List<UrlResultDto> batchResults)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();
            if (batchResults == null || !batchResults.Any()) return BadRequest();

            var taskIds = batchResults.Select(r => r.TaskId).ToList();
            var offersToUpdate = await _context.AllegroOffersToScrape
                .Where(o => taskIds.Contains(o.Id))
                .ToListAsync();

            var newScrapedOffers = new List<AllegroScrapedOffer>();

            foreach (var result in batchResults)
            {
                var offer = offersToUpdate.FirstOrDefault(o => o.Id == result.TaskId);
                if (offer == null) continue;

                offer.IsProcessing = false;

                if (result.Status == "success")
                {
                    var validOffers = result.Offers
                        .Where(o => o.SellerName != "Brak sprzedawcy" && !string.IsNullOrWhiteSpace(o.SellerName))
                        .ToList();

                    offer.IsScraped = true;
                    offer.CollectedPricesCount = validOffers.Count;

                    foreach (var scraped in validOffers)
                    {
                        newScrapedOffers.Add(new AllegroScrapedOffer
                        {
                            AllegroOfferToScrapeId = offer.Id,
                            SellerName = scraped.SellerName,
                            Price = scraped.Price,
                            DeliveryCost = scraped.DeliveryCost,
                            DeliveryTime = scraped.DeliveryTime,
                            Popularity = scraped.Popularity,
                            // --- MAPOWANIE NOWYCH PÓL ---
                            SuperSeller = scraped.SuperSeller,
                            Smart = scraped.Smart
                            // -----------------------------
                        });
                    }
                }
                else
                {
                    offer.IsRejected = true;
                }

                await _hubContext.Clients.All.SendAsync("UpdateAllegroOfferRow", offer);
            }

            await _context.AllegroScrapedOffers.AddRangeAsync(newScrapedOffers);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Batch processed successfully." });
        }
    }

    public class ScrapedOfferDto
    {
        public string SellerName { get; set; }
        public decimal Price { get; set; }
        public decimal? DeliveryCost { get; set; }
        public int? DeliveryTime { get; set; }
        public int? Popularity { get; set; }


        public bool SuperSeller { get; set; }
        public bool Smart { get; set; }
        // ------------------
    }

    // DTO dla całej paczki wyników (pozostaje bez zmian)
    public class UrlResultDto
    {
        public int TaskId { get; set; }
        public string Status { get; set; }
        public List<ScrapedOfferDto> Offers { get; set; } = new List<ScrapedOfferDto>();
    }
}