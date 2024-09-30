using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class DatabaseSizeController : Controller
    {
        private readonly PriceSafariContext _context;
        private static decimal totalUsedSpaceMB;
        private static int totalPriceHistoriesCount;

        public DatabaseSizeController(PriceSafariContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // Istniejący kod dla PriceHistories
            var priceHistories = await _context.PriceHistories
                .GroupBy(ph => new { ph.ScrapHistory.StoreId, ph.ScrapHistory.Store.StoreName })
                .Select(g => new
                {
                    StoreId = g.Key.StoreId,
                    StoreName = g.Key.StoreName,
                    PriceHistoriesCount = g.Count()
                })
                .ToListAsync();

            var totalSizeKB = await CalculateTotalSpaceForPriceHistories();

            var totalPriceHistoriesCount = priceHistories.Sum(x => x.PriceHistoriesCount);

            var storeSummariesDict = new Dictionary<int, StoreSummaryViewModel>();

            foreach (var ph in priceHistories)
            {
                var totalSpaceKB = totalSizeKB * ph.PriceHistoriesCount / totalPriceHistoriesCount;

                storeSummariesDict[ph.StoreId] = new StoreSummaryViewModel
                {
                    StoreId = ph.StoreId,
                    StoreName = ph.StoreName,
                    TotalPriceHistoriesCount = ph.PriceHistoriesCount,
                    TotalUsedSpaceMB = Math.Round(totalSpaceKB / 1024.0m, 4)
                };
            }

            // Obliczenie całkowitego rozmiaru i liczby rekordów GlobalPriceReports
            var totalGlobalPriceReportsSizeKB = await CalculateTotalSpaceForGlobalPriceReports();
            var totalGlobalPriceReportsCount = await _context.GlobalPriceReports.CountAsync();

            // Pobranie liczby GlobalPriceReports dla każdego sklepu
            var globalPriceReportsPerStore = await _context.GlobalPriceReports
                .Include(gpr => gpr.PriceSafariReport)
                .ThenInclude(psr => psr.Store)
                .GroupBy(gpr => new { gpr.PriceSafariReport.StoreId, gpr.PriceSafariReport.Store.StoreName })
                .Select(g => new
                {
                    StoreId = g.Key.StoreId,
                    StoreName = g.Key.StoreName,
                    GlobalPriceReportsCount = g.Count()
                })
                .ToListAsync();

            foreach (var gpr in globalPriceReportsPerStore)
            {
                decimal usedSpaceKB = 0;
                if (totalGlobalPriceReportsCount > 0)
                {
                    usedSpaceKB = (decimal)totalGlobalPriceReportsSizeKB * gpr.GlobalPriceReportsCount / totalGlobalPriceReportsCount;
                }

                if (storeSummariesDict.TryGetValue(gpr.StoreId, out var existingStoreSummary))
                {
                    existingStoreSummary.TotalGlobalPriceReportsCount = gpr.GlobalPriceReportsCount;
                    existingStoreSummary.TotalGlobalPriceReportsUsedSpaceMB = Math.Round(usedSpaceKB / 1024.0m, 4);
                }
                else
                {
                    storeSummariesDict[gpr.StoreId] = new StoreSummaryViewModel
                    {
                        StoreId = gpr.StoreId,
                        StoreName = gpr.StoreName,
                        TotalPriceHistoriesCount = 0,
                        TotalUsedSpaceMB = 0,
                        TotalGlobalPriceReportsCount = gpr.GlobalPriceReportsCount,
                        TotalGlobalPriceReportsUsedSpaceMB = Math.Round(usedSpaceKB / 1024.0m, 4)
                    };
                }
            }

            var storeSummaries = storeSummariesDict.Values.ToList();

            // Obliczenie łącznych sum
            var totalUsedSpaceMB = storeSummaries.Sum(s => s.TotalUsedSpaceMB);
            totalPriceHistoriesCount = storeSummaries.Sum(s => s.TotalPriceHistoriesCount);

            var totalGlobalPriceReportsUsedSpaceMB = storeSummaries.Sum(s => s.TotalGlobalPriceReportsUsedSpaceMB);
            totalGlobalPriceReportsCount = storeSummaries.Sum(s => s.TotalGlobalPriceReportsCount);

            var viewModel = new DatabaseSizeSummaryViewModel
            {
                StoreSummaries = storeSummaries,
                TotalUsedSpaceMB = Math.Round(totalUsedSpaceMB, 4),
                TotalPriceHistories = totalPriceHistoriesCount,
                TotalGlobalPriceReportsUsedSpaceMB = Math.Round(totalGlobalPriceReportsUsedSpaceMB, 4),
                TotalGlobalPriceReportsCount = totalGlobalPriceReportsCount
            };

            return View("~/Views/ManagerPanel/DatabaseSize/Index.cshtml", viewModel);
        }


        public async Task<IActionResult> StoreDetails(int storeId)
        {
            // Calculate total size of PriceHistories table
            var totalSizeKB = await CalculateTotalSpaceForPriceHistories();

            // Calculate total number of PriceHistories in the database
            var totalPriceHistoriesCount = await _context.PriceHistories.CountAsync();

            // Existing code for priceHistories and tableSizes
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
                decimal totalSpaceKB = 0;
                if (totalPriceHistoriesCount > 0)
                {
                    totalSpaceKB = (decimal)totalSizeKB * ph.PriceHistoriesCount / totalPriceHistoriesCount;
                }

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

            // Fetch PriceSafariReports for the store where Prepared == true
            var preparedPriceSafariReports = await _context.PriceSafariReports
                .Where(psr => psr.StoreId == storeId && psr.Prepared == true)
                .ToListAsync();

            // Compute sizes of GlobalPriceReports
            var priceSafariReportSizes = new List<PriceSafariReportSizeInfo>();

            // Calculate total size of GlobalPriceReports table
            var totalGlobalPriceReportsSizeKB = await CalculateTotalSpaceForGlobalPriceReports();

            // Calculate total number of records in GlobalPriceReports
            var totalGlobalPriceReportsCount = await _context.GlobalPriceReports.CountAsync();

            foreach (var psr in preparedPriceSafariReports)
            {
                // Get number of GlobalPriceReports for the given PriceSafariReport
                var globalPriceReportsCount = await _context.GlobalPriceReports
                    .Where(gpr => gpr.PriceSafariReportId == psr.ReportId)
                    .CountAsync();

                // Compute used space
                decimal usedSpaceKB = 0;
                if (totalGlobalPriceReportsCount > 0)
                {
                    usedSpaceKB = (decimal)totalGlobalPriceReportsSizeKB * globalPriceReportsCount / totalGlobalPriceReportsCount;
                }

                priceSafariReportSizes.Add(new PriceSafariReportSizeInfo
                {
                    ReportId = psr.ReportId,
                    ReportName = psr.ReportName,
                    TotalGlobalPriceReportsCount = globalPriceReportsCount,
                    UsedSpaceMB = Math.Round(usedSpaceKB / 1024.0m, 4)
                });
            }

            var store = await _context.Stores.FindAsync(storeId);
            var viewModel = new StoreDetailsViewModel
            {
                StoreName = store.StoreName,
                TableSizes = tableSizes,
                PriceSafariReportSizes = priceSafariReportSizes // Added new data to ViewModel
            };

            return View("~/Views/ManagerPanel/DatabaseSize/StoreDetails.cshtml", viewModel);
        }




        [HttpPost]
        public async Task<IActionResult> DeletePriceSafariReport(int id)
        {
            var priceSafariReport = await _context.PriceSafariReports.FindAsync(id);
            if (priceSafariReport != null)
            {
                int storeId = priceSafariReport.StoreId;

                // Usunięcie powiązanych GlobalPriceReports
                var globalPriceReports = _context.GlobalPriceReports.Where(gpr => gpr.PriceSafariReportId == id);
                _context.GlobalPriceReports.RemoveRange(globalPriceReports);

                // Usunięcie PriceSafariReport
                _context.PriceSafariReports.Remove(priceSafariReport);

                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(StoreDetails), new { storeId });
            }
            return RedirectToAction(nameof(Index));
        }


        private async Task<long> CalculateTotalSpaceForGlobalPriceReports()
        {
                    var query = @"
                EXEC sp_spaceused 'GlobalPriceReports'
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



        [HttpPost]
        public async Task<IActionResult> DeleteScrapHistory(int id)
        {
            var scrapHistory = await _context.ScrapHistories.FindAsync(id);
            if (scrapHistory != null)
            {
                int storeId = scrapHistory.StoreId; 
                _context.ScrapHistories.Remove(scrapHistory);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(StoreDetails), new { storeId }); 
            }
            return RedirectToAction(nameof(Index));
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


    public class PriceSafariReportSizeInfo
    {
        public int ReportId { get; set; }
        public string ReportName { get; set; }
        public int TotalGlobalPriceReportsCount { get; set; }
        public decimal UsedSpaceMB { get; set; }
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
        public decimal TotalGlobalPriceReportsUsedSpaceMB { get; set; }
        public int TotalGlobalPriceReportsCount { get; set; }
    }


    public class StoreSummaryViewModel
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public int TotalPriceHistoriesCount { get; set; } = 0;
        public decimal TotalUsedSpaceMB { get; set; } = 0;
        public int TotalGlobalPriceReportsCount { get; set; } = 0;
        public decimal TotalGlobalPriceReportsUsedSpaceMB { get; set; } = 0;
    }


    public class StoreDetailsViewModel
    {
        public string StoreName { get; set; }
        public List<TableSizeInfo> TableSizes { get; set; }
        public List<PriceSafariReportSizeInfo> PriceSafariReportSizes { get; set; } // Nowa właściwość
    }

}
