using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceTracker.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class DatabaseSizeController : Controller
    {
        private readonly PriceTrackerContext _context;
        private static decimal totalUsedSpaceMB;
        private static int totalPriceHistoriesCount;

        public DatabaseSizeController(PriceTrackerContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var priceHistories = await _context.PriceHistories
                .GroupBy(ph => new { ph.ScrapHistoryId, ph.ScrapHistory.StoreId, ph.ScrapHistory.Store.StoreName })
                .Select(g => new
                {
                    ScrapHistoryId = g.Key.ScrapHistoryId,
                    StoreId = g.Key.StoreId,
                    StoreName = g.Key.StoreName,
                    PriceHistoriesCount = g.Count()
                })
                .ToListAsync();

            var totalSizeKB = await CalculateTotalSpaceForPriceHistories();

            var tableSizes = new List<TableSizeInfo>();

            foreach (var ph in priceHistories)
            {
                var totalSpaceKB = totalSizeKB * ph.PriceHistoriesCount / priceHistories.Sum(x => x.PriceHistoriesCount);

                tableSizes.Add(new TableSizeInfo
                {
                    ScrapHistoryId = ph.ScrapHistoryId,
                    StoreId = ph.StoreId,
                    StoreName = ph.StoreName,
                    PriceHistoriesCount = ph.PriceHistoriesCount,
                    TotalSpaceKB = totalSpaceKB,
                    UsedSpaceMB = Math.Round(totalSpaceKB / 1024.0m, 4)
                });
            }

            totalUsedSpaceMB = tableSizes.Sum(ts => ts.UsedSpaceMB);
            totalPriceHistoriesCount = tableSizes.Sum(ts => ts.PriceHistoriesCount);

            var storeSummaries = tableSizes
                .GroupBy(ts => new { ts.StoreId, ts.StoreName })
                .Select(g => new StoreSummaryViewModel
                {
                    StoreId = (int)g.Key.StoreId,
                    StoreName = g.Key.StoreName,
                    TotalPriceHistoriesCount = g.Sum(ts => ts.PriceHistoriesCount),
                    TotalUsedSpaceMB = Math.Round(g.Sum(ts => ts.UsedSpaceMB), 4)
                })
                .ToList();

            var viewModel = new DatabaseSizeSummaryViewModel
            {
                StoreSummaries = storeSummaries,
                TotalUsedSpaceMB = Math.Round(totalUsedSpaceMB, 4),
                TotalPriceHistories = totalPriceHistoriesCount
            };

            return View("~/Views/ManagerPanel/DatabaseSize/Index.cshtml", viewModel);
        }

        public async Task<IActionResult> StoreDetails(int storeId)
        {
            var priceHistories = await _context.PriceHistories
                .Where(ph => ph.ScrapHistory.StoreId == storeId)
                .GroupBy(ph => new { ph.ScrapHistoryId, ph.ScrapHistory.StoreId, ph.ScrapHistory.Store.StoreName })
                .Select(g => new
                {
                    ScrapHistoryId = g.Key.ScrapHistoryId,
                    StoreId = g.Key.StoreId,
                    StoreName = g.Key.StoreName,
                    PriceHistoriesCount = g.Count()
                })
                .ToListAsync();

            var tableSizes = new List<TableSizeInfo>();

            foreach (var ph in priceHistories)
            {
                var totalSpaceKB = (totalUsedSpaceMB * 1024m) * ph.PriceHistoriesCount / totalPriceHistoriesCount;

                tableSizes.Add(new TableSizeInfo
                {
                    ScrapHistoryId = ph.ScrapHistoryId,
                    StoreId = ph.StoreId,
                    StoreName = ph.StoreName,
                    PriceHistoriesCount = ph.PriceHistoriesCount,
                    TotalSpaceKB = (long)totalSpaceKB,
                    UsedSpaceMB = Math.Round(totalSpaceKB / 1024.0m, 4)
                });
            }

            var store = await _context.Stores.FindAsync(storeId);
            var viewModel = new StoreDetailsViewModel
            {
                StoreName = store.StoreName,
                TableSizes = tableSizes
            };

            return View("~/Views/ManagerPanel/DatabaseSize/StoreDetails.cshtml", viewModel);
        }

        private async Task<long> CalculateTotalSpaceForPriceHistories()
        {
            var query = @"
                EXEC sp_spaceused 'PriceHistories'
            ";

            long totalSpaceKB = 0;

            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = query;
                _context.Database.OpenConnection();
                using (var result = await command.ExecuteReaderAsync())
                {
                    if (await result.ReadAsync())
                    {
                        var reservedSpace = result["reserved"].ToString();
                        if (long.TryParse(reservedSpace.Replace(" KB", "").Replace(",", ""), out var reservedKB))
                        {
                            totalSpaceKB = reservedKB;
                        }
                    }
                }
                _context.Database.CloseConnection();
            }

            return totalSpaceKB;
        }
    }

    public class TableSizeInfo
    {
        public decimal UsedSpaceMB { get; set; }
        public long TotalSpaceKB { get; set; }
        public int? ScrapHistoryId { get; set; }
        public int? StoreId { get; set; }
        public string StoreName { get; set; }
        public int PriceHistoriesCount { get; set; }
    }

    public class DatabaseSizeSummaryViewModel
    {
        public List<StoreSummaryViewModel> StoreSummaries { get; set; }
        public decimal TotalUsedSpaceMB { get; set; }
        public int TotalPriceHistories { get; set; }
    }

    public class StoreSummaryViewModel
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public int TotalPriceHistoriesCount { get; set; }
        public decimal TotalUsedSpaceMB { get; set; }
    }

    public class StoreDetailsViewModel
    {
        public string StoreName { get; set; }
        public List<TableSizeInfo> TableSizes { get; set; }
    }
}
