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

            var scraper = AllegroTaskManager.ActiveScrapers.AddOrUpdate(
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

            var cancelledTask = AllegroTaskManager.ActiveTasks.FirstOrDefault(t => t.Value.Status == ScrapingStatus.Cancelled);
            if (cancelledTask.Key != null)
            {
                await SendLog($"Zlecono scraperowi '{scraperName}' potwierdzenie anulacji zadania '{cancelledTask.Key}'.", "WARN");
                return Ok(new { cancelledUsername = cancelledTask.Key });
            }

            var pendingTask = AllegroTaskManager.ActiveTasks.FirstOrDefault(t => t.Value.Status == ScrapingStatus.Pending);
            if (pendingTask.Key != null)
            {
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

                return Ok(new { allegroUsername = pendingTask.Key });
            }

            return Ok(new { message = "Brak oczekujących zadań." });
        }

        [HttpPost("acknowledge-cancel/{username}")]

        public async Task<IActionResult> AcknowledgeCancel(string username, [FromHeader(Name = "X-Api-Key")] string receivedApiKey)

        {
            if (receivedApiKey != ApiKey) return Unauthorized();
            if (AllegroTaskManager.ActiveTasks.TryGetValue(username, out var taskState) && taskState.Status == ScrapingStatus.Cancelled)

            {
                if (AllegroTaskManager.ActiveTasks.TryRemove(username, out _))

                {

                    await _hubContext.Clients.All.SendAsync("TaskFinished", username);
                    return Ok(new { message = $"Anulowanie zadania dla '{username}' zostało potwierdzone i usunięte." });

                }
            }
            return NotFound("Nie znaleziono anulowanego zadania o podanej nazwie.");
        }

        [HttpPost("remove-scraper/{scraperName}")]
        public async Task<IActionResult> RemoveScraper(string scraperName, [FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey) return Unauthorized();

            if (AllegroTaskManager.ActiveScrapers.TryRemove(scraperName, out var scraper))
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

            if (AllegroTaskManager.ActiveTasks.TryGetValue(username, out var taskState) &&
                !string.IsNullOrEmpty(taskState.AssignedScraperName) &&
                AllegroTaskManager.ActiveScrapers.TryGetValue(taskState.AssignedScraperName, out var scraper))
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

                await _hubContext.Clients.All.SendAsync("UpdateTaskProgress", username, taskState);
                return Ok();
            }
            return NotFound();
        }

        [HttpPost("finish-task/{username}")]
        public async Task<IActionResult> FinishTask(string username, [FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey) return Unauthorized();

            if (AllegroTaskManager.ActiveTasks.TryRemove(username, out var finishedTask))
            {

                if (!string.IsNullOrEmpty(finishedTask.AssignedScraperName) &&
                    AllegroTaskManager.ActiveScrapers.TryGetValue(finishedTask.AssignedScraperName, out var scraper))
                {
                    scraper.Status = ScraperLiveStatus.Idle;
                    scraper.CurrentTaskUsername = null;
                    await _hubContext.Clients.All.SendAsync("UpdateScraperStatus", scraper);
                }

                await _hubContext.Clients.All.SendAsync("TaskFinished", username);
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

            var storeName = productDtos.First().StoreNameAllegro;
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.StoreNameAllegro == storeName);
            if (store == null) return NotFound($"Nie znaleziono sklepu dla {storeName}");

            // 1. Zamiast URL, pobieramy HashSet istniejących IdOnAllegro dla tego sklepu
            // Pobieramy tylko te, które nie są nullem
            var existingIdsForStore = await _context.AllegroProducts
                .Where(p => p.StoreId == store.StoreId && p.IdOnAllegro != null)
                .Select(p => p.IdOnAllegro)
                .ToHashSetAsync();

            // 2. Tworzymy listę kandydatów - zamieniamy DTO na obiekty domenowe
            // Musimy to zrobić TERAZ, aby wywołać CalculateIdFromUrl() przed sprawdzeniem duplikatu
            var candidates = new List<AllegroProductClass>();

            foreach (var dto in productDtos)
            {
                var product = new AllegroProductClass
                {
                    StoreId = store.StoreId,
                    AllegroProductName = dto.Name,
                    AllegroOfferUrl = dto.Url, // Zapisujemy oryginalny URL od scrapera
                    AddedDate = DateTime.UtcNow
                };

                // Tu dzieje się magia - wyciągamy ID z URL (np. z parametru offerId lub z końca stringa)
                product.CalculateIdFromUrl();

                candidates.Add(product);
            }

            // 3. Filtrujemy kandydatów
            // a) IdOnAllegro nie może być nullem (błąd parsowania URL)
            // b) IdOnAllegro nie może istnieć w bazie (existingIdsForStore)
            // c) DistinctBy - zabezpieczenie, gdyby scraper przysłał duplikaty w jednej paczce
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

            if (AllegroTaskManager.ActiveTasks.TryGetValue(storeName, out var taskState))
            {
                taskState.IncrementOffers(newProducts.Count);
                await _hubContext.Clients.All.SendAsync("UpdateTaskProgress", storeName, taskState);
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