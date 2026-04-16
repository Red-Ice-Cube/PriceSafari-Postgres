//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Logging;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using PriceSafari.Models.DTOs;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;
//using System.Xml.Linq;

//namespace PriceSafari.Services.ScheduleService
//{

//    public class StorePriceBridgeResult
//    {
//        public int SuccessfulCount { get; set; }
//        public int FailedCount { get; set; }
//        public List<StorePriceBridgeError> Errors { get; set; } = new();
//        public List<StorePriceBridgeSuccessDetail> SuccessfulChangesDetails { get; set; } = new();
//    }

//    public class StorePriceBridgeError { public string ProductId { get; set; } public string Message { get; set; } }
//    public class StorePriceBridgeSuccessDetail { public string ExternalId { get; set; } public decimal? FetchedNewPrice { get; set; } }

//    public class StorePriceBridgeService
//    {
//        private const int ParallelDegree = 8;
//        private const int HttpTimeoutSeconds = 15;

//        private readonly PriceSafariContext _context;
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

//        public async Task<int> LogExportAsChangeAsync(int storeId, string exportType, string userId, List<PriceBridgeItemRequest> items)
//        {
//            _logger.LogInformation($"[LogExportAsChangeAsync] Rozpoczynam logowanie eksportu dla sklepu ID: {storeId}, typ: {exportType}, ilość pozycji: {items?.Count}");

//            if (items == null || !items.Any())
//            {
//                _logger.LogWarning("[LogExportAsChangeAsync] Próba zapisu pustej listy itemów.");
//                throw new ArgumentException("Brak danych do zapisu.");
//            }

//            PriceExportMethod method;
//            if (!Enum.TryParse(exportType, true, out method))
//            {
//                method = PriceExportMethod.Csv;
//                _logger.LogWarning($"[LogExportAsChangeAsync] Nieznany typ eksportu '{exportType}', ustawiono domyślnie Csv.");
//            }

//            var latestScrapId = await _context.ScrapHistories
//                .Where(sh => sh.StoreId == storeId)
//                .OrderByDescending(sh => sh.Date)
//                .Select(sh => sh.Id)
//                .FirstOrDefaultAsync();

//            if (latestScrapId == 0)
//            {
//                _logger.LogError($"[LogExportAsChangeAsync] Nie znaleziono historii scrapowania dla sklepu ID: {storeId}.");
//                throw new InvalidOperationException("Brak historii scrapowania dla tego sklepu.");
//            }

//            var batch = new PriceBridgeBatch
//            {
//                StoreId = storeId,
//                ScrapHistoryId = latestScrapId,
//                UserId = userId,
//                ExecutionDate = DateTime.Now,
//                SuccessfulCount = items.Count,
//                ExportMethod = method,
//                IsAutomation = false,

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

//            _logger.LogInformation($"[LogExportAsChangeAsync] Pomyślnie zapisano batch eksportu. ID Batcha: {batch.Id}, Liczba pozycji: {items.Count}");

//            return items.Count;
//        }

//        public async Task<StorePriceBridgeResult> ExecuteStorePriceChangesAsync(
//                    int storeId,
//                    int scrapHistoryId,
//                    string userId,
//                    List<PriceBridgeItemRequest> itemsToBridge,
//                    bool isAutomation = false,
//                    int? automationRuleId = null,

//                    int totalProductsInRule = 0,
//                    int targetMetCount = 0,
//                    int targetUnmetCount = 0,

//                    int priceIncreasedCount = 0,
//                    int priceDecreasedCount = 0,
//                    int priceMaintainedCount = 0
//                )
//        {
//            _logger.LogInformation($"[ExecuteStorePriceChangesAsync] Start. StoreId: {storeId}, Items: {itemsToBridge?.Count}, TotalRule: {totalProductsInRule}");

//            var result = new StorePriceBridgeResult();

//            var store = await _context.Stores.FindAsync(storeId);
//            if (store == null)
//            {
//                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Sklep nie istnieje." });
//                return result;
//            }

//            if (!store.IsStorePriceBridgeActive)
//            {
//                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Integracja wyłączona." });
//                return result;
//            }

//            switch (store.StoreSystemType)
//            {
//                case StoreSystemType.PrestaShop:
//                    return await ExecutePrestaShopSessionAsync(
//                        store,
//                        scrapHistoryId,
//                        userId,
//                        itemsToBridge,
//                        isAutomation,
//                        automationRuleId,
//                        totalProductsInRule,
//                        targetMetCount,
//                        targetUnmetCount,
//                        priceIncreasedCount,
//                        priceDecreasedCount,
//                        priceMaintainedCount
//                    );

//                case StoreSystemType.WooCommerce:
//                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "WooCommerce w trakcie wdrażania." });
//                    return result;

//                case StoreSystemType.Shoper:
//                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Shoper w trakcie wdrażania." });
//                    return result;

//                default:
//                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = $"Nieobsługiwany typ: {store.StoreSystemType}" });
//                    return result;
//            }
//        }

//        private async Task<StorePriceBridgeResult> ExecutePrestaShopSessionAsync(
//             StoreClass store,
//            int scrapHistoryId,
//            string userId,
//            List<PriceBridgeItemRequest> itemsToBridge,
//            bool isAutomation,
//            int? automationRuleId,
//            int totalProductsInRule,
//            int targetMetCount,
//            int targetUnmetCount,
//            int priceIncreasedCount,
//            int priceDecreasedCount,
//            int priceMaintainedCount)
//        {
//            _logger.LogInformation($"[PrestaShop] Rozpoczynam sesję dla {store.StoreName}.");

//            var result = new StorePriceBridgeResult();

//            var newBatch = new PriceBridgeBatch
//            {
//                ExecutionDate = DateTime.Now,
//                StoreId = store.StoreId,
//                ScrapHistoryId = scrapHistoryId,
//                UserId = userId,
//                SuccessfulCount = 0,
//                FailedCount = 0,
//                ExportMethod = PriceExportMethod.Api,
//                IsAutomation = isAutomation,
//                AutomationRuleId = automationRuleId,
//                BridgeItems = new List<PriceBridgeItem>(),

//                TotalProductsCount = totalProductsInRule > 0 ? totalProductsInRule : itemsToBridge.Count,

//                TargetMetCount = targetMetCount,
//                TargetUnmetCount = targetUnmetCount,

//                PriceIncreasedCount = (isAutomation && totalProductsInRule > 0)
//                                      ? priceIncreasedCount
//                                      : itemsToBridge.Count(x => x.NewPrice > x.CurrentPrice),

//                PriceDecreasedCount = (isAutomation && totalProductsInRule > 0)
//                                      ? priceDecreasedCount
//                                      : itemsToBridge.Count(x => x.NewPrice < x.CurrentPrice),

//                PriceMaintainedCount = (isAutomation && totalProductsInRule > 0)
//                                       ? priceMaintainedCount
//                                       : itemsToBridge.Count(x => x.NewPrice == x.CurrentPrice)
//            };

//            _context.PriceBridgeBatches.Add(newBatch);

//            if (string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
//            {
//                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Brak konfiguracji API." });
//                return result;
//            }

//            var client = _httpClientFactory.CreateClient();
//            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

//            string maskedKey = store.StoreApiKey.Length > 4 ? store.StoreApiKey.Substring(0, 4) + "..." : "***";
//            _logger.LogInformation($"[PrestaShop] Konfiguracja klienta HTTP. URL: {store.StoreApiUrl}, Key: {maskedKey}, Timeout: {HttpTimeoutSeconds}s");

//            var authToken = Encoding.ASCII.GetBytes($"{store.StoreApiKey}:");
//            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

//            _logger.LogInformation($"[PrestaShop] Rozpoczynam przetwarzanie {itemsToBridge.Count} produktów.");

//            var productIds = itemsToBridge.Select(x => x.ProductId).Distinct().ToList();
//            var productsDict = await _context.Products
//                .AsNoTracking()
//                .Where(p => productIds.Contains(p.ProductId))
//                .ToDictionaryAsync(p => p.ProductId);

//            using var semaphore = new SemaphoreSlim(ParallelDegree);

//            var patchTasks = itemsToBridge.Select(async itemRequest =>
//            {
//                await semaphore.WaitAsync();
//                try
//                {
//                    productsDict.TryGetValue(itemRequest.ProductId, out var product);

//                    if (product == null || product.ExternalId == null)
//                    {
//                        string msg = product == null ? "Produkt nie znaleziony w bazie." : "Produkt nie ma ExternalId (ID sklepu).";
//                        _logger.LogWarning($"[PrestaShop] Pominięto produkt ID {itemRequest.ProductId}: {msg}");
//                        return new PatchOutcome
//                        {
//                            ItemRequest = itemRequest,
//                            BridgeItem = null,
//                            Success = false,
//                            ErrorMsg = msg,
//                            ShopProductId = null
//                        };
//                    }

//                    string shopProductId = product.ExternalId.ToString();

//                    var bridgeItem = new PriceBridgeItem
//                    {
//                        Batch = newBatch,
//                        ProductId = itemRequest.ProductId,
//                        PriceBefore = itemRequest.CurrentPrice,
//                        PriceAfter = itemRequest.NewPrice,
//                        MarginPrice = itemRequest.MarginPrice,
//                        RankingGoogleBefore = itemRequest.CurrentGoogleRanking,
//                        RankingCeneoBefore = itemRequest.CurrentCeneoRanking,
//                        RankingGoogleAfterSimulated = itemRequest.NewGoogleRanking,
//                        RankingCeneoAfterSimulated = itemRequest.NewCeneoRanking,
//                        Mode = itemRequest.Mode,
//                        PriceIndexTarget = itemRequest.PriceIndexTarget,
//                        StepPriceApplied = itemRequest.StepPriceApplied
//                    };

//                    bool success = false;
//                    string errorMsg = "";

//                    try
//                    {
//                        _logger.LogDebug($"[PrestaShop] Aktualizuję produkt ID {shopProductId} na cenę {itemRequest.NewPrice}...");
//                        (success, errorMsg) = await UpdatePrestaShopProductXmlAsync(client, store.StoreApiUrl, shopProductId, itemRequest.NewPrice);

//                        if (!success)
//                            _logger.LogWarning($"[PrestaShop] Nieudana aktualizacja ID {shopProductId}. Błąd: {errorMsg}");
//                    }
//                    catch (Exception ex)
//                    {
//                        success = false;
//                        errorMsg = ex.Message;
//                        _logger.LogError(ex, $"[PrestaShop] Wyjątek podczas aktualizacji produktu ID {shopProductId}");
//                    }

//                    bridgeItem.Success = success;

//                    return new PatchOutcome
//                    {
//                        ItemRequest = itemRequest,
//                        BridgeItem = bridgeItem,
//                        Success = success,
//                        ErrorMsg = errorMsg,
//                        ShopProductId = shopProductId
//                    };
//                }
//                finally
//                {
//                    semaphore.Release();
//                }
//            }).ToList();

//            var patchOutcomes = await Task.WhenAll(patchTasks);

//            var itemsToVerify = new List<PriceBridgeItem>();
//            foreach (var outcome in patchOutcomes)
//            {
//                if (outcome.BridgeItem == null)
//                {

//                    result.FailedCount++;
//                    result.Errors.Add(new StorePriceBridgeError
//                    {
//                        ProductId = outcome.ItemRequest.ProductId.ToString(),
//                        Message = outcome.ErrorMsg
//                    });
//                    continue;
//                }

//                if (!outcome.Success)
//                {
//                    result.FailedCount++;
//                    result.Errors.Add(new StorePriceBridgeError
//                    {
//                        ProductId = outcome.ShopProductId,
//                        Message = outcome.ErrorMsg
//                    });
//                }
//                else
//                {
//                    result.SuccessfulCount++;
//                    itemsToVerify.Add(outcome.BridgeItem);
//                }

//                newBatch.BridgeItems.Add(outcome.BridgeItem);
//            }

//            await _context.SaveChangesAsync();
//            _logger.LogInformation($"[PrestaShop] Zakończono wysyłanie. Sukcesy: {result.SuccessfulCount}, Błędy: {result.FailedCount}. Batch ID: {newBatch.Id}");

//            if (itemsToVerify.Any())
//            {
//                _logger.LogInformation($"[PrestaShop] Oczekiwanie 500ms przed weryfikacją (Boomerang)...");
//                await Task.Delay(500);

//                var verifyTasks = itemsToVerify.Select(async bridgeItem =>
//                {
//                    await semaphore.WaitAsync();
//                    try
//                    {
//                        if (!productsDict.TryGetValue(bridgeItem.ProductId, out var product) || product?.ExternalId == null)
//                            return new VerifyOutcome { BridgeItem = bridgeItem, VerifiedPrice = null, ShopProductId = null };

//                        string shopProductId = product.ExternalId.ToString();

//                        _logger.LogDebug($"[PrestaShop] Weryfikacja ceny dla ID {shopProductId}...");
//                        decimal? verifiedPrice = await GetPrestaShopPriceAsync(client, store.StoreApiUrl, shopProductId);

//                        if (!verifiedPrice.HasValue)
//                            _logger.LogWarning($"[PrestaShop] Nie udało się zweryfikować ceny dla ID {shopProductId} (zwrócono null).");

//                        return new VerifyOutcome
//                        {
//                            BridgeItem = bridgeItem,
//                            VerifiedPrice = verifiedPrice,
//                            ShopProductId = shopProductId
//                        };
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.LogError(ex, $"[PrestaShop] Błąd weryfikacji ceny dla produktu ID {bridgeItem.ProductId}");
//                        return new VerifyOutcome { BridgeItem = bridgeItem, VerifiedPrice = null, ShopProductId = null };
//                    }
//                    finally
//                    {
//                        semaphore.Release();
//                    }
//                }).ToList();

//                var verifyOutcomes = await Task.WhenAll(verifyTasks);

//                foreach (var vo in verifyOutcomes)
//                {
//                    if (vo.VerifiedPrice.HasValue)
//                        vo.BridgeItem.PriceAfter = vo.VerifiedPrice.Value;

//                    if (vo.ShopProductId != null)
//                    {
//                        result.SuccessfulChangesDetails.Add(new StorePriceBridgeSuccessDetail
//                        {
//                            ExternalId = vo.ShopProductId,
//                            FetchedNewPrice = vo.VerifiedPrice
//                        });
//                    }
//                }

//                newBatch.SuccessfulCount = result.SuccessfulCount;
//                await _context.SaveChangesAsync();
//                _logger.LogInformation($"[PrestaShop] Zakończono weryfikację.");
//            }

//            return result;
//        }

//        private async Task<(bool success, string error)> UpdatePrestaShopProductXmlAsync(HttpClient client, string baseUrl, string productId, decimal newPriceBrutto)
//        {
//            try
//            {
//                decimal taxRate = 1.23m;
//                decimal priceNet = Math.Round(newPriceBrutto / taxRate, 6);

//                string priceNetString = priceNet.ToString(CultureInfo.InvariantCulture);

//                string apiUrl = $"{baseUrl.TrimEnd('/')}/products/{productId}";

//                string minXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
//                <prestashop xmlns:xlink=""http://www.w3.org/1999/xlink"">
//                    <product>
//                        <id>{productId}</id>
//                        <price>{priceNetString}</price>
//                    </product>
//                </prestashop>";

//                var request = new HttpRequestMessage(new HttpMethod("PATCH"), apiUrl)
//                {
//                    Content = new StringContent(minXml, Encoding.UTF8, "application/xml")
//                };

//                var response = await client.SendAsync(request);

//                if (response.IsSuccessStatusCode)
//                {
//                    return (true, string.Empty);
//                }
//                else
//                {
//                    var errorBody = await response.Content.ReadAsStringAsync();
//                    return (false, $"PATCH Error: {response.StatusCode}. Detale: {errorBody}");
//                }
//            }
//            catch (TaskCanceledException)
//            {
//                return (false, $"Timeout ({HttpTimeoutSeconds}s) przy PATCH dla produktu {productId}.");
//            }
//            catch (Exception ex)
//            {
//                return (false, "Exception w UpdatePrestaShopProductXmlAsync: " + ex.Message);
//            }
//        }

//        private async Task<decimal?> GetPrestaShopPriceAsync(HttpClient client, string baseUrl, string productId)
//        {
//            string apiUrl = $"{baseUrl.TrimEnd('/')}/products/{productId}?display=[price]";

//            try
//            {
//                var response = await client.GetAsync(apiUrl);
//                if (!response.IsSuccessStatusCode)
//                {
//                    _logger.LogWarning($"[PrestaShop] Weryfikacja GET nieudana dla ID {productId}. Status: {response.StatusCode}");
//                    return null;
//                }

//                var content = await response.Content.ReadAsStringAsync();

//                XDocument doc = XDocument.Parse(content);
//                var priceElement = doc.Descendants("price").FirstOrDefault();

//                if (priceElement != null && decimal.TryParse(priceElement.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal priceNetto))
//                {
//                    decimal priceBrutto = Math.Round(priceNetto * 1.23m, 2);
//                    return priceBrutto;
//                }

//                _logger.LogWarning($"[PrestaShop] Nie udało się sparsować ceny z XML weryfikacyjnego dla ID {productId}.");
//                return null;
//            }
//            catch (TaskCanceledException)
//            {
//                _logger.LogWarning($"[PrestaShop] Timeout ({HttpTimeoutSeconds}s) przy weryfikacji GET dla ID {productId}");
//                return null;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, $"[PrestaShop] Wyjątek podczas weryfikacji GET dla ID {productId}");
//                return null;
//            }
//        }

//        private class PatchOutcome
//        {
//            public PriceBridgeItemRequest ItemRequest { get; set; }
//            public PriceBridgeItem BridgeItem { get; set; }
//            public bool Success { get; set; }
//            public string ErrorMsg { get; set; }
//            public string ShopProductId { get; set; }
//        }

//        private class VerifyOutcome
//        {
//            public PriceBridgeItem BridgeItem { get; set; }
//            public decimal? VerifiedPrice { get; set; }
//            public string ShopProductId { get; set; }
//        }
//    }
//}












//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Logging;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using PriceSafari.Models.DTOs;
//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using System.Text;
//using System.Text.Json.Nodes;
//using System.Threading;
//using System.Threading.Tasks;
//using System.Xml.Linq;

//namespace PriceSafari.Services.ScheduleService
//{

//    public class StorePriceBridgeResult
//    {
//        public int SuccessfulCount { get; set; }
//        public int FailedCount { get; set; }
//        public List<StorePriceBridgeError> Errors { get; set; } = new();
//        public List<StorePriceBridgeSuccessDetail> SuccessfulChangesDetails { get; set; } = new();
//    }

//    public class StorePriceBridgeError { public string ProductId { get; set; } public string Message { get; set; } }
//    public class StorePriceBridgeSuccessDetail { public string ExternalId { get; set; } public decimal? FetchedNewPrice { get; set; } }

//    public class StorePriceBridgeService
//    {
//        private const int ParallelDegree = 2;
//        private const int HttpTimeoutSeconds = 15;

//        private readonly PriceSafariContext _context;
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

//        public async Task<int> LogExportAsChangeAsync(int storeId, string exportType, string userId, List<PriceBridgeItemRequest> items)
//        {
//            _logger.LogInformation($"[LogExportAsChangeAsync] Rozpoczynam logowanie eksportu dla sklepu ID: {storeId}, typ: {exportType}, ilość pozycji: {items?.Count}");

//            if (items == null || !items.Any())
//            {
//                _logger.LogWarning("[LogExportAsChangeAsync] Próba zapisu pustej listy itemów.");
//                throw new ArgumentException("Brak danych do zapisu.");
//            }

//            PriceExportMethod method;
//            if (!Enum.TryParse(exportType, true, out method))
//            {
//                method = PriceExportMethod.Csv;
//                _logger.LogWarning($"[LogExportAsChangeAsync] Nieznany typ eksportu '{exportType}', ustawiono domyślnie Csv.");
//            }

//            var latestScrapId = await _context.ScrapHistories
//                .Where(sh => sh.StoreId == storeId)
//                .OrderByDescending(sh => sh.Date)
//                .Select(sh => sh.Id)
//                .FirstOrDefaultAsync();

//            if (latestScrapId == 0)
//            {
//                _logger.LogError($"[LogExportAsChangeAsync] Nie znaleziono historii scrapowania dla sklepu ID: {storeId}.");
//                throw new InvalidOperationException("Brak historii scrapowania dla tego sklepu.");
//            }

//            var batch = new PriceBridgeBatch
//            {
//                StoreId = storeId,
//                ScrapHistoryId = latestScrapId,
//                UserId = userId,
//                ExecutionDate = DateTime.Now,
//                SuccessfulCount = items.Count,
//                ExportMethod = method,
//                IsAutomation = false,

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

//            _logger.LogInformation($"[LogExportAsChangeAsync] Pomyślnie zapisano batch eksportu. ID Batcha: {batch.Id}, Liczba pozycji: {items.Count}");

//            return items.Count;
//        }

//        public async Task<StorePriceBridgeResult> ExecuteStorePriceChangesAsync(
//                    int storeId,
//                    int scrapHistoryId,
//                    string userId,
//                    List<PriceBridgeItemRequest> itemsToBridge,
//                    bool isAutomation = false,
//                    int? automationRuleId = null,

//                    int totalProductsInRule = 0,
//                    int targetMetCount = 0,
//                    int targetUnmetCount = 0,

//                    int priceIncreasedCount = 0,
//                    int priceDecreasedCount = 0,
//                    int priceMaintainedCount = 0
//                )
//        {
//            _logger.LogInformation($"[ExecuteStorePriceChangesAsync] Start. StoreId: {storeId}, Items: {itemsToBridge?.Count}, TotalRule: {totalProductsInRule}");

//            var result = new StorePriceBridgeResult();

//            var store = await _context.Stores.FindAsync(storeId);
//            if (store == null)
//            {
//                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Sklep nie istnieje." });
//                return result;
//            }

//            if (!store.IsStorePriceBridgeActive)
//            {
//                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Integracja wyłączona." });
//                return result;
//            }

//            switch (store.StoreSystemType)
//            {
//                case StoreSystemType.PrestaShop:
//                    return await ExecutePrestaShopSessionAsync(
//                        store,
//                        scrapHistoryId,
//                        userId,
//                        itemsToBridge,
//                        isAutomation,
//                        automationRuleId,
//                        totalProductsInRule,
//                        targetMetCount,
//                        targetUnmetCount,
//                        priceIncreasedCount,
//                        priceDecreasedCount,
//                        priceMaintainedCount
//                    );

//                case StoreSystemType.IdoSell:
//                    return await ExecuteIdoSellSessionAsync(
//                        store,
//                        scrapHistoryId,
//                        userId,
//                        itemsToBridge,
//                        isAutomation,
//                        automationRuleId,
//                        totalProductsInRule,
//                        targetMetCount,
//                        targetUnmetCount,
//                        priceIncreasedCount,
//                        priceDecreasedCount,
//                        priceMaintainedCount
//                    );

//                case StoreSystemType.WooCommerce:
//                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "WooCommerce w trakcie wdrażania." });
//                    return result;

//                case StoreSystemType.Shoper:
//                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Shoper w trakcie wdrażania." });
//                    return result;

//                default:
//                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = $"Nieobsługiwany typ: {store.StoreSystemType}" });
//                    return result;
//            }
//        }

//        private async Task<StorePriceBridgeResult> ExecutePrestaShopSessionAsync(
//             StoreClass store,
//            int scrapHistoryId,
//            string userId,
//            List<PriceBridgeItemRequest> itemsToBridge,
//            bool isAutomation,
//            int? automationRuleId,
//            int totalProductsInRule,
//            int targetMetCount,
//            int targetUnmetCount,
//            int priceIncreasedCount,
//            int priceDecreasedCount,
//            int priceMaintainedCount)
//        {
//            _logger.LogInformation($"[PrestaShop] Rozpoczynam sesję dla {store.StoreName}.");

//            var result = new StorePriceBridgeResult();

//            var newBatch = new PriceBridgeBatch
//            {
//                ExecutionDate = DateTime.Now,
//                StoreId = store.StoreId,
//                ScrapHistoryId = scrapHistoryId,
//                UserId = userId,
//                SuccessfulCount = 0,
//                FailedCount = 0,
//                ExportMethod = PriceExportMethod.Api,
//                IsAutomation = isAutomation,
//                AutomationRuleId = automationRuleId,
//                BridgeItems = new List<PriceBridgeItem>(),

//                TotalProductsCount = totalProductsInRule > 0 ? totalProductsInRule : itemsToBridge.Count,

//                TargetMetCount = targetMetCount,
//                TargetUnmetCount = targetUnmetCount,

//                PriceIncreasedCount = (isAutomation && totalProductsInRule > 0)
//                                      ? priceIncreasedCount
//                                      : itemsToBridge.Count(x => x.NewPrice > x.CurrentPrice),

//                PriceDecreasedCount = (isAutomation && totalProductsInRule > 0)
//                                      ? priceDecreasedCount
//                                      : itemsToBridge.Count(x => x.NewPrice < x.CurrentPrice),

//                PriceMaintainedCount = (isAutomation && totalProductsInRule > 0)
//                                       ? priceMaintainedCount
//                                       : itemsToBridge.Count(x => x.NewPrice == x.CurrentPrice)
//            };

//            _context.PriceBridgeBatches.Add(newBatch);

//            if (string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
//            {
//                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Brak konfiguracji API." });
//                return result;
//            }

//            var client = _httpClientFactory.CreateClient();
//            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

//            string maskedKey = store.StoreApiKey.Length > 4 ? store.StoreApiKey.Substring(0, 4) + "..." : "***";
//            _logger.LogInformation($"[PrestaShop] Konfiguracja klienta HTTP. URL: {store.StoreApiUrl}, Key: {maskedKey}, Timeout: {HttpTimeoutSeconds}s");

//            var authToken = Encoding.ASCII.GetBytes($"{store.StoreApiKey}:");
//            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

//            _logger.LogInformation($"[PrestaShop] Rozpoczynam przetwarzanie {itemsToBridge.Count} produktów.");

//            var productIds = itemsToBridge.Select(x => x.ProductId).Distinct().ToList();
//            var productsDict = await _context.Products
//                .AsNoTracking()
//                .Where(p => productIds.Contains(p.ProductId))
//                .ToDictionaryAsync(p => p.ProductId);

//            using var semaphore = new SemaphoreSlim(ParallelDegree);

//            var patchTasks = itemsToBridge.Select(async itemRequest =>
//            {
//                await semaphore.WaitAsync();
//                try
//                {
//                    productsDict.TryGetValue(itemRequest.ProductId, out var product);

//                    if (product == null || product.ExternalId == null)
//                    {
//                        string msg = product == null ? "Produkt nie znaleziony w bazie." : "Produkt nie ma ExternalId (ID sklepu).";
//                        _logger.LogWarning($"[PrestaShop] Pominięto produkt ID {itemRequest.ProductId}: {msg}");
//                        return new PatchOutcome
//                        {
//                            ItemRequest = itemRequest,
//                            BridgeItem = null,
//                            Success = false,
//                            ErrorMsg = msg,
//                            ShopProductId = null
//                        };
//                    }

//                    string shopProductId = product.ExternalId.ToString();

//                    var bridgeItem = new PriceBridgeItem
//                    {
//                        Batch = newBatch,
//                        ProductId = itemRequest.ProductId,
//                        PriceBefore = itemRequest.CurrentPrice,
//                        PriceAfter = itemRequest.NewPrice,
//                        MarginPrice = itemRequest.MarginPrice,
//                        RankingGoogleBefore = itemRequest.CurrentGoogleRanking,
//                        RankingCeneoBefore = itemRequest.CurrentCeneoRanking,
//                        RankingGoogleAfterSimulated = itemRequest.NewGoogleRanking,
//                        RankingCeneoAfterSimulated = itemRequest.NewCeneoRanking,
//                        Mode = itemRequest.Mode,
//                        PriceIndexTarget = itemRequest.PriceIndexTarget,
//                        StepPriceApplied = itemRequest.StepPriceApplied
//                    };

//                    bool success = false;
//                    string errorMsg = "";

//                    try
//                    {
//                        _logger.LogDebug($"[PrestaShop] Aktualizuję produkt ID {shopProductId} na cenę {itemRequest.NewPrice}...");
//                        (success, errorMsg) = await UpdatePrestaShopProductXmlAsync(client, store.StoreApiUrl, shopProductId, itemRequest.NewPrice);

//                        if (!success)
//                            _logger.LogWarning($"[PrestaShop] Nieudana aktualizacja ID {shopProductId}. Błąd: {errorMsg}");
//                    }
//                    catch (Exception ex)
//                    {
//                        success = false;
//                        errorMsg = ex.Message;
//                        _logger.LogError(ex, $"[PrestaShop] Wyjątek podczas aktualizacji produktu ID {shopProductId}");
//                    }

//                    bridgeItem.Success = success;

//                    return new PatchOutcome
//                    {
//                        ItemRequest = itemRequest,
//                        BridgeItem = bridgeItem,
//                        Success = success,
//                        ErrorMsg = errorMsg,
//                        ShopProductId = shopProductId
//                    };
//                }
//                finally
//                {
//                    semaphore.Release();
//                }
//            }).ToList();

//            var patchOutcomes = await Task.WhenAll(patchTasks);

//            var itemsToVerify = new List<PriceBridgeItem>();
//            foreach (var outcome in patchOutcomes)
//            {
//                if (outcome.BridgeItem == null)
//                {

//                    result.FailedCount++;
//                    result.Errors.Add(new StorePriceBridgeError
//                    {
//                        ProductId = outcome.ItemRequest.ProductId.ToString(),
//                        Message = outcome.ErrorMsg
//                    });
//                    continue;
//                }

//                if (!outcome.Success)
//                {
//                    result.FailedCount++;
//                    result.Errors.Add(new StorePriceBridgeError
//                    {
//                        ProductId = outcome.ShopProductId,
//                        Message = outcome.ErrorMsg
//                    });
//                }
//                else
//                {
//                    result.SuccessfulCount++;
//                    itemsToVerify.Add(outcome.BridgeItem);
//                }

//                newBatch.BridgeItems.Add(outcome.BridgeItem);
//            }

//            await _context.SaveChangesAsync();
//            _logger.LogInformation($"[PrestaShop] Zakończono wysyłanie. Sukcesy: {result.SuccessfulCount}, Błędy: {result.FailedCount}. Batch ID: {newBatch.Id}");

//            if (itemsToVerify.Any())
//            {
//                _logger.LogInformation($"[PrestaShop] Oczekiwanie 500ms przed weryfikacją (Boomerang)...");
//                await Task.Delay(500);

//                var verifyTasks = itemsToVerify.Select(async bridgeItem =>
//                {
//                    await semaphore.WaitAsync();
//                    try
//                    {
//                        if (!productsDict.TryGetValue(bridgeItem.ProductId, out var product) || product?.ExternalId == null)
//                            return new VerifyOutcome { BridgeItem = bridgeItem, VerifiedPrice = null, ShopProductId = null };

//                        string shopProductId = product.ExternalId.ToString();

//                        _logger.LogDebug($"[PrestaShop] Weryfikacja ceny dla ID {shopProductId}...");
//                        decimal? verifiedPrice = await GetPrestaShopPriceAsync(client, store.StoreApiUrl, shopProductId);

//                        if (!verifiedPrice.HasValue)
//                            _logger.LogWarning($"[PrestaShop] Nie udało się zweryfikować ceny dla ID {shopProductId} (zwrócono null).");

//                        return new VerifyOutcome
//                        {
//                            BridgeItem = bridgeItem,
//                            VerifiedPrice = verifiedPrice,
//                            ShopProductId = shopProductId
//                        };
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.LogError(ex, $"[PrestaShop] Błąd weryfikacji ceny dla produktu ID {bridgeItem.ProductId}");
//                        return new VerifyOutcome { BridgeItem = bridgeItem, VerifiedPrice = null, ShopProductId = null };
//                    }
//                    finally
//                    {
//                        semaphore.Release();
//                    }
//                }).ToList();

//                var verifyOutcomes = await Task.WhenAll(verifyTasks);

//                foreach (var vo in verifyOutcomes)
//                {
//                    if (vo.VerifiedPrice.HasValue)
//                        vo.BridgeItem.PriceAfter = vo.VerifiedPrice.Value;

//                    if (vo.ShopProductId != null)
//                    {
//                        result.SuccessfulChangesDetails.Add(new StorePriceBridgeSuccessDetail
//                        {
//                            ExternalId = vo.ShopProductId,
//                            FetchedNewPrice = vo.VerifiedPrice
//                        });
//                    }
//                }

//                newBatch.SuccessfulCount = result.SuccessfulCount;
//                await _context.SaveChangesAsync();
//                _logger.LogInformation($"[PrestaShop] Zakończono weryfikację.");
//            }

//            return result;
//        }

//        private async Task<StorePriceBridgeResult> ExecuteIdoSellSessionAsync(
//            StoreClass store,
//            int scrapHistoryId,
//            string userId,
//            List<PriceBridgeItemRequest> itemsToBridge,
//            bool isAutomation,
//            int? automationRuleId,
//            int totalProductsInRule,
//            int targetMetCount,
//            int targetUnmetCount,
//            int priceIncreasedCount,
//            int priceDecreasedCount,
//            int priceMaintainedCount)
//        {
//            _logger.LogInformation($"[IdoSell] Rozpoczynam sesję dla {store.StoreName}.");

//            var result = new StorePriceBridgeResult();

//            var newBatch = new PriceBridgeBatch
//            {
//                ExecutionDate = DateTime.Now,
//                StoreId = store.StoreId,
//                ScrapHistoryId = scrapHistoryId,
//                UserId = userId,
//                SuccessfulCount = 0,
//                FailedCount = 0,
//                ExportMethod = PriceExportMethod.Api,
//                IsAutomation = isAutomation,
//                AutomationRuleId = automationRuleId,
//                BridgeItems = new List<PriceBridgeItem>(),

//                TotalProductsCount = totalProductsInRule > 0 ? totalProductsInRule : itemsToBridge.Count,
//                TargetMetCount = targetMetCount,
//                TargetUnmetCount = targetUnmetCount,

//                PriceIncreasedCount = (isAutomation && totalProductsInRule > 0)
//                                      ? priceIncreasedCount
//                                      : itemsToBridge.Count(x => x.NewPrice > x.CurrentPrice),

//                PriceDecreasedCount = (isAutomation && totalProductsInRule > 0)
//                                      ? priceDecreasedCount
//                                      : itemsToBridge.Count(x => x.NewPrice < x.CurrentPrice),

//                PriceMaintainedCount = (isAutomation && totalProductsInRule > 0)
//                                       ? priceMaintainedCount
//                                       : itemsToBridge.Count(x => x.NewPrice == x.CurrentPrice)
//            };

//            _context.PriceBridgeBatches.Add(newBatch);

//            if (string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
//            {
//                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Brak konfiguracji API." });
//                return result;
//            }

//            var client = _httpClientFactory.CreateClient();
//            client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

//            string maskedKey = store.StoreApiKey.Length > 4 ? store.StoreApiKey.Substring(0, 4) + "..." : "***";
//            _logger.LogInformation($"[IdoSell] Konfiguracja klienta HTTP. URL: {store.StoreApiUrl}, Key: {maskedKey}, Timeout: {HttpTimeoutSeconds}s");

//            _logger.LogInformation($"[IdoSell] Rozpoczynam przetwarzanie {itemsToBridge.Count} produktów.");

//            var productIds = itemsToBridge.Select(x => x.ProductId).Distinct().ToList();
//            var productsDict = await _context.Products
//                .AsNoTracking()
//                .Where(p => productIds.Contains(p.ProductId))
//                .ToDictionaryAsync(p => p.ProductId);

//            using var semaphore = new SemaphoreSlim(ParallelDegree);

//            var patchTasks = itemsToBridge.Select(async itemRequest =>
//            {
//                await semaphore.WaitAsync();
//                try
//                {
//                    productsDict.TryGetValue(itemRequest.ProductId, out var product);

//                    if (product == null || product.ExternalId == null)
//                    {
//                        string msg = product == null ? "Produkt nie znaleziony w bazie." : "Produkt nie ma ExternalId (ID sklepu).";
//                        _logger.LogWarning($"[IdoSell] Pominięto produkt ID {itemRequest.ProductId}: {msg}");
//                        return new PatchOutcome
//                        {
//                            ItemRequest = itemRequest,
//                            BridgeItem = null,
//                            Success = false,
//                            ErrorMsg = msg,
//                            ShopProductId = null
//                        };
//                    }

//                    string shopProductId = product.ExternalId.ToString();

//                    var bridgeItem = new PriceBridgeItem
//                    {
//                        Batch = newBatch,
//                        ProductId = itemRequest.ProductId,
//                        PriceBefore = itemRequest.CurrentPrice,
//                        PriceAfter = itemRequest.NewPrice,
//                        MarginPrice = itemRequest.MarginPrice,
//                        RankingGoogleBefore = itemRequest.CurrentGoogleRanking,
//                        RankingCeneoBefore = itemRequest.CurrentCeneoRanking,
//                        RankingGoogleAfterSimulated = itemRequest.NewGoogleRanking,
//                        RankingCeneoAfterSimulated = itemRequest.NewCeneoRanking,
//                        Mode = itemRequest.Mode,
//                        PriceIndexTarget = itemRequest.PriceIndexTarget,
//                        StepPriceApplied = itemRequest.StepPriceApplied
//                    };

//                    bool success = false;
//                    string errorMsg = "";

//                    try
//                    {
//                        _logger.LogDebug($"[IdoSell] Aktualizuję produkt ID {shopProductId} na cenę BRUTTO {itemRequest.NewPrice}...");
//                        (success, errorMsg) = await UpdateIdoSellProductAsync(client, store.StoreApiUrl, store.StoreApiKey, shopProductId, itemRequest.NewPrice);

//                        if (!success)
//                            _logger.LogWarning($"[IdoSell] Nieudana aktualizacja ID {shopProductId}. Błąd: {errorMsg}");
//                    }
//                    catch (Exception ex)
//                    {
//                        success = false;
//                        errorMsg = ex.Message;
//                        _logger.LogError(ex, $"[IdoSell] Wyjątek podczas aktualizacji produktu ID {shopProductId}");
//                    }

//                    bridgeItem.Success = success;

//                    return new PatchOutcome
//                    {
//                        ItemRequest = itemRequest,
//                        BridgeItem = bridgeItem,
//                        Success = success,
//                        ErrorMsg = errorMsg,
//                        ShopProductId = shopProductId
//                    };
//                }
//                finally
//                {
//                    semaphore.Release();
//                }
//            }).ToList();

//            var patchOutcomes = await Task.WhenAll(patchTasks);

//            var itemsToVerify = new List<PriceBridgeItem>();
//            foreach (var outcome in patchOutcomes)
//            {
//                if (outcome.BridgeItem == null)
//                {
//                    result.FailedCount++;
//                    result.Errors.Add(new StorePriceBridgeError
//                    {
//                        ProductId = outcome.ItemRequest.ProductId.ToString(),
//                        Message = outcome.ErrorMsg
//                    });
//                    continue;
//                }

//                if (!outcome.Success)
//                {
//                    result.FailedCount++;
//                    result.Errors.Add(new StorePriceBridgeError
//                    {
//                        ProductId = outcome.ShopProductId,
//                        Message = outcome.ErrorMsg
//                    });
//                }
//                else
//                {
//                    result.SuccessfulCount++;
//                    itemsToVerify.Add(outcome.BridgeItem);
//                }

//                newBatch.BridgeItems.Add(outcome.BridgeItem);
//            }

//            await _context.SaveChangesAsync();
//            _logger.LogInformation($"[IdoSell] Zakończono wysyłanie. Sukcesy: {result.SuccessfulCount}, Błędy: {result.FailedCount}. Batch ID: {newBatch.Id}");

//            if (itemsToVerify.Any())
//            {
//                _logger.LogInformation($"[IdoSell] Oczekiwanie 500ms przed weryfikacją (Boomerang)...");
//                await Task.Delay(500);

//                var verifyTasks = itemsToVerify.Select(async bridgeItem =>
//                {
//                    await semaphore.WaitAsync();
//                    try
//                    {
//                        if (!productsDict.TryGetValue(bridgeItem.ProductId, out var product) || product?.ExternalId == null)
//                            return new VerifyOutcome { BridgeItem = bridgeItem, VerifiedPrice = null, ShopProductId = null };

//                        string shopProductId = product.ExternalId.ToString();

//                        _logger.LogDebug($"[IdoSell] Weryfikacja ceny dla ID {shopProductId}...");
//                        decimal? verifiedPrice = await GetIdoSellPriceAsync(client, store.StoreApiUrl, store.StoreApiKey, shopProductId);

//                        if (!verifiedPrice.HasValue)
//                            _logger.LogWarning($"[IdoSell] Nie udało się zweryfikować ceny dla ID {shopProductId} (zwrócono null).");

//                        return new VerifyOutcome
//                        {
//                            BridgeItem = bridgeItem,
//                            VerifiedPrice = verifiedPrice,
//                            ShopProductId = shopProductId
//                        };
//                    }
//                    catch (Exception ex)
//                    {
//                        _logger.LogError(ex, $"[IdoSell] Błąd weryfikacji ceny dla produktu ID {bridgeItem.ProductId}");
//                        return new VerifyOutcome { BridgeItem = bridgeItem, VerifiedPrice = null, ShopProductId = null };
//                    }
//                    finally
//                    {
//                        semaphore.Release();
//                    }
//                }).ToList();

//                var verifyOutcomes = await Task.WhenAll(verifyTasks);

//                foreach (var vo in verifyOutcomes)
//                {
//                    if (vo.VerifiedPrice.HasValue)
//                        vo.BridgeItem.PriceAfter = vo.VerifiedPrice.Value;

//                    if (vo.ShopProductId != null)
//                    {
//                        result.SuccessfulChangesDetails.Add(new StorePriceBridgeSuccessDetail
//                        {
//                            ExternalId = vo.ShopProductId,
//                            FetchedNewPrice = vo.VerifiedPrice
//                        });
//                    }
//                }

//                newBatch.SuccessfulCount = result.SuccessfulCount;
//                await _context.SaveChangesAsync();
//                _logger.LogInformation($"[IdoSell] Zakończono weryfikację.");
//            }

//            return result;
//        }

//        private async Task<(bool success, string error)> UpdatePrestaShopProductXmlAsync(HttpClient client, string baseUrl, string productId, decimal newPriceBrutto)
//        {
//            try
//            {
//                decimal taxRate = 1.23m;
//                decimal priceNet = Math.Round(newPriceBrutto / taxRate, 6);

//                string priceNetString = priceNet.ToString(CultureInfo.InvariantCulture);

//                string apiUrl = $"{baseUrl.TrimEnd('/')}/products/{productId}";

//                string minXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
//                <prestashop xmlns:xlink=""http://www.w3.org/1999/xlink"">
//                    <product>
//                        <id>{productId}</id>
//                        <price>{priceNetString}</price>
//                    </product>
//                </prestashop>";

//                var request = new HttpRequestMessage(new HttpMethod("PATCH"), apiUrl)
//                {
//                    Content = new StringContent(minXml, Encoding.UTF8, "application/xml")
//                };

//                var response = await client.SendAsync(request);

//                if (response.IsSuccessStatusCode)
//                {
//                    return (true, string.Empty);
//                }
//                else
//                {
//                    var errorBody = await response.Content.ReadAsStringAsync();
//                    return (false, $"PATCH Error: {response.StatusCode}. Detale: {errorBody}");
//                }
//            }
//            catch (TaskCanceledException)
//            {
//                return (false, $"Timeout ({HttpTimeoutSeconds}s) przy PATCH dla produktu {productId}.");
//            }
//            catch (Exception ex)
//            {
//                return (false, "Exception w UpdatePrestaShopProductXmlAsync: " + ex.Message);
//            }
//        }

//        private async Task<decimal?> GetPrestaShopPriceAsync(HttpClient client, string baseUrl, string productId)
//        {
//            string apiUrl = $"{baseUrl.TrimEnd('/')}/products/{productId}?display=[price]";

//            try
//            {
//                var response = await client.GetAsync(apiUrl);
//                if (!response.IsSuccessStatusCode)
//                {
//                    _logger.LogWarning($"[PrestaShop] Weryfikacja GET nieudana dla ID {productId}. Status: {response.StatusCode}");
//                    return null;
//                }

//                var content = await response.Content.ReadAsStringAsync();

//                XDocument doc = XDocument.Parse(content);
//                var priceElement = doc.Descendants("price").FirstOrDefault();

//                if (priceElement != null && decimal.TryParse(priceElement.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal priceNetto))
//                {
//                    decimal priceBrutto = Math.Round(priceNetto * 1.23m, 2);
//                    return priceBrutto;
//                }

//                _logger.LogWarning($"[PrestaShop] Nie udało się sparsować ceny z XML weryfikacyjnego dla ID {productId}.");
//                return null;
//            }
//            catch (TaskCanceledException)
//            {
//                _logger.LogWarning($"[PrestaShop] Timeout ({HttpTimeoutSeconds}s) przy weryfikacji GET dla ID {productId}");
//                return null;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, $"[PrestaShop] Wyjątek podczas weryfikacji GET dla ID {productId}");
//                return null;
//            }
//        }

//        private async Task<(bool success, string error)> UpdateIdoSellProductAsync(HttpClient client, string baseUrl, string apiKey, string productId, decimal newPriceBrutto)
//        {
//            try
//            {
//                if (!int.TryParse(productId, out int productIdInt))
//                    return (false, $"Nieprawidłowe productId '{productId}' (oczekiwano int).");

//                decimal priceBrutto = Math.Round(newPriceBrutto, 2);
//                string priceString = priceBrutto.ToString(CultureInfo.InvariantCulture);

//                string apiUrl = $"{baseUrl.TrimEnd('/')}/api/admin/v3/products/products";

//                // Potwierdzony działający format IdoSell API Admin 3:
//                // { "params": { "products": [ { "productId": N, "productRetailPrice": X.XX } ] } }
//                string jsonBody = $@"{{""params"":{{""products"":[{{""productId"":{productIdInt},""productRetailPrice"":{priceString}}}]}}}}";

//                _logger.LogDebug($"[IdoSell] PUT body dla ID {productId}: {jsonBody}");

//                using var request = new HttpRequestMessage(HttpMethod.Put, apiUrl)
//                {
//                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
//                };
//                request.Headers.Add("X-API-KEY", apiKey);
//                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

//                using var response = await client.SendAsync(request);
//                var body = await response.Content.ReadAsStringAsync();

//                // Loguj zawsze body - IdoSell zwraca 207 Multi-Status i wynik jest w body
//                var preview = body != null && body.Length > 1500 ? body.Substring(0, 1500) + "...[ucięte]" : body;
//                _logger.LogInformation($"[IdoSell] PUT ID {productId} <- HTTP {(int)response.StatusCode}, body: {preview}");

//                // HTTP 4xx/5xx (bez 207) = ewidentny błąd
//                if (!response.IsSuccessStatusCode && (int)response.StatusCode != 207)
//                {
//                    return (false, $"PUT Error: {response.StatusCode}. Detale: {preview}");
//                }

//                // Parsuj body - dla IdoSell nawet przy HTTP 200/207 wynik może być błędem
//                try
//                {
//                    var root = JsonNode.Parse(body);

//                    // Wariant 1: "errors" na poziomie root - IdoSell zwraca to jako OBIEKT przy błędzie parametrów
//                    // Przykład: {"errors":{"faultString":"...","faultCode":"no_products"},"results":{}}
//                    var errorsNode = root?["errors"];
//                    if (errorsNode != null)
//                    {
//                        if (errorsNode is JsonObject errorsObj && errorsObj.Count > 0)
//                        {
//                            var errFaultCode = errorsObj["faultCode"]?.ToString();
//                            var errFaultString = errorsObj["faultString"]?.ToString();
//                            if (!string.IsNullOrEmpty(errFaultCode) && errFaultCode != "0")
//                            {
//                                return (false, $"IdoSell błąd '{errFaultCode}': {errFaultString}");
//                            }
//                        }
//                        else if (errorsNode is JsonArray errorsArr && errorsArr.Count > 0)
//                        {
//                            var firstErr = errorsArr[0]?.ToJsonString();
//                            return (false, $"IdoSell errors: {firstErr}");
//                        }
//                    }

//                    // Wariant 2: faultCode/faultString na poziomie root
//                    var faultCode = root?["faultCode"]?.ToString();
//                    var faultString = root?["faultString"]?.ToString();
//                    if (!string.IsNullOrEmpty(faultCode) && faultCode != "0")
//                    {
//                        return (false, $"IdoSell fault {faultCode}: {faultString}");
//                    }

//                    // Wariant 3: results.productsResults[] - typowy format sukcesu IdoSell
//                    // Przykład sukcesu: {"results":{"productsResults":[{"faults":[],"productId":4916,...}]}}
//                    // Przykład błędu per-produkt: {"results":{"productsResults":[{"faults":[{"faultCode":X,"faultString":"..."}],...}]}}
//                    var productsResults = root?["results"]?["productsResults"] as JsonArray;
//                    if (productsResults != null)
//                    {
//                        foreach (var resNode in productsResults)
//                        {
//                            if (resNode == null) continue;

//                            var resIdStr = resNode["productId"]?.ToString();
//                            if (!string.IsNullOrEmpty(resIdStr) && resIdStr != productId)
//                                continue;

//                            // Sprawdź faults per produkt
//                            if (resNode["faults"] is JsonArray faultsArr && faultsArr.Count > 0)
//                            {
//                                var firstFault = faultsArr[0];
//                                var fCode = firstFault?["faultCode"]?.ToString();
//                                var fString = firstFault?["faultString"]?.ToString();
//                                return (false, $"IdoSell fault produktu {resIdStr}: {fCode} - {fString}");
//                            }
//                        }
//                    }
//                }
//                catch (Exception parseEx)
//                {
//                    _logger.LogWarning(parseEx, $"[IdoSell] Nie udało się sparsować body odpowiedzi dla ID {productId}, zakładam sukces po HTTP {(int)response.StatusCode}");
//                }

//                return (true, string.Empty);
//            }
//            catch (TaskCanceledException)
//            {
//                return (false, $"Timeout ({HttpTimeoutSeconds}s) przy PUT dla produktu {productId}.");
//            }
//            catch (Exception ex)
//            {
//                return (false, "Exception w UpdateIdoSellProductAsync: " + ex.Message);
//            }
//        }

//        private async Task<decimal?> GetIdoSellPriceAsync(HttpClient client, string baseUrl, string apiKey, string productId)
//        {
//            string apiUrl = $"{baseUrl.TrimEnd('/')}/api/admin/v3/products/products?productIds={productId}";

//            try
//            {
//                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
//                request.Headers.Add("X-API-KEY", apiKey);
//                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

//                using var response = await client.SendAsync(request);
//                if (!response.IsSuccessStatusCode)
//                {
//                    _logger.LogWarning($"[IdoSell] Weryfikacja GET nieudana dla ID {productId}. Status: {response.StatusCode}");
//                    return null;
//                }

//                var content = await response.Content.ReadAsStringAsync();
//                var root = JsonNode.Parse(content);
//                var results = root?["results"] as JsonArray;

//                if (results == null || results.Count == 0)
//                {
//                    _logger.LogWarning($"[IdoSell] Brak 'results' w odpowiedzi weryfikacyjnej dla ID {productId}.");
//                    return null;
//                }

//                var priceStr = results[0]?["productRetailPrice"]?.ToString();

//                if (decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal priceBrutto) && priceBrutto > 0)
//                {
//                    return Math.Round(priceBrutto, 2);
//                }

//                _logger.LogWarning($"[IdoSell] Nie udało się sparsować ceny z JSON weryfikacyjnego dla ID {productId} (wartość: '{priceStr}').");
//                return null;
//            }
//            catch (TaskCanceledException)
//            {
//                _logger.LogWarning($"[IdoSell] Timeout ({HttpTimeoutSeconds}s) przy weryfikacji GET dla ID {productId}");
//                return null;
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, $"[IdoSell] Wyjątek podczas weryfikacji GET dla ID {productId}");
//                return null;
//            }
//        }

//        private class PatchOutcome
//        {
//            public PriceBridgeItemRequest ItemRequest { get; set; }
//            public PriceBridgeItem BridgeItem { get; set; }
//            public bool Success { get; set; }
//            public string ErrorMsg { get; set; }
//            public string ShopProductId { get; set; }
//        }

//        private class VerifyOutcome
//        {
//            public PriceBridgeItem BridgeItem { get; set; }
//            public decimal? VerifiedPrice { get; set; }
//            public string ShopProductId { get; set; }
//        }
//    }
//}










using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.DTOs;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
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
        // PrestaShop: PATCH sekwencyjnie, 1 na raz
        private const int PrestaParallelDegree = 2;
        private const int PrestaTimeoutSeconds = 20;

        // IdoSell: osobne ustawienia
        private const int IdoSellParallelDegree = 2;
        private const int IdoSellTimeoutSeconds = 20;

        // Batch GET weryfikacja - ile ID w jednym uzyciu filtra
        private const int VerifyBatchSize = 50;

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
            _logger.LogInformation($"[LogExportAsChangeAsync] Rozpoczynam logowanie eksportu dla sklepu ID: {storeId}, typ: {exportType}, ilosc pozycji: {items?.Count}");

            if (items == null || !items.Any())
            {
                _logger.LogWarning("[LogExportAsChangeAsync] Proba zapisu pustej listy itemow.");
                throw new ArgumentException("Brak danych do zapisu.");
            }

            PriceExportMethod method;
            if (!Enum.TryParse(exportType, true, out method))
            {
                method = PriceExportMethod.Csv;
                _logger.LogWarning($"[LogExportAsChangeAsync] Nieznany typ eksportu '{exportType}', ustawiono domyslnie Csv.");
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

            _logger.LogInformation($"[LogExportAsChangeAsync] Pomyslnie zapisano batch eksportu. ID Batcha: {batch.Id}, Liczba pozycji: {items.Count}");
            return items.Count;
        }

        public async Task<StorePriceBridgeResult> ExecuteStorePriceChangesAsync(
                    int storeId, int scrapHistoryId, string userId,
                    List<PriceBridgeItemRequest> itemsToBridge,
                    bool isAutomation = false, int? automationRuleId = null,
                    int totalProductsInRule = 0, int targetMetCount = 0, int targetUnmetCount = 0,
                    int priceIncreasedCount = 0, int priceDecreasedCount = 0, int priceMaintainedCount = 0)
        {
            _logger.LogInformation($"[ExecuteStorePriceChangesAsync] Start. StoreId: {storeId}, Items: {itemsToBridge?.Count}, TotalRule: {totalProductsInRule}");

            var result = new StorePriceBridgeResult();
            var store = await _context.Stores.FindAsync(storeId);

            if (store == null)
            {
                _logger.LogError($"[ExecuteStorePriceChangesAsync] BLAD: Sklep {storeId} nie istnieje.");
                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Sklep nie istnieje." });
                return result;
            }

            if (!store.IsStorePriceBridgeActive)
            {
                _logger.LogError($"[ExecuteStorePriceChangesAsync] BLAD: Integracja wylaczona dla {store.StoreName}.");
                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Integracja wylaczona." });
                return result;
            }

            switch (store.StoreSystemType)
            {
                case StoreSystemType.PrestaShop:
                    return await ExecutePrestaShopSessionAsync(store, scrapHistoryId, userId, itemsToBridge,
                        isAutomation, automationRuleId, totalProductsInRule,
                        targetMetCount, targetUnmetCount, priceIncreasedCount, priceDecreasedCount, priceMaintainedCount);

                case StoreSystemType.IdoSell:
                    return await ExecuteIdoSellSessionAsync(store, scrapHistoryId, userId, itemsToBridge,
                        isAutomation, automationRuleId, totalProductsInRule,
                        targetMetCount, targetUnmetCount, priceIncreasedCount, priceDecreasedCount, priceMaintainedCount);

                case StoreSystemType.WooCommerce:
                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "WooCommerce w trakcie wdrazania." });
                    return result;

                case StoreSystemType.Shoper:
                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Shoper w trakcie wdrazania." });
                    return result;

                default:
                    _logger.LogError($"[ExecuteStorePriceChangesAsync] BLAD: Nieobslugiwany typ: {store.StoreSystemType}");
                    result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = $"Nieobslugiwany typ: {store.StoreSystemType}" });
                    return result;
            }
        }

        // =====================================================================
        // PRESTASHOP - PATCH SEKWENCYJNY + BATCH GET WERYFIKACJA
        // =====================================================================
        private async Task<StorePriceBridgeResult> ExecutePrestaShopSessionAsync(
            StoreClass store, int scrapHistoryId, string userId,
            List<PriceBridgeItemRequest> itemsToBridge, bool isAutomation, int? automationRuleId,
            int totalProductsInRule, int targetMetCount, int targetUnmetCount,
            int priceIncreasedCount, int priceDecreasedCount, int priceMaintainedCount)
        {
            var totalSw = Stopwatch.StartNew();
            _logger.LogInformation($"[PrestaShop] ============ START sesji dla {store.StoreName} ({itemsToBridge.Count} produktow) ============");

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
                PriceIncreasedCount = (isAutomation && totalProductsInRule > 0) ? priceIncreasedCount : itemsToBridge.Count(x => x.NewPrice > x.CurrentPrice),
                PriceDecreasedCount = (isAutomation && totalProductsInRule > 0) ? priceDecreasedCount : itemsToBridge.Count(x => x.NewPrice < x.CurrentPrice),
                PriceMaintainedCount = (isAutomation && totalProductsInRule > 0) ? priceMaintainedCount : itemsToBridge.Count(x => x.NewPrice == x.CurrentPrice)
            };
            _context.PriceBridgeBatches.Add(newBatch);

            if (string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
            {
                _logger.LogError($"[PrestaShop] BLAD: Brak konfiguracji API dla {store.StoreName}.");
                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Brak konfiguracji API." });
                return result;
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(PrestaTimeoutSeconds + 10);

            var authToken = Encoding.ASCII.GetBytes($"{store.StoreApiKey}:");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            // Pobranie ExternalId
            var productIds = itemsToBridge.Select(x => x.ProductId).Distinct().ToList();
            var productsDict = await _context.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId);

            // Przygotowanie valid/invalid
            var validItems = new List<(PriceBridgeItemRequest itemRequest, PriceBridgeItem bridgeItem, string shopProductId, decimal expectedPriceBrutto)>();
            var allOutcomes = new List<PatchOutcome>();

            foreach (var itemRequest in itemsToBridge)
            {
                productsDict.TryGetValue(itemRequest.ProductId, out var product);
                if (product == null || product.ExternalId == null)
                {
                    string msg = product == null ? "Produkt nie znaleziony w bazie." : "Produkt nie ma ExternalId.";
                    _logger.LogError($"[PrestaShop] BLAD: Pominieto produkt ID {itemRequest.ProductId}: {msg}");
                    allOutcomes.Add(new PatchOutcome { ItemRequest = itemRequest, BridgeItem = null, Success = false, ErrorMsg = msg, ShopProductId = null });
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
                validItems.Add((itemRequest, bridgeItem, shopProductId, itemRequest.NewPrice));
            }

            _logger.LogInformation($"[PrestaShop] Przygotowano {validItems.Count} valid, {allOutcomes.Count} invalid.");

            // ============================================================
            // FAZA 1: PATCH SEKWENCYJNY
            // ============================================================
            int patchOk = 0, patchFail = 0, patchTimeout = 0;
            var expectedPrices = new Dictionary<string, decimal>();
            var patchedShopIds = new List<string>();

            using var semaphore = new SemaphoreSlim(PrestaParallelDegree);

            var patchTasks = validItems.Select(async v =>
            {
                await semaphore.WaitAsync();
                var sw = Stopwatch.StartNew();
                try
                {
                    decimal priceNet = Math.Round(v.expectedPriceBrutto / 1.23m, 6);
                    string priceNetStr = priceNet.ToString(CultureInfo.InvariantCulture);
                    string apiUrl = $"{store.StoreApiUrl.TrimEnd('/')}/products/{v.shopProductId}";

                    string xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<prestashop xmlns:xlink=""http://www.w3.org/1999/xlink"">
    <product><id>{v.shopProductId}</id><price>{priceNetStr}</price></product>
</prestashop>";

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(PrestaTimeoutSeconds));
                    var req = new HttpRequestMessage(new HttpMethod("PATCH"), apiUrl)
                    {
                        Content = new StringContent(xml, Encoding.UTF8, "application/xml")
                    };

                    var resp = await client.SendAsync(req, cts.Token);
                    var body = await resp.Content.ReadAsStringAsync();
                    sw.Stop();

                    if (!resp.IsSuccessStatusCode)
                    {
                        var bp = body.Length > 300 ? body.Substring(0, 300) : body;
                        _logger.LogError($"[PrestaShop] BLAD PATCH ID {v.shopProductId}: HTTP {(int)resp.StatusCode} po {sw.ElapsedMilliseconds}ms | {bp}");
                        Interlocked.Increment(ref patchFail);
                        return (v, false, $"PATCH HTTP {resp.StatusCode}");
                    }

                    if (body.Contains("<errors>") || body.Contains("<e>"))
                    {
                        var bp = body.Length > 300 ? body.Substring(0, 300) : body;
                        _logger.LogError($"[PrestaShop] BLAD PATCH ID {v.shopProductId}: HTTP 200 z <errors> po {sw.ElapsedMilliseconds}ms | {bp}");
                        Interlocked.Increment(ref patchFail);
                        return (v, false, "HTTP 200 z bledami w body");
                    }

                    _logger.LogInformation($"[PrestaShop] PATCH OK ID {v.shopProductId} po {sw.ElapsedMilliseconds}ms");
                    Interlocked.Increment(ref patchOk);

                    lock (expectedPrices)
                    {
                        expectedPrices[v.shopProductId] = v.expectedPriceBrutto;
                        patchedShopIds.Add(v.shopProductId);
                    }

                    return (v, true, string.Empty);
                }
                catch (TaskCanceledException)
                {
                    sw.Stop();
                    _logger.LogError($"[PrestaShop] TIMEOUT PATCH ID {v.shopProductId} po {sw.ElapsedMilliseconds}ms (limit {PrestaTimeoutSeconds}s)");
                    Interlocked.Increment(ref patchTimeout);
                    return (v, false, $"Timeout ({PrestaTimeoutSeconds}s) po {sw.ElapsedMilliseconds}ms");
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    _logger.LogError(ex, $"[PrestaShop] WYJATEK PATCH ID {v.shopProductId} po {sw.ElapsedMilliseconds}ms");
                    Interlocked.Increment(ref patchFail);
                    return (v, false, $"Exception: {ex.Message}");
                }
                finally { semaphore.Release(); }
            }).ToList();

            var patchResults = await Task.WhenAll(patchTasks);
            _logger.LogInformation($"[PrestaShop] Faza PATCH: OK={patchOk}, Bledy={patchFail}, Timeouty={patchTimeout} po {totalSw.ElapsedMilliseconds}ms");

            // ============================================================
            // FAZA 2: BATCH GET WERYFIKACJA
            // ============================================================
            var verifiedPrices = new Dictionary<string, decimal?>();

            if (patchedShopIds.Any())
            {
                _logger.LogInformation($"[PrestaShop] Oczekiwanie 500ms przed weryfikacja...");
                await Task.Delay(500);

                var verifySw = Stopwatch.StartNew();
                for (int i = 0; i < patchedShopIds.Count; i += VerifyBatchSize)
                {
                    var chunk = patchedShopIds.Skip(i).Take(VerifyBatchSize).ToList();
                    var chunkPrices = await BatchGetPrestaShopPricesAsync(client, store.StoreApiUrl, chunk);
                    foreach (var kv in chunkPrices) verifiedPrices[kv.Key] = kv.Value;
                }
                verifySw.Stop();

                int gotPrices = verifiedPrices.Count(kv => kv.Value.HasValue);
                _logger.LogInformation($"[PrestaShop] Weryfikacja po {verifySw.ElapsedMilliseconds}ms. Pobrano cen: {gotPrices}/{patchedShopIds.Count}");
            }

            // ============================================================
            // FAZA 3: POROWNANIE CEN I ZAPIS WYNIKOW
            // ============================================================
            foreach (var (v, patchSuccess, patchError) in patchResults)
            {
                if (v.bridgeItem == null) continue; // invalid - juz w allOutcomes

                if (!patchSuccess)
                {
                    v.bridgeItem.Success = false;
                    allOutcomes.Add(new PatchOutcome { ItemRequest = v.itemRequest, BridgeItem = v.bridgeItem, Success = false, ErrorMsg = patchError, ShopProductId = v.shopProductId });
                    continue;
                }

                // PATCH OK - sprawdzamy weryfikacje
                verifiedPrices.TryGetValue(v.shopProductId, out var actualPrice);

                if (actualPrice.HasValue)
                {
                    v.bridgeItem.PriceAfter = actualPrice.Value;
                    decimal diff = Math.Abs(actualPrice.Value - v.expectedPriceBrutto);

                    if (diff <= 0.02m)
                    {
                        v.bridgeItem.Success = true;
                        allOutcomes.Add(new PatchOutcome { ItemRequest = v.itemRequest, BridgeItem = v.bridgeItem, Success = true, ErrorMsg = string.Empty, ShopProductId = v.shopProductId });
                    }
                    else
                    {
                        _logger.LogError($"[PrestaShop] ROZBIEZNOSC ID {v.shopProductId}: oczekiwano {v.expectedPriceBrutto} zl, w sklepie {actualPrice.Value} zl (diff={diff:F2}). Mozliwe: specific_price, modul blokujacy, multi-shop.");
                        v.bridgeItem.Success = false;
                        allOutcomes.Add(new PatchOutcome { ItemRequest = v.itemRequest, BridgeItem = v.bridgeItem, Success = false, ErrorMsg = $"Cena nie zmieniona: oczekiwano {v.expectedPriceBrutto}, w sklepie {actualPrice.Value}", ShopProductId = v.shopProductId });
                    }
                }
                else
                {
                    // Brak weryfikacji - zakladamy sukces PATCH
                    _logger.LogWarning($"[PrestaShop] Brak danych weryfikacji dla ID {v.shopProductId} - zakladam sukces PATCH.");
                    v.bridgeItem.Success = true;
                    allOutcomes.Add(new PatchOutcome { ItemRequest = v.itemRequest, BridgeItem = v.bridgeItem, Success = true, ErrorMsg = string.Empty, ShopProductId = v.shopProductId });
                }

                result.SuccessfulChangesDetails.Add(new StorePriceBridgeSuccessDetail { ExternalId = v.shopProductId, FetchedNewPrice = actualPrice });
            }

            // Zliczanie i zapis
            foreach (var outcome in allOutcomes)
            {
                if (outcome.BridgeItem == null)
                {
                    result.FailedCount++;
                    result.Errors.Add(new StorePriceBridgeError { ProductId = outcome.ItemRequest.ProductId.ToString(), Message = outcome.ErrorMsg });
                    continue;
                }
                if (!outcome.Success)
                {
                    result.FailedCount++;
                    result.Errors.Add(new StorePriceBridgeError { ProductId = outcome.ShopProductId, Message = outcome.ErrorMsg });
                }
                else
                {
                    result.SuccessfulCount++;
                }
                newBatch.BridgeItems.Add(outcome.BridgeItem);
            }

            newBatch.SuccessfulCount = result.SuccessfulCount;
            newBatch.FailedCount = result.FailedCount;
            await _context.SaveChangesAsync();

            totalSw.Stop();
            if (result.FailedCount > 0)
                _logger.LogError($"[PrestaShop] ============ KONIEC {store.StoreName}. OK: {result.SuccessfulCount}, BLEDY: {result.FailedCount}, Timeouty: {patchTimeout}. Czas: {totalSw.ElapsedMilliseconds}ms. Batch: {newBatch.Id} ============");
            else
                _logger.LogInformation($"[PrestaShop] ============ KONIEC {store.StoreName}. OK: {result.SuccessfulCount}. Czas: {totalSw.ElapsedMilliseconds}ms. Batch: {newBatch.Id} ============");

            return result;
        }

        /// <summary>
        /// Batch GET cen z PrestaShop - analogicznie do ApiBotService.ProcessPrestaShopBatchAsync.
        /// Jeden request po wiele produktow z filtrem filter[id].
        /// </summary>
        private async Task<Dictionary<string, decimal?>> BatchGetPrestaShopPricesAsync(
            HttpClient client, string baseUrl, List<string> shopProductIds)
        {
            var results = new Dictionary<string, decimal?>();
            if (!shopProductIds.Any()) return results;

            var sw = Stopwatch.StartNew();
            string idsFilter = string.Join("|", shopProductIds);
            string apiUrl = $"{baseUrl.TrimEnd('/')}/products?display=[id,price]&filter[id]=[{idsFilter}]";

            try
            {
                var response = await client.GetAsync(apiUrl);
                sw.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"[PrestaShop] BLAD BATCH-GET: HTTP {(int)response.StatusCode} po {sw.ElapsedMilliseconds}ms");
                    foreach (var id in shopProductIds) results[id] = null;
                    return results;
                }

                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"[PrestaShop] BATCH-GET OK po {sw.ElapsedMilliseconds}ms, size={content.Length}B, IDs={shopProductIds.Count}");

                var doc = XDocument.Parse(content);
                foreach (var prodEl in doc.Descendants("product"))
                {
                    var idEl = prodEl.Element("id");
                    var priceEl = prodEl.Element("price");
                    if (idEl == null || priceEl == null) continue;

                    string id = idEl.Value.Trim();
                    if (decimal.TryParse(priceEl.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal priceNetto))
                        results[id] = Math.Round(priceNetto * 1.23m, 2);
                    else
                        results[id] = null;
                }

                foreach (var id in shopProductIds)
                    if (!results.ContainsKey(id)) results[id] = null;

                return results;
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
                _logger.LogError($"[PrestaShop] TIMEOUT BATCH-GET po {sw.ElapsedMilliseconds}ms");
                foreach (var id in shopProductIds) results[id] = null;
                return results;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, $"[PrestaShop] WYJATEK BATCH-GET po {sw.ElapsedMilliseconds}ms");
                foreach (var id in shopProductIds) results[id] = null;
                return results;
            }
        }

        // =====================================================================
        // IDOSELL - BEZ ZMIAN
        // =====================================================================
        private async Task<StorePriceBridgeResult> ExecuteIdoSellSessionAsync(
            StoreClass store, int scrapHistoryId, string userId,
            List<PriceBridgeItemRequest> itemsToBridge, bool isAutomation, int? automationRuleId,
            int totalProductsInRule, int targetMetCount, int targetUnmetCount,
            int priceIncreasedCount, int priceDecreasedCount, int priceMaintainedCount)
        {
            _logger.LogInformation($"[IdoSell] Rozpoczynam sesje dla {store.StoreName}.");

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
                PriceIncreasedCount = (isAutomation && totalProductsInRule > 0) ? priceIncreasedCount : itemsToBridge.Count(x => x.NewPrice > x.CurrentPrice),
                PriceDecreasedCount = (isAutomation && totalProductsInRule > 0) ? priceDecreasedCount : itemsToBridge.Count(x => x.NewPrice < x.CurrentPrice),
                PriceMaintainedCount = (isAutomation && totalProductsInRule > 0) ? priceMaintainedCount : itemsToBridge.Count(x => x.NewPrice == x.CurrentPrice)
            };
            _context.PriceBridgeBatches.Add(newBatch);

            if (string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
            {
                _logger.LogError($"[IdoSell] BLAD: Brak konfiguracji API dla {store.StoreName}.");
                result.Errors.Add(new StorePriceBridgeError { ProductId = "ALL", Message = "Brak konfiguracji API." });
                return result;
            }

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(IdoSellTimeoutSeconds);

            var productIds = itemsToBridge.Select(x => x.ProductId).Distinct().ToList();
            var productsDict = await _context.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId)).ToDictionaryAsync(p => p.ProductId);

            using var semaphore = new SemaphoreSlim(IdoSellParallelDegree);

            var patchTasks = itemsToBridge.Select(async itemRequest =>
            {
                await semaphore.WaitAsync();
                try
                {
                    productsDict.TryGetValue(itemRequest.ProductId, out var product);
                    if (product == null || product.ExternalId == null)
                    {
                        string msg = product == null ? "Produkt nie znaleziony w bazie." : "Produkt nie ma ExternalId.";
                        _logger.LogError($"[IdoSell] BLAD: Pominieto produkt ID {itemRequest.ProductId}: {msg}");
                        return new PatchOutcome { ItemRequest = itemRequest, BridgeItem = null, Success = false, ErrorMsg = msg, ShopProductId = null };
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

                    var (success, errorMsg) = await UpdateIdoSellProductAsync(client, store.StoreApiUrl, store.StoreApiKey, shopProductId, itemRequest.NewPrice);
                    if (!success)
                        _logger.LogError($"[IdoSell] BLAD aktualizacji ID {shopProductId}: {errorMsg}");

                    bridgeItem.Success = success;
                    return new PatchOutcome { ItemRequest = itemRequest, BridgeItem = bridgeItem, Success = success, ErrorMsg = errorMsg, ShopProductId = shopProductId };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[IdoSell] WYJATEK produktu ID {itemRequest.ProductId}");
                    return new PatchOutcome { ItemRequest = itemRequest, BridgeItem = null, Success = false, ErrorMsg = ex.Message, ShopProductId = null };
                }
                finally { semaphore.Release(); }
            }).ToList();

            var patchOutcomes = await Task.WhenAll(patchTasks);

            var itemsToVerify = new List<PriceBridgeItem>();
            foreach (var outcome in patchOutcomes)
            {
                if (outcome.BridgeItem == null)
                {
                    result.FailedCount++;
                    result.Errors.Add(new StorePriceBridgeError { ProductId = outcome.ItemRequest.ProductId.ToString(), Message = outcome.ErrorMsg });
                    continue;
                }
                if (!outcome.Success)
                {
                    result.FailedCount++;
                    result.Errors.Add(new StorePriceBridgeError { ProductId = outcome.ShopProductId, Message = outcome.ErrorMsg });
                }
                else
                {
                    result.SuccessfulCount++;
                    itemsToVerify.Add(outcome.BridgeItem);
                }
                newBatch.BridgeItems.Add(outcome.BridgeItem);
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation($"[IdoSell] Zakonczono wysylanie. Sukcesy: {result.SuccessfulCount}, Bledy: {result.FailedCount}. Batch ID: {newBatch.Id}");

            if (itemsToVerify.Any())
            {
                _logger.LogInformation($"[IdoSell] Oczekiwanie 500ms przed weryfikacja...");
                await Task.Delay(500);

                var verifyTasks = itemsToVerify.Select(async bridgeItem =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        if (!productsDict.TryGetValue(bridgeItem.ProductId, out var product) || product?.ExternalId == null)
                            return new VerifyOutcome { BridgeItem = bridgeItem, VerifiedPrice = null, ShopProductId = null };

                        string shopProductId = product.ExternalId.ToString();
                        decimal? verifiedPrice = await GetIdoSellPriceAsync(client, store.StoreApiUrl, store.StoreApiKey, shopProductId);
                        if (!verifiedPrice.HasValue)
                            _logger.LogWarning($"[IdoSell] Nie udalo sie zweryfikowac ceny dla ID {shopProductId}.");

                        return new VerifyOutcome { BridgeItem = bridgeItem, VerifiedPrice = verifiedPrice, ShopProductId = shopProductId };
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"[IdoSell] WYJATEK weryfikacji ID {bridgeItem.ProductId}");
                        return new VerifyOutcome { BridgeItem = bridgeItem, VerifiedPrice = null, ShopProductId = null };
                    }
                    finally { semaphore.Release(); }
                }).ToList();

                var verifyOutcomes = await Task.WhenAll(verifyTasks);
                foreach (var vo in verifyOutcomes)
                {
                    if (vo.VerifiedPrice.HasValue) vo.BridgeItem.PriceAfter = vo.VerifiedPrice.Value;
                    if (vo.ShopProductId != null)
                        result.SuccessfulChangesDetails.Add(new StorePriceBridgeSuccessDetail { ExternalId = vo.ShopProductId, FetchedNewPrice = vo.VerifiedPrice });
                }

                newBatch.SuccessfulCount = result.SuccessfulCount;
                await _context.SaveChangesAsync();
                _logger.LogInformation($"[IdoSell] Zakonczono weryfikacje.");
            }

            return result;
        }

        private async Task<(bool success, string error)> UpdateIdoSellProductAsync(HttpClient client, string baseUrl, string apiKey, string productId, decimal newPriceBrutto)
        {
            try
            {
                if (!int.TryParse(productId, out int productIdInt))
                    return (false, $"Nieprawidlowe productId '{productId}' (oczekiwano int).");

                decimal priceBrutto = Math.Round(newPriceBrutto, 2);
                string priceString = priceBrutto.ToString(CultureInfo.InvariantCulture);
                string apiUrl = $"{baseUrl.TrimEnd('/')}/api/admin/v3/products/products";

                string jsonBody = $@"{{""params"":{{""products"":[{{""productId"":{productIdInt},""productRetailPrice"":{priceString}}}]}}}}";

                using var request = new HttpRequestMessage(HttpMethod.Put, apiUrl)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("X-API-KEY", apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                var preview = body != null && body.Length > 1500 ? body.Substring(0, 1500) + "...[uciete]" : body;
                _logger.LogInformation($"[IdoSell] PUT ID {productId} <- HTTP {(int)response.StatusCode}, body: {preview}");

                if (!response.IsSuccessStatusCode && (int)response.StatusCode != 207)
                    return (false, $"PUT Error: {response.StatusCode}. Detale: {preview}");

                try
                {
                    var root = JsonNode.Parse(body);

                    var errorsNode = root?["errors"];
                    if (errorsNode is JsonObject errorsObj && errorsObj.Count > 0)
                    {
                        var fc = errorsObj["faultCode"]?.ToString();
                        var fs = errorsObj["faultString"]?.ToString();
                        if (!string.IsNullOrEmpty(fc) && fc != "0") return (false, $"IdoSell blad '{fc}': {fs}");
                    }
                    else if (errorsNode is JsonArray errorsArr && errorsArr.Count > 0)
                        return (false, $"IdoSell errors: {errorsArr[0]?.ToJsonString()}");

                    var fc2 = root?["faultCode"]?.ToString();
                    if (!string.IsNullOrEmpty(fc2) && fc2 != "0")
                        return (false, $"IdoSell fault {fc2}: {root?["faultString"]}");

                    if (root?["results"]?["productsResults"] is JsonArray productsResults)
                    {
                        foreach (var resNode in productsResults)
                        {
                            if (resNode?["faults"] is JsonArray faultsArr && faultsArr.Count > 0)
                            {
                                var f = faultsArr[0];
                                return (false, $"IdoSell fault produktu {resNode["productId"]}: {f?["faultCode"]} - {f?["faultString"]}");
                            }
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, $"[IdoSell] Nie udalo sie sparsowac body dla ID {productId}");
                }

                return (true, string.Empty);
            }
            catch (TaskCanceledException) { return (false, $"Timeout ({IdoSellTimeoutSeconds}s) przy PUT dla produktu {productId}."); }
            catch (Exception ex) { return (false, "Exception: " + ex.Message); }
        }

        private async Task<decimal?> GetIdoSellPriceAsync(HttpClient client, string baseUrl, string apiKey, string productId)
        {
            string apiUrl = $"{baseUrl.TrimEnd('/')}/api/admin/v3/products/products?productIds={productId}";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Add("X-API-KEY", apiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                var content = await response.Content.ReadAsStringAsync();
                var results = JsonNode.Parse(content)?["results"] as JsonArray;
                if (results == null || results.Count == 0) return null;

                var priceStr = results[0]?["productRetailPrice"]?.ToString();
                if (decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal p) && p > 0)
                    return Math.Round(p, 2);
                return null;
            }
            catch { return null; }
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