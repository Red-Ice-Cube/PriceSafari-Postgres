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
        private const int BatchSize = 100;

        public AllegroScrapeApiController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        // === NOWY ENDPOINT DO USTAWIEŃ ===
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            // Sprawdzenie API KEY (opcjonalne dla settings, ale dobra praktyka)
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

            var settings = await _context.Settings.FirstOrDefaultAsync();

            if (settings == null)
            {
                // Domyślne wartości, jeśli brak rekordu w bazie
                return Ok(new
                {
                    generatorsCount = 1,
                    headlessMode = true,
                    maxWorkers = 1
                });
            }

            return Ok(new
            {
                // Mapujemy Twoje nowe pola z modelu Settings
                generatorsCount = settings.GeneratorsAllegroCount > 0 ? settings.GeneratorsAllegroCount : 1,
                headlessMode = settings.HeadLessForAllegroGenerators,
                maxWorkers = settings.SemophoreAllegroCount > 0 ? settings.SemophoreAllegroCount : 1
            });
        }
        // =================================

        [HttpGet("get-task")]
        public async Task<IActionResult> GetTaskBatch([FromQuery] string scraperName)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();

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

            var offersToScrape = await _context.AllegroOffersToScrape
                .Where(o => !o.IsScraped && !o.IsRejected && !o.IsProcessing)
                .OrderBy(o => o.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (!offersToScrape.Any())
            {
                var anyTasksStillProcessing = await _context.AllegroOffersToScrape.AnyAsync(o => o.IsProcessing);

                if (!anyTasksStillProcessing && AllegroScrapeManager.CurrentStatus == ScrapingProcessStatus.Running)
                {
                    AllegroScrapeManager.CurrentStatus = ScrapingProcessStatus.Idle;
                    AllegroScrapeManager.ScrapingStartTime = null;
                    await _hubContext.Clients.All.SendAsync("UpdateScrapingProcessStatus", new { status = "Idle" });

                    return Ok(new { message = "No pending tasks available. Scraping process has been completed and stopped." });
                }

                return Ok(new { message = "No pending tasks available." });
            }

            foreach (var offer in offersToScrape)
            {
                offer.IsProcessing = true;
            }
            await _context.SaveChangesAsync();

            foreach (var offer in offersToScrape)
            {
                await _hubContext.Clients.All.SendAsync("UpdateAllegroOfferRow", offer);
            }

            scraper.Status = ScraperLiveStatus.Busy;
            scraper.CurrentTaskId = offersToScrape.FirstOrDefault()?.Id;
            await _hubContext.Clients.All.SendAsync("UpdateDetailScraperStatus", scraper);

            // Zwracamy format zgodny z tym, czego oczekuje Python
            var tasksForPython = offersToScrape.Select(o => new { taskId = o.Id, url = o.AllegroOfferUrl });
            return Ok(tasksForPython);
        }

        [HttpPost("submit-batch-results")]
        public async Task<IActionResult> SubmitBatchResults([FromBody] List<UrlResultDto> batchResults)
        {
            if (Request.Headers["X-Api-Key"] != ApiKey) return Unauthorized();
            if (batchResults == null || !batchResults.Any()) return BadRequest();

            // Opcjonalnie: Przeróbka na asynchroniczne przetwarzanie w tle (Task.Run) jak w Google Scraperze,
            // ale na razie zostawiam logikę biznesową Allegro bez zmian, by nie zepsuć zapisu.

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
                            SuperSeller = scraped.SuperSeller,
                            Smart = scraped.Smart,
                            IsBestPriceGuarantee = scraped.IsBestPriceGuarantee,
                            TopOffer = scraped.TopOffer,
                            SuperPrice = scraped.SuperPrice,
                            Promoted = scraped.Promoted,
                            Sponsored = scraped.Sponsored,
                            IdAllegro = scraped.IdAllegro,
                        });
                    }
                }
                else
                {
                    offer.IsRejected = true;
                }

                await _hubContext.Clients.All.SendAsync("UpdateAllegroOfferRow", offer);
            }

            if (newScrapedOffers.Any())
            {
                await _context.AllegroScrapedOffers.AddRangeAsync(newScrapedOffers);
            }
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
        public bool IsBestPriceGuarantee { get; set; }
        public bool TopOffer { get; set; }
        public bool SuperPrice { get; set; }
        public bool Promoted { get; set; }
        public bool Sponsored { get; set; }
        public long IdAllegro { get; set; }
    }

    public class UrlResultDto
    {
        public int TaskId { get; set; }
        public string Status { get; set; }
        public List<ScrapedOfferDto> Offers { get; set; } = new List<ScrapedOfferDto>();
    }
}