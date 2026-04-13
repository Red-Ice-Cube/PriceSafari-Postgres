using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.DTOs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PriceSafari.Services.ScheduleService
{

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
        private const int ParallelDegree = 8;
        private const int HttpTimeoutSeconds = 15;

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

        public async Task<int> LogExportAsChangeAsync(int storeId, string exportType, string userId, List<PriceBridgeItemRequest> items)
        {
            _logger.LogInformation($"[LogExportAsChangeAsync] Rozpoczynam logowanie eksportu dla sklepu ID: {storeId}, typ: {exportType}, ilość pozycji: {items?.Count}");

            if (items == null || !items.Any())
            {
                _logger.LogWarning("[LogExportAsChangeAsync] Próba zapisu pustej listy itemów.");
                throw new ArgumentException("Brak danych do zapisu.");
            }

            PriceExportMethod method;
            if (!Enum.TryParse(exportType, true, out method))
            {
                method = PriceExportMethod.Csv;
                _logger.LogWarning($"[LogExportAsChangeAsync] Nieznany typ eksportu '{exportType}', ustawiono domyślnie Csv.");
            }

            var latestScrapId = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            if (latestScrapId == 0)
            {
                _logger.LogError($"[LogExportAsChangeAsync] Nie znaleziono historii scrapowania dla sklepu ID: {storeId}.");
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
                IsAutomation = false,

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

            _logger.LogInformation($"[LogExportAsChangeAsync] Pomyślnie zapisano batch eksportu. ID Batcha: {batch.Id}, Liczba pozycji: {items.Count}");

            return items.Count;
        }

        public async Task<StorePriceBridgeResult> ExecuteStorePriceChangesAsync(
                    int storeId,
                    int scrapHistoryId,
                    string userId,
                    List<PriceBridgeItemRequest> itemsToBridge,
                    bool isAutomation = false,
                    int? automationRuleId = null,

                    int totalProductsInRule = 0,
                    int targetMetCount = 0,
                    int targetUnmetCount = 0,

                    int priceIncreasedCount = 0,
                    int priceDecreasedCount = 0,
                    int priceMaintainedCount = 0
                )
        {
            _logger.LogInformation($"[ExecuteStorePriceChangesAsync] Start. StoreId: {storeId}, Items: {itemsToBridge?.Count}, TotalRule: {totalProductsInRule}");

            var result = new StorePriceBridgeResult();

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Sklep nie istnieje." });
                return result;
            }

            if (!store.IsStorePriceBridgeActive)
            {
                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Integracja wyłączona." });
                return result;
            }

            switch (store.StoreSystemType)
            {
                case StoreSystemType.PrestaShop:
                    return await ExecutePrestaShopSessionAsync(
                        store,
                        scrapHistoryId,
                        userId,
                        itemsToBridge,
                        isAutomation,
                        automationRuleId,
                        totalProductsInRule,
                        targetMetCount,
                        targetUnmetCount,
                        priceIncreasedCount,
                        priceDecreasedCount,
                        priceMaintainedCount
                    );

                case StoreSystemType.WooCommerce:
                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "WooCommerce w trakcie wdrażania." });
                    return result;

                case StoreSystemType.Shoper:
                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Shoper w trakcie wdrażania." });
                    return result;

                default:
                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = $"Nieobsługiwany typ: {store.StoreSystemType}" });
                    return result;
            }
        }

        private async Task<StorePriceBridgeResult> ExecutePrestaShopSessionAsync(
             StoreClass store,
            int scrapHistoryId,
            string userId,
            List<PriceBridgeItemRequest> itemsToBridge,
            bool isAutomation,
            int? automationRuleId,
            int totalProductsInRule,
            int targetMetCount,
            int targetUnmetCount,
            int priceIncreasedCount,
            int priceDecreasedCount,
            int priceMaintainedCount)
        {
            _logger.LogInformation($"[PrestaShop] Rozpoczynam sesję dla {store.StoreName}.");

            var result = new StorePriceBridgeResult();

            var newBatch = new PriceBridgeBatch
            {
                ExecutionDate = DateTime.Now,
                StoreId = store.StoreId,
                ScrapHistoryId = scrapHistoryId,
                UserId = userId,
                SuccessfulCount = 0,
                FailedCount = 0,
                ExportMethod = PriceExportMethod.Api,
                IsAutomation = isAutomation,
                AutomationRuleId = automationRuleId,
                BridgeItems = new List<PriceBridgeItem>(),

                TotalProductsCount = totalProductsInRule > 0 ? totalProductsInRule : itemsToBridge.Count,

                TargetMetCount = targetMetCount,
                TargetUnmetCount = targetUnmetCount,

                PriceIncreasedCount = (isAutomation && totalProductsInRule > 0)
                                      ? priceIncreasedCount
                                      : itemsToBridge.Count(x => x.NewPrice > x.CurrentPrice),

                PriceDecreasedCount = (isAutomation && totalProductsInRule > 0)
                                      ? priceDecreasedCount
                                      : itemsToBridge.Count(x => x.NewPrice < x.CurrentPrice),

                PriceMaintainedCount = (isAutomation && totalProductsInRule > 0)
                                       ? priceMaintainedCount
                                       : itemsToBridge.Count(x => x.NewPrice == x.CurrentPrice)
            };

            _context.PriceBridgeBatches.Add(newBatch);

            if (string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
            {
                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Brak konfiguracji API." });
                return result;
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

            string maskedKey = store.StoreApiKey.Length > 4 ? store.StoreApiKey.Substring(0, 4) + "..." : "***";
            _logger.LogInformation($"[PrestaShop] Konfiguracja klienta HTTP. URL: {store.StoreApiUrl}, Key: {maskedKey}, Timeout: {HttpTimeoutSeconds}s");

            var authToken = Encoding.ASCII.GetBytes($"{store.StoreApiKey}:");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            _logger.LogInformation($"[PrestaShop] Rozpoczynam przetwarzanie {itemsToBridge.Count} produktów.");

            var productIds = itemsToBridge.Select(x => x.ProductId).Distinct().ToList();
            var productsDict = await _context.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId);

            using var semaphore = new SemaphoreSlim(ParallelDegree);

            var patchTasks = itemsToBridge.Select(async itemRequest =>
            {
                await semaphore.WaitAsync();
                try
                {
                    productsDict.TryGetValue(itemRequest.ProductId, out var product);

                    if (product == null || product.ExternalId == null)
                    {
                        string msg = product == null ? "Produkt nie znaleziony w bazie." : "Produkt nie ma ExternalId (ID sklepu).";
                        _logger.LogWarning($"[PrestaShop] Pominięto produkt ID {itemRequest.ProductId}: {msg}");
                        return new PatchOutcome
                        {
                            ItemRequest = itemRequest,
                            BridgeItem = null,
                            Success = false,
                            ErrorMsg = msg,
                            ShopProductId = null
                        };
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
                        _logger.LogDebug($"[PrestaShop] Aktualizuję produkt ID {shopProductId} na cenę {itemRequest.NewPrice}...");
                        (success, errorMsg) = await UpdatePrestaShopProductXmlAsync(client, store.StoreApiUrl, shopProductId, itemRequest.NewPrice);

                        if (!success)
                            _logger.LogWarning($"[PrestaShop] Nieudana aktualizacja ID {shopProductId}. Błąd: {errorMsg}");
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        errorMsg = ex.Message;
                        _logger.LogError(ex, $"[PrestaShop] Wyjątek podczas aktualizacji produktu ID {shopProductId}");
                    }

                    bridgeItem.Success = success;

                    return new PatchOutcome
                    {
                        ItemRequest = itemRequest,
                        BridgeItem = bridgeItem,
                        Success = success,
                        ErrorMsg = errorMsg,
                        ShopProductId = shopProductId
                    };
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var patchOutcomes = await Task.WhenAll(patchTasks);

            var itemsToVerify = new List<PriceBridgeItem>();
            foreach (var outcome in patchOutcomes)
            {
                if (outcome.BridgeItem == null)
                {

                    result.FailedCount++;
                    result.Errors.Add(new StorePriceBridgeError
                    {
                        ProductId = outcome.ItemRequest.ProductId.ToString(),
                        Message = outcome.ErrorMsg
                    });
                    continue;
                }

                if (!outcome.Success)
                {
                    result.FailedCount++;
                    result.Errors.Add(new StorePriceBridgeError
                    {
                        ProductId = outcome.ShopProductId,
                        Message = outcome.ErrorMsg
                    });
                }
                else
                {
                    result.SuccessfulCount++;
                    itemsToVerify.Add(outcome.BridgeItem);
                }

                newBatch.BridgeItems.Add(outcome.BridgeItem);
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation($"[PrestaShop] Zakończono wysyłanie. Sukcesy: {result.SuccessfulCount}, Błędy: {result.FailedCount}. Batch ID: {newBatch.Id}");

            if (itemsToVerify.Any())
            {
                _logger.LogInformation($"[PrestaShop] Oczekiwanie 500ms przed weryfikacją (Boomerang)...");
                await Task.Delay(500);

                var verifyTasks = itemsToVerify.Select(async bridgeItem =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        if (!productsDict.TryGetValue(bridgeItem.ProductId, out var product) || product?.ExternalId == null)
                            return new VerifyOutcome { BridgeItem = bridgeItem, VerifiedPrice = null, ShopProductId = null };

                        string shopProductId = product.ExternalId.ToString();

                        _logger.LogDebug($"[PrestaShop] Weryfikacja ceny dla ID {shopProductId}...");
                        decimal? verifiedPrice = await GetPrestaShopPriceAsync(client, store.StoreApiUrl, shopProductId);

                        if (!verifiedPrice.HasValue)
                            _logger.LogWarning($"[PrestaShop] Nie udało się zweryfikować ceny dla ID {shopProductId} (zwrócono null).");

                        return new VerifyOutcome
                        {
                            BridgeItem = bridgeItem,
                            VerifiedPrice = verifiedPrice,
                            ShopProductId = shopProductId
                        };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[PrestaShop] Błąd weryfikacji ceny dla produktu ID {bridgeItem.ProductId}");
                        return new VerifyOutcome { BridgeItem = bridgeItem, VerifiedPrice = null, ShopProductId = null };
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                var verifyOutcomes = await Task.WhenAll(verifyTasks);

                foreach (var vo in verifyOutcomes)
                {
                    if (vo.VerifiedPrice.HasValue)
                        vo.BridgeItem.PriceAfter = vo.VerifiedPrice.Value;

                    if (vo.ShopProductId != null)
                    {
                        result.SuccessfulChangesDetails.Add(new StorePriceBridgeSuccessDetail
                        {
                            ExternalId = vo.ShopProductId,
                            FetchedNewPrice = vo.VerifiedPrice
                        });
                    }
                }

                newBatch.SuccessfulCount = result.SuccessfulCount;
                await _context.SaveChangesAsync();
                _logger.LogInformation($"[PrestaShop] Zakończono weryfikację.");
            }

            return result;
        }

        private async Task<(bool success, string error)> UpdatePrestaShopProductXmlAsync(HttpClient client, string baseUrl, string productId, decimal newPriceBrutto)
        {
            try
            {
                decimal taxRate = 1.23m;
                decimal priceNet = Math.Round(newPriceBrutto / taxRate, 6);

                string priceNetString = priceNet.ToString(CultureInfo.InvariantCulture);

                string apiUrl = $"{baseUrl.TrimEnd('/')}/products/{productId}";

                string minXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
                <prestashop xmlns:xlink=""http://www.w3.org/1999/xlink"">
                    <product>
                        <id>{productId}</id>
                        <price>{priceNetString}</price>
                    </product>
                </prestashop>";

                var request = new HttpRequestMessage(new HttpMethod("PATCH"), apiUrl)
                {
                    Content = new StringContent(minXml, Encoding.UTF8, "application/xml")
                };

                var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    return (true, string.Empty);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    return (false, $"PATCH Error: {response.StatusCode}. Detale: {errorBody}");
                }
            }
            catch (TaskCanceledException)
            {
                return (false, $"Timeout ({HttpTimeoutSeconds}s) przy PATCH dla produktu {productId}.");
            }
            catch (Exception ex)
            {
                return (false, "Exception w UpdatePrestaShopProductXmlAsync: " + ex.Message);
            }
        }

        private async Task<decimal?> GetPrestaShopPriceAsync(HttpClient client, string baseUrl, string productId)
        {
            string apiUrl = $"{baseUrl.TrimEnd('/')}/products/{productId}?display=[price]";

            try
            {
                var response = await client.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"[PrestaShop] Weryfikacja GET nieudana dla ID {productId}. Status: {response.StatusCode}");
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();

                XDocument doc = XDocument.Parse(content);
                var priceElement = doc.Descendants("price").FirstOrDefault();

                if (priceElement != null && decimal.TryParse(priceElement.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal priceNetto))
                {
                    decimal priceBrutto = Math.Round(priceNetto * 1.23m, 2);
                    return priceBrutto;
                }

                _logger.LogWarning($"[PrestaShop] Nie udało się sparsować ceny z XML weryfikacyjnego dla ID {productId}.");
                return null;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning($"[PrestaShop] Timeout ({HttpTimeoutSeconds}s) przy weryfikacji GET dla ID {productId}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[PrestaShop] Wyjątek podczas weryfikacji GET dla ID {productId}");
                return null;
            }
        }

        private class PatchOutcome
        {
            public PriceBridgeItemRequest ItemRequest { get; set; }
            public PriceBridgeItem BridgeItem { get; set; }
            public bool Success { get; set; }
            public string ErrorMsg { get; set; }
            public string ShopProductId { get; set; }
        }

        private class VerifyOutcome
        {
            public PriceBridgeItem BridgeItem { get; set; }
            public decimal? VerifiedPrice { get; set; }
            public string ShopProductId { get; set; }
        }
    }
}