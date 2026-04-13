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

            if (!store.FetchExtendedData || string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
            {

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

            var itemsToProcess = await _context.CoOfrStoreDatas
                .Where(d => d.StoreId == targetStoreId && !d.IsApiProcessed && d.ProductExternalId != null)
                .ToListAsync();

            if (!itemsToProcess.Any())
            {
                result.Message = "Brak produktów do przetworzenia.";
                return result;
            }

            result.ProductsProcessed = itemsToProcess.Count;

            _logger.LogInformation($"[{store.StoreName}] Rozpoczynam pobieranie {itemsToProcess.Count} produktów API.");

            try
            {
                switch (store.StoreSystemType)
                {
                    case StoreSystemType.PrestaShop:
                        await ProcessPrestaShopBatchAsync(store, itemsToProcess);
                        break;

                    case StoreSystemType.IdoSell: // <--- DODANY CASE DLA IDOSELL
                        await ProcessIdoSellBatchAsync(store, itemsToProcess);
                        break;

                    case StoreSystemType.Shoper:
                    case StoreSystemType.WooCommerce:
                    case StoreSystemType.Custom:
                    default:
                        _logger.LogWarning($"[{store.StoreName}] System {store.StoreSystemType} nieobsługiwany.");
                        MarkAsProcessed(itemsToProcess);
                        result.ProductsProcessed = 0;
                        result.Message = "System nieobsługiwany.";
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd krytyczny API dla sklepu {store.StoreName}");
                result.Message = $"Błąd: {ex.Message}";

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

                        foreach (var item in chunk)
                        {
                            if (!item.IsApiProcessed) item.IsApiProcessed = true;
                        }
                    }
                    else
                    {
                        foreach (var item in chunk) item.IsApiProcessed = true;

                    }
                }
                catch
                {
                    foreach (var item in chunk) item.IsApiProcessed = true;

                }

                await Task.Delay(200);

            }
        }

        private async Task ProcessIdoSellBatchAsync(StoreClass store, List<CoOfrStoreData> items)
        {
            var client = _httpClientFactory.CreateClient();

            // 1. Nowa, poprawna autoryzacja - dedykowany klucz API wstawiany z bazy
            client.DefaultRequestHeaders.Add("X-API-KEY", store.StoreApiKey);

            // Wymagany przez IdoSell nagłówek Accept
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Zabezpieczenie przed podwójnym slashem w URL
            string baseUrl = store.StoreApiUrl.TrimEnd('/');

            // Dzielimy na paczki po 50 sztuk
            var chunks = items.Chunk(50).ToList();

            foreach (var chunk in chunks)
            {
                try
                {
                    string requestUrl = $"{baseUrl}/api/admin/v3/products/products/get";

                    var requestBody = new
                    {
                        @params = new
                        {
                            products = chunk.Select(x => int.Parse(x.ProductExternalId)).ToArray()
                        }
                    };

                    var jsonContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(requestUrl, jsonContent);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonString = await response.Content.ReadAsStringAsync();
                        var rootNode = JsonNode.Parse(jsonString);

                        // IdoSell v3 zwraca produkty bezpośrednio w formie tablicy na głównym poziomie
                        var productsArray = rootNode as JsonArray;

                        if (productsArray != null)
                        {
                            foreach (var productNode in productsArray)
                            {
                                var idStr = productNode?["productId"]?.ToString();

                                // 2. Nowa ścieżka do ceny - dopasowana do Twojego JSON-a
                                var priceStr = productNode?["productRetailPrice"]?.ToString();

                                var itemToUpdate = chunk.FirstOrDefault(x => x.ProductExternalId == idStr);

                                if (itemToUpdate != null && decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price))
                                {
                                    // Mamy cenę brutto z IdoSell
                                    itemToUpdate.ExtendedDataApiPrice = Math.Round(price, 2);
                                    itemToUpdate.IsApiProcessed = true;
                                }
                            }
                        }

                        // Oznaczamy resztę jako przetworzoną (np. produkty usunięte ze sklepu)
                        foreach (var item in chunk)
                        {
                            if (!item.IsApiProcessed) item.IsApiProcessed = true;
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"[{store.StoreName}] Błąd odpytywania IdoSell: HTTP {(int)response.StatusCode}. URL: {requestUrl}");
                        foreach (var item in chunk) item.IsApiProcessed = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[{store.StoreName}] Błąd przetwarzania paczki IdoSell.");
                    foreach (var item in chunk) item.IsApiProcessed = true;
                }

                await Task.Delay(300);
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
            public string SystemType { get; set; }

            public int ProductsProcessed { get; set; }

            public bool WasSkipped { get; set; }

            public string Message { get; set; }

        }
    }
}