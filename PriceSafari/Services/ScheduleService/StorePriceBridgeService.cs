using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
// Upewnij się, że masz using do miejsca, gdzie jest PriceBridgeItemRequest

namespace PriceSafari.Services.ScheduleService
{
    public class StorePriceBridgeService
    {
        private readonly PriceSafariContext _context;
        // HttpClient i Logger zostawiam, jeśli będą potrzebne do innej logiki w tym serwisie
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

        // Metoda zwraca int (liczbę zapisanych elementów) lub rzuca wyjątek w przypadku błędu logicznego
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
                UserId = userId, // UserId przekazujemy z kontrolera
                ExecutionDate = DateTime.Now,
                SuccessfulCount = items.Count,
                ExportMethod = method,
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
    }
}