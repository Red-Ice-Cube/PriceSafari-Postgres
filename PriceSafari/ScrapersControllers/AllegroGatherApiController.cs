using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.ScrapersControllers
{

    [ApiController]
    [Route("api/allegro-gather")]
    public class AllegroGatherApiController : ControllerBase
    {
        private readonly PriceSafariContext _context;

        private readonly IHubContext<ScrapingHub> _hubContext;
        private const string ApiKey = "Thdg12639Ghkdhiop273hdo2989wunwi618Hidoe8492CNWI7428";

        public AllegroGatherApiController(PriceSafariContext context, IHubContext<ScrapingHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        private async Task SendLog(string message, string level = "INFO")
        {
            await _hubContext.Clients.All.SendAsync("ReceiveLog", DateTime.Now.ToString("HH:mm:ss"), message, level);
        }

        [HttpGet("get-task")]
        public async Task<IActionResult> GetTask([FromQuery] string scraperName, [FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey) return Unauthorized();

            await SendLog($"Scraper '{scraperName}' zgłosił się po zadanie.", "DEBUG");

            var scraper = AllegroGatherManager.ActiveScrapers.AddOrUpdate(
                scraperName,
                new ScraperClient { Name = scraperName, Status = ScraperLiveStatus.Idle, LastCheckIn = DateTime.UtcNow },
                (key, existingClient) =>
                {
                    existingClient.Status = ScraperLiveStatus.Idle;
                    existingClient.LastCheckIn = DateTime.UtcNow;
                    existingClient.CurrentTaskUsername = null;
                    return existingClient;
                });
            await _hubContext.Clients.All.SendAsync("UpdateScraperStatus", scraper);

            var cancelledTask = AllegroGatherManager.ActiveTasks.FirstOrDefault(t => t.Value.Status == ScrapingStatus.Cancelled);
            if (cancelledTask.Key != null)
            {
                await SendLog($"Zlecono scraperowi '{scraperName}' potwierdzenie anulacji zadania '{cancelledTask.Key}'.", "WARN");
                return Ok(new { cancelledUsername = cancelledTask.Key });
            }

            var pendingTask = AllegroGatherManager.ActiveTasks.FirstOrDefault(t => t.Value.Status == ScrapingStatus.Pending);
            if (pendingTask.Key != null)
            {
                // 1. Zmiana: Szukamy sklepu, by sprawdzić czy ma "ExtraStoreNameOnAllegro"
                var store = await _context.Stores.FirstOrDefaultAsync(s => s.StoreNameAllegro == pendingTask.Key);
                string targetUsername = (!string.IsNullOrEmpty(store?.ExtraStoreNameOnAllegro))
                    ? store.ExtraStoreNameOnAllegro
                    : pendingTask.Key;

                var taskState = pendingTask.Value;
                taskState.Status = ScrapingStatus.Running;
                taskState.AssignedScraperName = scraperName;
                taskState.LastUpdateTime = DateTime.UtcNow;
                taskState.LastProgressMessage = "Rozpoczynanie pracy...";

                scraper.Status = ScraperLiveStatus.Busy;
                scraper.CurrentTaskUsername = pendingTask.Key;

                await SendLog($"Przydzielono zadanie '{pendingTask.Key}' do scrapera '{scraperName}'.");
                await _hubContext.Clients.All.SendAsync("UpdateScraperStatus", scraper);
                await _hubContext.Clients.All.SendAsync("UpdateTaskProgress", pendingTask.Key, taskState);

                return Ok(new { allegroUsername = targetUsername });
            }

            return Ok(new { message = "Brak oczekujących zadań." });
        }

        private async Task<string> ResolveMainUsername(string incomingUsername)
        {
            var store = await _context.Stores.FirstOrDefaultAsync(s =>
                s.StoreNameAllegro == incomingUsername ||
                s.ExtraStoreNameOnAllegro == incomingUsername);

            return store?.StoreNameAllegro ?? incomingUsername;
        }


        [HttpPost("acknowledge-cancel/{username}")]
        public async Task<IActionResult> AcknowledgeCancel(string username, [FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey) return Unauthorized();

            // Tłumaczymy incoming alias na główną nazwę
            string mainUsername = await ResolveMainUsername(username);

            if (AllegroGatherManager.ActiveTasks.TryGetValue(mainUsername, out var taskState) && taskState.Status == ScrapingStatus.Cancelled)
            {
                if (AllegroGatherManager.ActiveTasks.TryRemove(mainUsername, out _))
                {
                    await _hubContext.Clients.All.SendAsync("TaskFinished", mainUsername);
                    return Ok(new { message = $"Anulowanie zadania dla '{mainUsername}' zostało potwierdzone i usunięte." });
                }
            }
            return NotFound("Nie znaleziono anulowanego zadania o podanej nazwie.");
        }


        [HttpPost("remove-scraper/{scraperName}")]
        public async Task<IActionResult> RemoveScraper(string scraperName, [FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey) return Unauthorized();

            if (AllegroGatherManager.ActiveScrapers.TryRemove(scraperName, out var scraper))
            {
                await SendLog($"Użytkownik usunął scrapera '{scraperName}' z listy.", "WARN");
                await _hubContext.Clients.All.SendAsync("ScraperRemoved", scraperName);
                return Ok(new { message = "Scraper usunięty." });
            }
            return NotFound("Nie znaleziono scrapera.");
        }

        [HttpPost("report-progress/{username}")]
        public async Task<IActionResult> ReportProgress(string username, [FromBody] ProgressReportDto report, [FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey) return Unauthorized();

            // Tłumaczymy incoming alias na główną nazwę
            string mainUsername = await ResolveMainUsername(username);

            if (AllegroGatherManager.ActiveTasks.TryGetValue(mainUsername, out var taskState) &&
                !string.IsNullOrEmpty(taskState.AssignedScraperName) &&
                AllegroGatherManager.ActiveScrapers.TryGetValue(taskState.AssignedScraperName, out var scraper))
            {

                bool statusChanged = false;
                switch (report.Message)
                {
                    case "NETWORK_RESET_START":
                        scraper.Status = ScraperLiveStatus.ResettingNetwork;
                        taskState.LastProgressMessage = "Rozpoczęto resetowanie sieci z powodu CAPTCHA...";
                        statusChanged = true;
                        break;

                    case "NETWORK_RESET_SUCCESS":
                        scraper.Status = ScraperLiveStatus.Busy;

                        taskState.LastProgressMessage = "Sieć zresetowana. Wznawiam scrapowanie...";
                        statusChanged = true;
                        break;

                    default:

                        if (scraper.Status == ScraperLiveStatus.ResettingNetwork)
                        {
                            scraper.Status = ScraperLiveStatus.Busy;
                            statusChanged = true;
                        }
                        taskState.LastProgressMessage = report.Message;
                        break;
                }

                taskState.LastUpdateTime = DateTime.UtcNow;
                scraper.LastCheckIn = DateTime.UtcNow;

                if (statusChanged)
                {
                    await _hubContext.Clients.All.SendAsync("UpdateScraperStatus", scraper);
                }

                await _hubContext.Clients.All.SendAsync("UpdateTaskProgress", mainUsername, taskState);
                return Ok();
            }
            return NotFound();
        }



        [HttpPost("finish-task/{username}")]
        public async Task<IActionResult> FinishTask(string username, [FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey) return Unauthorized();

            string mainUsername = await ResolveMainUsername(username);

            if (AllegroGatherManager.ActiveTasks.TryRemove(mainUsername, out var finishedTask))
            {

                if (!string.IsNullOrEmpty(finishedTask.AssignedScraperName) &&
                    AllegroGatherManager.ActiveScrapers.TryGetValue(finishedTask.AssignedScraperName, out var scraper))
                {
                    scraper.Status = ScraperLiveStatus.Idle;
                    scraper.CurrentTaskUsername = null;
                    await _hubContext.Clients.All.SendAsync("UpdateScraperStatus", scraper);
                }

                await _hubContext.Clients.All.SendAsync("TaskFinished", mainUsername);
            }
            return Ok(new { message = "Zadanie oznaczone jako zakończone." });
        }



        [HttpPost("submit-products")]
        public async Task<IActionResult> SubmitProducts(
        [FromHeader(Name = "X-Api-Key")] string receivedApiKey,
        [FromBody] List<AllegroProductDto> productDtos)
        {
            if (receivedApiKey != ApiKey) return Unauthorized("Błędny klucz API.");
            if (productDtos == null || !productDtos.Any()) return BadRequest("Otrzymano pustą listę produktów.");

            // Scraper przysyła nam to, co dostał - czyli potencjalnie alias
            var incomingName = productDtos.First().StoreNameAllegro;

            // Szukamy po aliasie LUB nazwie głównej
            var store = await _context.Stores.FirstOrDefaultAsync(s =>
                s.StoreNameAllegro == incomingName ||
                s.ExtraStoreNameOnAllegro == incomingName);

            if (store == null) return NotFound($"Nie znaleziono sklepu dla {incomingName}");

            // Do reszty operacji bazodanowych i SignalR używamy GŁÓWNEJ nazwy z bazy!
            var mainStoreName = store.StoreNameAllegro;

            var existingIdsForStore = await _context.AllegroProducts
                .Where(p => p.StoreId == store.StoreId && p.IdOnAllegro != null)
                .Select(p => p.IdOnAllegro)
                .ToHashSetAsync();

            var candidates = new List<AllegroProductClass>();

            foreach (var dto in productDtos)
            {
                var product = new AllegroProductClass
                {
                    StoreId = store.StoreId,
                    AllegroProductName = dto.Name,
                    AllegroOfferUrl = dto.Url,

                    AddedDate = DateTime.UtcNow
                };

                product.CalculateIdFromUrl();

                candidates.Add(product);
            }

            var newProducts = candidates
                .Where(p => !string.IsNullOrEmpty(p.IdOnAllegro) && !existingIdsForStore.Contains(p.IdOnAllegro))
                .DistinctBy(p => p.IdOnAllegro)
                .ToList();

            if (!newProducts.Any())
            {
                return Ok(new { message = $"Otrzymano {productDtos.Count} produktów, ale wszystkie to duplikaty (na podstawie ID)." });
            }

            await _context.AllegroProducts.AddRangeAsync(newProducts);
            await _context.SaveChangesAsync();

            if (AllegroGatherManager.ActiveTasks.TryGetValue(mainStoreName, out var taskState))
            {
                taskState.IncrementOffers(newProducts.Count);
                await _hubContext.Clients.All.SendAsync("UpdateTaskProgress", mainStoreName, taskState);
            }

            return Ok(new { message = $"Zapisano {newProducts.Count} nowych produktów (pominięto {productDtos.Count - newProducts.Count} duplikatów)." });
        }
    }
    public class AllegroProductDto
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string StoreNameAllegro { get; set; }
    }

    public class ProgressReportDto
    {
        public string Message { get; set; }
    }

}