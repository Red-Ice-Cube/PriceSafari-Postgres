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

        /// <summary>
        /// Przetwarza oczekujące zadania API.
        /// </summary>
        /// <param name="targetStoreId">Opcjonalne ID sklepu. Jeśli podane, przetworzy tylko ten sklep. Jeśli null, przetworzy wszystkie.</param>
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

            // 1. Budujemy zapytanie bazowe
            var query = _context.CoOfrStoreDatas
                .Where(d => !d.IsApiProcessed && d.ProductExternalId != null);

            // 2. Jeśli podano konkretny sklep, zawężamy wyniki
            if (targetStoreId.HasValue)
            {
                query = query.Where(d => d.StoreId == targetStoreId.Value);
            }

            // 3. Pobieramy dane
            var pendingItems = await query.ToListAsync();

            if (!pendingItems.Any())
            {
                _logger.LogInformation("Brak oczekujących zadań API do przetworzenia.");
                return;
            }

            _logger.LogInformation($"Znaleziono łącznie {pendingItems.Count} zadań do przetworzenia.");

            // 4. Grupujemy po StoreId (nawet jeśli to jeden sklep, logika pozostaje uniwersalna)
            var groupedItems = pendingItems.GroupBy(d => d.StoreId);

            foreach (var group in groupedItems)
            {
                int storeId = group.Key;
                var itemsToProcess = group.ToList();

                // 5. Pobieramy konfigurację sklepu
                var store = await _context.Stores
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.StoreId == storeId);

                // Walidacja konfiguracji sklepu
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
                    // 6. Router systemów sklepowych
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

                // Zapisujemy zmiany w bazie po każdym sklepie
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Zakończono przetwarzanie kolejek API.");
        }

        private async Task ProcessPrestaShopBatchAsync(StoreClass store, List<CoOfrStoreData> items)
        {
            var client = _httpClientFactory.CreateClient();

            // Konfiguracja Basic Auth
            var authToken = Encoding.ASCII.GetBytes($"{store.StoreApiKey}:");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            string baseUrl = store.StoreApiUrl.TrimEnd('/');

            // 1. Dzielimy listę na paczki po 50 sztuk (Batching)
            // PrestaShop zazwyczaj radzi sobie z ~50-100 ID w filtrze URL.
            var chunks = items.Chunk(50).ToList();

            _logger.LogInformation($"[PrestaShop] Rozpoczynam pobieranie w trybie BATCH. Ilość paczek: {chunks.Count}");

            foreach (var chunk in chunks)
            {
                try
                {
                    // 2. Budujemy listę ID do filtru: [1|2|3|4]
                    var idsToFetch = string.Join("|", chunk.Select(x => x.ProductExternalId));

                    // 3. Budujemy URL zoptymalizowany:
                    // display=[id,price] -> pobiera TYLKO id i cenę (oszczędność transferu)
                    // filter[id]=[...] -> pobiera wiele produktów na raz
                    string requestUrl = $"{baseUrl}/products?display=[id,price]&filter[id]=[{idsToFetch}]&output_format=JSON";

                    var response = await client.GetAsync(requestUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonString = await response.Content.ReadAsStringAsync();
                        var rootNode = JsonNode.Parse(jsonString);

                        // Presta przy liście zwraca tablicę "products": [ {id:1, price:...}, {id:2, price:...} ]
                        var productsArray = rootNode?["products"]?.AsArray();

                        if (productsArray != null)
                        {
                            // Mapujemy wyniki z API do naszych obiektów w pamięci
                            foreach (var productNode in productsArray)
                            {
                                var idStr = productNode?["id"]?.ToString();
                                var priceStr = productNode?["price"]?.ToString();

                                // Znajdź odpowiedni obiekt w chunku po ID
                                var itemToUpdate = chunk.FirstOrDefault(x => x.ProductExternalId == idStr);

                                // --- Fragment metody ProcessPrestaShopBatchAsync ---

                                if (itemToUpdate != null && decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price))
                                {
                                    // POPRAWKA: Presta zwraca netto, mnożymy przez 1.23 (VAT 23%)
                                    // Używamy sufiksu 'm' dla typu decimal (1.23m)
                                    itemToUpdate.ExtendedDataApiPrice = Math.Round(price * 1.23m, 2);

                                    // Oznaczamy sukces
                                    itemToUpdate.IsApiProcessed = true;
                                }
                            }
                        }

                        // WAŻNE: Obsługa produktów, których API nie zwróciło (np. usunięte w sklepie, ale my mamy ID)
                        // Oznaczamy je jako przetworzone, żeby nie pytać o nie w nieskończoność.
                        foreach (var item in chunk)
                        {
                            if (!item.IsApiProcessed)
                            {
                                // Jeśli po przetworzeniu odpowiedzi nadal false, to znaczy, że API nie zwróciło tego ID
                                // (np. produkt usunięty/nieaktywny w PrestaShop)
                                item.IsApiProcessed = true;
                                _logger.LogWarning($"[PrestaShop] ID {item.ProductExternalId} nie zostało zwrócone przez API (może być nieaktywne).");
                            }
                        }

                        _logger.LogInformation($"[PrestaShop] Przetworzono paczkę {chunk.Length} produktów.");
                    }
                    else
                    {
                        _logger.LogWarning($"[PrestaShop] Błąd HTTP {response.StatusCode} dla paczki ID: {idsToFetch}");
                        // W przypadku błędu całej paczki (np. URL za długi), oznaczamy jako przetworzone "na siłę"
                        // lub zostawiamy false, by spróbować pojedynczo (tutaj oznaczamy, by nie blokować).
                        foreach (var item in chunk) item.IsApiProcessed = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PrestaShop] Błąd krytyczny podczas przetwarzania paczki.");
                    // Zabezpieczenie przed pętlą
                    foreach (var item in chunk) item.IsApiProcessed = true;
                }

                // Krótkie opóźnienie między paczkami, żeby nie zabić serwera
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