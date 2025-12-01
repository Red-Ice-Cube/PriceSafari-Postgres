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

        // <summary>

        // Przetwarza oczekujące zadania API.

        // </summary>

        // <param name="targetStoreId">Opcjonalne ID sklepu. Jeśli podane, przetworzy tylko ten sklep. Jeśli null, przetworzy wszystkie.</param>

        public async Task ProcessPendingApiRequestsAsync(int? targetStoreId = null)
        {
            if (targetStoreId.HasValue)
            {
                _logger.LogInformation($"Rozpoczynam przetwarzanie zadań API TYLKO dla sklepu ID: {targetStoreId.Value}...");
            }
            else
            {
                _logger.LogInformation("Rozpoczynam GLOBALNE przetwarzanie kolejek API dla wszystkich sklepów...");
            }

            var query = _context.CoOfrStoreDatas
                .Where(d => !d.IsApiProcessed && d.ProductExternalId != null);

            if (targetStoreId.HasValue)
            {
                query = query.Where(d => d.StoreId == targetStoreId.Value);
            }

            var pendingItems = await query.ToListAsync();

            if (!pendingItems.Any())
            {
                _logger.LogInformation("Brak oczekujących zadań API do przetworzenia.");
                return;
            }

            _logger.LogInformation($"Znaleziono łącznie {pendingItems.Count} zadań do przetworzenia.");

            var groupedItems = pendingItems.GroupBy(d => d.StoreId);

            foreach (var group in groupedItems)
            {
                int storeId = group.Key;
                var itemsToProcess = group.ToList();

                var store = await _context.Stores
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.StoreId == storeId);

                if (store == null || !store.FetchExtendedData || string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
                {
                    _logger.LogWarning($"Sklep ID {storeId} ma zadania API, ale brakuje konfiguracji lub opcja jest wyłączona. Oznaczam zadania jako pominięte.");
                    MarkAsProcessed(itemsToProcess);
                    await _context.SaveChangesAsync();
                    continue;
                }

                _logger.LogInformation($"Przetwarzanie {itemsToProcess.Count} produktów dla sklepu: {store.StoreName} (System: {store.StoreSystemType})");

                try
                {

                    switch (store.StoreSystemType)
                    {
                        case StoreSystemType.PrestaShop:
                            await ProcessPrestaShopBatchAsync(store, itemsToProcess);
                            break;

                        case StoreSystemType.Shoper:
                            _logger.LogWarning($"Implementacja dla Shoper/ClickShop nie jest jeszcze gotowa.");
                            MarkAsProcessed(itemsToProcess);
                            break;

                        case StoreSystemType.WooCommerce:
                            _logger.LogWarning($"Implementacja dla WooCommerce nie jest jeszcze gotowa.");
                            MarkAsProcessed(itemsToProcess);
                            break;

                        case StoreSystemType.Custom:
                        default:
                            _logger.LogWarning($"Nieobsługiwany typ systemu: {store.StoreSystemType} dla sklepu {store.StoreName}.");
                            MarkAsProcessed(itemsToProcess);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Krytyczny błąd podczas przetwarzania batcha API dla sklepu {store.StoreName}");
                }

                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Zakończono przetwarzanie kolejek API.");
        }

        private async Task ProcessPrestaShopBatchAsync(StoreClass store, List<CoOfrStoreData> items)
        {
            var client = _httpClientFactory.CreateClient();

            var authToken = Encoding.ASCII.GetBytes($"{store.StoreApiKey}:");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            string baseUrl = store.StoreApiUrl.TrimEnd('/');

            var chunks = items.Chunk(100).ToList();

            _logger.LogInformation($"[PrestaShop] Rozpoczynam pobieranie w trybie BATCH. Ilość paczek: {chunks.Count}");

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
                            if (!item.IsApiProcessed)
                            {

                                item.IsApiProcessed = true;
                                _logger.LogWarning($"[PrestaShop] ID {item.ProductExternalId} nie zostało zwrócone przez API (może być nieaktywne).");
                            }
                        }

                        _logger.LogInformation($"[PrestaShop] Przetworzono paczkę {chunk.Length} produktów.");
                    }
                    else
                    {
                        _logger.LogWarning($"[PrestaShop] Błąd HTTP {response.StatusCode} dla paczki ID: {idsToFetch}");

                        foreach (var item in chunk) item.IsApiProcessed = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PrestaShop] Błąd krytyczny podczas przetwarzania paczki.");

                    foreach (var item in chunk) item.IsApiProcessed = true;
                }

                await Task.Delay(200);
            }
        }

        private void MarkAsProcessed(List<CoOfrStoreData> items)
        {
            foreach (var item in items)
            {
                item.IsApiProcessed = true;
            }
        }
    }
}