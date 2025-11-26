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

        public async Task ProcessPendingApiRequestsAsync()
        {
            _logger.LogInformation("Rozpoczynam przetwarzanie kolejek API dla sklepów...");

            // 1. Pobieramy nieprzetworzone rekordy, które mają zewnętrzne ID (bez ID nie zapytamy API)
            // Używamy Include, by mieć dostęp do danych powiązanych, jeśli będą potrzebne, ale tutaj kluczowe jest StoreId
            var pendingItems = await _context.CoOfrStoreDatas
                .Where(d => !d.IsApiProcessed && d.ProductExternalId != null)
                .ToListAsync();

            if (!pendingItems.Any())
            {
                _logger.LogInformation("Brak oczekujących zadań API.");
                return;
            }

            // 2. Grupujemy po StoreId, aby dla każdego sklepu utworzyć klienta HTTP tylko raz
            var groupedItems = pendingItems.GroupBy(d => d.StoreId);

            foreach (var group in groupedItems)
            {
                int storeId = group.Key;
                var itemsToProcess = group.ToList();

                // 3. Pobieramy konfigurację sklepu
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
                    // 4. Router systemów sklepowych oparty na Enumie
                    switch (store.StoreSystemType)
                    {
                        case StoreSystemType.PrestaShop:
                            await ProcessPrestaShopBatchAsync(store, itemsToProcess);
                            break;

                        case StoreSystemType.Shoper:
                            _logger.LogWarning($"Implementacja dla Shoper/ClickShop nie jest jeszcze gotowa.");
                            MarkAsProcessed(itemsToProcess); // Oznaczamy, żeby nie wisiały w nieskończoność
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

                // Zapisujemy zmiany w bazie (ceny i statusy IsApiProcessed)
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Zakończono przetwarzanie kolejek API.");
        }

        // --- IMPLEMENTACJA PRESTASHOP ---
        private async Task ProcessPrestaShopBatchAsync(StoreClass store, List<CoOfrStoreData> items)
        {
            var client = _httpClientFactory.CreateClient();

            // Konfiguracja Basic Auth dla PrestaShop
            // W PrestaShop API Key to "Login", a hasło zostawiamy puste.
            var authToken = Encoding.ASCII.GetBytes($"{store.StoreApiKey}:");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            // Upewniamy się, że URL nie ma podwójnego slash na końcu
            string baseUrl = store.StoreApiUrl.TrimEnd('/');

            foreach (var item in items)
            {
                try
                {
                    // Budujemy URL. PrestaShop domyślnie zwraca XML, wymuszamy JSON parametrem output_format
                    string requestUrl = $"{baseUrl}/api/products/{item.ProductExternalId}?output_format=JSON";

                    var response = await client.GetAsync(requestUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonString = await response.Content.ReadAsStringAsync();

                        // Parsowanie JSON
                        // Przykładowa struktura: { "product": { "id": 1, "price": "123.000000", ... } }
                        var jsonNode = JsonNode.Parse(jsonString);
                        var priceStr = jsonNode?["product"]?["price"]?.ToString();

                        if (decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price))
                        {
                            // Sukces - zapisujemy cenę
                            // UWAGA: PrestaShop w polu 'price' zazwyczaj zwraca cenę NETTO.
                            // Jeśli potrzebujesz brutto, może być konieczna dodatkowa logika lub konfiguracja API sklepu.
                            item.ExtendedDataApiPrice = Math.Round(price, 2);
                            _logger.LogInformation($"[PrestaShop] ID {item.ProductExternalId}: Pobrano cenę {item.ExtendedDataApiPrice}");
                        }
                        else
                        {
                            _logger.LogWarning($"[PrestaShop] Nie udało się sparsować ceny dla produktu ID {item.ProductExternalId}.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"[PrestaShop] Błąd HTTP {response.StatusCode} dla produktu ID {item.ProductExternalId}. URL: {requestUrl}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[PrestaShop] Wyjątek połączenia dla produktu ID {item.ProductExternalId}");
                }
                finally
                {
                    // Zawsze oznaczamy jako przetworzone, nawet jeśli był błąd (np. 404 - produkt usunięty).
                    // W przeciwnym razie bot będzie próbował w nieskończoność dla błędnych ID.
                    item.IsApiProcessed = true;
                }

                // Krótkie opóźnienie, aby nie zablokować serwera klienta (Rate Limiting)
                await Task.Delay(100);
            }
        }

        // Pomocnicza metoda do masowego oznaczania rekordów jako przetworzone (np. w przypadku błędów konfiguracji)
        private void MarkAsProcessed(List<CoOfrStoreData> items)
        {
            foreach (var item in items)
            {
                item.IsApiProcessed = true;
            }
        }
    }
}