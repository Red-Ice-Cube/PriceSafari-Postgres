//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using PriceSafari.Models.DTOs;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//// Upewnij się, że masz using do miejsca, gdzie jest PriceBridgeItemRequest

//namespace PriceSafari.Services.ScheduleService
//{
//    public class StorePriceBridgeService
//    {
//        private readonly PriceSafariContext _context;
//        // HttpClient i Logger zostawiam, jeśli będą potrzebne do innej logiki w tym serwisie
//        private readonly IHttpClientFactory _httpClientFactory;
//        private readonly ILogger<StorePriceBridgeService> _logger;

//        public StorePriceBridgeService(
//            PriceSafariContext context,
//            IHttpClientFactory httpClientFactory,
//            ILogger<StorePriceBridgeService> logger)
//        {
//            _context = context;
//            _httpClientFactory = httpClientFactory;
//            _logger = logger;
//        }

//        // Metoda zwraca int (liczbę zapisanych elementów) lub rzuca wyjątek w przypadku błędu logicznego
//        public async Task<int> LogExportAsChangeAsync(int storeId, string exportType, string userId, List<PriceBridgeItemRequest> items)
//        {
//            if (items == null || !items.Any())
//            {
//                throw new ArgumentException("Brak danych do zapisu.");
//            }

//            PriceExportMethod method;
//            if (!Enum.TryParse(exportType, true, out method))
//            {
//                method = PriceExportMethod.Csv;
//            }

//            var latestScrapId = await _context.ScrapHistories
//                .Where(sh => sh.StoreId == storeId)
//                .OrderByDescending(sh => sh.Date)
//                .Select(sh => sh.Id)
//                .FirstOrDefaultAsync();

//            if (latestScrapId == 0)
//            {
//                throw new InvalidOperationException("Brak historii scrapowania dla tego sklepu.");
//            }

//            var batch = new PriceBridgeBatch
//            {
//                StoreId = storeId,
//                ScrapHistoryId = latestScrapId,
//                UserId = userId, // UserId przekazujemy z kontrolera
//                ExecutionDate = DateTime.Now,
//                SuccessfulCount = items.Count,
//                ExportMethod = method,
//                BridgeItems = new List<PriceBridgeItem>()
//            };

//            foreach (var item in items)
//            {
//                batch.BridgeItems.Add(new PriceBridgeItem
//                {
//                    ProductId = item.ProductId,
//                    PriceBefore = item.CurrentPrice,
//                    PriceAfter = item.NewPrice,
//                    MarginPrice = item.MarginPrice,

//                    RankingGoogleBefore = item.CurrentGoogleRanking,
//                    RankingCeneoBefore = item.CurrentCeneoRanking,

//                    RankingGoogleAfterSimulated = item.NewGoogleRanking,
//                    RankingCeneoAfterSimulated = item.NewCeneoRanking,

//                    Mode = item.Mode,
//                    PriceIndexTarget = item.PriceIndexTarget,
//                    StepPriceApplied = item.StepPriceApplied,

//                    Success = true
//                });
//            }

//            _context.PriceBridgeBatches.Add(batch);
//            await _context.SaveChangesAsync();

//            return items.Count;
//        }
//    }
//}





using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PriceSafari.Services.ScheduleService
{
    // DTO wynikowe dla kontrolera
    public class StorePriceBridgeResult
    {
        public int SuccessfulCount { get; set; }
        public int FailedCount { get; set; }
        public List<StorePriceBridgeError> Errors { get; set; } = new();
        public List<StorePriceBridgeSuccessDetail> SuccessfulChangesDetails { get; set; } = new();
    }

    public class StorePriceBridgeError { public string ProductId { get; set; } public string Message { get; set; } }
    public class StorePriceBridgeSuccessDetail { public string ExternalId { get; set; } public decimal? FetchedNewPrice { get; set; } }

    public class StorePriceBridgeService
    {
        private readonly PriceSafariContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<StorePriceBridgeService> _logger;

        public StorePriceBridgeService(
            PriceSafariContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<StorePriceBridgeService> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // =================================================================================
        // 1. METODA DO LOGOWANIA EKSPORTU PLIKOWEGO (CSV/EXCEL) - POZOSTAWIONA BEZ ZMIAN
        // =================================================================================
        public async Task<int> LogExportAsChangeAsync(int storeId, string exportType, string userId, List<PriceBridgeItemRequest> items)
        {
            if (items == null || !items.Any())
            {
                throw new ArgumentException("Brak danych do zapisu.");
            }

            PriceExportMethod method;
            if (!Enum.TryParse(exportType, true, out method))
            {
                method = PriceExportMethod.Csv;
            }

            var latestScrapId = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            if (latestScrapId == 0)
            {
                throw new InvalidOperationException("Brak historii scrapowania dla tego sklepu.");
            }

            var batch = new PriceBridgeBatch
            {
                StoreId = storeId,
                ScrapHistoryId = latestScrapId,
                UserId = userId,
                ExecutionDate = DateTime.Now,
                SuccessfulCount = items.Count,
                ExportMethod = method,
                IsAutomation = false, // Ręczny eksport
                BridgeItems = new List<PriceBridgeItem>()
            };

            foreach (var item in items)
            {
                batch.BridgeItems.Add(new PriceBridgeItem
                {
                    ProductId = item.ProductId,
                    PriceBefore = item.CurrentPrice,
                    PriceAfter = item.NewPrice,
                    MarginPrice = item.MarginPrice,

                    RankingGoogleBefore = item.CurrentGoogleRanking,
                    RankingCeneoBefore = item.CurrentCeneoRanking,

                    RankingGoogleAfterSimulated = item.NewGoogleRanking,
                    RankingCeneoAfterSimulated = item.NewCeneoRanking,

                    Mode = item.Mode,
                    PriceIndexTarget = item.PriceIndexTarget,
                    StepPriceApplied = item.StepPriceApplied,

                    Success = true
                });
            }

            _context.PriceBridgeBatches.Add(batch);
            await _context.SaveChangesAsync();

            return items.Count;
        }

        // =================================================================================
        // 2. NOWA METODA: WYKONANIE ZMIANY CEN PO API (DISPATCHER)
        // =================================================================================
        public async Task<StorePriceBridgeResult> ExecuteStorePriceChangesAsync(
            int storeId,
            int scrapHistoryId,
            string userId,
            List<PriceBridgeItemRequest> itemsToBridge)
        {
            var result = new StorePriceBridgeResult();

            // Walidacja sklepu
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null || !store.IsStorePriceBridgeActive)
            {
                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Integracja wyłączona lub sklep nie istnieje." });
                return result;
            }

            // Rozdzielacz w zależności od systemu
            switch (store.StoreSystemType)
            {
                case StoreSystemType.PrestaShop:
                    return await ExecutePrestaShopSessionAsync(store, scrapHistoryId, userId, itemsToBridge);

                case StoreSystemType.WooCommerce:
                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "WooCommerce w trakcie wdrażania." });
                    return result;

                case StoreSystemType.Shoper:
                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Shoper w trakcie wdrażania." });
                    return result;

                default:
                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = $"Nieobsługiwany typ sklepu: {store.StoreSystemType}" });
                    return result;
            }
        }

        // =================================================================================
        // 3. LOGIKA BIZNESOWA DLA PRESTASHOP
        // =================================================================================
        private async Task<StorePriceBridgeResult> ExecutePrestaShopSessionAsync(
            StoreClass store,
            int scrapHistoryId,
            string userId,
            List<PriceBridgeItemRequest> itemsToBridge)
        {
            var result = new StorePriceBridgeResult();

            // A. Tworzenie Batcha
            var newBatch = new PriceBridgeBatch
            {
                ExecutionDate = DateTime.Now,
                StoreId = store.StoreId,
                ScrapHistoryId = scrapHistoryId,
                UserId = userId,
                SuccessfulCount = 0,
                FailedCount = 0,
                ExportMethod = PriceExportMethod.Api,

                // Zgodnie z założeniem: Zmiana ręczna (przycisk) -> IsAutomation = false
                IsAutomation = false,

                BridgeItems = new List<PriceBridgeItem>(),

                TotalProductsCount = itemsToBridge.Count,
                PriceIncreasedCount = itemsToBridge.Count(x => x.NewPrice > x.CurrentPrice),
                PriceDecreasedCount = itemsToBridge.Count(x => x.NewPrice < x.CurrentPrice),
                PriceMaintainedCount = itemsToBridge.Count(x => x.NewPrice == x.CurrentPrice)
            };

            _context.PriceBridgeBatches.Add(newBatch);

            // B. Konfiguracja klienta HTTP
            var client = _httpClientFactory.CreateClient();
            var authToken = Encoding.ASCII.GetBytes($"{store.StoreApiKey}:");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            var itemsToVerify = new List<PriceBridgeItem>();

            // C. Pętla 1: Aktualizacja cen (UPDATE)
            foreach (var itemRequest in itemsToBridge)
            {
                var product = await _context.Products.FindAsync(itemRequest.ProductId);

                if (product == null || product.ExternalId == null)
                {
                    result.FailedCount++;
                    result.Errors.Add(new StorePriceBridgeError { ProductId = itemRequest.ProductId.ToString(), Message = "Brak ExternalId (ID sklepu)." });
                    continue;
                }

                string shopProductId = product.ExternalId.ToString();

                var bridgeItem = new PriceBridgeItem
                {
                    Batch = newBatch,
                    ProductId = itemRequest.ProductId,
                    PriceBefore = itemRequest.CurrentPrice,
                    PriceAfter = itemRequest.NewPrice,
                    MarginPrice = itemRequest.MarginPrice,

                    RankingGoogleBefore = itemRequest.CurrentGoogleRanking,
                    RankingCeneoBefore = itemRequest.CurrentCeneoRanking,
                    RankingGoogleAfterSimulated = itemRequest.NewGoogleRanking,
                    RankingCeneoAfterSimulated = itemRequest.NewCeneoRanking,

                    Mode = itemRequest.Mode,
                    PriceIndexTarget = itemRequest.PriceIndexTarget,
                    StepPriceApplied = itemRequest.StepPriceApplied
                };

                bool success = false;
                string errorMsg = "";

                try
                {
                    (success, errorMsg) = await UpdatePrestaShopProductXmlAsync(client, store.StoreApiUrl, shopProductId, itemRequest.NewPrice);
                }
                catch (Exception ex)
                {
                    success = false;
                    errorMsg = ex.Message;
                    _logger.LogError(ex, "Błąd PrestaShop API dla produktu ID {ExternalId}", shopProductId);
                }

                bridgeItem.Success = success;

                if (!success)
                {
                    result.FailedCount++;
                    result.Errors.Add(new StorePriceBridgeError { ProductId = shopProductId, Message = errorMsg });
                }
                else
                {
                    result.SuccessfulCount++;
                    itemsToVerify.Add(bridgeItem);
                }

                newBatch.BridgeItems.Add(bridgeItem);
            }

            await _context.SaveChangesAsync();

            // D. Pętla 2: Weryfikacja (Boomerang)
            if (itemsToVerify.Any())
            {
                await Task.Delay(2000); // Czekamy na przetworzenie

                foreach (var bridgeItem in itemsToVerify)
                {
                    try
                    {
                        var product = await _context.Products.FindAsync(bridgeItem.ProductId);
                        if (product?.ExternalId == null) continue;

                        string shopProductId = product.ExternalId.ToString();
                        decimal? verifiedPrice = await GetPrestaShopPriceAsync(client, store.StoreApiUrl, shopProductId);

                        if (verifiedPrice.HasValue)
                        {
                            bridgeItem.PriceAfter = verifiedPrice.Value; // Nadpisanie wartością ze sklepu
                        }

                        result.SuccessfulChangesDetails.Add(new StorePriceBridgeSuccessDetail
                        {
                            ExternalId = shopProductId,
                            FetchedNewPrice = verifiedPrice
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Błąd weryfikacji ceny dla produktu {ProductId}", bridgeItem.ProductId);
                    }
                }

                newBatch.SuccessfulCount = result.SuccessfulCount;
                await _context.SaveChangesAsync();
            }

            return result;
        }

        // =================================================================================
        // 4. METODY POMOCNICZE HTTP (XML)
        // =================================================================================

        private async Task<(bool success, string error)> UpdatePrestaShopProductXmlAsync(HttpClient client, string baseUrl, string productId, decimal newPrice)
        {
            try
            {
                string apiUrl = $"{baseUrl.TrimEnd('/')}/api/products/{productId}";
                var getResponse = await client.GetAsync(apiUrl);

                if (!getResponse.IsSuccessStatusCode)
                    return (false, $"GET Error: {getResponse.StatusCode}");

                var content = await getResponse.Content.ReadAsStringAsync();

                XDocument doc = XDocument.Parse(content);
                XNamespace ns = doc.Root.Name.Namespace;

                var productNode = doc.Descendants("product").FirstOrDefault();
                if (productNode == null) return (false, "XML Error: Brak węzła <product>.");

                var priceElement = productNode.Element("price");
                if (priceElement != null)
                {
                    // PrestaShop wymaga kropki jako separatora (Invariant)
                    priceElement.Value = newPrice.ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    return (false, "XML Error: Brak pola <price>.");
                }

                var putContent = new StringContent(doc.ToString(), Encoding.UTF8, "application/xml");
                var putResponse = await client.PutAsync(apiUrl, putContent);

                if (putResponse.IsSuccessStatusCode)
                {
                    return (true, string.Empty);
                }
                else
                {
                    var errorBody = await putResponse.Content.ReadAsStringAsync();
                    return (false, $"PUT Error: {putResponse.StatusCode}. {errorBody}");
                }
            }
            catch (Exception ex)
            {
                return (false, "Exception: " + ex.Message);
            }
        }

        private async Task<decimal?> GetPrestaShopPriceAsync(HttpClient client, string baseUrl, string productId)
        {
            string apiUrl = $"{baseUrl.TrimEnd('/')}/api/products/{productId}?display=[price]";

            try
            {
                var response = await client.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode) return null;

                var content = await response.Content.ReadAsStringAsync();

                XDocument doc = XDocument.Parse(content);
                var priceElement = doc.Descendants("price").FirstOrDefault();

                if (priceElement != null && decimal.TryParse(priceElement.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
                {
                    return price;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}