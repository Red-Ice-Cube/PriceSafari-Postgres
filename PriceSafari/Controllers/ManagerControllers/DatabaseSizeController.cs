using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class DatabaseSizeController : Controller
    {
        private readonly PriceSafariContext _context;

        public DatabaseSizeController(PriceSafariContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var storeSummariesDict = new Dictionary<int, StoreSummaryViewModel>();

            var (_, totalPhDataKB) = await GetTableSpaceUsageKB("PriceHistories");
            var phPerStore = await _context.PriceHistories
                .GroupBy(ph => new { ph.ScrapHistory.StoreId, ph.ScrapHistory.Store.StoreName })
                .Select(g => new { g.Key.StoreId, g.Key.StoreName, Count = g.Count() })
                .ToListAsync();

            var totalPhCount = (decimal)phPerStore.Sum(x => x.Count);
            foreach (var item in phPerStore)
            {
                var spaceKB = totalPhCount > 0 ? totalPhDataKB * item.Count / totalPhCount : 0;
                storeSummariesDict[item.StoreId] = new StoreSummaryViewModel
                {
                    StoreId = item.StoreId,
                    StoreName = item.StoreName,
                    TotalPriceHistoriesCount = item.Count,
                    TotalUsedSpaceMB = Math.Round(spaceKB / 1024.0m, 4)
                };
            }

            var (_, totalGprDataKB) = await GetTableSpaceUsageKB("GlobalPriceReports");
            var gprPerStore = await _context.GlobalPriceReports
                .GroupBy(gpr => new { gpr.PriceSafariReport.StoreId, gpr.PriceSafariReport.Store.StoreName })
                .Select(g => new { g.Key.StoreId, g.Key.StoreName, Count = g.Count() })
                .ToListAsync();

            var totalGprCount = (decimal)gprPerStore.Sum(x => x.Count);
            foreach (var item in gprPerStore)
            {
                var spaceKB = totalGprCount > 0 ? totalGprDataKB * item.Count / totalGprCount : 0;
                if (storeSummariesDict.TryGetValue(item.StoreId, out var summary))
                {
                    summary.TotalGlobalPriceReportsCount = item.Count;
                    summary.TotalGlobalPriceReportsUsedSpaceMB = Math.Round(spaceKB / 1024.0m, 4);
                }
                else
                {
                    storeSummariesDict[item.StoreId] = new StoreSummaryViewModel
                    {
                        StoreId = item.StoreId,
                        StoreName = item.StoreName,
                        TotalGlobalPriceReportsCount = item.Count,
                        TotalGlobalPriceReportsUsedSpaceMB = Math.Round(spaceKB / 1024.0m, 4)
                    };
                }
            }

            var (_, totalAphDataKB) = await GetTableSpaceUsageKB("AllegroPriceHistories");
            var aphPerStore = await _context.AllegroPriceHistories
                .GroupBy(aph => new { aph.AllegroScrapeHistory.StoreId, aph.AllegroScrapeHistory.Store.StoreName })
                .Select(g => new { g.Key.StoreId, g.Key.StoreName, Count = g.Count() })
                .ToListAsync();

            var totalAphCount = (decimal)aphPerStore.Sum(x => x.Count);
            foreach (var item in aphPerStore)
            {
                var spaceKB = totalAphCount > 0 ? totalAphDataKB * item.Count / totalAphCount : 0;
                if (storeSummariesDict.TryGetValue(item.StoreId, out var summary))
                {
                    summary.TotalAllegroPriceHistoriesCount = item.Count;
                    summary.TotalAllegroPriceHistoriesUsedSpaceMB = Math.Round(spaceKB / 1024.0m, 4);
                }
                else
                {
                    storeSummariesDict[item.StoreId] = new StoreSummaryViewModel
                    {
                        StoreId = item.StoreId,
                        StoreName = item.StoreName,
                        TotalAllegroPriceHistoriesCount = item.Count,
                        TotalAllegroPriceHistoriesUsedSpaceMB = Math.Round(spaceKB / 1024.0m, 4)
                    };
                }
            }

            var totalDbSize = await GetTotalDatabaseSizeMB();
            var topTables = await GetTopTableSizesAsync(10);
            var spaceDetails = await GetDatabaseSpaceDetailsAsync();

            var storeSummaries = storeSummariesDict.Values.OrderByDescending(s => s.TotalUsedSpaceMB + s.TotalAllegroPriceHistoriesUsedSpaceMB + s.TotalGlobalPriceReportsUsedSpaceMB).ToList();
            var viewModel = new DatabaseSizeSummaryViewModel
            {
                StoreSummaries = storeSummaries,
                TotalDatabaseSizeMB = totalDbSize,
                TopTables = topTables,
                TotalPriceHistories = storeSummaries.Sum(s => s.TotalPriceHistoriesCount),
                TotalUsedSpaceMB = storeSummaries.Sum(s => s.TotalUsedSpaceMB),
                TotalGlobalPriceReportsCount = storeSummaries.Sum(s => s.TotalGlobalPriceReportsCount),
                TotalGlobalPriceReportsUsedSpaceMB = storeSummaries.Sum(s => s.TotalGlobalPriceReportsUsedSpaceMB),
                TotalAllegroPriceHistories = storeSummaries.Sum(s => s.TotalAllegroPriceHistoriesCount),
                TotalAllegroPriceHistoriesUsedSpaceMB = storeSummaries.Sum(s => s.TotalAllegroPriceHistoriesUsedSpaceMB),
                SpaceDetails = spaceDetails
            };

            return View("~/Views/ManagerPanel/DatabaseSize/Index.cshtml", viewModel);
        }

        public async Task<IActionResult> StoreDetails(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            var (_, totalPhDataKB) = await GetTableSpaceUsageKB("PriceHistories");
            var totalPhCount = (decimal)await _context.PriceHistories.CountAsync();
            var phGroups = await _context.PriceHistories
                .Where(ph => ph.ScrapHistory.StoreId == storeId)
                .GroupBy(ph => new { ph.ScrapHistoryId, ph.ScrapHistory.Date })
                .Select(g => new { g.Key.ScrapHistoryId, g.Key.Date, Count = g.Count() })
                .OrderByDescending(g => g.Date)
                .ToListAsync();

            var tableSizes = phGroups.Select(g => new TableSizeInfo
            {
                ScrapHistoryId = g.ScrapHistoryId,
                Date = g.Date,
                PriceHistoriesCount = g.Count,
                UsedSpaceMB = totalPhCount > 0 ? Math.Round((totalPhDataKB * g.Count / totalPhCount) / 1024.0m, 4) : 0
            }).ToList();

            var (_, totalGprDataKB) = await GetTableSpaceUsageKB("GlobalPriceReports");
            var totalGprCount = (decimal)await _context.GlobalPriceReports.CountAsync();
            var preparedReports = await _context.PriceSafariReports
                .Where(psr => psr.StoreId == storeId && psr.Prepared == true)
                .OrderByDescending(psr => psr.CreatedDate)
                .Select(psr => new { psr.ReportId, psr.ReportName, psr.CreatedDate })
                .ToListAsync();

            var reportIds = preparedReports.Select(r => r.ReportId).ToList();
            var reportCounts = await _context.GlobalPriceReports
                .Where(gpr => reportIds.Contains(gpr.PriceSafariReportId))
                .GroupBy(gpr => gpr.PriceSafariReportId)
                .Select(g => new { ReportId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ReportId, x => x.Count);

            var reportSizes = preparedReports.Select(psr =>
            {
                reportCounts.TryGetValue(psr.ReportId, out var count);
                return new PriceSafariReportSizeInfo
                {
                    ReportId = psr.ReportId,
                    ReportName = psr.ReportName,
                    ReportDate = psr.CreatedDate,
                    TotalGlobalPriceReportsCount = count,
                    UsedSpaceMB = totalGprCount > 0 ? Math.Round((totalGprDataKB * count / totalGprCount) / 1024.0m, 4) : 0
                };
            }).ToList();

            var (_, totalAphDataKB) = await GetTableSpaceUsageKB("AllegroPriceHistories");
            var totalAphCount = (decimal)await _context.AllegroPriceHistories.CountAsync();
            var aphGroups = await _context.AllegroPriceHistories
                .Where(aph => aph.AllegroScrapeHistory.StoreId == storeId)
                .GroupBy(aph => new { aph.AllegroScrapeHistoryId, aph.AllegroScrapeHistory.Date })
                .Select(g => new { g.Key.AllegroScrapeHistoryId, g.Key.Date, Count = g.Count() })
                .OrderByDescending(g => g.Date)
                .ToListAsync();

            var allegroSizes = aphGroups.Select(g => new AllegroScrapeHistorySizeInfo
            {
                AllegroScrapeHistoryId = g.AllegroScrapeHistoryId,
                Date = g.Date,
                PriceHistoriesCount = g.Count,
                UsedSpaceMB = totalAphCount > 0 ? Math.Round((totalAphDataKB * g.Count / totalAphCount) / 1024.0m, 4) : 0
            }).ToList();

            var totalStoreHistorySizeMB = tableSizes.Sum(s => s.UsedSpaceMB);
            var totalAllegroHistorySizeMB = allegroSizes.Sum(s => s.UsedSpaceMB);
            var totalReportsSizeMB = reportSizes.Sum(s => s.UsedSpaceMB);

            var viewModel = new StoreDetailsViewModel
            {
                StoreId = storeId,
                StoreName = store.StoreName,
                TableSizes = tableSizes,
                PriceSafariReportSizes = reportSizes,
                AllegroScrapeHistorySizes = allegroSizes,
                TotalStoreHistorySizeMB = totalStoreHistorySizeMB,
                TotalAllegroHistorySizeMB = totalAllegroHistorySizeMB,
                TotalReportsSizeMB = totalReportsSizeMB
            };

            return View("~/Views/ManagerPanel/DatabaseSize/StoreDetails.cshtml", viewModel);
        }

        #region Akcje Usuwania

        [HttpPost, ActionName("DeleteSelectedScrapHistories")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSelectedScrapHistories(int[] selectedIds, int storeId)
        {
            if (selectedIds != null && selectedIds.Any())
            {
                // Wydłużamy czas na wypadek dużej bazy
                _context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

                foreach (var id in selectedIds)
                {
                    // 1. Najcięższa operacja: Usuń setki tysięcy cen BEZPOŚREDNIO w bazie (omija RAM)
                    // Upewnij się, że tabela w bazie nazywa się 'PriceHistories'
                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM [PriceHistories] WHERE [ScrapHistoryId] = {0}", id);

                    // 2. Usuń nagłówek historii (teraz jest już lekki, bo nie ma dzieci)
                    // Używamy prostego zapytania SQL również tutaj dla spójności i szybkości
                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM [ScrapHistories] WHERE [Id] = {0}", id);
                }
            }
            return RedirectToAction(nameof(StoreDetails), new { storeId });
        }

        [HttpPost, ActionName("DeleteSelectedPriceSafariReports")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSelectedPriceSafariReports(int[] selectedIds, int storeId)
        {
            if (selectedIds != null && selectedIds.Any())
            {
                _context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

                foreach (var reportId in selectedIds)
                {
                    // 1. Usuń szczegóły raportu bezpośrednim SQL
                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM [GlobalPriceReports] WHERE [PriceSafariReportId] = {0}", reportId);

                    // 2. Usuń nagłówek raportu
                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM [PriceSafariReports] WHERE [ReportId] = {0}", reportId);
                }
            }
            return RedirectToAction(nameof(StoreDetails), new { storeId });
        }
        [HttpPost, ActionName("DeleteSelectedAllegroScrapHistories")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSelectedAllegroScrapHistories(int[] selectedIds, int storeId)
        {
            if (selectedIds != null && selectedIds.Any())
            {
                // Ustawiamy długi timeout, bo operacji jest teraz więcej
                _context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
                int deletedCount = 0;

                foreach (var id in selectedIds)
                {
                    // KROK 1: Usuń ceny (to co robiliśmy wcześniej)
                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM [AllegroPriceHistories] WHERE [AllegroScrapeHistoryId] = {0}", id);

                    // KROK 2: Usuń powiązane "Bridge Batches" (TO NAPRAWIA TWOJEGO BŁĘDA)
                    // Usuwamy rekordy z tabeli, która blokowała usunięcie constraintem FK
                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM [AllegroPriceBridgeBatches] WHERE [AllegroScrapeHistoryId] = {0}", id);

                    // KROK 3: Usuń samą historię (rodzica)
                    // Teraz, gdy usunęliśmy dzieci z obu tabel, rodzica można bezpiecznie usunąć.
                    var entity = await _context.AllegroScrapeHistories.FindAsync(id);
                    if (entity != null)
                    {
                        _context.AllegroScrapeHistories.Remove(entity);
                        await _context.SaveChangesAsync();

                        // Czyścimy śledzenie zmian, żeby nie zatykać RAMu
                        _context.ChangeTracker.Clear();
                        deletedCount++;
                    }
                }

                if (deletedCount > 0)
                {
                    TempData["SuccessMessage"] = $"Pomyślnie usunięto {deletedCount} wpisów historii.";
                }
            }

            return RedirectToAction(nameof(StoreDetails), new { storeId });
        }

        #endregion

        #region Metody Pomocnicze
        private async Task<(long reservedKB, long dataKB)> GetTableSpaceUsageKB(string tableName)
        {
            long reservedKB = 0;
            long dataKB = 0;
            var query = $"EXEC sp_spaceused '{tableName}'";

            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = query;
                await _context.Database.OpenConnectionAsync();
                using (var result = await command.ExecuteReaderAsync())
                {
                    if (await result.ReadAsync())
                    {
                        var reservedSpace = result["reserved"].ToString();
                        var dataSpace = result["data"].ToString();
                        long.TryParse(reservedSpace?.Replace(" KB", "").Replace(",", ""), out reservedKB);
                        long.TryParse(dataSpace?.Replace(" KB", "").Replace(",", ""), out dataKB);
                    }
                }
                await _context.Database.CloseConnectionAsync();
            }
            return (reservedKB, dataKB);
        }

        private async Task<decimal> GetTotalDatabaseSizeMB()
        {
            decimal totalSizeMB = 0;
            var query = "EXEC sp_spaceused";

            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = query;
                await _context.Database.OpenConnectionAsync();
                using (var result = await command.ExecuteReaderAsync())
                {
                    if (await result.ReadAsync())
                    {
                        var dbSizeString = result["database_size"].ToString();
                        decimal.TryParse(dbSizeString?.Replace(" MB", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out totalSizeMB);
                    }
                }
                await _context.Database.CloseConnectionAsync();
            }
            return totalSizeMB;
        }

        private async Task<List<TableUsageInfo>> GetTopTableSizesAsync(int topN)
        {
            var result = new List<TableUsageInfo>();
            var query = @"
                SELECT TOP (@TopN)
                    t.NAME AS TableName,
                    SUM(p.rows) AS RowCounts,
                    CAST(ROUND(((SUM(a.total_pages) * 8) / 1024.00), 2) AS NUMERIC(36, 2)) AS TotalSpaceMB
                FROM sys.tables t
                INNER JOIN sys.indexes i ON t.OBJECT_ID = i.object_id
                INNER JOIN sys.partitions p ON i.object_id = p.OBJECT_ID AND i.index_id = p.index_id
                INNER JOIN sys.allocation_units a ON p.partition_id = a.container_id
                WHERE t.NAME NOT LIKE 'dt%' AND i.OBJECT_ID > 255 AND i.index_id <= 1
                GROUP BY t.NAME
                ORDER BY TotalSpaceMB DESC";

            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = query;
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@TopN";
                parameter.Value = topN;
                command.Parameters.Add(parameter);

                await _context.Database.OpenConnectionAsync();
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        result.Add(new TableUsageInfo
                        {
                            TableName = reader["TableName"].ToString(),
                            SizeMB = Convert.ToDecimal(reader["TotalSpaceMB"])
                        });
                    }
                }
                await _context.Database.CloseConnectionAsync();
            }
            return result;
        }

        [HttpGet]
        public async Task<IActionResult> EditScrapHistoryModal(int id, int storeId)
        {
            var scrapHistory = await _context.ScrapHistories
                .Where(sh => sh.Id == id && sh.StoreId == storeId)
                .Select(sh => new ScrapHistoryEditViewModel
                {
                    Id = sh.Id,
                    Date = sh.Date,
                    StoreId = sh.StoreId
                })
                .FirstOrDefaultAsync();

            if (scrapHistory == null)
            {
                return NotFound();
            }

            return PartialView("_EditScrapHistoryModal", scrapHistory);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditScrapHistory(ScrapHistoryEditViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var scrapHistory = await _context.ScrapHistories.FindAsync(viewModel.Id);
                if (scrapHistory == null)
                {
                    return Json(new { success = false, message = "Record not found." });
                }

                scrapHistory.Date = viewModel.Date;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Zmiany zostały zapisane.", newDate = viewModel.Date.ToString("yyyy-MM-dd HH:mm:ss") });
            }

            return Json(new { success = false, message = "Wystąpił błąd walidacji." });
        }

        private async Task<DatabaseSpaceDetailsViewModel> GetDatabaseSpaceDetailsAsync()
        {
            var details = new DatabaseSpaceDetailsViewModel();

            var connection = _context.Database.GetDbConnection();

            try
            {

                await connection.OpenAsync();

                using (var logCommand = connection.CreateCommand())
                {
                    logCommand.CommandText = "SELECT CAST(SUM(size) * 8.0 / 1024 AS DECIMAL(18, 2)) FROM sys.database_files WHERE type_desc = 'LOG'";
                    var logSizeResult = await logCommand.ExecuteScalarAsync();
                    if (logSizeResult != null && logSizeResult != DBNull.Value)
                    {
                        details.LogFileMB = Convert.ToDecimal(logSizeResult);
                    }
                }

                using (var spaceUsedCommand = connection.CreateCommand())
                {
                    spaceUsedCommand.CommandText = "EXEC sp_spaceused";
                    using (var reader = await spaceUsedCommand.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {

                            var unallocatedString = reader["unallocated space"].ToString();
                            decimal unallocatedMB = 0;
                            decimal.TryParse(unallocatedString?.Replace(" MB", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out unallocatedMB);
                            details.UnallocatedSpaceMB = unallocatedMB;
                        }

                        if (await reader.NextResultAsync() && await reader.ReadAsync())
                        {

                            long.TryParse(reader["data"].ToString()?.Replace(" KB", "").Replace(",", ""), out long dataKB);
                            long.TryParse(reader["index_size"].ToString()?.Replace(" KB", "").Replace(",", ""), out long indexKB);
                            long.TryParse(reader["unused"].ToString()?.Replace(" KB", "").Replace(",", ""), out long unusedKB);

                            details.DataMB = Math.Round(dataKB / 1024.0m, 2);
                            details.IndexSizeMB = Math.Round(indexKB / 1024.0m, 2);
                            details.UnusedMB = Math.Round(unusedKB / 1024.0m, 2);
                        }
                    }
                }
            }
            finally
            {

                if (connection.State == System.Data.ConnectionState.Open)
                {
                    await connection.CloseAsync();
                }
            }

            return details;
        }

        #endregion
    }

    #region ViewModels
    public class TableUsageInfo
    {
        public string TableName { get; set; }
        public decimal SizeMB { get; set; }
    }

    public class DatabaseSpaceDetailsViewModel
    {
        public decimal DataMB { get; set; }
        public decimal IndexSizeMB { get; set; }
        public decimal UnusedMB { get; set; }
        public decimal UnallocatedSpaceMB { get; set; }
        public decimal LogFileMB { get; set; }
    }

    public class DatabaseSizeSummaryViewModel
    {
        public List<StoreSummaryViewModel> StoreSummaries { get; set; }
        public decimal TotalDatabaseSizeMB { get; set; }
        public decimal TotalUsedSpaceMB { get; set; }
        public int TotalPriceHistories { get; set; }
        public decimal TotalGlobalPriceReportsUsedSpaceMB { get; set; }
        public int TotalGlobalPriceReportsCount { get; set; }
        public int TotalAllegroPriceHistories { get; set; }
        public decimal TotalAllegroPriceHistoriesUsedSpaceMB { get; set; }
        public List<TableUsageInfo> TopTables { get; set; }
        public DatabaseSpaceDetailsViewModel SpaceDetails { get; set; }
    }

    public class StoreSummaryViewModel
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public int TotalPriceHistoriesCount { get; set; }
        public decimal TotalUsedSpaceMB { get; set; }
        public int TotalGlobalPriceReportsCount { get; set; }
        public decimal TotalGlobalPriceReportsUsedSpaceMB { get; set; }
        public int TotalAllegroPriceHistoriesCount { get; set; }
        public decimal TotalAllegroPriceHistoriesUsedSpaceMB { get; set; }
    }

    public class StoreDetailsViewModel
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public List<TableSizeInfo> TableSizes { get; set; }
        public List<PriceSafariReportSizeInfo> PriceSafariReportSizes { get; set; }
        public List<AllegroScrapeHistorySizeInfo> AllegroScrapeHistorySizes { get; set; }
        public decimal TotalStoreHistorySizeMB { get; set; }
        public decimal TotalAllegroHistorySizeMB { get; set; }
        public decimal TotalReportsSizeMB { get; set; }
    }

    public class TableSizeInfo
    {
        public int ScrapHistoryId { get; set; }
        public DateTime Date { get; set; }
        public int PriceHistoriesCount { get; set; }
        public decimal UsedSpaceMB { get; set; }
    }

    public class PriceSafariReportSizeInfo
    {
        public int ReportId { get; set; }
        public string ReportName { get; set; }
        public DateTime ReportDate { get; set; }
        public int TotalGlobalPriceReportsCount { get; set; }
        public decimal UsedSpaceMB { get; set; }
    }

    public class AllegroScrapeHistorySizeInfo
    {
        public int AllegroScrapeHistoryId { get; set; }
        public DateTime Date { get; set; }
        public int PriceHistoriesCount { get; set; }
        public decimal UsedSpaceMB { get; set; }
    }

    public class ScrapHistoryEditViewModel
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public int StoreId { get; set; }
    }

    #endregion

}