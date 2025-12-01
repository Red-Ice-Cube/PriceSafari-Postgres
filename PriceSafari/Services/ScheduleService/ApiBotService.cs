using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace PriceSafari.Services.ScheduleService
{
    public class ApiBotService
    {
        private readonly PriceSafariContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ApiBotService> _logger;

        public ApiBotService(PriceSafariContext context, IHttpClientFactory httpClientFactory, ILogger<ApiBotService> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // ZMIANA: Zwracamy wynik przetwarzania
        public async Task<ApiBotStoreResult> ProcessPendingApiRequestsAsync(int targetStoreId)
        {
            var result = new ApiBotStoreResult
            {
                StoreId = targetStoreId,
                ProductsProcessed = 0,
                WasSkipped = false
            };

            var store = await _context.Stores
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.StoreId == targetStoreId);

            if (store == null)
            {
                result.WasSkipped = true;
                result.Message = "Sklep nie istnieje.";
                return result;
            }

            result.StoreName = store.StoreName;
            result.SystemType = store.StoreSystemType.ToString();

            // 1. Sprawdzenie czy sklep ma włączoną obsługę API
            if (!store.FetchExtendedData || string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
            {
                // Pobieramy itemy tylko po to, by je oznaczyć jako pominięte (żeby nie wisiały)
                var itemsToSkip = await _context.CoOfrStoreDatas
                    .Where(d => d.StoreId == targetStoreId && !d.IsApiProcessed && d.ProductExternalId != null)
                    .ToListAsync();

                if (itemsToSkip.Any())
                {
                    MarkAsProcessed(itemsToSkip);
                    await _context.SaveChangesAsync();
                }

                result.WasSkipped = true;
                result.Message = "Wyłączone w ustawieniach lub brak kluczy API.";
                return result;
            }

            // 2. Pobranie itemów do przetworzenia
            var itemsToProcess = await _context.CoOfrStoreDatas
                .Where(d => d.StoreId == targetStoreId && !d.IsApiProcessed && d.ProductExternalId != null)
                .ToListAsync();

            if (!itemsToProcess.Any())
            {
                result.Message = "Brak produktów do przetworzenia.";
                return result;
            }

            result.ProductsProcessed = itemsToProcess.Count; // Zakładamy, że spróbujemy pobrać wszystkie
            _logger.LogInformation($"[{store.StoreName}] Rozpoczynam pobieranie {itemsToProcess.Count} produktów API.");

            try
            {
                switch (store.StoreSystemType)
                {
                    case StoreSystemType.PrestaShop:
                        await ProcessPrestaShopBatchAsync(store, itemsToProcess);
                        break;

                    case StoreSystemType.Shoper:
                    case StoreSystemType.WooCommerce:
                    case StoreSystemType.Custom:
                    default:
                        // Dla nieobsługiwanych tylko oznaczamy, ale nie liczymy jako "pobrane dane"
                        _logger.LogWarning($"[{store.StoreName}] System {store.StoreSystemType} nieobsługiwany.");
                        MarkAsProcessed(itemsToProcess);
                        result.ProductsProcessed = 0; // Resetujemy licznik, bo nic nie pobraliśmy
                        result.Message = "System nieobsługiwany.";
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd krytyczny API dla sklepu {store.StoreName}");
                result.Message = $"Błąd: {ex.Message}";
                // Mimo błędu zapisujemy stan (te co przeszły, przeszły)
            }

            await _context.SaveChangesAsync();
            return result;
        }

        private async Task ProcessPrestaShopBatchAsync(StoreClass store, List<CoOfrStoreData> items)
        {
            var client = _httpClientFactory.CreateClient();
            var authToken = Encoding.ASCII.GetBytes($"{store.StoreApiKey}:");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            string baseUrl = store.StoreApiUrl.TrimEnd('/');
            var chunks = items.Chunk(100).ToList();

            foreach (var chunk in chunks)
            {
                try
                {
                    var idsToFetch = string.Join("|", chunk.Select(x => x.ProductExternalId));
                    string requestUrl = $"{baseUrl}/products?display=[id,price]&filter[id]=[{idsToFetch}]&output_format=JSON";

                    var response = await client.GetAsync(requestUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonString = await response.Content.ReadAsStringAsync();
                        var rootNode = JsonNode.Parse(jsonString);
                        var productsArray = rootNode?["products"]?.AsArray();

                        if (productsArray != null)
                        {
                            foreach (var productNode in productsArray)
                            {
                                var idStr = productNode?["id"]?.ToString();
                                var priceStr = productNode?["price"]?.ToString();

                                var itemToUpdate = chunk.FirstOrDefault(x => x.ProductExternalId == idStr);

                                if (itemToUpdate != null && decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price))
                                {
                                    itemToUpdate.ExtendedDataApiPrice = Math.Round(price * 1.23m, 2);
                                    itemToUpdate.IsApiProcessed = true;
                                }
                            }
                        }

                        // Oznaczamy resztę (których API nie zwróciło) jako przetworzone
                        foreach (var item in chunk)
                        {
                            if (!item.IsApiProcessed) item.IsApiProcessed = true;
                        }
                    }
                    else
                    {
                        foreach (var item in chunk) item.IsApiProcessed = true; // Błąd HTTP, pomijamy
                    }
                }
                catch
                {
                    foreach (var item in chunk) item.IsApiProcessed = true; // Błąd Exception, pomijamy
                }

                await Task.Delay(200); // Małe opóźnienie między paczkami
            }
        }

        private void MarkAsProcessed(List<CoOfrStoreData> items)
        {
            foreach (var item in items) item.IsApiProcessed = true;
        }
    


        public class ApiBotStoreResult
        {
            public int StoreId { get; set; }
            public string StoreName { get; set; }
            public string SystemType { get; set; } // np. PrestaShop, WooCommerce
            public int ProductsProcessed { get; set; } // Ile faktycznie pobrano z API
            public bool WasSkipped { get; set; } // Czy sklep był wyłączony/pominięty
            public string Message { get; set; } // Ewentualny błąd lub info
        }
    }
}