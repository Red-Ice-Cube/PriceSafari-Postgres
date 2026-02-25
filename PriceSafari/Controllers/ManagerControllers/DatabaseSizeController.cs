using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using System;
using System.Collections.Generic;
using System.Data;
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

        // Metody Index i StoreDetails zostają bez zmian - korzystają z poprawionych metod w regionach poniżej
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

        #region Akcje Usuwania (Poprawione pod PostgreSQL)

        [HttpPost, ActionName("DeleteSelectedScrapHistories")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSelectedScrapHistories(int[] selectedIds, int storeId)
        {
            if (selectedIds != null && selectedIds.Any())
            {
                _context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
                foreach (var id in selectedIds)
                {
                    // W Postgres używamy " " zamiast [ ]
                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM \"PriceHistories\" WHERE \"ScrapHistoryId\" = {0}", id);

                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM \"ScrapHistories\" WHERE \"Id\" = {0}", id);
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
                    // Postgres nie obsługuje DELETE TOP. Usuwamy wszystko - Postgres i tak jest szybki.
                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM \"GlobalPriceReports\" WHERE \"PriceSafariReportId\" = {0}", reportId);

                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM \"PriceSafariReports\" WHERE \"ReportId\" = {0}", reportId);
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
                _context.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
                foreach (var id in selectedIds)
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM \"AllegroPriceHistories\" WHERE \"AllegroScrapeHistoryId\" = {0}", id);

                    await _context.Database.ExecuteSqlRawAsync(
                        "DELETE FROM \"AllegroPriceBridgeBatches\" WHERE \"AllegroScrapeHistoryId\" = {0}", id);

                    var entity = await _context.AllegroScrapeHistories.FindAsync(id);
                    if (entity != null)
                    {
                        _context.AllegroScrapeHistories.Remove(entity);
                        await _context.SaveChangesAsync();
                        _context.ChangeTracker.Clear();
                    }
                }
            }
            return RedirectToAction(nameof(StoreDetails), new { storeId });
        }

        #endregion

        #region Metody Pomocnicze (Poprawione pod PostgreSQL)

        private async Task<(long reservedKB, long dataKB)> GetTableSpaceUsageKB(string tableName)
        {
            // PostgreSQL: pg_total_relation_size pobiera wszystko, pg_relation_size same dane
            var sql = $@"
                SELECT 
                    pg_total_relation_size('""{tableName}""') / 1024 as reserved_kb,
                    pg_relation_size('""{tableName}""') / 1024 as data_kb";

            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = sql;
                if (command.Connection.State != ConnectionState.Open) await command.Connection.OpenAsync();
                using (var result = await command.ExecuteReaderAsync())
                {
                    if (await result.ReadAsync())
                    {
                        return (result.GetInt64(0), result.GetInt64(1));
                    }
                }
            }
            return (0, 0);
        }

        private async Task<decimal> GetTotalDatabaseSizeMB()
        {
            var sql = "SELECT pg_database_size(current_database()) / 1024.0 / 1024.0";
            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = sql;
                if (command.Connection.State != ConnectionState.Open) await command.Connection.OpenAsync();
                var result = await command.ExecuteScalarAsync();
                return result != null ? Convert.ToDecimal(result) : 0;
            }
        }

        private async Task<List<TableUsageInfo>> GetTopTableSizesAsync(int topN)
        {
            var result = new List<TableUsageInfo>();
            // Postgres statystyki z pg_stat_user_tables
            var query = $@"
                SELECT 
                    relname AS TableName,
                    n_live_tup AS RowCounts,
                    pg_total_relation_size(relid) / 1024.0 / 1024.0 AS TotalSpaceMB
                FROM pg_stat_user_tables
                ORDER BY pg_total_relation_size(relid) DESC
                LIMIT {topN}";

            using (var command = _context.Database.GetDbConnection().CreateCommand())
            {
                command.CommandText = query;
                if (command.Connection.State != ConnectionState.Open) await command.Connection.OpenAsync();
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
            }
            return result;
        }

        private async Task<DatabaseSpaceDetailsViewModel> GetDatabaseSpaceDetailsAsync()
        {
            var details = new DatabaseSpaceDetailsViewModel();
            var connection = _context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open) await connection.OpenAsync();

            using (var command = connection.CreateCommand())
            {
                // Agregacja rozmiaru danych i indeksów dla całej bazy w Postgres
                command.CommandText = @"
                    SELECT 
                        SUM(pg_relation_size(quote_ident(schemaname) || '.' || quote_ident(relname))) / 1024 / 1024.0 as data_mb,
                        SUM(pg_total_relation_size(quote_ident(schemaname) || '.' || quote_ident(relname)) - pg_relation_size(quote_ident(schemaname) || '.' || quote_ident(relname))) / 1024 / 1024.0 as index_mb
                    FROM pg_stat_user_tables";

                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        details.DataMB = reader.IsDBNull(0) ? 0 : Convert.ToDecimal(reader.GetValue(0));
                        details.IndexSizeMB = reader.IsDBNull(1) ? 0 : Convert.ToDecimal(reader.GetValue(1));
                    }
                }
            }
            return details;
        }
        #endregion
    }

    // ViewModels pozostają bez zmian pod kodem kontrolera...
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