using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data; // Załóżmy, że tutaj masz swój DbContext
using PriceSafari.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;



namespace PriceSafari.ScrapersControllers
{



    [ApiController]
    [Route("api/allegro-gather")] // Używamy ścieżki typowej dla API
    public class AllegroGatherApiController : ControllerBase
    {
        private readonly PriceSafariContext _context; // Wstrzyknij swój DbContext
        private const string ApiKey = "twoj-super-tajny-klucz-api-123"; // Klucz API do testów

        public AllegroGatherApiController(PriceSafariContext context)
        {
            _context = context;
        }



        [HttpGet("get-task")]
        public IActionResult GetTask([FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey) return Unauthorized("Błędny klucz API.");

            // 1. Znajdź pierwsze zadanie ze stanem "Pending"
            var pendingTask = AllegroTaskManager.ActiveTasks
                .FirstOrDefault(t => t.Value == ScrapingStatus.Pending);

            if (pendingTask.Key != null)
            {
                // 2. Jeśli znaleziono, spróbuj ATOMOWO zmienić jego stan na "Running"
                // To zapobiega sytuacji, w której dwa scrapery pobiorą to samo zadanie.
                bool updated = AllegroTaskManager.ActiveTasks.TryUpdate(
                    key: pendingTask.Key,
                    newValue: ScrapingStatus.Running,
                    comparisonValue: ScrapingStatus.Pending // Zmień tylko jeśli stan to wciąż "Pending"
                );

                if (updated)
                {
                    // 3. Jeśli się udało, wyślij zadanie do scrapera
                    return Ok(new { allegroUsername = pendingTask.Key });
                }
            }

            // Jeśli nie ma zadań "Pending" lub aktualizacja się nie powiodła, zwróć pustą odpowiedź
            return Ok(new { message = "Brak oczekujących zadań." });
        }

        // NOWY ENDPOINT: Scraper pyta "Czy mam kontynuować pracę?"
        [HttpGet("check-status/{username}")]
        public IActionResult CheckStatus(string username, [FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey) return Unauthorized();

            if (AllegroTaskManager.ActiveTasks.TryGetValue(username, out var status))
            {
                return Ok(new { status = status.ToString() }); // Zwraca "Running" lub "Cancelled"
            }
            return NotFound("Zadanie nie zostało znalezione.");
        }

        // NOWY ENDPOINT: Scraper informuje "Skończyłem pracę"
        [HttpPost("finish-task/{username}")]
        public IActionResult FinishTask(string username, [FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey) return Unauthorized();

            // Usuwamy zakończone zadanie ze słownika aktywnych zadań
            AllegroTaskManager.ActiveTasks.TryRemove(username, out _);
            return Ok(new { message = "Zadanie oznaczone jako zakończone." });
        }

        // Endpoint "submit-products" pozostaje bez większych zmian
        [HttpPost("submit-products")]
        public async Task<IActionResult> SubmitProducts(
            [FromHeader(Name = "X-Api-Key")] string receivedApiKey,
            [FromBody] List<AllegroProductDto> productDtos)
        {
            // ... (logika zapisu do bazy jak poprzednio) ...
            if (receivedApiKey != ApiKey) return Unauthorized("Błędny klucz API.");
            if (productDtos == null || !productDtos.Any()) return BadRequest("Otrzymano pustą listę produktów.");
            var storeName = productDtos.First().StoreNameAllegro;
            var store = await _context.Stores.FirstOrDefaultAsync(s => s.StoreNameAllegro == storeName);
            if (store == null) return NotFound($"Nie znaleziono sklepu dla użytkownika Allegro: {storeName}");
            var newProducts = productDtos.Select(dto => new AllegroProductClass
            {
                StoreId = store.StoreId,
                AllegroProductName = dto.Name,
                AllegroOfferUrl = dto.Url,
                AddedDate = DateTime.UtcNow
            }).ToList();
            await _context.AllegroProducts.AddRangeAsync(newProducts);
            await _context.SaveChangesAsync();
            return Ok(new { message = $"Zapisano {newProducts.Count} produktów dla sklepu {store.StoreName}." });
        }
    }

    // Prosty obiekt DTO (Data Transfer Object) do przesyłania danych z Pythona
    public class AllegroProductDto
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string StoreNameAllegro { get; set; } // Dodajemy to, żeby wiedzieć, do kogo przypisać produkty
    }



}
