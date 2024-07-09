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

        public DatabaseSizeController(PriceTrackerContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Pobranie wszystkich PriceHistories z grupowaniem według ScrapHistoryId i StoreId
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

            // Obliczanie rozmiaru danych
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
                    UsedSpaceMB = totalSpaceKB / 1024.0m
                });
            }

            // Sumowanie wartości
            var totalUsedSpaceMB = tableSizes.Sum(ts => ts.UsedSpaceMB);
            var totalPriceHistories = tableSizes.Sum(ts => ts.PriceHistoriesCount);

            // Sortowanie wyników
            var sortedTableSizes = tableSizes.OrderByDescending(ts => ts.ScrapHistoryId).ToList();

            // Przygotowanie danych do widoku
            var viewModel = new DatabaseSizeViewModel
            {
                TableSizes = sortedTableSizes,
                TotalUsedSpaceMB = totalUsedSpaceMB,
                TotalPriceHistories = totalPriceHistories
            };

            return View("~/Views/ManagerPanel/DatabaseSize/Index.cshtml", viewModel);
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

    public class DatabaseSizeViewModel
    {
        public List<TableSizeInfo> TableSizes { get; set; }
        public decimal TotalUsedSpaceMB { get; set; }
        public int TotalPriceHistories { get; set; }
    }
}
