using NPOI.XSSF.UserModel;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Net.Http.Headers;
using System.Text;
using static PriceSafari.Controllers.MemberControllers.PriceHistoryController;

namespace PriceSafari.Services.ScheduleService
{
    public class StorePriceBridgeService
    {
        private readonly PriceSafariContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<StorePriceBridgeService> _logger;

        public StorePriceBridgeService(PriceSafariContext context, IHttpClientFactory httpClientFactory, ILogger<StorePriceBridgeService> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<(bool Success, int Count, byte[]? FileContent, string FileName)> ExecuteStorePriceActionAsync(
            int storeId,
            int scrapHistoryId,
            string userId,
            string actionType, // "api", "csv", "excel"
            List<PriceBridgeItemRequest> items)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return (false, 0, null, "");

            // Tworzymy Batcha
            var batch = new PriceBridgeBatch
            {
                StoreId = storeId,
                ScrapHistoryId = scrapHistoryId,
                UserId = userId,
                ExecutionDate = DateTime.Now,
                SuccessfulCount = 0,
                // Mapowanie typu eksportu (uproszczone)
                ExportMethod = actionType.ToLower() == "api" ? PriceExportMethod.Api :
                               (actionType.ToLower() == "excel" ? PriceExportMethod.Excel : PriceExportMethod.Csv),
                BridgeItems = new List<PriceBridgeItem>()
            };

            byte[]? fileBytes = null;
            string fileName = "";
            bool isApiAction = actionType.ToLower() == "api";

            // 1. Logika API (jeśli aktywne i wybrano API)
            if (isApiAction && store.IsStorePriceBridgeActive)
            {
                // Tutaj następuje faktyczna wysyłka do sklepu
                await ProcessApiUpdateAsync(store, items, batch);
            }
            else
            {
                // 2. Logika Eksportu (Symulacja zmiany + plik)
                // Jeśli wybrano API, ale sklep ma wyłączoną flagę -> fallback do CSV/Logowania bez wysyłki
                foreach (var item in items)
                {
                    batch.BridgeItems.Add(CreateBridgeItem(item, success: true)); // Zakładamy sukces generowania pliku
                }
                batch.SuccessfulCount = items.Count;

                if (actionType.ToLower() == "excel")
                {
                    fileBytes = GenerateExcelFile(items, store.StoreName);
                    fileName = $"ZmianyCen_{store.StoreName}_{DateTime.Now:yyyyMMddHHmm}.xlsx";
                }
                else // CSV (domyślnie)
                {
                    fileBytes = GenerateCsvFile(items);
                    fileName = $"ZmianyCen_{store.StoreName}_{DateTime.Now:yyyyMMddHHmm}.csv";
                }
            }

            _context.PriceBridgeBatches.Add(batch);
            await _context.SaveChangesAsync();

            return (true, batch.SuccessfulCount, fileBytes, fileName);
        }

        private async Task ProcessApiUpdateAsync(StoreClass store, List<PriceBridgeItemRequest> items, PriceBridgeBatch batch)
        {
            // Sprawdzamy typ systemu
            switch (store.StoreSystemType)
            {
                case StoreSystemType.PrestaShop:
                    await UpdatePrestaShopBatchAsync(store, items, batch);
                    break;

                // Tutaj dodasz kolejne systemy (WooCommerce, Shoper)
                case StoreSystemType.WooCommerce:
                    // await UpdateWooCommerceBatchAsync(store, items, batch);
                    foreach (var item in items)
                        batch.BridgeItems.Add(CreateBridgeItem(item, false, "WooCommerce: not implemented yet"));
                    break;

                default:
                    _logger.LogWarning($"Próba zmiany cen API dla nieobsługiwanego systemu: {store.StoreSystemType}");
                    foreach (var item in items)
                        batch.BridgeItems.Add(CreateBridgeItem(item, false, "System nieobsługiwany przez API"));
                    break;
            }
        }

        private async Task UpdatePrestaShopBatchAsync(StoreClass store, List<PriceBridgeItemRequest> items, PriceBridgeBatch batch)
        {
            var client = _httpClientFactory.CreateClient();
            var authToken = Encoding.ASCII.GetBytes($"{store.StoreApiKey}:");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            string baseUrl = store.StoreApiUrl.TrimEnd('/');

            foreach (var item in items)
            {
                bool success = false;
                string errorMsg = "";

                try
                {
                    // W PrestaShop zazwyczaj trzeba najpierw pobrać produkt, zmienić cenę i wysłać PUT XML/JSON
                    // To jest uproszczony przykład JSON (wymaga włączenia JSON w Presta)

                    // 1. Budujemy URL do aktualizacji (uwaga: Presta API jest specyficzne, często wymaga XML)
                    // Przykład dla JSON (jeśli moduł API JSON jest aktywny):
                    var payload = new
                    {
                        product = new
                        {
                            id = item.ProductId, // Tutaj uwaga: item.ProductId to nasze ID. Potrzebujemy ExternalId!
                            // Musisz upewnić się, że masz ExternalId w requestcie lub pobrać je z bazy
                            price = item.NewPrice
                        }
                    };

                    // UWAGA: W realnym scenariuszu musisz mapować InternalID -> ExternalID
                    // Zakładam, że item.ProductId w requestcie to już ID zrozumiałe dla sklepu (ExternalId) 
                    // lub musisz je tu pobrać z bazy.

                    // Symulacja sukcesu dla przykładu (gdyż implementacja pełnego PUT Presta jest długa):
                    // var response = await client.PutAsJsonAsync($"{baseUrl}/products/{externalId}", payload);

                    // Placeholder logic:
                    await Task.Delay(50); // Symulacja opóźnienia sieciowego
                    success = true;

                    // if (!response.IsSuccessStatusCode) { success = false; errorMsg = response.ReasonPhrase; }
                }
                catch (Exception ex)
                {
                    success = false;
                    errorMsg = ex.Message;
                    _logger.LogError(ex, "Błąd aktualizacji ceny PrestaShop dla produktu {Id}", item.ProductId);
                }

                if (success) batch.SuccessfulCount++;
                batch.BridgeItems.Add(CreateBridgeItem(item, success, errorMsg));
            }
        }

        private PriceBridgeItem CreateBridgeItem(PriceBridgeItemRequest item, bool success, string error = null)
        {
            return new PriceBridgeItem
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

                Success = success,
                ErrorMessage = error
            };
        }

        // --- METODY GENEROWANIA PLIKÓW (PRZENIESIONE Z KONTROLERA) ---

        private byte[] GenerateCsvFile(List<PriceBridgeItemRequest> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ProductId;OldPrice;NewPrice;Margin");
            foreach (var item in items)
            {
                sb.AppendLine($"{item.ProductId};{item.CurrentPrice};{item.NewPrice};{item.MarginPrice}");
            }
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        private byte[] GenerateExcelFile(List<PriceBridgeItemRequest> items, string storeName)
        {
            using (var workbook = new XSSFWorkbook())
            {
                var sheet = workbook.CreateSheet("Zmiany Cen");
                var headerRow = sheet.CreateRow(0);
                headerRow.CreateCell(0).SetCellValue("ID Produktu");
                headerRow.CreateCell(1).SetCellValue("Stara Cena");
                headerRow.CreateCell(2).SetCellValue("Nowa Cena");
                headerRow.CreateCell(3).SetCellValue("Marża");

                int rowIdx = 1;
                foreach (var item in items)
                {
                    var row = sheet.CreateRow(rowIdx++);
                    row.CreateCell(0).SetCellValue(item.ProductId);
                    row.CreateCell(1).SetCellValue((double)item.CurrentPrice);
                    row.CreateCell(2).SetCellValue((double)item.NewPrice);
                    row.CreateCell(3).SetCellValue((double)(item.MarginPrice ?? 0));
                }

                using (var stream = new MemoryStream())
                {
                    workbook.Write(stream);
                    return stream.ToArray();
                }
            }
        }
    }
}
