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
            client.Timeout = TimeSpan.FromSeconds(60);

            string baseUrl = store.StoreApiUrl.TrimEnd('/');
            var chunks = items.Chunk(100).ToList(); // GET endpoint pozwala na 100 ID per request

            _logger.LogInformation($"[{store.StoreName}] IdoSell: {items.Count} produktów do pobrania, podzielone na {chunks.Count} paczek (po max 100).");

            int chunkIndex = 0;
            int totalMatched = 0;
            int totalPriceParsed = 0;
            int totalRetriedLater = 0;

            foreach (var chunk in chunks)
            {
                chunkIndex++;

                // Mapa: int productId -> CoOfrStoreData
                var itemsById = new Dictionary<int, CoOfrStoreData>();
                foreach (var x in chunk)
                {
                    if (int.TryParse(x.ProductExternalId, out int pid))
                    {
                        itemsById[pid] = x;
                    }
                    else
                    {
                        _logger.LogWarning($"[{store.StoreName}] IdoSell: pomijam nieprawidłowe ProductExternalId='{x.ProductExternalId}', oznaczam jako przetworzone.");
                        x.IsApiProcessed = true;
                    }
                }

                if (itemsById.Count == 0)
                {
                    _logger.LogWarning($"[{store.StoreName}] IdoSell: paczka {chunkIndex}/{chunks.Count} - brak prawidłowych ID, pomijam.");
                    continue;
                }

                // GET endpoint /api/admin/v3/products/products z parametrem productIds w query stringu.
                // IdoSell przyjmuje wartości oddzielone przecinkiem. Max 100 ID per request.
                var idsCsv = string.Join(",", itemsById.Keys);
                string requestUrl = $"{baseUrl}/api/admin/v3/products/products?productIds={idsCsv}";

                _logger.LogInformation($"[{store.StoreName}] IdoSell: paczka {chunkIndex}/{chunks.Count} -> GET (productIds: {itemsById.Count})");
                _logger.LogDebug($"[{store.StoreName}] IdoSell: full URL = {requestUrl}");

                bool transientError = false;

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                    request.Headers.Add("X-API-KEY", store.StoreApiKey);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    using var response = await client.SendAsync(request);
                    var jsonString = await response.Content.ReadAsStringAsync();

                    _logger.LogInformation($"[{store.StoreName}] IdoSell: paczka {chunkIndex}/{chunks.Count} <- HTTP {(int)response.StatusCode}, długość odpowiedzi: {jsonString?.Length ?? 0} znaków");

                    var preview = jsonString != null && jsonString.Length > 1500 ? jsonString.Substring(0, 1500) + "...[ucięte]" : jsonString;
                    _logger.LogDebug($"[{store.StoreName}] IdoSell: raw response preview = {preview}");

                    if (!response.IsSuccessStatusCode)
                    {
                        int status = (int)response.StatusCode;
                        if (status >= 500)
                        {
                            transientError = true;
                            _logger.LogWarning($"[{store.StoreName}] IdoSell: HTTP {status} (przejściowy). Paczka {chunkIndex} zostanie ponowiona w kolejnym przebiegu.");
                        }
                        else
                        {
                            _logger.LogWarning($"[{store.StoreName}] IdoSell: HTTP {status} (trwały). Treść: {preview}. Oznaczam paczkę jako przetworzoną.");
                            foreach (var item in itemsById.Values) item.IsApiProcessed = true;
                        }
                        continue;
                    }

                    JsonNode rootNode;
                    try
                    {
                        rootNode = JsonNode.Parse(jsonString);
                    }
                    catch (Exception parseEx)
                    {
                        _logger.LogError(parseEx, $"[{store.StoreName}] IdoSell: błąd parsowania JSON. Treść: {preview}");
                        foreach (var item in itemsById.Values) item.IsApiProcessed = true;
                        continue;
                    }

                    // Endpoint GET /products/products zwraca { "resultsLimit": N, "results": [...] }
                    JsonArray productsArray = rootNode?["results"] as JsonArray;

                    if (productsArray == null)
                    {
                        var keys = rootNode is JsonObject obj ? string.Join(",", obj.Select(k => k.Key)) : "(brak/inny typ)";
                        _logger.LogWarning($"[{store.StoreName}] IdoSell: nie znaleziono tablicy 'results'. Klucze top-level: {keys}. Oznaczam paczkę.");
                        foreach (var item in itemsById.Values) item.IsApiProcessed = true;
                        continue;
                    }

                    _logger.LogInformation($"[{store.StoreName}] IdoSell: paczka {chunkIndex} - odebrano {productsArray.Count} produktów w odpowiedzi.");

                    int matchedInChunk = 0;
                    int priceParsedInChunk = 0;

                    foreach (var productNode in productsArray)
                    {
                        if (productNode == null) continue;

                        var idStr = productNode["productId"]?.ToString();
                        if (!int.TryParse(idStr, out int productIdInt))
                        {
                            _logger.LogWarning($"[{store.StoreName}] IdoSell: produkt z odpowiedzi ma nieprawidłowe productId='{idStr}', pomijam.");
                            continue;
                        }

                        if (!itemsById.TryGetValue(productIdInt, out var itemToUpdate))
                        {
                            _logger.LogWarning($"[{store.StoreName}] IdoSell: API zwróciło productId={productIdInt}, którego nie pytaliśmy.");
                            continue;
                        }

                        matchedInChunk++;

                        decimal? grossPrice = ExtractIdoSellGrossPrice(productNode, store.StoreName, idStr);

                        if (grossPrice.HasValue && grossPrice.Value > 0)
                        {
                            itemToUpdate.ExtendedDataApiPrice = Math.Round(grossPrice.Value, 2);
                            itemToUpdate.IsApiProcessed = true;
                            priceParsedInChunk++;

                            _logger.LogInformation($"[{store.StoreName}] IdoSell: id={productIdInt} -> cena BRUTTO {itemToUpdate.ExtendedDataApiPrice} PLN");
                        }
                        else
                        {
                            itemToUpdate.IsApiProcessed = true;
                        }
                    }

                    // Produkty z naszej paczki, których API w ogóle nie zwróciło (np. usunięte ze sklepu)
                    int notReturnedCount = 0;
                    foreach (var item in itemsById.Values)
                    {
                        if (!item.IsApiProcessed)
                        {
                            item.IsApiProcessed = true;
                            notReturnedCount++;
                        }
                    }

                    totalMatched += matchedInChunk;
                    totalPriceParsed += priceParsedInChunk;

                    _logger.LogInformation($"[{store.StoreName}] IdoSell: paczka {chunkIndex} - dopasowanych: {matchedInChunk}/{itemsById.Count}, z ceną: {priceParsedInChunk}, niezwróconych przez API: {notReturnedCount}");
                }
                catch (TaskCanceledException tcEx)
                {
                    transientError = true;
                    _logger.LogWarning(tcEx, $"[{store.StoreName}] IdoSell: timeout w paczce {chunkIndex}/{chunks.Count}. Ponowimy w kolejnym przebiegu.");
                }
                catch (HttpRequestException httpEx)
                {
                    transientError = true;
                    _logger.LogWarning(httpEx, $"[{store.StoreName}] IdoSell: błąd sieciowy w paczce {chunkIndex}/{chunks.Count}. Ponowimy w kolejnym przebiegu.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[{store.StoreName}] IdoSell: nieoczekiwany wyjątek w paczce {chunkIndex}/{chunks.Count}. Oznaczam paczkę jako przetworzoną.");
                    foreach (var item in itemsById.Values) item.IsApiProcessed = true;
                }

                if (transientError)
                {
                    totalRetriedLater += itemsById.Count;
                }

                await Task.Delay(300);
            }

            _logger.LogInformation($"[{store.StoreName}] IdoSell: ZAKOŃCZONO. Dopasowanych: {totalMatched}, z ceną: {totalPriceParsed}, do ponowienia: {totalRetriedLater}, łącznie produktów: {items.Count}.");
        }

        private decimal? ExtractIdoSellGrossPrice(JsonNode productNode, string storeName, string productId)
        {
            var priceStr = productNode["productRetailPrice"]?.ToString();
            _logger.LogDebug($"[{storeName}] IdoSell: id={productId} - productRetailPrice='{priceStr ?? "(null)"}'");

            if (decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price) && price > 0)
            {
                return price;
            }

            _logger.LogWarning($"[{storeName}] IdoSell: id={productId} - brak ceny w productRetailPrice (wartość: '{priceStr ?? "(null)"}').");
            return null;
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