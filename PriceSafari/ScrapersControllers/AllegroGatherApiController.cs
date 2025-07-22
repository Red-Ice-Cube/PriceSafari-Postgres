using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
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
        private const string ApiKey = "twoj-super-tajny-klucz-api-123";

        public AllegroGatherApiController(PriceSafariContext context)
        {
            _context = context;
        }

        [HttpPost("start-task/{storeId}")]
        public async Task<IActionResult> StartTask(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null || string.IsNullOrEmpty(store.StoreNameAllegro))
            {
                return NotFound("Nie znaleziono sklepu lub sklep nie ma przypisanej nazwy Allegro.");
            }

            AllegroTaskQueue.UsernamesToScrape.Enqueue(store.StoreNameAllegro);

            return Ok(new { message = $"Zadanie dla '{store.StoreNameAllegro}' zostało dodane do kolejki." });
        }

        [HttpGet("get-task")]
        public IActionResult GetTask([FromHeader(Name = "X-Api-Key")] string receivedApiKey)
        {
            if (receivedApiKey != ApiKey)
            {
                return Unauthorized("Błędny klucz API.");
            }

            if (AllegroTaskQueue.UsernamesToScrape.TryDequeue(out var username))
            {

                return Ok(new { allegroUsername = username });
            }
            else
            {

                return Ok(new { message = "Brak zadań w kolejce." });
            }
        }

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

            await _context.AllegroProducts.AddRangeAsync(newProducts);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Zapisano {newProducts.Count} produktów dla sklepu {store.StoreName}." });
        }
    }

    public class AllegroProductDto
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string StoreNameAllegro { get; set; }
    }

    public static class AllegroTaskQueue
    {

        public static readonly ConcurrentQueue<string> UsernamesToScrape = new();
    }

}