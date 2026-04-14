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
            client.DefaultRequestHeaders.Add("X-API-KEY", store.StoreApiKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            string baseUrl = store.StoreApiUrl.TrimEnd('/');
            var chunks = items.Chunk(50).ToList();

            _logger.LogInformation($"[{store.StoreName}] IdoSell: {items.Count} produktów do pobrania, podzielone na {chunks.Count} paczek (po max 50).");

            int chunkIndex = 0;
            int totalMatched = 0;
            int totalPriceParsed = 0;

            foreach (var chunk in chunks)
            {
                chunkIndex++;
                // IdoSell ebikeserwis.pl - działa v3, v5 zwraca 404
                string requestUrl = $"{baseUrl}/api/admin/v3/products/products/get";

                var productIds = new List<int>();
                foreach (var x in chunk)
                {
                    if (int.TryParse(x.ProductExternalId, out int pid))
                        productIds.Add(pid);
                    else
                        _logger.LogWarning($"[{store.StoreName}] IdoSell: pomijam nieprawidłowe ProductExternalId='{x.ProductExternalId}'.");
                }

                if (productIds.Count == 0)
                {
                    _logger.LogWarning($"[{store.StoreName}] IdoSell: paczka {chunkIndex}/{chunks.Count} - brak prawidłowych ID, oznaczam jako przetworzone.");
                    foreach (var item in chunk) item.IsApiProcessed = true;
                    continue;
                }

                var requestBody = new
                {
                    @params = new
                    {
                        products = productIds.ToArray()
                    }
                };

                var requestJson = System.Text.Json.JsonSerializer.Serialize(requestBody);
                _logger.LogInformation($"[{store.StoreName}] IdoSell: paczka {chunkIndex}/{chunks.Count} -> POST {requestUrl} | IDs: [{string.Join(",", productIds)}]");
                _logger.LogDebug($"[{store.StoreName}] IdoSell: request body = {requestJson}");

                try
                {
                    var jsonContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(requestUrl, jsonContent);
                    var jsonString = await response.Content.ReadAsStringAsync();

                    _logger.LogInformation($"[{store.StoreName}] IdoSell: paczka {chunkIndex}/{chunks.Count} <- HTTP {(int)response.StatusCode}, długość odpowiedzi: {jsonString?.Length ?? 0} znaków");

                    var preview = jsonString != null && jsonString.Length > 1500 ? jsonString.Substring(0, 1500) + "...[ucięte]" : jsonString;
                    _logger.LogDebug($"[{store.StoreName}] IdoSell: raw response preview = {preview}");

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogWarning($"[{store.StoreName}] IdoSell: błąd HTTP {(int)response.StatusCode}. Treść: {preview}");
                        foreach (var item in chunk) item.IsApiProcessed = true;
                        continue;
                    }

                    var rootNode = JsonNode.Parse(jsonString);

                    // IdoSell v3 zwraca { "results": [...] }. Awaryjnie obsługujemy też "products" oraz tablicę top-level.
                    JsonArray productsArray =
                        (rootNode?["results"] as JsonArray)
                        ?? (rootNode?["products"] as JsonArray)
                        ?? (rootNode as JsonArray);

                    if (productsArray == null)
                    {
                        var keys = rootNode is JsonObject obj ? string.Join(",", obj.Select(k => k.Key)) : "(brak/inny typ)";
                        _logger.LogWarning($"[{store.StoreName}] IdoSell: nie znaleziono tablicy 'results'/'products' w odpowiedzi. Klucze top-level: {keys}");
                        foreach (var item in chunk) item.IsApiProcessed = true;
                        continue;
                    }

                    _logger.LogInformation($"[{store.StoreName}] IdoSell: paczka {chunkIndex} - odebrano {productsArray.Count} produktów w odpowiedzi.");

                    int matchedInChunk = 0;
                    int priceParsedInChunk = 0;

                    foreach (var productNode in productsArray)
                    {
                        if (productNode == null) continue;

                        var idStr = productNode["productId"]?.ToString();

                        decimal? grossPrice = ExtractIdoSellGrossPrice(productNode, store.StoreName, idStr);

                        var itemToUpdate = chunk.FirstOrDefault(x => x.ProductExternalId == idStr);
                        if (itemToUpdate == null)
                        {
                            _logger.LogWarning($"[{store.StoreName}] IdoSell: nie znaleziono dopasowania w paczce dla productId={idStr}.");
                            continue;
                        }

                        matchedInChunk++;

                        if (grossPrice.HasValue && grossPrice.Value > 0)
                        {
                            itemToUpdate.ExtendedDataApiPrice = Math.Round(grossPrice.Value, 2);
                            itemToUpdate.IsApiProcessed = true;
                            priceParsedInChunk++;

                            _logger.LogInformation($"[{store.StoreName}] IdoSell: id={idStr} -> cena BRUTTO {itemToUpdate.ExtendedDataApiPrice} PLN");
                        }
                        else
                        {
                            _logger.LogWarning($"[{store.StoreName}] IdoSell: id={idStr} - nie udało się wyciągnąć ceny brutto.");
                        }
                    }

                    totalMatched += matchedInChunk;
                    totalPriceParsed += priceParsedInChunk;

                    _logger.LogInformation($"[{store.StoreName}] IdoSell: paczka {chunkIndex} - dopasowanych: {matchedInChunk}/{chunk.Length}, sparsowanych cen: {priceParsedInChunk}");

                    foreach (var item in chunk)
                    {
                        if (!item.IsApiProcessed) item.IsApiProcessed = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[{store.StoreName}] IdoSell: wyjątek w paczce {chunkIndex}/{chunks.Count}.");
                    foreach (var item in chunk) item.IsApiProcessed = true;
                }

                await Task.Delay(300);
            }

            _logger.LogInformation($"[{store.StoreName}] IdoSell: ZAKOŃCZONO. Łącznie dopasowanych: {totalMatched}, sparsowanych cen: {totalPriceParsed} z {items.Count} produktów.");
        }

        /// <summary>
        /// Wyciąga finalną cenę BRUTTO produktu z odpowiedzi IdoSell v3.
        /// IdoSell zwraca productRetailPrice już jako brutto (dla produktów ze stawką VAT).
        /// Strategia:
        ///   1) productRetailPrice na głównym poziomie produktu (najczęstsze, działa dla produktów bez wariantów)
        ///   2) productShopsAttributes[].productRetailPrice (gdy główny poziom jest zerowy)
        ///   3) productSizesAttributes[].productRetailPrice (pierwszy rozmiar z niezerową ceną)
        /// UWAGA: NIE używamy productPosPrice - to cena dla POS, inna od ceny e-commerce.
        /// </summary>
        private decimal? ExtractIdoSellGrossPrice(JsonNode productNode, string storeName, string productId)
        {
            // ŚCIEŻKA 1: productRetailPrice na głównym poziomie
            var topLevelStr = productNode["productRetailPrice"]?.ToString();
            _logger.LogDebug($"[{storeName}] IdoSell: id={productId} - top-level productRetailPrice='{topLevelStr ?? "(null)"}'");

            if (decimal.TryParse(topLevelStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal topPrice) && topPrice > 0)
            {
                return topPrice;
            }

            // ŚCIEŻKA 2: productShopsAttributes - cena per sklep (przydatne gdy są multi-shopy)
            var shopsAttrs = productNode["productShopsAttributes"] as JsonArray;
            if (shopsAttrs != null)
            {
                foreach (var shopAttr in shopsAttrs)
                {
                    if (shopAttr == null) continue;
                    var shopPriceStr = shopAttr["productRetailPrice"]?.ToString();
                    _logger.LogDebug($"[{storeName}] IdoSell: id={productId} - shopAttr productRetailPrice='{shopPriceStr ?? "(null)"}'");

                    if (decimal.TryParse(shopPriceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal shopPrice) && shopPrice > 0)
                    {
                        return shopPrice;
                    }
                }
            }

            // ŚCIEŻKA 3: productSizesAttributes - per rozmiar/wariant
            var sizesAttrs = productNode["productSizesAttributes"] as JsonArray;
            if (sizesAttrs != null)
            {
                _logger.LogDebug($"[{storeName}] IdoSell: id={productId} - znaleziono {sizesAttrs.Count} rozmiarów w productSizesAttributes.");

                foreach (var size in sizesAttrs)
                {
                    if (size == null) continue;
                    var sizeId = size["sizeId"]?.ToString();
                    var sizePriceStr = size["productRetailPrice"]?.ToString();
                    _logger.LogDebug($"[{storeName}] IdoSell: id={productId} - rozmiar '{sizeId}' productRetailPrice='{sizePriceStr ?? "(null)"}'");

                    if (decimal.TryParse(sizePriceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal sizePrice) && sizePrice > 0)
                    {
                        return sizePrice;
                    }
                }
            }

            _logger.LogWarning($"[{storeName}] IdoSell: id={productId} - żadna ze ścieżek nie zwróciła ceny > 0.");
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