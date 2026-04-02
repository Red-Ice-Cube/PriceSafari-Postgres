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

            var (_, totalGprDataKB) = await GetTableSpaceUsageKB("GlobalPriceReports");
            var totalGprCount = (decimal)await _context.GlobalPriceReports.CountAsync();

            var (_, totalAphDataKB) = await GetTableSpaceUsageKB("AllegroPriceHistories");
            var totalAphCount = (decimal)await _context.AllegroPriceHistories.CountAsync();

            var scrapHistories = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new
                {
                    sh.Id,
                    sh.Date,

                    Count = _context.PriceHistories.Count(ph => ph.ScrapHistoryId == sh.Id)
                })
                .ToListAsync();

            var tableSizes = scrapHistories.Select(sh => new TableSizeInfo
            {
                ScrapHistoryId = sh.Id,
                Date = sh.Date,
                PriceHistoriesCount = sh.Count,
                UsedSpaceMB = (totalPhCount > 0 && sh.Count > 0)
                    ? Math.Round((totalPhDataKB * sh.Count / totalPhCount) / 1024.0m, 4)
                    : 0
            }).ToList();

            var preparedReports = await _context.PriceSafariReports
                .Where(psr => psr.StoreId == storeId && psr.Prepared == true)
                .OrderByDescending(psr => psr.CreatedDate)
                .Select(psr => new
                {
                    psr.ReportId,
                    psr.ReportName,
                    psr.CreatedDate,
                    Count = _context.GlobalPriceReports.Count(gpr => gpr.PriceSafariReportId == psr.ReportId)
                })
                .ToListAsync();

            var reportSizes = preparedReports.Select(psr => new PriceSafariReportSizeInfo
            {
                ReportId = psr.ReportId,
                ReportName = psr.ReportName,
                ReportDate = psr.CreatedDate,
                TotalGlobalPriceReportsCount = psr.Count,
                UsedSpaceMB = (totalGprCount > 0 && psr.Count > 0)
                    ? Math.Round((totalGprDataKB * psr.Count / totalGprCount) / 1024.0m, 4)
                    : 0
            }).ToList();

            var allegroHistories = await _context.AllegroScrapeHistories
                .Where(ash => ash.StoreId == storeId)
                .OrderByDescending(ash => ash.Date)
                .Select(ash => new
                {
                    ash.Id,
                    ash.Date,
                    Count = _context.AllegroPriceHistories.Count(aph => aph.AllegroScrapeHistoryId == ash.Id)
                })
                .ToListAsync();

            var allegroSizes = allegroHistories.Select(ash => new AllegroScrapeHistorySizeInfo
            {
                AllegroScrapeHistoryId = ash.Id,
                Date = ash.Date,
                PriceHistoriesCount = ash.Count,
                UsedSpaceMB = (totalAphCount > 0 && ash.Count > 0)
                    ? Math.Round((totalAphDataKB * ash.Count / totalAphCount) / 1024.0m, 4)
                    : 0
            }).ToList();

            var viewModel = new StoreDetailsViewModel
            {
                StoreId = storeId,
                StoreName = store.StoreName,
                TableSizes = tableSizes,
                PriceSafariReportSizes = reportSizes,
                AllegroScrapeHistorySizes = allegroSizes,
                TotalStoreHistorySizeMB = tableSizes.Sum(s => s.UsedSpaceMB),
                TotalAllegroHistorySizeMB = allegroSizes.Sum(s => s.UsedSpaceMB),
                TotalReportsSizeMB = reportSizes.Sum(s => s.UsedSpaceMB)
            };

            return View("~/Views/ManagerPanel/DatabaseSize/StoreDetails.cshtml", viewModel);
        }

        // ═══════════════════════════════════════════════════════════════
        // DODAJ TE METODY DO DatabaseSizeController
        // ═══════════════════════════════════════════════════════════════

        // 1. Nowy endpoint — kondycja tabel (dead tuples, bloat, autovacuum)
        [HttpGet]
        public async Task<IActionResult> GetTableHealth()
        {
            var result = new List<TableHealthInfo>();
            var connection = _context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open) await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
            SELECT 
                s.relname,
                s.n_live_tup,
                s.n_dead_tup,
                CASE WHEN s.n_live_tup > 0 
                     THEN ROUND(s.n_dead_tup::numeric / s.n_live_tup * 100, 1) 
                     ELSE 0 END AS dead_pct,
                pg_size_pretty(pg_relation_size(s.relid)) AS table_size,
                pg_size_pretty(pg_total_relation_size(s.relid)) AS total_size,
                pg_size_pretty(pg_indexes_size(s.relid)) AS index_size,
                CASE WHEN s.n_live_tup > 0 
                     THEN pg_relation_size(s.relid) / s.n_live_tup 
                     ELSE 0 END AS avg_row_bytes,
                s.last_vacuum,
                s.last_autovacuum,
                s.last_analyze,
                s.last_autoanalyze,
                s.vacuum_count,
                s.autovacuum_count,
                (SELECT option_value FROM pg_options_to_table(c.reloptions) WHERE option_name = 'autovacuum_vacuum_scale_factor' LIMIT 1)
                    AS custom_vacuum_scale_factor,
                (SELECT option_value FROM pg_options_to_table(c.reloptions) WHERE option_name = 'autovacuum_analyze_scale_factor' LIMIT 1)
                    AS custom_analyze_scale_factor
            FROM pg_stat_user_tables s
            LEFT JOIN pg_class c ON c.relname = s.relname 
                                 AND c.relnamespace = (SELECT oid FROM pg_namespace WHERE nspname = 'public')
                                 AND c.reloptions IS NOT NULL
            WHERE s.schemaname = 'public'
            ORDER BY s.n_dead_tup DESC";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new TableHealthInfo
                {
                    TableName = reader["relname"].ToString(),
                    LiveTuples = reader.IsDBNull(reader.GetOrdinal("n_live_tup")) ? 0 : Convert.ToInt64(reader["n_live_tup"]),
                    DeadTuples = reader.IsDBNull(reader.GetOrdinal("n_dead_tup")) ? 0 : Convert.ToInt64(reader["n_dead_tup"]),
                    DeadPercent = reader.IsDBNull(reader.GetOrdinal("dead_pct")) ? 0 : Convert.ToDecimal(reader["dead_pct"]),
                    TableSize = reader["table_size"]?.ToString() ?? "0",
                    TotalSize = reader["total_size"]?.ToString() ?? "0",
                    IndexSize = reader["index_size"]?.ToString() ?? "0",
                    AvgRowBytes = reader.IsDBNull(reader.GetOrdinal("avg_row_bytes")) ? 0 : Convert.ToInt64(reader["avg_row_bytes"]),
                    LastVacuum = reader.IsDBNull(reader.GetOrdinal("last_vacuum")) ? null : Convert.ToDateTime(reader["last_vacuum"]),
                    LastAutovacuum = reader.IsDBNull(reader.GetOrdinal("last_autovacuum")) ? null : Convert.ToDateTime(reader["last_autovacuum"]),
                    LastAnalyze = reader.IsDBNull(reader.GetOrdinal("last_analyze")) ? null : Convert.ToDateTime(reader["last_analyze"]),
                    LastAutoanalyze = reader.IsDBNull(reader.GetOrdinal("last_autoanalyze")) ? null : Convert.ToDateTime(reader["last_autoanalyze"]),
                    VacuumCount = reader.IsDBNull(reader.GetOrdinal("vacuum_count")) ? 0 : Convert.ToInt64(reader["vacuum_count"]),
                    AutovacuumCount = reader.IsDBNull(reader.GetOrdinal("autovacuum_count")) ? 0 : Convert.ToInt64(reader["autovacuum_count"]),
                    CustomVacuumScaleFactor = reader.IsDBNull(reader.GetOrdinal("custom_vacuum_scale_factor"))
                        ? null : reader["custom_vacuum_scale_factor"].ToString(),
                    CustomAnalyzeScaleFactor = reader.IsDBNull(reader.GetOrdinal("custom_analyze_scale_factor"))
                        ? null : reader["custom_analyze_scale_factor"].ToString()
                });
            }

            return Json(result);
        }

        // 2. Akcja VACUUM na wybranej tabeli
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VacuumTable(string tableName, bool full = false)
        {
            // Walidacja — tylko znane tabele (ochrona przed SQL injection)
            var allowedTables = new HashSet<string>
    {
        "PriceHistories", "AllegroPriceHistories", "Products", "CoOfrs",
        "CoOfrPriceHistories", "AllegroScrapedOffers", "AllegroPriceBridgeItems",
        "AllegroPriceBridgeBatches", "AllegroProducts", "ProductFlags",
        "AutomationProductAssignments", "PriceHistoryExtendedInfos",
        "AllegroPriceHistoryExtendedInfos", "AllegroOffersToScrape",
        "ProductMaps", "ScrapHistories", "GlobalPriceReports",
        "PriceSafariReports", "CompetitorPresets", "CompetitorPresetItems",
        "PriceBridgeItems", "PriceBridgeBatches"
    };

            if (!allowedTables.Contains(tableName))
            {
                return BadRequest($"Tabela '{tableName}' nie jest dozwolona.");
            }

            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open) await connection.OpenAsync();

                // VACUUM musi być poza transakcją — użyj oddzielnego połączenia
                using var vacuumConn = new Npgsql.NpgsqlConnection(connection.ConnectionString);
                await vacuumConn.OpenAsync();

                var sql = full
                    ? $"VACUUM FULL ANALYZE \"{tableName}\""
                    : $"VACUUM ANALYZE \"{tableName}\"";

                using var cmd = new Npgsql.NpgsqlCommand(sql, vacuumConn);
                cmd.CommandTimeout = 600; // 10 minut na VACUUM FULL
                await cmd.ExecuteNonQueryAsync();

                return Json(new { success = true, message = $"VACUUM {(full ? "FULL " : "")}ANALYZE na tabeli \"{tableName}\" zakończony pomyślnie." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Błąd: {ex.Message}" });
            }
        }

        // 3. Akcja — ustaw agresywny autovacuum dla tabeli
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetAutovacuumThreshold(string tableName, decimal scaleFactor = 0.05m)
        {
            var allowedTables = new HashSet<string>
    {
        "PriceHistories", "AllegroPriceHistories", "Products", "CoOfrs",
        "CoOfrPriceHistories", "AllegroScrapedOffers", "AllegroPriceBridgeItems",
        "AllegroPriceBridgeBatches", "AllegroProducts", "PriceHistoryExtendedInfos",
        "AllegroPriceHistoryExtendedInfos"
    };

            if (!allowedTables.Contains(tableName))
                return BadRequest($"Tabela '{tableName}' nie jest dozwolona.");

            if (scaleFactor < 0.01m || scaleFactor > 0.50m)
                return BadRequest("Scale factor musi być między 0.01 a 0.50.");

            try
            {
                var sfStr = scaleFactor.ToString(CultureInfo.InvariantCulture);
                var sql = $@"ALTER TABLE ""{tableName}"" SET (
            autovacuum_vacuum_scale_factor = {sfStr},
            autovacuum_analyze_scale_factor = {sfStr}
        )";

                await _context.Database.ExecuteSqlRawAsync(sql);

                return Json(new { success = true, message = $"Autovacuum dla \"{tableName}\" ustawiony na {scaleFactor:P0}." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Błąd: {ex.Message}" });
            }
        }


        // ═══════════════════════════════════════════════════════════════
        // DODAJ TEN VIEWMODEL NA DOLE PLIKU (sekcja ViewModels)
        // ═══════════════════════════════════════════════════════════════

        public class TableHealthInfo
        {
            public string TableName { get; set; }
            public long LiveTuples { get; set; }
            public long DeadTuples { get; set; }
            public decimal DeadPercent { get; set; }
            public string TableSize { get; set; }
            public string TotalSize { get; set; }
            public string IndexSize { get; set; }
            public long AvgRowBytes { get; set; }
            public DateTime? LastVacuum { get; set; }
            public DateTime? LastAutovacuum { get; set; }
            public DateTime? LastAnalyze { get; set; }
            public DateTime? LastAutoanalyze { get; set; }
            public long VacuumCount { get; set; }
            public long AutovacuumCount { get; set; }
            public string CustomVacuumScaleFactor { get; set; }
            public string CustomAnalyzeScaleFactor { get; set; }

            // Obliczone na frontendzie lub tutaj
            public string HealthStatus => DeadPercent switch
            {
                >= 20 => "critical",
                >= 10 => "warning",
                >= 5 => "attention",
                _ => "healthy"
            };
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