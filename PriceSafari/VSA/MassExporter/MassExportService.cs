using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;

namespace PriceSafari.VSA.MassExporter
{
    public class MassExportService : IMassExportService
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<MassExportService> _logger;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTime> _exportCooldowns = new();
        private static readonly TimeSpan ExportCooldown = TimeSpan.FromMinutes(5);

        public MassExportService(PriceSafariContext context, IHubContext<ScrapingHub> hubContext, ILogger<MassExportService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        // ====================================================================
        // GET AVAILABLE SCRAPS (comparison + marketplace)
        // ====================================================================

        public async Task<object> GetAvailableScrapsAsync(int storeId, string userId, string sourceType = "comparison")
        {
            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (!hasAccess)
                throw new UnauthorizedAccessException("Brak dostępu do tego sklepu.");

            if (sourceType == "marketplace")
                return await GetAvailableAllegroScrapsAsync(storeId);

            return await GetAvailableComparisonScrapsAsync(storeId);
        }

        private async Task<object> GetAvailableComparisonScrapsAsync(int storeId)
        {
            var scraps = await _context.ScrapHistories
                .AsNoTracking()
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(360)
                .Select(sh => new { sh.Id, sh.Date })
                .ToListAsync();

            if (!scraps.Any())
                return new List<object>();

            var scrapIds = scraps.Select(s => s.Id).ToList();

            var priceCounts = await _context.PriceHistories
                .AsNoTracking()
                .Where(ph => scrapIds.Contains(ph.ScrapHistoryId))
                .GroupBy(ph => ph.ScrapHistoryId)
                .Select(g => new { ScrapId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ScrapId, x => x.Count);

            return scraps.Select(s => new
            {
                id = s.Id,
                date = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                priceCount = priceCounts.GetValueOrDefault(s.Id, 0)
            }).ToList();
        }

        private async Task<object> GetAvailableAllegroScrapsAsync(int storeId)
        {
            var scraps = await _context.AllegroScrapeHistories
                .AsNoTracking()
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(360)
                .Select(sh => new { sh.Id, sh.Date })
                .ToListAsync();

            if (!scraps.Any())
                return new List<object>();

            var scrapIds = scraps.Select(s => s.Id).ToList();

            var priceCounts = await _context.AllegroPriceHistories
                .AsNoTracking()
                .Where(ph => scrapIds.Contains(ph.AllegroScrapeHistoryId))
                .GroupBy(ph => ph.AllegroScrapeHistoryId)
                .Select(g => new { ScrapId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ScrapId, x => x.Count);

            return scraps.Select(s => new
            {
                id = s.Id,
                date = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                priceCount = priceCounts.GetValueOrDefault(s.Id, 0)
            }).ToList();
        }

        // ====================================================================
        // GŁÓWNA METODA GENEROWANIA EKSPORTU
        // ====================================================================

        public async Task<(byte[] FileContent, string FileName, string ContentType)> GenerateExportAsync(
            int storeId, ExportMultiRequest request, string userId)
        {
            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (!hasAccess)
                throw new UnauthorizedAccessException("Brak dostępu do tego sklepu.");

            var now = DateTime.UtcNow;
            if (_exportCooldowns.TryGetValue(storeId, out var lastExport))
            {
                var remaining = ExportCooldown - (now - lastExport);
                if (remaining > TimeSpan.Zero)
                {
                    var secondsLeft = (int)Math.Ceiling(remaining.TotalSeconds);
                    string timeStr = secondsLeft > 60 ? $"{(int)Math.Ceiling(remaining.TotalMinutes)} min" : $"{secondsLeft} sek";
                    throw new InvalidOperationException($"Eksport będzie dostępny za {timeStr}.");
                }
            }
            _exportCooldowns[storeId] = now;

            var sourceType = request.SourceType ?? "comparison";

            if (sourceType == "marketplace")
                return await GenerateAllegroExportAsync(storeId, request);

            return await GenerateComparisonExportAsync(storeId, request);
        }

        // ====================================================================
        // COMPARISON EXPORT (Ceneo / Google) — istniejąca logika
        // ====================================================================

        private async Task<(byte[] FileContent, string FileName, string ContentType)> GenerateComparisonExportAsync(
            int storeId, ExportMultiRequest request)
        {
            var connectionId = request.ConnectionId;
            var exportType = request.ExportType ?? "prices";

            var storeName = await _context.Stores.AsNoTracking()
                .Where(s => s.StoreId == storeId).Select(s => s.StoreName).FirstOrDefaultAsync();
            var myStoreNameLower = storeName?.ToLower().Trim() ?? "";

            var priceValues = await _context.PriceValues.AsNoTracking()
                .Where(pv => pv.StoreId == storeId)
                .Select(pv => new { pv.UsePriceWithDelivery })
                .FirstOrDefaultAsync() ?? new { UsePriceWithDelivery = false };

            var activePreset = await _context.CompetitorPresets.AsNoTracking()
                .Include(x => x.CompetitorItems)
                .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse && cp.Type == PresetType.PriceComparison);

            Dictionary<(string Store, DataSourceType Source), bool> competitorItemsDict = null;
            if (activePreset?.Type == PresetType.PriceComparison)
            {
                competitorItemsDict = activePreset.CompetitorItems.ToDictionary(
                    ci => (Store: ci.StoreName.ToLower().Trim(), Source: ci.DataSource),
                    ci => ci.UseCompetitor);
            }

            var scraps = await _context.ScrapHistories.AsNoTracking()
                .Where(sh => request.ScrapIds.Contains(sh.Id) && sh.StoreId == storeId)
                .OrderBy(sh => sh.Date)
                .ToListAsync();

            if (!scraps.Any())
                throw new InvalidOperationException("Nie znaleziono wybranych analiz.");

            using var workbook = new XSSFWorkbook();
            var styles = CreateExportStyles(workbook);

            int totalScraps = scraps.Count;
            int processedScraps = 0;
            int grandTotalPrices = 0;

            foreach (var scrap in scraps)
            {
                var scrapDateStr = scrap.Date.ToString("dd.MM.yyyy");
                var scrapDateShort = scrap.Date.ToString("dd.MM");

                await SendExportProgress(connectionId, new
                {
                    step = "processing",
                    currentIndex = processedScraps + 1,
                    totalScraps,
                    scrapDate = scrapDateStr,
                    priceCount = 0,
                    grandTotalPrices,
                    percentComplete = (int)((double)processedScraps / totalScraps * 95)
                });

                var rawData = await LoadRawExportData(scrap.Id, storeId, myStoreNameLower,
                    priceValues.UsePriceWithDelivery, activePreset, competitorItemsDict);

                grandTotalPrices += rawData.Count;

                await SendExportProgress(connectionId, new
                {
                    step = "writing",
                    currentIndex = processedScraps + 1,
                    totalScraps,
                    scrapDate = scrapDateStr,
                    priceCount = rawData.Count,
                    grandTotalPrices,
                    percentComplete = (int)(((double)processedScraps + 0.5) / totalScraps * 95)
                });

                if (exportType == "competition")
                {
                    var suffix = totalScraps > 1 ? $" {scrapDateShort}" : "";
                    var (competitors, brands) = BuildCompetitionData(rawData, myStoreNameLower);

                    WriteCompetitionOverviewSheet(workbook, $"Przegląd{suffix}", competitors, scrapDateStr, storeName, activePreset?.PresetName, styles);
                    WriteCompetitionDistributionSheet(workbook, $"Rozkład{suffix}", competitors, scrapDateStr, styles);
                    WriteBrandAnalysisSheet(workbook, $"Marki{suffix}", brands, scrapDateStr, styles);
                }
                else
                {
                    var exportRows = BuildPriceExportRows(rawData, myStoreNameLower);
                    var sheetName = scrapDateStr;
                    var sheet = workbook.CreateSheet(sheetName);
                    WritePriceExportSheet(sheet, exportRows, styles);
                }

                processedScraps++;
                await SendExportProgress(connectionId, new
                {
                    step = "processing",
                    currentIndex = processedScraps,
                    totalScraps,
                    scrapDate = scrapDateStr,
                    priceCount = rawData.Count,
                    grandTotalPrices,
                    percentComplete = (int)((double)processedScraps / totalScraps * 95)
                });
            }

            await SendExportProgress(connectionId, new
            {
                step = "finalizing",
                currentIndex = totalScraps,
                totalScraps,
                scrapDate = "",
                priceCount = 0,
                grandTotalPrices,
                percentComplete = 100
            });

            using var stream = new MemoryStream();
            workbook.Write(stream);
            var content = stream.ToArray();

            var dateRange = scraps.Count == 1
                ? scraps[0].Date.ToString("yyyy-MM-dd")
                : $"{scraps.First().Date:yyyy-MM-dd}_do_{scraps.Last().Date:yyyy-MM-dd}";

            var typeLabel = exportType == "competition" ? "Konkurencja" : "Analiza";
            var fileName = $"{typeLabel}_{storeName}_{dateRange}.xlsx";
            var contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return (content, fileName, contentType);
        }

        // ====================================================================
        // ALLEGRO EXPORT (Marketplace)
        // ====================================================================

        private async Task<(byte[] FileContent, string FileName, string ContentType)> GenerateAllegroExportAsync(
            int storeId, ExportMultiRequest request)
        {
            var connectionId = request.ConnectionId;
            var exportType = request.ExportType ?? "prices";

            var store = await _context.Stores.AsNoTracking()
                .Where(s => s.StoreId == storeId)
                .Select(s => new { s.StoreName, s.StoreNameAllegro })
                .FirstOrDefaultAsync();

            var storeName = store?.StoreName ?? "";
            var storeNameAllegro = store?.StoreNameAllegro ?? "";
            var myStoreNameLower = storeNameAllegro.ToLower().Trim();

            var activePreset = await _context.CompetitorPresets.AsNoTracking()
                .Include(x => x.CompetitorItems)
                .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse && cp.Type == PresetType.Marketplace);

            Dictionary<string, bool> competitorRules = null;
            if (activePreset != null)
            {
                competitorRules = activePreset.CompetitorItems
                    .Where(ci => ci.DataSource == DataSourceType.Allegro)
                    .ToDictionary(ci => ci.StoreName.ToLower().Trim(), ci => ci.UseCompetitor);
            }

            var scraps = await _context.AllegroScrapeHistories.AsNoTracking()
                .Where(sh => request.ScrapIds.Contains(sh.Id) && sh.StoreId == storeId)
                .OrderBy(sh => sh.Date)
                .ToListAsync();

            if (!scraps.Any())
                throw new InvalidOperationException("Nie znaleziono wybranych analiz.");

            using var workbook = new XSSFWorkbook();
            var styles = CreateExportStyles(workbook);

            int totalScraps = scraps.Count;
            int processedScraps = 0;
            int grandTotalPrices = 0;

            foreach (var scrap in scraps)
            {
                var scrapDateStr = scrap.Date.ToString("dd.MM.yyyy");
                var scrapDateShort = scrap.Date.ToString("dd.MM");

                await SendExportProgress(connectionId, new
                {
                    step = "processing",
                    currentIndex = processedScraps + 1,
                    totalScraps,
                    scrapDate = scrapDateStr,
                    priceCount = 0,
                    grandTotalPrices,
                    percentComplete = (int)((double)processedScraps / totalScraps * 95)
                });

                var rawData = await LoadRawAllegroExportData(scrap.Id, storeId, storeNameAllegro, activePreset, competitorRules);

                grandTotalPrices += rawData.Count;

                await SendExportProgress(connectionId, new
                {
                    step = "writing",
                    currentIndex = processedScraps + 1,
                    totalScraps,
                    scrapDate = scrapDateStr,
                    priceCount = rawData.Count,
                    grandTotalPrices,
                    percentComplete = (int)(((double)processedScraps + 0.5) / totalScraps * 95)
                });

                if (exportType == "competition")
                {
                    var suffix = totalScraps > 1 ? $" {scrapDateShort}" : "";

                    // Deduplikacja przed raportem konkurencji:
                    // Grupujemy po EAN (priorytet) lub AllegroProductId, potem:
                    // - nasza oferta: najtańsza (priorytet targetOfferId)
                    // - konkurent: najtańsza per SellerName
                    var dedupedForCompetition = rawData
                        .GroupBy(x => !string.IsNullOrWhiteSpace(x.Ean)
                            ? $"ean:{x.Ean.Trim()}"
                            : $"pid:{x.AllegroProductId}")
                        .SelectMany(g =>
                        {
                            var items = g.ToList();
                            var result = new List<RawAllegroExportEntry>();

                            // Moja oferta: priorytet targetOfferId, potem najtańsza
                            var myItems = items.Where(x => x.IsMe && x.Price > 0).ToList();
                            if (myItems.Any())
                            {
                                RawAllegroExportEntry myBest = null;
                                foreach (var entry in myItems)
                                {
                                    if (long.TryParse(entry.IdOnAllegro, out var tid) && entry.IdAllegro == tid)
                                    { myBest = entry; break; }
                                }
                                result.Add(myBest ?? myItems.OrderBy(x => x.Price).First());
                            }
                            else
                            {
                                var myZero = items.FirstOrDefault(x => x.IsMe);
                                if (myZero != null) result.Add(myZero);
                            }

                            // Konkurenci: najtańsza oferta per sprzedawca
                            var compDeduped = items
                                .Where(x => !x.IsMe && x.Price > 0)
                                .GroupBy(x => x.SellerName.ToLower().Trim())
                                .Select(sg => sg.OrderBy(x => x.Price).First());
                            result.AddRange(compDeduped);

                            return result;
                        })
                        .ToList();

                    var mappedForCompetition = dedupedForCompetition.Select(x => new RawExportEntry
                    {
                        ProductName = x.ProductName,
                        Producer = x.Producer,
                        Ean = x.Ean,
                        CatalogNumber = x.AllegroSku,
                        ExternalId = null,
                        MarginPrice = x.MarginPrice,
                        Price = x.Price,
                        StoreName = x.SellerName,
                        IsGoogle = false,
                        ShippingCostNum = null,
                        CeneoInStock = null,
                        GoogleInStock = null,
                        IsMe = x.IsMe,
                        FinalPrice = x.Price
                    }).ToList();

                    var (competitors, brands) = BuildCompetitionData(mappedForCompetition, myStoreNameLower);

                    WriteCompetitionOverviewSheet(workbook, $"Przegląd{suffix}", competitors, scrapDateStr, storeName, activePreset?.PresetName, styles);
                    WriteCompetitionDistributionSheet(workbook, $"Rozkład{suffix}", competitors, scrapDateStr, styles);
                    WriteBrandAnalysisSheet(workbook, $"Marki{suffix}", brands, scrapDateStr, styles);
                }
                else
                {
                    var exportRows = BuildAllegroPriceExportRows(rawData, myStoreNameLower);
                    var sheetName = scrapDateStr;
                    var sheet = workbook.CreateSheet(sheetName);
                    WriteAllegroPriceExportSheet(sheet, exportRows, styles);
                }

                processedScraps++;
                await SendExportProgress(connectionId, new
                {
                    step = "processing",
                    currentIndex = processedScraps,
                    totalScraps,
                    scrapDate = scrapDateStr,
                    priceCount = rawData.Count,
                    grandTotalPrices,
                    percentComplete = (int)((double)processedScraps / totalScraps * 95)
                });
            }

            await SendExportProgress(connectionId, new
            {
                step = "finalizing",
                currentIndex = totalScraps,
                totalScraps,
                scrapDate = "",
                priceCount = 0,
                grandTotalPrices,
                percentComplete = 100
            });

            using var stream = new MemoryStream();
            workbook.Write(stream);
            var content = stream.ToArray();

            var dateRange = scraps.Count == 1
                ? scraps[0].Date.ToString("yyyy-MM-dd")
                : $"{scraps.First().Date:yyyy-MM-dd}_do_{scraps.Last().Date:yyyy-MM-dd}";

            var typeLabel = exportType == "competition" ? "Konkurencja_Allegro" : "Analiza_Allegro";
            var fileName = $"{typeLabel}_{storeName}_{dateRange}.xlsx";
            var contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

            return (content, fileName, contentType);
        }

        // ====================================================================
        // MODELE POMOCNICZE — Comparison (istniejące)
        // ====================================================================

        private class RawExportEntry
        {
            public string ProductName { get; set; }
            public string Producer { get; set; }
            public string Ean { get; set; }
            public string CatalogNumber { get; set; }
            public int? ExternalId { get; set; }
            public decimal? MarginPrice { get; set; }
            public decimal Price { get; set; }
            public string StoreName { get; set; }
            public bool IsGoogle { get; set; }
            public decimal? ShippingCostNum { get; set; }
            public bool? CeneoInStock { get; set; }
            public bool? GoogleInStock { get; set; }
            public bool IsMe { get; set; }
            public decimal FinalPrice { get; set; }
        }

        private class ExportProductRow
        {
            public int? ExternalId { get; set; }
            public string ProductName { get; set; }
            public string Producer { get; set; }
            public string Ean { get; set; }
            public string CatalogNumber { get; set; }
            public decimal? MarginPrice { get; set; }
            public decimal? MyPrice { get; set; }
            public decimal? BestCompetitorPrice { get; set; }
            public string BestCompetitorStore { get; set; }
            public decimal? DiffToLowest { get; set; }
            public decimal? DiffToLowestPercent { get; set; }
            public int TotalOffers { get; set; }
            public int MyRank { get; set; }
            public string PositionString { get; set; }
            public int ColorCode { get; set; }
            public bool? MyGoogleInStock { get; set; }
            public bool? MyCeneoInStock { get; set; }
            public bool? CompGoogleInStock { get; set; }
            public bool? CompCeneoInStock { get; set; }
            public List<ExportCompetitorOffer> Competitors { get; set; } = new();
        }

        private class ExportCompetitorOffer
        {
            public string Store { get; set; }
            public decimal FinalPrice { get; set; }
        }

        // ====================================================================
        // MODELE POMOCNICZE — Allegro (nowe)
        // ====================================================================

        private class RawAllegroExportEntry
        {
            public int AllegroProductId { get; set; }
            public string ProductName { get; set; }
            public string Producer { get; set; }
            public string Ean { get; set; }
            public string AllegroSku { get; set; }
            public string IdOnAllegro { get; set; }
            public decimal? MarginPrice { get; set; }
            public decimal Price { get; set; }
            public string SellerName { get; set; }
            public long IdAllegro { get; set; }
            public int? DeliveryTime { get; set; }
            public int? Popularity { get; set; }
            public bool SuperSeller { get; set; }
            public bool Smart { get; set; }
            public bool IsBestPriceGuarantee { get; set; }
            public bool TopOffer { get; set; }
            public bool SuperPrice { get; set; }
            public bool IsMe { get; set; }
        }

        private class ExportAllegroProductRow
        {
            public int AllegroProductId { get; set; }
            public string ProductName { get; set; }
            public string Producer { get; set; }
            public string Ean { get; set; }
            public string AllegroSku { get; set; }
            public decimal? MarginPrice { get; set; }
            public decimal? MyPrice { get; set; }
            public decimal? BestCompetitorPrice { get; set; }
            public string BestCompetitorStore { get; set; }
            public decimal? DiffToLowest { get; set; }
            public decimal? DiffToLowestPercent { get; set; }
            public int TotalOffers { get; set; }
            public int MyRank { get; set; }
            public string PositionString { get; set; }
            public int ColorCode { get; set; }
            public int? MyPopularity { get; set; }
            public int TotalPopularity { get; set; }
            public List<ExportCompetitorOffer> Competitors { get; set; } = new();
        }

        // ====================================================================
        // MODELE POMOCNICZE — Competition (współdzielone)
        // ====================================================================

        private class CompetitorSummary
        {
            public string StoreName { get; set; }
            public int OverlapCount { get; set; }
            public int TheyCheaperCount { get; set; }
            public int TheyMoreExpensiveCount { get; set; }
            public int EqualCount { get; set; }
            public decimal AvgDiffPercent { get; set; }
            public decimal MedianDiffPercent { get; set; }
            public List<decimal> AllDiffs { get; set; } = new();

            public int TheyCheaper_0_5 { get; set; }
            public int TheyCheaper_5_10 { get; set; }
            public int TheyCheaper_10_15 { get; set; }
            public int TheyCheaper_15_20 { get; set; }
            public int TheyCheaper_20_25 { get; set; }
            public int TheyCheaper_25_30 { get; set; }
            public int TheyCheaper_30_35 { get; set; }
            public int TheyCheaper_35_40 { get; set; }
            public int TheyCheaper_40_45 { get; set; }
            public int TheyCheaper_45_50 { get; set; }
            public int TheyCheaper_50plus { get; set; }

            public int TheyExpensive_0_5 { get; set; }
            public int TheyExpensive_5_10 { get; set; }
            public int TheyExpensive_10_15 { get; set; }
            public int TheyExpensive_15_20 { get; set; }
            public int TheyExpensive_20_25 { get; set; }
            public int TheyExpensive_25_30 { get; set; }
            public int TheyExpensive_30_35 { get; set; }
            public int TheyExpensive_35_40 { get; set; }
            public int TheyExpensive_40_45 { get; set; }
            public int TheyExpensive_45_50 { get; set; }
            public int TheyExpensive_50plus { get; set; }

            public Dictionary<string, (int Cheaper, int Expensive, int Equal)> BrandBreakdown { get; set; } = new();
        }

        private class BrandSummary
        {
            public string BrandName { get; set; }
            public int ProductCount { get; set; }
            public decimal AvgOurPrice { get; set; }
            public decimal AvgMarketPrice { get; set; }
            public decimal PriceIndexPercent { get; set; }
            public int WeAreCheapestCount { get; set; }
            public decimal WeAreCheapestPercent { get; set; }
            public int WeAreMostExpensiveCount { get; set; }
            public decimal WeAreMostExpensivePercent { get; set; }
        }

        // ====================================================================
        // ŁADOWANIE DANYCH — Comparison
        // ====================================================================

        private async Task<List<RawExportEntry>> LoadRawExportData(
            int scrapId, int storeId, string myStoreNameLower,
            bool usePriceWithDelivery,
            CompetitorPresetClass activePreset,
            Dictionary<(string Store, DataSourceType Source), bool> competitorItemsDict)
        {
            var query = from p in _context.Products.AsNoTracking()
                        join ph in _context.PriceHistories.AsNoTracking()
                            on p.ProductId equals ph.ProductId
                        where p.StoreId == storeId && p.IsScrapable && ph.ScrapHistoryId == scrapId
                        select new
                        {
                            p.ProductName,
                            p.Producer,
                            p.Ean,
                            p.CatalogNumber,
                            p.ExternalId,
                            p.MarginPrice,
                            ph.Price,
                            ph.StoreName,
                            ph.IsGoogle,
                            ph.ShippingCostNum,
                            ph.CeneoInStock,
                            ph.GoogleInStock
                        };

            if (activePreset != null)
            {
                if (!activePreset.SourceGoogle) query = query.Where(x => x.IsGoogle != true);
                if (!activePreset.SourceCeneo) query = query.Where(x => x.IsGoogle == true);
            }

            var rawList = await query.ToListAsync();

            if (activePreset?.Type == PresetType.PriceComparison && competitorItemsDict != null)
            {
                rawList = rawList.Where(row =>
                {
                    if (row.StoreName != null && row.StoreName.ToLower().Trim() == myStoreNameLower)
                        return true;
                    DataSourceType src = row.IsGoogle == true ? DataSourceType.Google : DataSourceType.Ceneo;
                    var key = (Store: (row.StoreName ?? "").ToLower().Trim(), Source: src);
                    if (competitorItemsDict.TryGetValue(key, out bool use)) return use;
                    return activePreset.UseUnmarkedStores;
                }).ToList();
            }

            return rawList.Select(x =>
            {
                bool isMe = x.StoreName != null && x.StoreName.ToLower().Trim() == myStoreNameLower;
                decimal finalPrice = (usePriceWithDelivery && x.ShippingCostNum.HasValue)
                    ? x.Price + x.ShippingCostNum.Value : x.Price;

                return new RawExportEntry
                {
                    ProductName = x.ProductName,
                    Producer = x.Producer,
                    Ean = x.Ean,
                    CatalogNumber = x.CatalogNumber,
                    ExternalId = x.ExternalId,
                    MarginPrice = x.MarginPrice,
                    Price = x.Price,
                    StoreName = x.StoreName ?? (x.IsGoogle ? "Google" : "Ceneo"),
                    IsGoogle = x.IsGoogle,
                    ShippingCostNum = x.ShippingCostNum,
                    CeneoInStock = x.CeneoInStock,
                    GoogleInStock = x.GoogleInStock,
                    IsMe = isMe,
                    FinalPrice = finalPrice
                };
            }).ToList();
        }

        // ====================================================================
        // ŁADOWANIE DANYCH — Allegro
        // ====================================================================

        private async Task<List<RawAllegroExportEntry>> LoadRawAllegroExportData(
            int scrapId, int storeId, string storeNameAllegro,
            CompetitorPresetClass activePreset,
            Dictionary<string, bool> competitorRules)
        {
            var products = await _context.AllegroProducts.AsNoTracking()
                .Where(p => p.StoreId == storeId && p.IsScrapable)
                .ToListAsync();

            var productIds = products.Select(p => p.AllegroProductId).ToList();
            var productDict = products.ToDictionary(p => p.AllegroProductId);

            var rawPrices = await _context.AllegroPriceHistories.AsNoTracking()
                .Where(ph => ph.AllegroScrapeHistoryId == scrapId && productIds.Contains(ph.AllegroProductId))
                .ToListAsync();

            // Deduplikacja: jeden rekord per produkt + oferta
            rawPrices = rawPrices
                .GroupBy(ph => new { ph.AllegroProductId, ph.IdAllegro })
                .Select(g => g.First())
                .ToList();

            bool includeNoDelivery = activePreset?.IncludeNoDeliveryInfo ?? true;
            int minDelivery = activePreset?.MinDeliveryDays ?? 0;
            int maxDelivery = activePreset?.MaxDeliveryDays ?? 31;

            var result = new List<RawAllegroExportEntry>();

            foreach (var ph in rawPrices)
            {
                if (!productDict.TryGetValue(ph.AllegroProductId, out var product))
                    continue;

                bool isMe = ph.SellerName.Equals(storeNameAllegro, StringComparison.OrdinalIgnoreCase);

                // Filtrowanie presetu (nie filtrujemy "moich" ofert)
                if (!isMe && activePreset != null)
                {
                    // Filtr czasu dostawy
                    if (ph.DeliveryTime.HasValue)
                    {
                        if (ph.DeliveryTime.Value < minDelivery || ph.DeliveryTime.Value > maxDelivery)
                            continue;
                    }
                    else
                    {
                        if (!includeNoDelivery)
                            continue;
                    }

                    // Filtr sprzedawcy
                    if (competitorRules != null)
                    {
                        var sellerLower = (ph.SellerName ?? "").ToLower().Trim();
                        if (competitorRules.TryGetValue(sellerLower, out bool useCompetitor))
                        {
                            if (!useCompetitor) continue;
                        }
                        else if (!activePreset.UseUnmarkedStores)
                        {
                            continue;
                        }
                    }
                }

                result.Add(new RawAllegroExportEntry
                {
                    AllegroProductId = product.AllegroProductId,
                    ProductName = product.AllegroProductName,
                    Producer = product.Producer,
                    Ean = product.AllegroEan,
                    AllegroSku = product.AllegroSku,
                    IdOnAllegro = product.IdOnAllegro,
                    MarginPrice = product.AllegroMarginPrice,
                    Price = ph.Price,
                    SellerName = ph.SellerName,
                    IdAllegro = ph.IdAllegro,
                    DeliveryTime = ph.DeliveryTime,
                    Popularity = ph.Popularity,
                    SuperSeller = ph.SuperSeller,
                    Smart = ph.Smart,
                    IsBestPriceGuarantee = ph.IsBestPriceGuarantee,
                    TopOffer = ph.TopOffer,
                    SuperPrice = ph.SuperPrice,
                    IsMe = isMe
                });
            }

            return result;
        }

        // ====================================================================
        // BUDOWANIE WIERSZY — Comparison
        // ====================================================================

        private List<ExportProductRow> BuildPriceExportRows(List<RawExportEntry> rawData, string myStoreNameLower)
        {
            return rawData
                .GroupBy(x => new { x.ProductName, x.Producer, x.Ean, x.CatalogNumber, x.ExternalId, x.MarginPrice })
                .Select(g =>
                {
                    var all = g.ToList();
                    var myOffer = all.FirstOrDefault(x => x.IsMe);
                    var competitors = all.Where(x => !x.IsMe).OrderBy(x => x.FinalPrice).ToList();
                    var allOffers = new List<RawExportEntry>(competitors);
                    if (myOffer != null) allOffers.Add(myOffer);

                    var bestComp = competitors.FirstOrDefault();
                    int totalOffers = allOffers.Count;
                    int myRank = 0;
                    string posStr = "-";
                    decimal? diffPln = null;
                    decimal? diffPct = null;
                    int colorCode = 0;

                    if (myOffer != null && totalOffers > 0)
                    {
                        int cheaper = allOffers.Count(x => x.FinalPrice < myOffer.FinalPrice);
                        myRank = cheaper + 1;
                        posStr = $"{myRank} z {totalOffers}";

                        if (bestComp != null)
                        {
                            diffPln = myOffer.FinalPrice - bestComp.FinalPrice;
                            if (bestComp.FinalPrice > 0)
                                diffPct = Math.Round((myOffer.FinalPrice - bestComp.FinalPrice) / bestComp.FinalPrice * 100, 2);
                        }

                        decimal minPrice = allOffers.Min(x => x.FinalPrice);
                        if (myOffer.FinalPrice == minPrice)
                        {
                            int othersAtMin = allOffers.Count(x => x.FinalPrice == minPrice && !x.IsMe);
                            colorCode = othersAtMin == 0 ? 1 : 2;
                        }
                        else colorCode = 3;
                    }

                    bool? myGoogle = myOffer?.IsGoogle == true ? myOffer.GoogleInStock : null;
                    bool? myCeneo = myOffer != null && myOffer.IsGoogle == false ? myOffer.CeneoInStock : null;
                    var myEntries = all.Where(x => x.IsMe).ToList();
                    foreach (var e in myEntries)
                    {
                        if (e.IsGoogle && e.GoogleInStock.HasValue) myGoogle = e.GoogleInStock;
                        if (!e.IsGoogle && e.CeneoInStock.HasValue) myCeneo = e.CeneoInStock;
                    }

                    bool? compGoogle = bestComp?.IsGoogle == true ? bestComp.GoogleInStock : null;
                    bool? compCeneo = bestComp != null && !bestComp.IsGoogle ? bestComp.CeneoInStock : null;

                    return new ExportProductRow
                    {
                        ExternalId = g.Key.ExternalId,
                        ProductName = g.Key.ProductName,
                        Producer = g.Key.Producer,
                        Ean = g.Key.Ean,
                        CatalogNumber = g.Key.CatalogNumber,
                        MarginPrice = g.Key.MarginPrice,
                        MyPrice = myOffer?.FinalPrice,
                        BestCompetitorPrice = bestComp?.FinalPrice,
                        BestCompetitorStore = bestComp?.StoreName,
                        DiffToLowest = diffPln,
                        DiffToLowestPercent = diffPct,
                        TotalOffers = totalOffers,
                        MyRank = myRank,
                        PositionString = posStr,
                        ColorCode = colorCode,
                        MyGoogleInStock = myGoogle,
                        MyCeneoInStock = myCeneo,
                        CompGoogleInStock = compGoogle,
                        CompCeneoInStock = compCeneo,
                        Competitors = competitors.Select(c => new ExportCompetitorOffer
                        {
                            Store = c.StoreName,
                            FinalPrice = c.FinalPrice
                        }).ToList()
                    };
                })
                .OrderBy(x => x.ProductName)
                .ToList();
        }

        // ====================================================================
        // BUDOWANIE WIERSZY — Allegro
        // ====================================================================

        private List<ExportAllegroProductRow> BuildAllegroPriceExportRows(List<RawAllegroExportEntry> rawData, string myStoreNameLower)
        {
            // Grupowanie priorytetowo po EAN (jeśli istnieje), fallback po AllegroProductId.
            // Dzięki temu produkty z tym samym EAN w różnych katalogach schodzą do jednego wiersza.
            return rawData
                .GroupBy(x => !string.IsNullOrWhiteSpace(x.Ean)
                    ? $"ean:{x.Ean.Trim()}"
                    : $"pid:{x.AllegroProductId}")
                .Select(g =>
                {
                    var all = g.ToList();

                    // Metadane z pierwszego wpisu (lub tego z najlepszymi danymi)
                    var representative = all.First();

                    // ── MOJA OFERTA: priorytet targetOfferId, potem najtańsza ──
                    var myEntries = all.Where(x => x.IsMe && x.Price > 0).ToList();
                    RawAllegroExportEntry myOffer = null;

                    if (myEntries.Any())
                    {
                        // Najpierw szukamy oferty pasującej do IdOnAllegro (główna aukcja)
                        foreach (var entry in myEntries)
                        {
                            if (long.TryParse(entry.IdOnAllegro, out var tid) && entry.IdAllegro == tid)
                            {
                                myOffer = entry;
                                break;
                            }
                        }
                        // Fallback: najtańsza z moich ofert
                        myOffer ??= myEntries.OrderBy(x => x.Price).First();
                    }

                    // ── KONKURENCI: deduplikacja po SellerName, najtańsza oferta per sprzedawca ──
                    var competitors = all
                        .Where(x => !x.IsMe && x.Price > 0)
                        .GroupBy(x => x.SellerName.ToLower().Trim())
                        .Select(sg => sg.OrderBy(x => x.Price).First())
                        .OrderBy(x => x.Price)
                        .ToList();

                    // Wszystkie unikalne oferty (do rankingu)
                    var allUniqueOffers = new List<RawAllegroExportEntry>(competitors);
                    if (myOffer != null) allUniqueOffers.Add(myOffer);

                    var bestComp = competitors.FirstOrDefault();
                    int totalOffers = allUniqueOffers.Count;
                    int myRank = 0;
                    string posStr = "-";
                    decimal? diffPln = null;
                    decimal? diffPct = null;
                    int colorCode = 0;

                    if (myOffer != null && myOffer.Price > 0 && totalOffers > 0)
                    {
                        int cheaper = allUniqueOffers.Count(x => x.Price < myOffer.Price);
                        myRank = cheaper + 1;
                        posStr = $"{myRank} z {totalOffers}";

                        if (bestComp != null)
                        {
                            diffPln = myOffer.Price - bestComp.Price;
                            if (bestComp.Price > 0)
                                diffPct = Math.Round((myOffer.Price - bestComp.Price) / bestComp.Price * 100, 2);
                        }

                        decimal minPrice = allUniqueOffers.Where(x => x.Price > 0).Min(x => x.Price);
                        if (myOffer.Price == minPrice)
                        {
                            int othersAtMin = allUniqueOffers.Count(x => x.Price == minPrice && !x.IsMe);
                            colorCode = othersAtMin == 0 ? 1 : 2;
                        }
                        else colorCode = 3;
                    }

                    // Popularity: sumujemy WSZYSTKIE wpisy (bez dedupu) — bo każda aukcja ma swój wolumen
                    int totalPopularity = all.Sum(x => x.Popularity ?? 0);
                    int? myPopularity = myEntries.Any() ? myEntries.Sum(x => x.Popularity ?? 0) : (int?)null;

                    return new ExportAllegroProductRow
                    {
                        AllegroProductId = representative.AllegroProductId,
                        ProductName = representative.ProductName,
                        Producer = representative.Producer,
                        Ean = representative.Ean,
                        AllegroSku = representative.AllegroSku,
                        MarginPrice = representative.MarginPrice,
                        MyPrice = myOffer?.Price,
                        BestCompetitorPrice = bestComp?.Price,
                        BestCompetitorStore = bestComp?.SellerName,
                        DiffToLowest = diffPln,
                        DiffToLowestPercent = diffPct,
                        TotalOffers = totalOffers,
                        MyRank = myRank,
                        PositionString = posStr,
                        ColorCode = colorCode,
                        MyPopularity = myPopularity,
                        TotalPopularity = totalPopularity,
                        Competitors = competitors.Select(c => new ExportCompetitorOffer
                        {
                            Store = c.SellerName,
                            FinalPrice = c.Price
                        }).ToList()
                    };
                })
                .OrderBy(x => x.ProductName)
                .ToList();
        }

        // ====================================================================
        // ZAPIS ARKUSZA — Comparison
        // ====================================================================

        private void WritePriceExportSheet(ISheet sheet, List<ExportProductRow> data, ExportStyles s)
        {
            var headerRow = sheet.CreateRow(0);
            int col = 0;

            string[] headers = {
                "ID Produktu", "Nazwa Produktu", "Producent", "EAN", "SKU",
                "Cena Zakupu", "Twoja Cena", "Najt. Cena Konkurencji", "Najt. Sklep",
                "Różnica PLN", "Różnica %",
                "Ilość Ofert", "Twoja Pozycja",
                "Google - Ty", "Ceneo - Ty",
                "Google - Konkurent", "Ceneo - Konkurent"
            };

            foreach (var h in headers)
            {
                var cell = headerRow.CreateCell(col++);
                cell.SetCellValue(h);
                cell.CellStyle = s.Header;
            }

            int maxComp = 60;
            for (int i = 1; i <= maxComp; i++)
            {
                var c1 = headerRow.CreateCell(col++); c1.SetCellValue($"Sklep {i}"); c1.CellStyle = s.Header;
                var c2 = headerRow.CreateCell(col++); c2.SetCellValue($"Cena {i}"); c2.CellStyle = s.Header;
            }

            int rowIdx = 1;
            foreach (var item in data)
            {
                var row = sheet.CreateRow(rowIdx++);
                col = 0;

                row.CreateCell(col++).SetCellValue(item.ExternalId?.ToString() ?? "");
                row.CreateCell(col++).SetCellValue(item.ProductName ?? "");
                row.CreateCell(col++).SetCellValue(item.Producer ?? "");
                row.CreateCell(col++).SetCellValue(item.Ean ?? "");
                row.CreateCell(col++).SetCellValue(item.CatalogNumber ?? "");

                SetDecimalCell(row.CreateCell(col++), item.MarginPrice, s.Currency);

                var cellMyPrice = row.CreateCell(col++);
                if (item.MyPrice.HasValue)
                {
                    cellMyPrice.SetCellValue((double)item.MyPrice.Value);
                    cellMyPrice.CellStyle = item.ColorCode switch
                    {
                        1 => s.PriceGreen,
                        2 => s.PriceLightGreen,
                        3 => s.PriceRed,
                        _ => s.Currency
                    };
                }
                else cellMyPrice.SetCellValue("-");

                SetDecimalCell(row.CreateCell(col++), item.BestCompetitorPrice, s.Currency);
                row.CreateCell(col++).SetCellValue(item.BestCompetitorStore ?? "");

                SetDecimalCell(row.CreateCell(col++), item.DiffToLowest, s.Currency);
                SetDecimalCell(row.CreateCell(col++), item.DiffToLowestPercent, s.Percent);

                row.CreateCell(col++).SetCellValue(item.TotalOffers);
                row.CreateCell(col++).SetCellValue(item.PositionString);

                row.CreateCell(col++).SetCellValue(StockText(item.MyGoogleInStock));
                row.CreateCell(col++).SetCellValue(StockText(item.MyCeneoInStock));
                row.CreateCell(col++).SetCellValue(StockText(item.CompGoogleInStock));
                row.CreateCell(col++).SetCellValue(StockText(item.CompCeneoInStock));

                for (int i = 0; i < maxComp; i++)
                {
                    if (i < item.Competitors.Count)
                    {
                        row.CreateCell(col++).SetCellValue(item.Competitors[i].Store);
                        var cp = row.CreateCell(col++);
                        cp.SetCellValue((double)item.Competitors[i].FinalPrice);
                        cp.CellStyle = s.Currency;
                    }
                    else col += 2;
                }
            }

            for (int i = 0; i < 17; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
        }

        // ====================================================================
        // ZAPIS ARKUSZA — Allegro
        // ====================================================================

        private void WriteAllegroPriceExportSheet(ISheet sheet, List<ExportAllegroProductRow> data, ExportStyles s)
        {
            var headerRow = sheet.CreateRow(0);
            int col = 0;

            string[] headers = {
                "ID Produktu", "Nazwa Produktu", "Producent", "EAN", "SKU Allegro",
                "Cena Zakupu", "Twoja Cena", "Najt. Cena Konkurencji", "Najt. Sprzedawca",
                "Różnica PLN", "Różnica %",
                "Ilość Ofert", "Twoja Pozycja",
                "Twoja Sprzedaż", "Sprzedaż Katalogu"
            };

            foreach (var h in headers)
            {
                var cell = headerRow.CreateCell(col++);
                cell.SetCellValue(h);
                cell.CellStyle = s.Header;
            }

            int maxComp = 60;
            for (int i = 1; i <= maxComp; i++)
            {
                var c1 = headerRow.CreateCell(col++); c1.SetCellValue($"Sprzedawca {i}"); c1.CellStyle = s.Header;
                var c2 = headerRow.CreateCell(col++); c2.SetCellValue($"Cena {i}"); c2.CellStyle = s.Header;
            }

            int rowIdx = 1;
            foreach (var item in data)
            {
                var row = sheet.CreateRow(rowIdx++);
                col = 0;

                row.CreateCell(col++).SetCellValue(item.AllegroProductId);
                row.CreateCell(col++).SetCellValue(item.ProductName ?? "");
                row.CreateCell(col++).SetCellValue(item.Producer ?? "");
                row.CreateCell(col++).SetCellValue(item.Ean ?? "");
                row.CreateCell(col++).SetCellValue(item.AllegroSku ?? "");

                SetDecimalCell(row.CreateCell(col++), item.MarginPrice, s.Currency);

                var cellMyPrice = row.CreateCell(col++);
                if (item.MyPrice.HasValue)
                {
                    cellMyPrice.SetCellValue((double)item.MyPrice.Value);
                    cellMyPrice.CellStyle = item.ColorCode switch
                    {
                        1 => s.PriceGreen,
                        2 => s.PriceLightGreen,
                        3 => s.PriceRed,
                        _ => s.Currency
                    };
                }
                else cellMyPrice.SetCellValue("-");

                SetDecimalCell(row.CreateCell(col++), item.BestCompetitorPrice, s.Currency);
                row.CreateCell(col++).SetCellValue(item.BestCompetitorStore ?? "");

                SetDecimalCell(row.CreateCell(col++), item.DiffToLowest, s.Currency);
                SetDecimalCell(row.CreateCell(col++), item.DiffToLowestPercent, s.Percent);

                row.CreateCell(col++).SetCellValue(item.TotalOffers);
                row.CreateCell(col++).SetCellValue(item.PositionString);

                row.CreateCell(col++).SetCellValue(item.MyPopularity ?? 0);
                row.CreateCell(col++).SetCellValue(item.TotalPopularity);

                for (int i = 0; i < maxComp; i++)
                {
                    if (i < item.Competitors.Count)
                    {
                        row.CreateCell(col++).SetCellValue(item.Competitors[i].Store);
                        var cp = row.CreateCell(col++);
                        cp.SetCellValue((double)item.Competitors[i].FinalPrice);
                        cp.CellStyle = s.Currency;
                    }
                    else col += 2;
                }
            }

            for (int i = 0; i < 15; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
        }

        // ====================================================================
        // BUDOWANIE DANYCH KONKURENCJI (współdzielone przez oba tryby)
        // ====================================================================

        private (List<CompetitorSummary> competitors, List<BrandSummary> brands) BuildCompetitionData(
            List<RawExportEntry> rawData, string myStoreNameLower)
        {
            var productGroups = rawData
                .GroupBy(x => new { x.ProductName, x.Producer })
                .ToList();

            var competitorDict = new Dictionary<string, CompetitorSummary>(StringComparer.OrdinalIgnoreCase);
            var brandStats = new Dictionary<string, List<(decimal myPrice, decimal bestCompPrice, bool isCheapest, bool isMostExpensive)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var g in productGroups)
            {
                var entries = g.ToList();
                var myEntry = entries.FirstOrDefault(x => x.IsMe);
                if (myEntry == null || myEntry.FinalPrice <= 0) continue;

                decimal myPrice = myEntry.FinalPrice;
                var compEntries = entries.Where(x => !x.IsMe && x.FinalPrice > 0).ToList();
                if (!compEntries.Any()) continue;

                string brand = g.Key.Producer ?? "Brak producenta";
                decimal bestCompPrice = compEntries.Min(x => x.FinalPrice);
                decimal worstCompPrice = compEntries.Max(x => x.FinalPrice);
                bool isCheapest = myPrice <= bestCompPrice;
                bool isMostExpensive = myPrice >= worstCompPrice && compEntries.Count > 0;

                if (!brandStats.ContainsKey(brand)) brandStats[brand] = new();
                brandStats[brand].Add((myPrice, bestCompPrice, isCheapest, isMostExpensive));

                var uniqueCompetitors = compEntries
                    .GroupBy(x => x.StoreName.ToLower().Trim())
                    .Select(cg => cg.OrderBy(x => x.FinalPrice).First())
                    .ToList();

                foreach (var comp in uniqueCompetitors)
                {
                    string compKey = comp.StoreName.Trim();
                    if (!competitorDict.ContainsKey(compKey))
                        competitorDict[compKey] = new CompetitorSummary { StoreName = compKey };

                    var cs = competitorDict[compKey];
                    cs.OverlapCount++;

                    decimal diffPct = Math.Round((myPrice - comp.FinalPrice) / myPrice * 100, 2);
                    cs.AllDiffs.Add(diffPct);

                    string brandKey = brand;
                    if (!cs.BrandBreakdown.ContainsKey(brandKey))
                        cs.BrandBreakdown[brandKey] = (0, 0, 0);

                    if (Math.Abs(diffPct) < 0.01m)
                    {
                        cs.EqualCount++;
                        var b = cs.BrandBreakdown[brandKey]; cs.BrandBreakdown[brandKey] = (b.Cheaper, b.Expensive, b.Equal + 1);
                    }
                    else if (diffPct > 0)
                    {
                        cs.TheyCheaperCount++;
                        var b = cs.BrandBreakdown[brandKey]; cs.BrandBreakdown[brandKey] = (b.Cheaper + 1, b.Expensive, b.Equal);
                        decimal absDiff = Math.Abs(diffPct);

                        if (absDiff <= 5) cs.TheyCheaper_0_5++;
                        else if (absDiff <= 10) cs.TheyCheaper_5_10++;
                        else if (absDiff <= 15) cs.TheyCheaper_10_15++;
                        else if (absDiff <= 20) cs.TheyCheaper_15_20++;
                        else if (absDiff <= 25) cs.TheyCheaper_20_25++;
                        else if (absDiff <= 30) cs.TheyCheaper_25_30++;
                        else if (absDiff <= 35) cs.TheyCheaper_30_35++;
                        else if (absDiff <= 40) cs.TheyCheaper_35_40++;
                        else if (absDiff <= 45) cs.TheyCheaper_40_45++;
                        else if (absDiff <= 50) cs.TheyCheaper_45_50++;
                        else cs.TheyCheaper_50plus++;
                    }
                    else
                    {
                        cs.TheyMoreExpensiveCount++;
                        var b = cs.BrandBreakdown[brandKey]; cs.BrandBreakdown[brandKey] = (b.Cheaper, b.Expensive + 1, b.Equal);
                        decimal absDiff = Math.Abs(diffPct);

                        if (absDiff <= 5) cs.TheyExpensive_0_5++;
                        else if (absDiff <= 10) cs.TheyExpensive_5_10++;
                        else if (absDiff <= 15) cs.TheyExpensive_10_15++;
                        else if (absDiff <= 20) cs.TheyExpensive_15_20++;
                        else if (absDiff <= 25) cs.TheyExpensive_20_25++;
                        else if (absDiff <= 30) cs.TheyExpensive_25_30++;
                        else if (absDiff <= 35) cs.TheyExpensive_30_35++;
                        else if (absDiff <= 40) cs.TheyExpensive_35_40++;
                        else if (absDiff <= 45) cs.TheyExpensive_40_45++;
                        else if (absDiff <= 50) cs.TheyExpensive_45_50++;
                        else cs.TheyExpensive_50plus++;
                    }
                }
            }

            foreach (var cs in competitorDict.Values)
            {
                if (cs.AllDiffs.Any())
                {
                    cs.AvgDiffPercent = Math.Round(cs.AllDiffs.Average(), 2);
                    var sorted = cs.AllDiffs.OrderBy(x => x).ToList();
                    int n = sorted.Count;
                    cs.MedianDiffPercent = n % 2 == 0
                        ? Math.Round((sorted[n / 2 - 1] + sorted[n / 2]) / 2m, 2)
                        : sorted[n / 2];
                }
            }

            var competitors = competitorDict.Values
                .OrderByDescending(x => x.OverlapCount)
                .ToList();

            var brands = brandStats
                .Select(kvp =>
                {
                    var items = kvp.Value;
                    int count = items.Count;
                    decimal avgOur = Math.Round(items.Average(x => x.myPrice), 2);
                    decimal avgMarket = Math.Round(items.Average(x => x.bestCompPrice), 2);
                    decimal idx = avgMarket > 0 ? Math.Round((avgOur / avgMarket) * 100, 2) : 100;
                    int cheapest = items.Count(x => x.isCheapest);
                    int expensive = items.Count(x => x.isMostExpensive);

                    return new BrandSummary
                    {
                        BrandName = kvp.Key,
                        ProductCount = count,
                        AvgOurPrice = avgOur,
                        AvgMarketPrice = avgMarket,
                        PriceIndexPercent = idx,
                        WeAreCheapestCount = cheapest,
                        WeAreCheapestPercent = count > 0 ? Math.Round((decimal)cheapest / count * 100, 1) : 0,
                        WeAreMostExpensiveCount = expensive,
                        WeAreMostExpensivePercent = count > 0 ? Math.Round((decimal)expensive / count * 100, 1) : 0
                    };
                })
                .OrderByDescending(x => x.ProductCount)
                .ToList();

            return (competitors, brands);
        }

        // ====================================================================
        // ARKUSZE KONKURENCJI (współdzielone)
        // ====================================================================

        private void WriteCompetitionOverviewSheet(XSSFWorkbook wb, string sheetName,
            List<CompetitorSummary> data, string scrapDate, string storeName, string presetName, ExportStyles s)
        {
            var sheet = wb.CreateSheet(sheetName);

            int r = 0;
            var infoRow = sheet.CreateRow(r++);
            var infoCell = infoRow.CreateCell(0);
            infoCell.SetCellValue($"Raport Konkurencji — {storeName} — Analiza: {scrapDate} — Preset: {presetName ?? "Domyślny"}");
            infoCell.CellStyle = s.InfoHeader;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 9));

            r++;

            var headerRow = sheet.CreateRow(r++);
            string[] headers = {
                "Sklep", "Wspólne produkty",
                "Tańsi od nas (szt.)", "Tańsi od nas (%)",
                "Drożsi od nas (szt.)", "Drożsi od nas (%)",
                "Równa cena (szt.)",
                "Śr. różnica (%)", "Mediana różnicy (%)",
                "Pozycja cenowa"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = headerRow.CreateCell(i);
                cell.SetCellValue(headers[i]);
                cell.CellStyle = s.HeaderDark;
            }

            foreach (var comp in data)
            {
                var row = sheet.CreateRow(r++);
                int c = 0;

                row.CreateCell(c++).SetCellValue(comp.StoreName);
                row.CreateCell(c++).SetCellValue(comp.OverlapCount);

                SetIntCell(row.CreateCell(c++), comp.TheyCheaperCount, comp.TheyCheaperCount > comp.TheyMoreExpensiveCount ? s.CellRedBg : s.Default);
                SetPercentValueCell(row.CreateCell(c++), comp.OverlapCount > 0 ? Math.Round((decimal)comp.TheyCheaperCount / comp.OverlapCount * 100, 1) : 0, s.Percent);

                SetIntCell(row.CreateCell(c++), comp.TheyMoreExpensiveCount, comp.TheyMoreExpensiveCount > comp.TheyCheaperCount ? s.CellGreenBg : s.Default);
                SetPercentValueCell(row.CreateCell(c++), comp.OverlapCount > 0 ? Math.Round((decimal)comp.TheyMoreExpensiveCount / comp.OverlapCount * 100, 1) : 0, s.Percent);

                row.CreateCell(c++).SetCellValue(comp.EqualCount);

                var avgCell = row.CreateCell(c++);
                avgCell.SetCellValue((double)comp.AvgDiffPercent);
                avgCell.CellStyle = comp.AvgDiffPercent > 0 ? s.PercentRed : s.PercentGreen;

                var medCell = row.CreateCell(c++);
                medCell.SetCellValue((double)comp.MedianDiffPercent);
                medCell.CellStyle = comp.MedianDiffPercent > 0 ? s.PercentRed : s.PercentGreen;

                string position;
                if (comp.TheyCheaperCount > comp.TheyMoreExpensiveCount * 2) position = "Znacznie tańszy";
                else if (comp.TheyCheaperCount > comp.TheyMoreExpensiveCount) position = "Tańszy";
                else if (comp.TheyMoreExpensiveCount > comp.TheyCheaperCount * 2) position = "Znacznie droższy";
                else if (comp.TheyMoreExpensiveCount > comp.TheyCheaperCount) position = "Droższy";
                else position = "Porównywalny";

                row.CreateCell(c++).SetCellValue(position);
            }

            for (int i = 0; i < headers.Length; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
        }

        private void WriteCompetitionDistributionSheet(XSSFWorkbook wb, string sheetName,
            List<CompetitorSummary> data, string scrapDate, ExportStyles s)
        {
            var sheet = wb.CreateSheet(sheetName);
            int r = 0;

            var infoRow = sheet.CreateRow(r++);
            var infoCell = infoRow.CreateCell(0);
            infoCell.SetCellValue($"Rozkład różnic cenowych — Analiza: {scrapDate}");
            infoCell.CellStyle = s.InfoHeader;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 24));

            r++;
            var subRow = sheet.CreateRow(r++);

            var subCell1 = subRow.CreateCell(1);
            subCell1.SetCellValue("← KONKURENT TAŃSZY (masz wyższe ceny)");
            subCell1.CellStyle = s.SubHeaderRed;
            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 1, 11));

            var subCellEq = subRow.CreateCell(12);
            subCellEq.SetCellValue("REMIS");
            subCellEq.CellStyle = s.SubHeaderBlue;

            var subCell2 = subRow.CreateCell(13);
            subCell2.SetCellValue("KONKURENT DROŻSZY (masz niższe ceny) →");
            subCell2.CellStyle = s.SubHeaderGreen;
            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 13, 23));

            var headerRow = sheet.CreateRow(r++);
            string[] cols = {
                "Sklep",
                ">50%", "45-50%", "40-45%", "35-40%", "30-35%", "25-30%", "20-25%", "15-20%", "10-15%", "5-10%", "0-5%",
                "0%",
                "0-5%", "5-10%", "10-15%", "15-20%", "20-25%", "25-30%", "30-35%", "35-40%", "40-45%", "45-50%", ">50%",
                "Wspólne"
            };

            for (int i = 0; i < cols.Length; i++)
            {
                var cell = headerRow.CreateCell(i);
                cell.SetCellValue(cols[i]);
                cell.CellStyle = s.HeaderDark;
            }

            foreach (var comp in data)
            {
                var row = sheet.CreateRow(r++);
                int c = 0;

                row.CreateCell(c++).SetCellValue(comp.StoreName);

                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_50plus, s.CellRed11);
                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_45_50, s.CellRed10);
                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_40_45, s.CellRed9);
                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_35_40, s.CellRed8);
                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_30_35, s.CellRed7);
                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_25_30, s.CellRed6);
                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_20_25, s.CellRed5);
                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_15_20, s.CellRed4);
                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_10_15, s.CellRed3);
                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_5_10, s.CellRed2);
                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_0_5, s.CellRed1);

                SetDistCell(row.CreateCell(c++), comp.EqualCount, s.CellBlue);

                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_0_5, s.CellGreen1);
                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_5_10, s.CellGreen2);
                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_10_15, s.CellGreen3);
                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_15_20, s.CellGreen4);
                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_20_25, s.CellGreen5);
                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_25_30, s.CellGreen6);
                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_30_35, s.CellGreen7);
                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_35_40, s.CellGreen8);
                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_40_45, s.CellGreen9);
                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_45_50, s.CellGreen10);
                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_50plus, s.CellGreen11);

                row.CreateCell(c++).SetCellValue(comp.OverlapCount);
            }

            for (int i = 0; i < cols.Length; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
        }

        private void WriteBrandAnalysisSheet(XSSFWorkbook wb, string sheetName,
            List<BrandSummary> data, string scrapDate, ExportStyles s)
        {
            var sheet = wb.CreateSheet(sheetName);
            int r = 0;

            var infoRow = sheet.CreateRow(r++);
            var infoCell = infoRow.CreateCell(0);
            infoCell.SetCellValue($"Analiza pozycji cenowej wg marek — Analiza: {scrapDate}");
            infoCell.CellStyle = s.InfoHeader;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 8));

            r++;
            var headerRow = sheet.CreateRow(r++);
            string[] headers = {
                "Marka", "Produkty (szt.)",
                "Śr. nasza cena", "Śr. cena rynku",
                "Indeks cenowy (%)",
                "Najtańsi (szt.)", "Najtańsi (%)",
                "Najdrożsi (szt.)", "Najdrożsi (%)"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                var cell = headerRow.CreateCell(i);
                cell.SetCellValue(headers[i]);
                cell.CellStyle = s.HeaderDark;
            }

            foreach (var brand in data)
            {
                var row = sheet.CreateRow(r++);
                int c = 0;

                row.CreateCell(c++).SetCellValue(brand.BrandName);
                row.CreateCell(c++).SetCellValue(brand.ProductCount);

                SetDecimalCell(row.CreateCell(c++), brand.AvgOurPrice, s.Currency);
                SetDecimalCell(row.CreateCell(c++), brand.AvgMarketPrice, s.Currency);

                var idxCell = row.CreateCell(c++);
                idxCell.SetCellValue((double)brand.PriceIndexPercent);
                idxCell.CellStyle = brand.PriceIndexPercent <= 100 ? s.PercentGreen : s.PercentRed;

                SetIntCell(row.CreateCell(c++), brand.WeAreCheapestCount, brand.WeAreCheapestPercent > 50 ? s.CellGreenBg : s.Default);
                SetPercentValueCell(row.CreateCell(c++), brand.WeAreCheapestPercent, s.Percent);

                SetIntCell(row.CreateCell(c++), brand.WeAreMostExpensiveCount, brand.WeAreMostExpensivePercent > 50 ? s.CellRedBg : s.Default);
                SetPercentValueCell(row.CreateCell(c++), brand.WeAreMostExpensivePercent, s.Percent);
            }

            for (int i = 0; i < headers.Length; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
        }

        // ====================================================================
        // STYLE EXCELA
        // ====================================================================

        private class ExportStyles
        {
            public ICellStyle Header { get; set; }
            public ICellStyle HeaderDark { get; set; }
            public ICellStyle InfoHeader { get; set; }
            public ICellStyle SubHeaderRed { get; set; }
            public ICellStyle SubHeaderGreen { get; set; }
            public ICellStyle Currency { get; set; }
            public ICellStyle Percent { get; set; }
            public ICellStyle PercentRed { get; set; }
            public ICellStyle PercentGreen { get; set; }
            public ICellStyle PriceGreen { get; set; }
            public ICellStyle PriceLightGreen { get; set; }
            public ICellStyle PriceRed { get; set; }
            public ICellStyle Default { get; set; }
            public ICellStyle CellRedBg { get; set; }
            public ICellStyle CellGreenBg { get; set; }
            public ICellStyle CellRed1 { get; set; }
            public ICellStyle CellRed2 { get; set; }
            public ICellStyle CellRed3 { get; set; }
            public ICellStyle CellRed4 { get; set; }
            public ICellStyle CellRed5 { get; set; }
            public ICellStyle CellRed6 { get; set; }
            public ICellStyle CellRed7 { get; set; }
            public ICellStyle CellRed8 { get; set; }
            public ICellStyle CellRed9 { get; set; }
            public ICellStyle CellRed10 { get; set; }
            public ICellStyle CellRed11 { get; set; }
            public ICellStyle CellGreen1 { get; set; }
            public ICellStyle CellGreen2 { get; set; }
            public ICellStyle CellGreen3 { get; set; }
            public ICellStyle CellGreen4 { get; set; }
            public ICellStyle CellGreen5 { get; set; }
            public ICellStyle CellGreen6 { get; set; }
            public ICellStyle CellGreen7 { get; set; }
            public ICellStyle CellGreen8 { get; set; }
            public ICellStyle CellGreen9 { get; set; }
            public ICellStyle CellGreen10 { get; set; }
            public ICellStyle CellGreen11 { get; set; }
            public ICellStyle SubHeaderBlue { get; set; }
            public ICellStyle CellBlue { get; set; }
        }

        private ExportStyles CreateExportStyles(XSSFWorkbook wb)
        {
            var s = new ExportStyles();
            var df = wb.CreateDataFormat();

            s.Default = wb.CreateCellStyle();

            s.Header = wb.CreateCellStyle();
            var hf = wb.CreateFont(); hf.IsBold = true; s.Header.SetFont(hf);

            s.HeaderDark = CreateColoredStyle(wb, new byte[] { 26, 39, 68 }, true, IndexedColors.White.Index);

            s.InfoHeader = wb.CreateCellStyle();
            var infoFont = wb.CreateFont(); infoFont.IsBold = true; infoFont.FontHeightInPoints = 12;
            s.InfoHeader.SetFont(infoFont);

            s.SubHeaderRed = CreateColoredStyle(wb, new byte[] { 220, 53, 69 }, true, IndexedColors.White.Index);
            s.SubHeaderGreen = CreateColoredStyle(wb, new byte[] { 40, 167, 69 }, true, IndexedColors.White.Index);

            s.Currency = wb.CreateCellStyle();
            s.Currency.DataFormat = df.GetFormat("#,##0.00");

            s.Percent = wb.CreateCellStyle();
            s.Percent.DataFormat = df.GetFormat("0.00");

            s.PercentRed = wb.CreateCellStyle();
            s.PercentRed.DataFormat = df.GetFormat("0.00");
            var redFont = wb.CreateFont(); redFont.Color = IndexedColors.Red.Index; redFont.IsBold = true;
            s.PercentRed.SetFont(redFont);

            s.PercentGreen = wb.CreateCellStyle();
            s.PercentGreen.DataFormat = df.GetFormat("0.00");
            var greenFont = wb.CreateFont(); greenFont.Color = IndexedColors.Green.Index; greenFont.IsBold = true;
            s.PercentGreen.SetFont(greenFont);

            s.PriceGreen = wb.CreateCellStyle(); s.PriceGreen.CloneStyleFrom(s.Currency);
            s.PriceGreen.FillForegroundColor = IndexedColors.LightGreen.Index;
            s.PriceGreen.FillPattern = FillPattern.SolidForeground;

            s.PriceLightGreen = wb.CreateCellStyle(); s.PriceLightGreen.CloneStyleFrom(s.Currency);
            s.PriceLightGreen.FillForegroundColor = IndexedColors.LemonChiffon.Index;
            s.PriceLightGreen.FillPattern = FillPattern.SolidForeground;

            s.PriceRed = wb.CreateCellStyle(); s.PriceRed.CloneStyleFrom(s.Currency);
            s.PriceRed.FillForegroundColor = IndexedColors.Rose.Index;
            s.PriceRed.FillPattern = FillPattern.SolidForeground;

            s.CellRedBg = CreateColoredStyle(wb, new byte[] { 248, 215, 218 }, false, 0);
            s.CellGreenBg = CreateColoredStyle(wb, new byte[] { 212, 237, 218 }, false, 0);

            s.CellRed1 = CreateColoredStyle(wb, new byte[] { 255, 240, 240 }, false, 0);
            s.CellRed2 = CreateColoredStyle(wb, new byte[] { 255, 220, 220 }, false, 0);
            s.CellRed3 = CreateColoredStyle(wb, new byte[] { 255, 200, 200 }, false, 0);
            s.CellRed4 = CreateColoredStyle(wb, new byte[] { 255, 180, 180 }, false, 0);
            s.CellRed5 = CreateColoredStyle(wb, new byte[] { 255, 160, 160 }, false, 0);
            s.CellRed6 = CreateColoredStyle(wb, new byte[] { 255, 140, 140 }, false, 0);
            s.CellRed7 = CreateColoredStyle(wb, new byte[] { 255, 115, 115 }, false, 0);
            s.CellRed8 = CreateColoredStyle(wb, new byte[] { 255, 90, 90 }, false, 0);
            s.CellRed9 = CreateColoredStyle(wb, new byte[] { 240, 60, 60 }, false, IndexedColors.White.Index);
            s.CellRed10 = CreateColoredStyle(wb, new byte[] { 220, 40, 40 }, false, IndexedColors.White.Index);
            s.CellRed11 = CreateColoredStyle(wb, new byte[] { 190, 20, 20 }, false, IndexedColors.White.Index);

            s.CellGreen1 = CreateColoredStyle(wb, new byte[] { 240, 255, 240 }, false, 0);
            s.CellGreen2 = CreateColoredStyle(wb, new byte[] { 220, 250, 220 }, false, 0);
            s.CellGreen3 = CreateColoredStyle(wb, new byte[] { 200, 240, 200 }, false, 0);
            s.CellGreen4 = CreateColoredStyle(wb, new byte[] { 180, 230, 180 }, false, 0);
            s.CellGreen5 = CreateColoredStyle(wb, new byte[] { 160, 220, 160 }, false, 0);
            s.CellGreen6 = CreateColoredStyle(wb, new byte[] { 140, 210, 140 }, false, 0);
            s.CellGreen7 = CreateColoredStyle(wb, new byte[] { 120, 200, 120 }, false, 0);
            s.CellGreen8 = CreateColoredStyle(wb, new byte[] { 100, 185, 100 }, false, 0);
            s.CellGreen9 = CreateColoredStyle(wb, new byte[] { 75, 170, 75 }, false, IndexedColors.White.Index);
            s.CellGreen10 = CreateColoredStyle(wb, new byte[] { 50, 150, 50 }, false, IndexedColors.White.Index);
            s.CellGreen11 = CreateColoredStyle(wb, new byte[] { 30, 130, 30 }, false, IndexedColors.White.Index);

            s.SubHeaderBlue = CreateColoredStyle(wb, new byte[] { 100, 180, 255 }, true, IndexedColors.White.Index);
            s.CellBlue = CreateColoredStyle(wb, new byte[] { 230, 245, 255 }, false, 0);

            return s;
        }

        private ICellStyle CreateColoredStyle(XSSFWorkbook wb, byte[] rgb, bool bold, short fontColorIndex)
        {
            var style = (XSSFCellStyle)wb.CreateCellStyle();
            var ctColor = new NPOI.OpenXmlFormats.Spreadsheet.CT_Color { rgb = rgb };
            style.SetFillForegroundColor(new XSSFColor(ctColor));
            style.FillPattern = FillPattern.SolidForeground;

            if (bold || fontColorIndex > 0)
            {
                var font = wb.CreateFont();
                if (bold) font.IsBold = true;
                if (fontColorIndex > 0) font.Color = fontColorIndex;
                style.SetFont(font);
            }

            return style;
        }

        // ====================================================================
        // METODY POMOCNICZE
        // ====================================================================

        private async Task SendExportProgress(string connectionId, object progress)
        {
            if (string.IsNullOrEmpty(connectionId)) return;
            try
            {
                await _hubContext.Clients.Client(connectionId).SendAsync("ExportProgress", progress);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nie udało się wysłać postępu eksportu.");
            }
        }

        private static void SetDecimalCell(ICell cell, decimal? value, ICellStyle style)
        {
            if (value.HasValue)
            {
                cell.SetCellValue((double)value.Value);
                cell.CellStyle = style;
            }
        }

        private static void SetPercentValueCell(ICell cell, decimal value, ICellStyle style)
        {
            cell.SetCellValue((double)value);
            cell.CellStyle = style;
        }

        private static void SetIntCell(ICell cell, int value, ICellStyle style)
        {
            cell.SetCellValue(value);
            cell.CellStyle = style;
        }

        private static void SetDistCell(ICell cell, int value, ICellStyle style)
        {
            if (value > 0)
            {
                cell.SetCellValue(value);
                cell.CellStyle = style;
            }
            else
            {
                cell.SetCellValue(0);
            }
        }

        private static string StockText(bool? inStock)
        {
            if (inStock == true) return "Dostępny";
            if (inStock == false) return "Niedostępny";
            return "Brak danych";
        }
    }
}