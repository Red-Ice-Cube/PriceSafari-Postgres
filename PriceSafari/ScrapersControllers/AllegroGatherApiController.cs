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

        // === Endpoint do tworzenia zadania (wywoływany z Twojej aplikacji webowej) ===
        [HttpPost("start-task/{storeId}")]
        public async Task<IActionResult> StartTask(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null || string.IsNullOrEmpty(store.StoreNameAllegro))
            {
                return NotFound("Nie znaleziono sklepu lub sklep nie ma przypisanej nazwy Allegro.");
            }

            // Dodajemy nazwę użytkownika Allegro do naszej kolejki zadań
            AllegroTaskQueue.UsernamesToScrape.Enqueue(store.StoreNameAllegro);

            return Ok(new { message = $"Zadanie dla '{store.StoreNameAllegro}' zostało dodane do kolejki." });
        }

        // === Endpoint dla Pythona: "Czy jest praca?" ===
        [HttpGet("get-task")]
        public IActionResult GetTask([FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey)
            {
                return Unauthorized("Błędny klucz API.");
            }

            // Próbujemy pobrać zadanie z kolejki
            if (AllegroTaskQueue.UsernamesToScrape.TryDequeue(out var username))
            {
                // Jest zadanie! Wysyłamy je do scrapera.
                return Ok(new { allegroUsername = username });
            }
            else
            {
                // Kolejka pusta, nie ma pracy.
                return Ok(new { message = "Brak zadań w kolejce." });
            }
        }

        // === Endpoint dla Pythona: "Oto znalezione produkty" ===
        [HttpPost("submit-products")]
        public async Task<IActionResult> SubmitProducts(
            [FromHeader(Name = "X-Api-Key")] string receivedApiKey,
            [FromBody] List<AllegroProductDto> productDtos)
        {
            if (receivedApiKey != ApiKey)
            {
                return Unauthorized("Błędny klucz API.");
            }

            if (productDtos == null || !productDtos.Any())
            {
                return BadRequest("Otrzymano pustą listę produktów.");
            }

            // Tutaj znajdź StoreId na podstawie nazwy użytkownika Allegro
            var storeName = productDtos.First().StoreNameAllegro;
            var store = await _context.Stores
                                      .FirstOrDefaultAsync(s => s.StoreNameAllegro == storeName);

            if (store == null)
            {
                return NotFound($"Nie znaleziono sklepu dla użytkownika Allegro: {storeName}");
            }

            var newProducts = productDtos.Select(dto => new AllegroProductClass
            {
                StoreId = store.StoreId,
                AllegroProductName = dto.Name,
                AllegroOfferUrl = dto.Url,
                AddedDate = DateTime.UtcNow
            }).ToList();

            // Zapisujemy nowe produkty w bazie
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

    public static class AllegroTaskQueue
    {
        // ConcurrentQueue jest bezpieczna do użycia w środowisku wielowątkowym,
        // co jest kluczowe w aplikacjach webowych.
        // Przechowujemy tu nazwy użytkowników Allegro do zescrapowania.
        public static readonly ConcurrentQueue<string> UsernamesToScrape = new();
    }

}
