//using Microsoft.AspNetCore.SignalR;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.IdentityModel.Tokens;
//using NPOI.OOXML.XSSF.UserModel;
//using NPOI.SS.UserModel;
//using NPOI.SS.Util;
//using NPOI.XSSF.UserModel;
//using PriceSafari.Data;
//using PriceSafari.Hubs;
//using PriceSafari.Models;
//using System.IO.Compression;

//namespace PriceSafari.VSA.MassExporter
//{
//    public class MassExportService : IMassExportService
//    {
//        private readonly PriceSafariContext _context;
//        private readonly IHubContext<ScrapingHub> _hubContext;
//        private readonly ILogger<MassExportService> _logger;

//        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTime> _exportCooldowns = new();
//        private static readonly TimeSpan ExportCooldown = TimeSpan.FromMinutes(5);

//        public MassExportService(PriceSafariContext context, IHubContext<ScrapingHub> hubContext, ILogger<MassExportService> logger)
//        {
//            _context = context;
//            _hubContext = hubContext;
//            _logger = logger;
//        }

//        // ====================================================================
//        // GET AVAILABLE SCRAPS (comparison + marketplace)
//        // ====================================================================

//        public async Task<object> GetAvailableScrapsAsync(int storeId, string userId, string sourceType = "comparison")
//        {
//            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
//            if (!hasAccess)
//                throw new UnauthorizedAccessException("Brak dostępu do tego sklepu.");

//            if (sourceType == "marketplace")
//                return await GetAvailableAllegroScrapsAsync(storeId);

//            return await GetAvailableComparisonScrapsAsync(storeId);
//        }

//        private async Task<object> GetAvailableComparisonScrapsAsync(int storeId)
//        {
//            var scraps = await _context.ScrapHistories
//                .AsNoTracking()
//                .Where(sh => sh.StoreId == storeId)
//                .OrderByDescending(sh => sh.Date)
//                .Take(360)
//                .Select(sh => new { sh.Id, sh.Date })
//                .ToListAsync();

//            if (!scraps.Any())
//                return new List<object>();

//            var scrapIds = scraps.Select(s => s.Id).ToList();

//            var priceCounts = await _context.PriceHistories
//                .AsNoTracking()
//                .Where(ph => scrapIds.Contains(ph.ScrapHistoryId))
//                .GroupBy(ph => ph.ScrapHistoryId)
//                .Select(g => new { ScrapId = g.Key, Count = g.Count() })
//                .ToDictionaryAsync(x => x.ScrapId, x => x.Count);

//            return scraps.Select(s => new
//            {
//                id = s.Id,
//                date = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
//                priceCount = priceCounts.GetValueOrDefault(s.Id, 0)
//            }).ToList();
//        }

//        private async Task<object> GetAvailableAllegroScrapsAsync(int storeId)
//        {
//            var scraps = await _context.AllegroScrapeHistories
//                .AsNoTracking()
//                .Where(sh => sh.StoreId == storeId)
//                .OrderByDescending(sh => sh.Date)
//                .Take(360)
//                .Select(sh => new { sh.Id, sh.Date })
//                .ToListAsync();

//            if (!scraps.Any())
//                return new List<object>();

//            var scrapIds = scraps.Select(s => s.Id).ToList();

//            var priceCounts = await _context.AllegroPriceHistories
//                .AsNoTracking()
//                .Where(ph => scrapIds.Contains(ph.AllegroScrapeHistoryId))
//                .GroupBy(ph => ph.AllegroScrapeHistoryId)
//                .Select(g => new { ScrapId = g.Key, Count = g.Count() })
//                .ToDictionaryAsync(x => x.ScrapId, x => x.Count);

//            return scraps.Select(s => new
//            {
//                id = s.Id,
//                date = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
//                priceCount = priceCounts.GetValueOrDefault(s.Id, 0)
//            }).ToList();
//        }

//        // ====================================================================
//        // GŁÓWNA METODA GENEROWANIA EKSPORTU
//        // ====================================================================

//        public async Task<(byte[] FileContent, string FileName, string ContentType)> GenerateExportAsync(
//            int storeId, ExportMultiRequest request, string userId)
//        {
//            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
//            if (!hasAccess)
//                throw new UnauthorizedAccessException("Brak dostępu do tego sklepu.");

//            var now = DateTime.UtcNow;
//            if (_exportCooldowns.TryGetValue(storeId, out var lastExport))
//            {
//                var remaining = ExportCooldown - (now - lastExport);
//                if (remaining > TimeSpan.Zero)
//                {
//                    var secondsLeft = (int)Math.Ceiling(remaining.TotalSeconds);
//                    string timeStr = secondsLeft > 60 ? $"{(int)Math.Ceiling(remaining.TotalMinutes)} min" : $"{secondsLeft} sek";
//                    throw new InvalidOperationException($"Eksport będzie dostępny za {timeStr}.");
//                }
//            }
//            _exportCooldowns[storeId] = now;

//            var sourceType = request.SourceType ?? "comparison";

//            if (sourceType == "marketplace")
//                return await GenerateAllegroExportAsync(storeId, request);

//            return await GenerateComparisonExportAsync(storeId, request);
//        }

//        // ====================================================================
//        // COMPARISON EXPORT (Ceneo / Google) — istniejąca logika
//        // ====================================================================

//        private async Task<(byte[] FileContent, string FileName, string ContentType)> GenerateComparisonExportAsync(
//            int storeId, ExportMultiRequest request)
//        {
//            var connectionId = request.ConnectionId;
//            var exportType = request.ExportType ?? "prices";

//            var storeName = await _context.Stores.AsNoTracking()
//                .Where(s => s.StoreId == storeId).Select(s => s.StoreName).FirstOrDefaultAsync();
//            var myStoreNameLower = storeName?.ToLower().Trim() ?? "";

//            var priceValues = await _context.PriceValues.AsNoTracking()
//                .Where(pv => pv.StoreId == storeId)
//                .Select(pv => new { pv.UsePriceWithDelivery })
//                .FirstOrDefaultAsync() ?? new { UsePriceWithDelivery = false };

//            var activePreset = await _context.CompetitorPresets.AsNoTracking()
//                .Include(x => x.CompetitorItems)
//                .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse && cp.Type == PresetType.PriceComparison);

//            Dictionary<(string Store, DataSourceType Source), bool> competitorItemsDict = null;
//            if (activePreset?.Type == PresetType.PriceComparison)
//            {
//                competitorItemsDict = activePreset.CompetitorItems.ToDictionary(
//                    ci => (Store: ci.StoreName.ToLower().Trim(), Source: ci.DataSource),
//                    ci => ci.UseCompetitor);
//            }

//            var scraps = await _context.ScrapHistories.AsNoTracking()
//                .Where(sh => request.ScrapIds.Contains(sh.Id) && sh.StoreId == storeId)
//                .OrderBy(sh => sh.Date)
//                .ToListAsync();

//            if (!scraps.Any())
//                throw new InvalidOperationException("Nie znaleziono wybranych analiz.");

//            using var workbook = new XSSFWorkbook();
//            var styles = CreateExportStyles(workbook);

//            int totalScraps = scraps.Count;
//            int processedScraps = 0;
//            int grandTotalPrices = 0;

//            foreach (var scrap in scraps)
//            {
//                var scrapDateStr = scrap.Date.ToString("dd.MM.yyyy");
//                var scrapDateShort = scrap.Date.ToString("dd.MM");

//                await SendExportProgress(connectionId, new
//                {
//                    step = "processing",
//                    currentIndex = processedScraps + 1,
//                    totalScraps,
//                    scrapDate = scrapDateStr,
//                    priceCount = 0,
//                    grandTotalPrices,
//                    percentComplete = (int)((double)processedScraps / totalScraps * 95)
//                });

//                var rawData = await LoadRawExportData(scrap.Id, storeId, myStoreNameLower,
//                    priceValues.UsePriceWithDelivery, activePreset, competitorItemsDict);

//                grandTotalPrices += rawData.Count;

//                await SendExportProgress(connectionId, new
//                {
//                    step = "writing",
//                    currentIndex = processedScraps + 1,
//                    totalScraps,
//                    scrapDate = scrapDateStr,
//                    priceCount = rawData.Count,
//                    grandTotalPrices,
//                    percentComplete = (int)(((double)processedScraps + 0.5) / totalScraps * 95)
//                });

//                if (exportType == "competition")
//                {
//                    var suffix = totalScraps > 1 ? $" {scrapDateShort}" : "";
//                    var (competitors, brands) = BuildCompetitionData(rawData, myStoreNameLower);

//                    WriteCompetitionOverviewSheet(workbook, $"Przegląd{suffix}", competitors, scrapDateStr, storeName, activePreset?.PresetName, styles);
//                    WriteCompetitionDistributionSheet(workbook, $"Rozkład{suffix}", competitors, scrapDateStr, styles);
//                    WriteBrandAnalysisSheet(workbook, $"Marki{suffix}", brands, scrapDateStr, styles);
//                }
//                else
//                {
//                    var exportRows = BuildPriceExportRows(rawData, myStoreNameLower);
//                    var sheetName = scrapDateStr;
//                    var sheet = workbook.CreateSheet(sheetName);
//                    WritePriceExportSheet(sheet, exportRows, styles);
//                }

//                processedScraps++;
//                await SendExportProgress(connectionId, new
//                {
//                    step = "processing",
//                    currentIndex = processedScraps,
//                    totalScraps,
//                    scrapDate = scrapDateStr,
//                    priceCount = rawData.Count,
//                    grandTotalPrices,
//                    percentComplete = (int)((double)processedScraps / totalScraps * 95)
//                });
//            }

//            await SendExportProgress(connectionId, new
//            {
//                step = "finalizing",
//                currentIndex = totalScraps,
//                totalScraps,
//                scrapDate = "",
//                priceCount = 0,
//                grandTotalPrices,
//                percentComplete = 100
//            });

//            using var stream = new MemoryStream();
//            workbook.Write(stream);
//            var content = stream.ToArray();

//            var dateRange = scraps.Count == 1
//                ? scraps[0].Date.ToString("yyyy-MM-dd")
//                : $"{scraps.First().Date:yyyy-MM-dd}_do_{scraps.Last().Date:yyyy-MM-dd}";

//            var typeLabel = exportType == "competition" ? "Konkurencja" : "Analiza";
//            var fileName = $"{typeLabel}_{storeName}_{dateRange}.xlsx";
//            var contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

//            return (content, fileName, contentType);
//        }

//        // ====================================================================
//        // ALLEGRO EXPORT (Marketplace)
//        // ====================================================================

//        private async Task<(byte[] FileContent, string FileName, string ContentType)> GenerateAllegroExportAsync(
//            int storeId, ExportMultiRequest request)
//        {
//            var connectionId = request.ConnectionId;
//            var exportType = request.ExportType ?? "prices";

//            var store = await _context.Stores.AsNoTracking()
//                .Where(s => s.StoreId == storeId)
//                .Select(s => new { s.StoreName, s.StoreNameAllegro })
//                .FirstOrDefaultAsync();

//            var storeName = store?.StoreName ?? "";
//            var storeNameAllegro = store?.StoreNameAllegro ?? "";
//            var myStoreNameLower = storeNameAllegro.ToLower().Trim();

//            var activePreset = await _context.CompetitorPresets.AsNoTracking()
//                .Include(x => x.CompetitorItems)
//                .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse && cp.Type == PresetType.Marketplace);

//            Dictionary<string, bool> competitorRules = null;
//            if (activePreset != null)
//            {
//                competitorRules = activePreset.CompetitorItems
//                    .Where(ci => ci.DataSource == DataSourceType.Allegro)
//                    .ToDictionary(ci => ci.StoreName.ToLower().Trim(), ci => ci.UseCompetitor);
//            }

//            var scraps = await _context.AllegroScrapeHistories.AsNoTracking()
//                .Where(sh => request.ScrapIds.Contains(sh.Id) && sh.StoreId == storeId)
//                .OrderBy(sh => sh.Date)
//                .ToListAsync();

//            if (!scraps.Any())
//                throw new InvalidOperationException("Nie znaleziono wybranych analiz.");

//            using var workbook = new XSSFWorkbook();
//            var styles = CreateExportStyles(workbook);

//            int totalScraps = scraps.Count;
//            int processedScraps = 0;
//            int grandTotalPrices = 0;

//            foreach (var scrap in scraps)
//            {
//                var scrapDateStr = scrap.Date.ToString("dd.MM.yyyy");
//                var scrapDateShort = scrap.Date.ToString("dd.MM");

//                await SendExportProgress(connectionId, new
//                {
//                    step = "processing",
//                    currentIndex = processedScraps + 1,
//                    totalScraps,
//                    scrapDate = scrapDateStr,
//                    priceCount = 0,
//                    grandTotalPrices,
//                    percentComplete = (int)((double)processedScraps / totalScraps * 95)
//                });

//                var rawData = await LoadRawAllegroExportData(scrap.Id, storeId, storeNameAllegro, activePreset, competitorRules);

//                grandTotalPrices += rawData.Count;

//                await SendExportProgress(connectionId, new
//                {
//                    step = "writing",
//                    currentIndex = processedScraps + 1,
//                    totalScraps,
//                    scrapDate = scrapDateStr,
//                    priceCount = rawData.Count,
//                    grandTotalPrices,
//                    percentComplete = (int)(((double)processedScraps + 0.5) / totalScraps * 95)
//                });

//                if (exportType == "competition")
//                {
//                    var suffix = totalScraps > 1 ? $" {scrapDateShort}" : "";

//                    // Deduplikacja przed raportem konkurencji:
//                    // Grupujemy po EAN (priorytet) lub AllegroProductId, potem:
//                    // - nasza oferta: najtańsza (priorytet targetOfferId)
//                    // - konkurent: najtańsza per SellerName
//                    var dedupedForCompetition = rawData
//                        .GroupBy(x => !string.IsNullOrWhiteSpace(x.Ean)
//                            ? $"ean:{x.Ean.Trim()}"
//                            : $"pid:{x.AllegroProductId}")
//                        .SelectMany(g =>
//                        {
//                            var items = g.ToList();
//                            var result = new List<RawAllegroExportEntry>();

//                            // Moja oferta: priorytet targetOfferId, potem najtańsza
//                            var myItems = items.Where(x => x.IsMe && x.Price > 0).ToList();
//                            if (myItems.Any())
//                            {
//                                RawAllegroExportEntry myBest = null;
//                                foreach (var entry in myItems)
//                                {
//                                    if (long.TryParse(entry.IdOnAllegro, out var tid) && entry.IdAllegro == tid)
//                                    { myBest = entry; break; }
//                                }
//                                result.Add(myBest ?? myItems.OrderBy(x => x.Price).First());
//                            }
//                            else
//                            {
//                                var myZero = items.FirstOrDefault(x => x.IsMe);
//                                if (myZero != null) result.Add(myZero);
//                            }

//                            // Konkurenci: najtańsza oferta per sprzedawca
//                            var compDeduped = items
//                                .Where(x => !x.IsMe && x.Price > 0)
//                                .GroupBy(x => x.SellerName.ToLower().Trim())
//                                .Select(sg => sg.OrderBy(x => x.Price).First());
//                            result.AddRange(compDeduped);

//                            return result;
//                        })
//                        .ToList();

//                    var mappedForCompetition = dedupedForCompetition.Select(x => new RawExportEntry
//                    {
//                        ProductName = x.ProductName,
//                        Producer = x.Producer,
//                        Ean = x.Ean,
//                        CatalogNumber = x.AllegroSku,
//                        ExternalId = null,
//                        MarginPrice = x.MarginPrice,
//                        Price = x.Price,
//                        StoreName = x.SellerName,
//                        IsGoogle = false,
//                        ShippingCostNum = null,
//                        CeneoInStock = null,
//                        GoogleInStock = null,
//                        IsMe = x.IsMe,
//                        FinalPrice = x.Price
//                    }).ToList();

//                    var (competitors, brands) = BuildCompetitionData(mappedForCompetition, myStoreNameLower);

//                    WriteCompetitionOverviewSheet(workbook, $"Przegląd{suffix}", competitors, scrapDateStr, storeName, activePreset?.PresetName, styles);
//                    WriteCompetitionDistributionSheet(workbook, $"Rozkład{suffix}", competitors, scrapDateStr, styles);
//                    WriteBrandAnalysisSheet(workbook, $"Marki{suffix}", brands, scrapDateStr, styles);
//                }
//                else
//                {
//                    var exportRows = BuildAllegroPriceExportRows(rawData, myStoreNameLower);
//                    var sheetName = scrapDateStr;
//                    var sheet = workbook.CreateSheet(sheetName);
//                    WriteAllegroPriceExportSheet(sheet, exportRows, styles);
//                }

//                processedScraps++;
//                await SendExportProgress(connectionId, new
//                {
//                    step = "processing",
//                    currentIndex = processedScraps,
//                    totalScraps,
//                    scrapDate = scrapDateStr,
//                    priceCount = rawData.Count,
//                    grandTotalPrices,
//                    percentComplete = (int)((double)processedScraps / totalScraps * 95)
//                });
//            }

//            await SendExportProgress(connectionId, new
//            {
//                step = "finalizing",
//                currentIndex = totalScraps,
//                totalScraps,
//                scrapDate = "",
//                priceCount = 0,
//                grandTotalPrices,
//                percentComplete = 100
//            });

//            using var stream = new MemoryStream();
//            workbook.Write(stream);
//            var content = stream.ToArray();

//            var dateRange = scraps.Count == 1
//                ? scraps[0].Date.ToString("yyyy-MM-dd")
//                : $"{scraps.First().Date:yyyy-MM-dd}_do_{scraps.Last().Date:yyyy-MM-dd}";

//            var typeLabel = exportType == "competition" ? "Konkurencja_Allegro" : "Analiza_Allegro";
//            var fileName = $"{typeLabel}_{storeName}_{dateRange}.xlsx";
//            var contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

//            return (content, fileName, contentType);
//        }

//        // ====================================================================
//        // MODELE POMOCNICZE — Comparison (istniejące)
//        // ====================================================================

//        private class RawExportEntry
//        {
//            public string ProductName { get; set; }
//            public string Producer { get; set; }
//            public string Ean { get; set; }
//            public string CatalogNumber { get; set; }
//            public int? ExternalId { get; set; }
//            public decimal? MarginPrice { get; set; }
//            public decimal Price { get; set; }
//            public string StoreName { get; set; }
//            public bool IsGoogle { get; set; }
//            public decimal? ShippingCostNum { get; set; }
//            public bool? CeneoInStock { get; set; }
//            public bool? GoogleInStock { get; set; }
//            public bool IsMe { get; set; }
//            public decimal FinalPrice { get; set; }
//        }

//        private class ExportProductRow
//        {
//            public int? ExternalId { get; set; }
//            public string ProductName { get; set; }
//            public string Producer { get; set; }
//            public string Ean { get; set; }
//            public string CatalogNumber { get; set; }
//            public decimal? MarginPrice { get; set; }
//            public decimal? MyPrice { get; set; }
//            public decimal? BestCompetitorPrice { get; set; }
//            public string BestCompetitorStore { get; set; }
//            public decimal? DiffToLowest { get; set; }
//            public decimal? DiffToLowestPercent { get; set; }
//            public int TotalOffers { get; set; }
//            public int MyRank { get; set; }
//            public string PositionString { get; set; }
//            public int ColorCode { get; set; }
//            public bool? MyGoogleInStock { get; set; }
//            public bool? MyCeneoInStock { get; set; }
//            public bool? CompGoogleInStock { get; set; }
//            public bool? CompCeneoInStock { get; set; }
//            public List<ExportCompetitorOffer> Competitors { get; set; } = new();
//        }

//        private class ExportCompetitorOffer
//        {
//            public string Store { get; set; }
//            public decimal FinalPrice { get; set; }
//        }

//        // ====================================================================
//        // MODELE POMOCNICZE — Allegro (nowe)
//        // ====================================================================

//        private class RawAllegroExportEntry
//        {
//            public int AllegroProductId { get; set; }
//            public string ProductName { get; set; }
//            public string Producer { get; set; }
//            public string Ean { get; set; }
//            public string AllegroSku { get; set; }
//            public string IdOnAllegro { get; set; }
//            public decimal? MarginPrice { get; set; }
//            public decimal Price { get; set; }
//            public string SellerName { get; set; }
//            public long IdAllegro { get; set; }
//            public int? DeliveryTime { get; set; }
//            public int? Popularity { get; set; }
//            public bool SuperSeller { get; set; }
//            public bool Smart { get; set; }
//            public bool IsBestPriceGuarantee { get; set; }
//            public bool TopOffer { get; set; }
//            public bool SuperPrice { get; set; }
//            public bool IsMe { get; set; }
//        }

//        private class ExportAllegroProductRow
//        {
//            public int AllegroProductId { get; set; }
//            public string ProductName { get; set; }
//            public string Producer { get; set; }
//            public string Ean { get; set; }
//            public string AllegroSku { get; set; }
//            public decimal? MarginPrice { get; set; }
//            public decimal? MyPrice { get; set; }
//            public decimal? BestCompetitorPrice { get; set; }
//            public string BestCompetitorStore { get; set; }
//            public decimal? DiffToLowest { get; set; }
//            public decimal? DiffToLowestPercent { get; set; }
//            public int TotalOffers { get; set; }
//            public int MyRank { get; set; }
//            public string PositionString { get; set; }
//            public int ColorCode { get; set; }
//            public int? MyPopularity { get; set; }
//            public int TotalPopularity { get; set; }
//            public List<ExportCompetitorOffer> Competitors { get; set; } = new();
//        }

//        // ====================================================================
//        // MODELE POMOCNICZE — Competition (współdzielone)
//        // ====================================================================

//        private class CompetitorSummary
//        {
//            public string StoreName { get; set; }
//            public int OverlapCount { get; set; }
//            public int TheyCheaperCount { get; set; }
//            public int TheyMoreExpensiveCount { get; set; }
//            public int EqualCount { get; set; }
//            public decimal AvgDiffPercent { get; set; }
//            public decimal MedianDiffPercent { get; set; }
//            public List<decimal> AllDiffs { get; set; } = new();

//            public int TheyCheaper_0_5 { get; set; }
//            public int TheyCheaper_5_10 { get; set; }
//            public int TheyCheaper_10_15 { get; set; }
//            public int TheyCheaper_15_20 { get; set; }
//            public int TheyCheaper_20_25 { get; set; }
//            public int TheyCheaper_25_30 { get; set; }
//            public int TheyCheaper_30_35 { get; set; }
//            public int TheyCheaper_35_40 { get; set; }
//            public int TheyCheaper_40_45 { get; set; }
//            public int TheyCheaper_45_50 { get; set; }
//            public int TheyCheaper_50plus { get; set; }

//            public int TheyExpensive_0_5 { get; set; }
//            public int TheyExpensive_5_10 { get; set; }
//            public int TheyExpensive_10_15 { get; set; }
//            public int TheyExpensive_15_20 { get; set; }
//            public int TheyExpensive_20_25 { get; set; }
//            public int TheyExpensive_25_30 { get; set; }
//            public int TheyExpensive_30_35 { get; set; }
//            public int TheyExpensive_35_40 { get; set; }
//            public int TheyExpensive_40_45 { get; set; }
//            public int TheyExpensive_45_50 { get; set; }
//            public int TheyExpensive_50plus { get; set; }

//            public Dictionary<string, (int Cheaper, int Expensive, int Equal)> BrandBreakdown { get; set; } = new();
//        }

//        private class BrandSummary
//        {
//            public string BrandName { get; set; }
//            public int ProductCount { get; set; }
//            public decimal AvgOurPrice { get; set; }
//            public decimal AvgMarketPrice { get; set; }
//            public decimal PriceIndexPercent { get; set; }
//            public int WeAreCheapestCount { get; set; }
//            public decimal WeAreCheapestPercent { get; set; }
//            public int WeAreMostExpensiveCount { get; set; }
//            public decimal WeAreMostExpensivePercent { get; set; }
//        }

//        // ====================================================================
//        // ŁADOWANIE DANYCH — Comparison
//        // ====================================================================

//        private async Task<List<RawExportEntry>> LoadRawExportData(
//            int scrapId, int storeId, string myStoreNameLower,
//            bool usePriceWithDelivery,
//            CompetitorPresetClass activePreset,
//            Dictionary<(string Store, DataSourceType Source), bool> competitorItemsDict)
//        {
//            var query = from p in _context.Products.AsNoTracking()
//                        join ph in _context.PriceHistories.AsNoTracking()
//                            on p.ProductId equals ph.ProductId
//                        where p.StoreId == storeId && p.IsScrapable && ph.ScrapHistoryId == scrapId
//                        select new
//                        {
//                            p.ProductName,
//                            p.Producer,
//                            p.Ean,
//                            p.CatalogNumber,
//                            p.ExternalId,
//                            p.MarginPrice,
//                            ph.Price,
//                            ph.StoreName,
//                            ph.IsGoogle,
//                            ph.ShippingCostNum,
//                            ph.CeneoInStock,
//                            ph.GoogleInStock
//                        };

//            if (activePreset != null)
//            {
//                if (!activePreset.SourceGoogle) query = query.Where(x => x.IsGoogle != true);
//                if (!activePreset.SourceCeneo) query = query.Where(x => x.IsGoogle == true);
//            }

//            var rawList = await query.ToListAsync();

//            if (activePreset?.Type == PresetType.PriceComparison && competitorItemsDict != null)
//            {
//                rawList = rawList.Where(row =>
//                {
//                    if (row.StoreName != null && row.StoreName.ToLower().Trim() == myStoreNameLower)
//                        return true;
//                    DataSourceType src = row.IsGoogle == true ? DataSourceType.Google : DataSourceType.Ceneo;
//                    var key = (Store: (row.StoreName ?? "").ToLower().Trim(), Source: src);
//                    if (competitorItemsDict.TryGetValue(key, out bool use)) return use;
//                    return activePreset.UseUnmarkedStores;
//                }).ToList();
//            }

//            return rawList.Select(x =>
//            {
//                bool isMe = x.StoreName != null && x.StoreName.ToLower().Trim() == myStoreNameLower;
//                decimal finalPrice = (usePriceWithDelivery && x.ShippingCostNum.HasValue)
//                    ? x.Price + x.ShippingCostNum.Value : x.Price;

//                return new RawExportEntry
//                {
//                    ProductName = x.ProductName,
//                    Producer = x.Producer,
//                    Ean = x.Ean,
//                    CatalogNumber = x.CatalogNumber,
//                    ExternalId = x.ExternalId,
//                    MarginPrice = x.MarginPrice,
//                    Price = x.Price,
//                    StoreName = x.StoreName ?? (x.IsGoogle ? "Google" : "Ceneo"),
//                    IsGoogle = x.IsGoogle,
//                    ShippingCostNum = x.ShippingCostNum,
//                    CeneoInStock = x.CeneoInStock,
//                    GoogleInStock = x.GoogleInStock,
//                    IsMe = isMe,
//                    FinalPrice = finalPrice
//                };
//            }).ToList();
//        }

//        // ====================================================================
//        // ŁADOWANIE DANYCH — Allegro
//        // ====================================================================

//        private async Task<List<RawAllegroExportEntry>> LoadRawAllegroExportData(
//            int scrapId, int storeId, string storeNameAllegro,
//            CompetitorPresetClass activePreset,
//            Dictionary<string, bool> competitorRules)
//        {
//            var products = await _context.AllegroProducts.AsNoTracking()
//                .Where(p => p.StoreId == storeId && p.IsScrapable)
//                .ToListAsync();

//            var productIds = products.Select(p => p.AllegroProductId).ToList();
//            var productDict = products.ToDictionary(p => p.AllegroProductId);

//            var rawPrices = await _context.AllegroPriceHistories.AsNoTracking()
//                .Where(ph => ph.AllegroScrapeHistoryId == scrapId && productIds.Contains(ph.AllegroProductId))
//                .ToListAsync();

//            // Deduplikacja: jeden rekord per produkt + oferta
//            rawPrices = rawPrices
//                .GroupBy(ph => new { ph.AllegroProductId, ph.IdAllegro })
//                .Select(g => g.First())
//                .ToList();

//            bool includeNoDelivery = activePreset?.IncludeNoDeliveryInfo ?? true;
//            int minDelivery = activePreset?.MinDeliveryDays ?? 0;
//            int maxDelivery = activePreset?.MaxDeliveryDays ?? 31;

//            var result = new List<RawAllegroExportEntry>();

//            foreach (var ph in rawPrices)
//            {
//                if (!productDict.TryGetValue(ph.AllegroProductId, out var product))
//                    continue;

//                bool isMe = ph.SellerName.Equals(storeNameAllegro, StringComparison.OrdinalIgnoreCase);

//                // Filtrowanie presetu (nie filtrujemy "moich" ofert)
//                if (!isMe && activePreset != null)
//                {
//                    // Filtr czasu dostawy
//                    if (ph.DeliveryTime.HasValue)
//                    {
//                        if (ph.DeliveryTime.Value < minDelivery || ph.DeliveryTime.Value > maxDelivery)
//                            continue;
//                    }
//                    else
//                    {
//                        if (!includeNoDelivery)
//                            continue;
//                    }

//                    // Filtr sprzedawcy
//                    if (competitorRules != null)
//                    {
//                        var sellerLower = (ph.SellerName ?? "").ToLower().Trim();
//                        if (competitorRules.TryGetValue(sellerLower, out bool useCompetitor))
//                        {
//                            if (!useCompetitor) continue;
//                        }
//                        else if (!activePreset.UseUnmarkedStores)
//                        {
//                            continue;
//                        }
//                    }
//                }

//                result.Add(new RawAllegroExportEntry
//                {
//                    AllegroProductId = product.AllegroProductId,
//                    ProductName = product.AllegroProductName,
//                    Producer = product.Producer,
//                    Ean = product.AllegroEan,
//                    AllegroSku = product.AllegroSku,
//                    IdOnAllegro = product.IdOnAllegro,
//                    MarginPrice = product.AllegroMarginPrice,
//                    Price = ph.Price,
//                    SellerName = ph.SellerName,
//                    IdAllegro = ph.IdAllegro,
//                    DeliveryTime = ph.DeliveryTime,
//                    Popularity = ph.Popularity,
//                    SuperSeller = ph.SuperSeller,
//                    Smart = ph.Smart,
//                    IsBestPriceGuarantee = ph.IsBestPriceGuarantee,
//                    TopOffer = ph.TopOffer,
//                    SuperPrice = ph.SuperPrice,
//                    IsMe = isMe
//                });
//            }

//            return result;
//        }

//        // ====================================================================
//        // BUDOWANIE WIERSZY — Comparison
//        // ====================================================================

//        private List<ExportProductRow> BuildPriceExportRows(List<RawExportEntry> rawData, string myStoreNameLower)
//        {
//            return rawData
//                .GroupBy(x => new { x.ProductName, x.Producer, x.Ean, x.CatalogNumber, x.ExternalId, x.MarginPrice })
//                .Select(g =>
//                {
//                    var all = g.ToList();
//                    var myOffer = all.FirstOrDefault(x => x.IsMe);
//                    var competitors = all.Where(x => !x.IsMe).OrderBy(x => x.FinalPrice).ToList();
//                    var allOffers = new List<RawExportEntry>(competitors);
//                    if (myOffer != null) allOffers.Add(myOffer);

//                    var bestComp = competitors.FirstOrDefault();
//                    int totalOffers = allOffers.Count;
//                    int myRank = 0;
//                    string posStr = "-";
//                    decimal? diffPln = null;
//                    decimal? diffPct = null;
//                    int colorCode = 0;

//                    if (myOffer != null && totalOffers > 0)
//                    {
//                        int cheaper = allOffers.Count(x => x.FinalPrice < myOffer.FinalPrice);
//                        myRank = cheaper + 1;
//                        posStr = $"{myRank} z {totalOffers}";

//                        if (bestComp != null)
//                        {
//                            diffPln = myOffer.FinalPrice - bestComp.FinalPrice;
//                            if (bestComp.FinalPrice > 0)
//                                diffPct = Math.Round((myOffer.FinalPrice - bestComp.FinalPrice) / bestComp.FinalPrice * 100, 2);
//                        }

//                        decimal minPrice = allOffers.Min(x => x.FinalPrice);
//                        if (myOffer.FinalPrice == minPrice)
//                        {
//                            int othersAtMin = allOffers.Count(x => x.FinalPrice == minPrice && !x.IsMe);
//                            colorCode = othersAtMin == 0 ? 1 : 2;
//                        }
//                        else colorCode = 3;
//                    }

//                    bool? myGoogle = myOffer?.IsGoogle == true ? myOffer.GoogleInStock : null;
//                    bool? myCeneo = myOffer != null && myOffer.IsGoogle == false ? myOffer.CeneoInStock : null;
//                    var myEntries = all.Where(x => x.IsMe).ToList();
//                    foreach (var e in myEntries)
//                    {
//                        if (e.IsGoogle && e.GoogleInStock.HasValue) myGoogle = e.GoogleInStock;
//                        if (!e.IsGoogle && e.CeneoInStock.HasValue) myCeneo = e.CeneoInStock;
//                    }

//                    bool? compGoogle = bestComp?.IsGoogle == true ? bestComp.GoogleInStock : null;
//                    bool? compCeneo = bestComp != null && !bestComp.IsGoogle ? bestComp.CeneoInStock : null;

//                    return new ExportProductRow
//                    {
//                        ExternalId = g.Key.ExternalId,
//                        ProductName = g.Key.ProductName,
//                        Producer = g.Key.Producer,
//                        Ean = g.Key.Ean,
//                        CatalogNumber = g.Key.CatalogNumber,
//                        MarginPrice = g.Key.MarginPrice,
//                        MyPrice = myOffer?.FinalPrice,
//                        BestCompetitorPrice = bestComp?.FinalPrice,
//                        BestCompetitorStore = bestComp?.StoreName,
//                        DiffToLowest = diffPln,
//                        DiffToLowestPercent = diffPct,
//                        TotalOffers = totalOffers,
//                        MyRank = myRank,
//                        PositionString = posStr,
//                        ColorCode = colorCode,
//                        MyGoogleInStock = myGoogle,
//                        MyCeneoInStock = myCeneo,
//                        CompGoogleInStock = compGoogle,
//                        CompCeneoInStock = compCeneo,
//                        Competitors = competitors.Select(c => new ExportCompetitorOffer
//                        {
//                            Store = c.StoreName,
//                            FinalPrice = c.FinalPrice
//                        }).ToList()
//                    };
//                })
//                .OrderBy(x => x.ProductName)
//                .ToList();
//        }

//        // ====================================================================
//        // BUDOWANIE WIERSZY — Allegro
//        // ====================================================================

//        private List<ExportAllegroProductRow> BuildAllegroPriceExportRows(List<RawAllegroExportEntry> rawData, string myStoreNameLower)
//        {
//            // Grupowanie priorytetowo po EAN (jeśli istnieje), fallback po AllegroProductId.
//            // Dzięki temu produkty z tym samym EAN w różnych katalogach schodzą do jednego wiersza.
//            return rawData
//                .GroupBy(x => !string.IsNullOrWhiteSpace(x.Ean)
//                    ? $"ean:{x.Ean.Trim()}"
//                    : $"pid:{x.AllegroProductId}")
//                .Select(g =>
//                {
//                    var all = g.ToList();

//                    // Metadane z pierwszego wpisu (lub tego z najlepszymi danymi)
//                    var representative = all.First();

//                    // ── MOJA OFERTA: priorytet targetOfferId, potem najtańsza ──
//                    var myEntries = all.Where(x => x.IsMe && x.Price > 0).ToList();
//                    RawAllegroExportEntry myOffer = null;

//                    if (myEntries.Any())
//                    {
//                        // Najpierw szukamy oferty pasującej do IdOnAllegro (główna aukcja)
//                        foreach (var entry in myEntries)
//                        {
//                            if (long.TryParse(entry.IdOnAllegro, out var tid) && entry.IdAllegro == tid)
//                            {
//                                myOffer = entry;
//                                break;
//                            }
//                        }
//                        // Fallback: najtańsza z moich ofert
//                        myOffer ??= myEntries.OrderBy(x => x.Price).First();
//                    }

//                    // ── KONKURENCI: deduplikacja po SellerName, najtańsza oferta per sprzedawca ──
//                    var competitors = all
//                        .Where(x => !x.IsMe && x.Price > 0)
//                        .GroupBy(x => x.SellerName.ToLower().Trim())
//                        .Select(sg => sg.OrderBy(x => x.Price).First())
//                        .OrderBy(x => x.Price)
//                        .ToList();

//                    // Wszystkie unikalne oferty (do rankingu)
//                    var allUniqueOffers = new List<RawAllegroExportEntry>(competitors);
//                    if (myOffer != null) allUniqueOffers.Add(myOffer);

//                    var bestComp = competitors.FirstOrDefault();
//                    int totalOffers = allUniqueOffers.Count;
//                    int myRank = 0;
//                    string posStr = "-";
//                    decimal? diffPln = null;
//                    decimal? diffPct = null;
//                    int colorCode = 0;

//                    if (myOffer != null && myOffer.Price > 0 && totalOffers > 0)
//                    {
//                        int cheaper = allUniqueOffers.Count(x => x.Price < myOffer.Price);
//                        myRank = cheaper + 1;
//                        posStr = $"{myRank} z {totalOffers}";

//                        if (bestComp != null)
//                        {
//                            diffPln = myOffer.Price - bestComp.Price;
//                            if (bestComp.Price > 0)
//                                diffPct = Math.Round((myOffer.Price - bestComp.Price) / bestComp.Price * 100, 2);
//                        }

//                        decimal minPrice = allUniqueOffers.Where(x => x.Price > 0).Min(x => x.Price);
//                        if (myOffer.Price == minPrice)
//                        {
//                            int othersAtMin = allUniqueOffers.Count(x => x.Price == minPrice && !x.IsMe);
//                            colorCode = othersAtMin == 0 ? 1 : 2;
//                        }
//                        else colorCode = 3;
//                    }

//                    // Popularity: sumujemy WSZYSTKIE wpisy (bez dedupu) — bo każda aukcja ma swój wolumen
//                    int totalPopularity = all.Sum(x => x.Popularity ?? 0);
//                    int? myPopularity = myEntries.Any() ? myEntries.Sum(x => x.Popularity ?? 0) : (int?)null;

//                    return new ExportAllegroProductRow
//                    {
//                        AllegroProductId = representative.AllegroProductId,
//                        ProductName = representative.ProductName,
//                        Producer = representative.Producer,
//                        Ean = representative.Ean,
//                        AllegroSku = representative.AllegroSku,
//                        MarginPrice = representative.MarginPrice,
//                        MyPrice = myOffer?.Price,
//                        BestCompetitorPrice = bestComp?.Price,
//                        BestCompetitorStore = bestComp?.SellerName,
//                        DiffToLowest = diffPln,
//                        DiffToLowestPercent = diffPct,
//                        TotalOffers = totalOffers,
//                        MyRank = myRank,
//                        PositionString = posStr,
//                        ColorCode = colorCode,
//                        MyPopularity = myPopularity,
//                        TotalPopularity = totalPopularity,
//                        Competitors = competitors.Select(c => new ExportCompetitorOffer
//                        {
//                            Store = c.SellerName,
//                            FinalPrice = c.Price
//                        }).ToList()
//                    };
//                })
//                .OrderBy(x => x.ProductName)
//                .ToList();
//        }

//        // ====================================================================
//        // ZAPIS ARKUSZA — Comparison
//        // ====================================================================

//        private void WritePriceExportSheet(ISheet sheet, List<ExportProductRow> data, ExportStyles s)
//        {
//            var headerRow = sheet.CreateRow(0);
//            int col = 0;

//            string[] headers = {
//                "ID Produktu", "Nazwa Produktu", "Producent", "EAN", "SKU",
//                "Cena Zakupu", "Twoja Cena", "Najt. Cena Konkurencji", "Najt. Sklep",
//                "Różnica PLN", "Różnica %",
//                "Ilość Ofert", "Twoja Pozycja",
//                "Google - Ty", "Ceneo - Ty",
//                "Google - Konkurent", "Ceneo - Konkurent"
//            };

//            foreach (var h in headers)
//            {
//                var cell = headerRow.CreateCell(col++);
//                cell.SetCellValue(h);
//                cell.CellStyle = s.Header;
//            }

//            int maxComp = 60;
//            for (int i = 1; i <= maxComp; i++)
//            {
//                var c1 = headerRow.CreateCell(col++); c1.SetCellValue($"Sklep {i}"); c1.CellStyle = s.Header;
//                var c2 = headerRow.CreateCell(col++); c2.SetCellValue($"Cena {i}"); c2.CellStyle = s.Header;
//            }

//            int rowIdx = 1;
//            foreach (var item in data)
//            {
//                var row = sheet.CreateRow(rowIdx++);
//                col = 0;

//                row.CreateCell(col++).SetCellValue(item.ExternalId?.ToString() ?? "");
//                row.CreateCell(col++).SetCellValue(item.ProductName ?? "");
//                row.CreateCell(col++).SetCellValue(item.Producer ?? "");
//                row.CreateCell(col++).SetCellValue(item.Ean ?? "");
//                row.CreateCell(col++).SetCellValue(item.CatalogNumber ?? "");

//                SetDecimalCell(row.CreateCell(col++), item.MarginPrice, s.Currency);

//                var cellMyPrice = row.CreateCell(col++);
//                if (item.MyPrice.HasValue)
//                {
//                    cellMyPrice.SetCellValue((double)item.MyPrice.Value);
//                    cellMyPrice.CellStyle = item.ColorCode switch
//                    {
//                        1 => s.PriceGreen,
//                        2 => s.PriceLightGreen,
//                        3 => s.PriceRed,
//                        _ => s.Currency
//                    };
//                }
//                else cellMyPrice.SetCellValue("-");

//                SetDecimalCell(row.CreateCell(col++), item.BestCompetitorPrice, s.Currency);
//                row.CreateCell(col++).SetCellValue(item.BestCompetitorStore ?? "");

//                SetDecimalCell(row.CreateCell(col++), item.DiffToLowest, s.Currency);
//                SetDecimalCell(row.CreateCell(col++), item.DiffToLowestPercent, s.Percent);

//                row.CreateCell(col++).SetCellValue(item.TotalOffers);
//                row.CreateCell(col++).SetCellValue(item.PositionString);

//                row.CreateCell(col++).SetCellValue(StockText(item.MyGoogleInStock));
//                row.CreateCell(col++).SetCellValue(StockText(item.MyCeneoInStock));
//                row.CreateCell(col++).SetCellValue(StockText(item.CompGoogleInStock));
//                row.CreateCell(col++).SetCellValue(StockText(item.CompCeneoInStock));

//                for (int i = 0; i < maxComp; i++)
//                {
//                    if (i < item.Competitors.Count)
//                    {
//                        row.CreateCell(col++).SetCellValue(item.Competitors[i].Store);
//                        var cp = row.CreateCell(col++);
//                        cp.SetCellValue((double)item.Competitors[i].FinalPrice);
//                        cp.CellStyle = s.Currency;
//                    }
//                    else col += 2;
//                }
//            }

//            for (int i = 0; i < 17; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
//        }

//        // ====================================================================
//        // ZAPIS ARKUSZA — Allegro
//        // ====================================================================

//        private void WriteAllegroPriceExportSheet(ISheet sheet, List<ExportAllegroProductRow> data, ExportStyles s)
//        {
//            var headerRow = sheet.CreateRow(0);
//            int col = 0;

//            string[] headers = {
//                "ID Produktu", "Nazwa Produktu", "Producent", "EAN", "SKU Allegro",
//                "Cena Zakupu", "Twoja Cena", "Najt. Cena Konkurencji", "Najt. Sprzedawca",
//                "Różnica PLN", "Różnica %",
//                "Ilość Ofert", "Twoja Pozycja",
//                "Twoja Sprzedaż", "Sprzedaż Katalogu"
//            };

//            foreach (var h in headers)
//            {
//                var cell = headerRow.CreateCell(col++);
//                cell.SetCellValue(h);
//                cell.CellStyle = s.Header;
//            }

//            int maxComp = 60;
//            for (int i = 1; i <= maxComp; i++)
//            {
//                var c1 = headerRow.CreateCell(col++); c1.SetCellValue($"Sprzedawca {i}"); c1.CellStyle = s.Header;
//                var c2 = headerRow.CreateCell(col++); c2.SetCellValue($"Cena {i}"); c2.CellStyle = s.Header;
//            }

//            int rowIdx = 1;
//            foreach (var item in data)
//            {
//                var row = sheet.CreateRow(rowIdx++);
//                col = 0;

//                row.CreateCell(col++).SetCellValue(item.AllegroProductId);
//                row.CreateCell(col++).SetCellValue(item.ProductName ?? "");
//                row.CreateCell(col++).SetCellValue(item.Producer ?? "");
//                row.CreateCell(col++).SetCellValue(item.Ean ?? "");
//                row.CreateCell(col++).SetCellValue(item.AllegroSku ?? "");

//                SetDecimalCell(row.CreateCell(col++), item.MarginPrice, s.Currency);

//                var cellMyPrice = row.CreateCell(col++);
//                if (item.MyPrice.HasValue)
//                {
//                    cellMyPrice.SetCellValue((double)item.MyPrice.Value);
//                    cellMyPrice.CellStyle = item.ColorCode switch
//                    {
//                        1 => s.PriceGreen,
//                        2 => s.PriceLightGreen,
//                        3 => s.PriceRed,
//                        _ => s.Currency
//                    };
//                }
//                else cellMyPrice.SetCellValue("-");

//                SetDecimalCell(row.CreateCell(col++), item.BestCompetitorPrice, s.Currency);
//                row.CreateCell(col++).SetCellValue(item.BestCompetitorStore ?? "");

//                SetDecimalCell(row.CreateCell(col++), item.DiffToLowest, s.Currency);
//                SetDecimalCell(row.CreateCell(col++), item.DiffToLowestPercent, s.Percent);

//                row.CreateCell(col++).SetCellValue(item.TotalOffers);
//                row.CreateCell(col++).SetCellValue(item.PositionString);

//                row.CreateCell(col++).SetCellValue(item.MyPopularity ?? 0);
//                row.CreateCell(col++).SetCellValue(item.TotalPopularity);

//                for (int i = 0; i < maxComp; i++)
//                {
//                    if (i < item.Competitors.Count)
//                    {
//                        row.CreateCell(col++).SetCellValue(item.Competitors[i].Store);
//                        var cp = row.CreateCell(col++);
//                        cp.SetCellValue((double)item.Competitors[i].FinalPrice);
//                        cp.CellStyle = s.Currency;
//                    }
//                    else col += 2;
//                }
//            }

//            for (int i = 0; i < 15; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
//        }

//        // ====================================================================
//        // BUDOWANIE DANYCH KONKURENCJI (współdzielone przez oba tryby)
//        // ====================================================================

//        private (List<CompetitorSummary> competitors, List<BrandSummary> brands) BuildCompetitionData(
//            List<RawExportEntry> rawData, string myStoreNameLower)
//        {
//            var productGroups = rawData
//                .GroupBy(x => new { x.ProductName, x.Producer })
//                .ToList();

//            var competitorDict = new Dictionary<string, CompetitorSummary>(StringComparer.OrdinalIgnoreCase);
//            var brandStats = new Dictionary<string, List<(decimal myPrice, decimal bestCompPrice, bool isCheapest, bool isMostExpensive)>>(StringComparer.OrdinalIgnoreCase);

//            foreach (var g in productGroups)
//            {
//                var entries = g.ToList();
//                var myEntry = entries.FirstOrDefault(x => x.IsMe);
//                if (myEntry == null || myEntry.FinalPrice <= 0) continue;

//                decimal myPrice = myEntry.FinalPrice;
//                var compEntries = entries.Where(x => !x.IsMe && x.FinalPrice > 0).ToList();
//                if (!compEntries.Any()) continue;

//                string brand = g.Key.Producer ?? "Brak producenta";
//                decimal bestCompPrice = compEntries.Min(x => x.FinalPrice);
//                decimal worstCompPrice = compEntries.Max(x => x.FinalPrice);
//                bool isCheapest = myPrice <= bestCompPrice;
//                bool isMostExpensive = myPrice >= worstCompPrice && compEntries.Count > 0;

//                if (!brandStats.ContainsKey(brand)) brandStats[brand] = new();
//                brandStats[brand].Add((myPrice, bestCompPrice, isCheapest, isMostExpensive));

//                var uniqueCompetitors = compEntries
//                    .GroupBy(x => x.StoreName.ToLower().Trim())
//                    .Select(cg => cg.OrderBy(x => x.FinalPrice).First())
//                    .ToList();

//                foreach (var comp in uniqueCompetitors)
//                {
//                    string compKey = comp.StoreName.Trim();
//                    if (!competitorDict.ContainsKey(compKey))
//                        competitorDict[compKey] = new CompetitorSummary { StoreName = compKey };

//                    var cs = competitorDict[compKey];
//                    cs.OverlapCount++;

//                    decimal diffPct = Math.Round((myPrice - comp.FinalPrice) / myPrice * 100, 2);
//                    cs.AllDiffs.Add(diffPct);

//                    string brandKey = brand;
//                    if (!cs.BrandBreakdown.ContainsKey(brandKey))
//                        cs.BrandBreakdown[brandKey] = (0, 0, 0);

//                    if (Math.Abs(diffPct) < 0.01m)
//                    {
//                        cs.EqualCount++;
//                        var b = cs.BrandBreakdown[brandKey]; cs.BrandBreakdown[brandKey] = (b.Cheaper, b.Expensive, b.Equal + 1);
//                    }
//                    else if (diffPct > 0)
//                    {
//                        cs.TheyCheaperCount++;
//                        var b = cs.BrandBreakdown[brandKey]; cs.BrandBreakdown[brandKey] = (b.Cheaper + 1, b.Expensive, b.Equal);
//                        decimal absDiff = Math.Abs(diffPct);

//                        if (absDiff <= 5) cs.TheyCheaper_0_5++;
//                        else if (absDiff <= 10) cs.TheyCheaper_5_10++;
//                        else if (absDiff <= 15) cs.TheyCheaper_10_15++;
//                        else if (absDiff <= 20) cs.TheyCheaper_15_20++;
//                        else if (absDiff <= 25) cs.TheyCheaper_20_25++;
//                        else if (absDiff <= 30) cs.TheyCheaper_25_30++;
//                        else if (absDiff <= 35) cs.TheyCheaper_30_35++;
//                        else if (absDiff <= 40) cs.TheyCheaper_35_40++;
//                        else if (absDiff <= 45) cs.TheyCheaper_40_45++;
//                        else if (absDiff <= 50) cs.TheyCheaper_45_50++;
//                        else cs.TheyCheaper_50plus++;
//                    }
//                    else
//                    {
//                        cs.TheyMoreExpensiveCount++;
//                        var b = cs.BrandBreakdown[brandKey]; cs.BrandBreakdown[brandKey] = (b.Cheaper, b.Expensive + 1, b.Equal);
//                        decimal absDiff = Math.Abs(diffPct);

//                        if (absDiff <= 5) cs.TheyExpensive_0_5++;
//                        else if (absDiff <= 10) cs.TheyExpensive_5_10++;
//                        else if (absDiff <= 15) cs.TheyExpensive_10_15++;
//                        else if (absDiff <= 20) cs.TheyExpensive_15_20++;
//                        else if (absDiff <= 25) cs.TheyExpensive_20_25++;
//                        else if (absDiff <= 30) cs.TheyExpensive_25_30++;
//                        else if (absDiff <= 35) cs.TheyExpensive_30_35++;
//                        else if (absDiff <= 40) cs.TheyExpensive_35_40++;
//                        else if (absDiff <= 45) cs.TheyExpensive_40_45++;
//                        else if (absDiff <= 50) cs.TheyExpensive_45_50++;
//                        else cs.TheyExpensive_50plus++;
//                    }
//                }
//            }

//            foreach (var cs in competitorDict.Values)
//            {
//                if (cs.AllDiffs.Any())
//                {
//                    cs.AvgDiffPercent = Math.Round(cs.AllDiffs.Average(), 2);
//                    var sorted = cs.AllDiffs.OrderBy(x => x).ToList();
//                    int n = sorted.Count;
//                    cs.MedianDiffPercent = n % 2 == 0
//                        ? Math.Round((sorted[n / 2 - 1] + sorted[n / 2]) / 2m, 2)
//                        : sorted[n / 2];
//                }
//            }

//            var competitors = competitorDict.Values
//                .OrderByDescending(x => x.OverlapCount)
//                .ToList();

//            var brands = brandStats
//                .Select(kvp =>
//                {
//                    var items = kvp.Value;
//                    int count = items.Count;
//                    decimal avgOur = Math.Round(items.Average(x => x.myPrice), 2);
//                    decimal avgMarket = Math.Round(items.Average(x => x.bestCompPrice), 2);
//                    decimal idx = avgMarket > 0 ? Math.Round((avgOur / avgMarket) * 100, 2) : 100;
//                    int cheapest = items.Count(x => x.isCheapest);
//                    int expensive = items.Count(x => x.isMostExpensive);

//                    return new BrandSummary
//                    {
//                        BrandName = kvp.Key,
//                        ProductCount = count,
//                        AvgOurPrice = avgOur,
//                        AvgMarketPrice = avgMarket,
//                        PriceIndexPercent = idx,
//                        WeAreCheapestCount = cheapest,
//                        WeAreCheapestPercent = count > 0 ? Math.Round((decimal)cheapest / count * 100, 1) : 0,
//                        WeAreMostExpensiveCount = expensive,
//                        WeAreMostExpensivePercent = count > 0 ? Math.Round((decimal)expensive / count * 100, 1) : 0
//                    };
//                })
//                .OrderByDescending(x => x.ProductCount)
//                .ToList();

//            return (competitors, brands);
//        }

//        // ====================================================================
//        // ARKUSZE KONKURENCJI (współdzielone)
//        // ====================================================================

//        private void WriteCompetitionOverviewSheet(XSSFWorkbook wb, string sheetName,
//            List<CompetitorSummary> data, string scrapDate, string storeName, string presetName, ExportStyles s)
//        {
//            var sheet = wb.CreateSheet(sheetName);

//            int r = 0;
//            var infoRow = sheet.CreateRow(r++);
//            var infoCell = infoRow.CreateCell(0);
//            infoCell.SetCellValue($"Raport Konkurencji — {storeName} — Analiza: {scrapDate} — Preset: {presetName ?? "Domyślny"}");
//            infoCell.CellStyle = s.InfoHeader;
//            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 9));

//            r++;

//            var headerRow = sheet.CreateRow(r++);
//            string[] headers = {
//                "Sklep", "Wspólne produkty",
//                "Tańsi od nas (szt.)", "Tańsi od nas (%)",
//                "Drożsi od nas (szt.)", "Drożsi od nas (%)",
//                "Równa cena (szt.)",
//                "Śr. różnica (%)", "Mediana różnicy (%)",
//                "Pozycja cenowa"
//            };

//            for (int i = 0; i < headers.Length; i++)
//            {
//                var cell = headerRow.CreateCell(i);
//                cell.SetCellValue(headers[i]);
//                cell.CellStyle = s.HeaderDark;
//            }

//            foreach (var comp in data)
//            {
//                var row = sheet.CreateRow(r++);
//                int c = 0;

//                row.CreateCell(c++).SetCellValue(comp.StoreName);
//                row.CreateCell(c++).SetCellValue(comp.OverlapCount);

//                SetIntCell(row.CreateCell(c++), comp.TheyCheaperCount, comp.TheyCheaperCount > comp.TheyMoreExpensiveCount ? s.CellRedBg : s.Default);
//                SetPercentValueCell(row.CreateCell(c++), comp.OverlapCount > 0 ? Math.Round((decimal)comp.TheyCheaperCount / comp.OverlapCount * 100, 1) : 0, s.Percent);

//                SetIntCell(row.CreateCell(c++), comp.TheyMoreExpensiveCount, comp.TheyMoreExpensiveCount > comp.TheyCheaperCount ? s.CellGreenBg : s.Default);
//                SetPercentValueCell(row.CreateCell(c++), comp.OverlapCount > 0 ? Math.Round((decimal)comp.TheyMoreExpensiveCount / comp.OverlapCount * 100, 1) : 0, s.Percent);

//                row.CreateCell(c++).SetCellValue(comp.EqualCount);

//                var avgCell = row.CreateCell(c++);
//                avgCell.SetCellValue((double)comp.AvgDiffPercent);
//                avgCell.CellStyle = comp.AvgDiffPercent > 0 ? s.PercentRed : s.PercentGreen;

//                var medCell = row.CreateCell(c++);
//                medCell.SetCellValue((double)comp.MedianDiffPercent);
//                medCell.CellStyle = comp.MedianDiffPercent > 0 ? s.PercentRed : s.PercentGreen;

//                string position;
//                if (comp.TheyCheaperCount > comp.TheyMoreExpensiveCount * 2) position = "Znacznie tańszy";
//                else if (comp.TheyCheaperCount > comp.TheyMoreExpensiveCount) position = "Tańszy";
//                else if (comp.TheyMoreExpensiveCount > comp.TheyCheaperCount * 2) position = "Znacznie droższy";
//                else if (comp.TheyMoreExpensiveCount > comp.TheyCheaperCount) position = "Droższy";
//                else position = "Porównywalny";

//                row.CreateCell(c++).SetCellValue(position);
//            }

//            for (int i = 0; i < headers.Length; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
//        }

//        private void WriteCompetitionDistributionSheet(XSSFWorkbook wb, string sheetName,
//            List<CompetitorSummary> data, string scrapDate, ExportStyles s)
//        {
//            var sheet = wb.CreateSheet(sheetName);
//            int r = 0;

//            var infoRow = sheet.CreateRow(r++);
//            var infoCell = infoRow.CreateCell(0);
//            infoCell.SetCellValue($"Rozkład różnic cenowych — Analiza: {scrapDate}");
//            infoCell.CellStyle = s.InfoHeader;
//            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 24));

//            r++;
//            var subRow = sheet.CreateRow(r++);

//            var subCell1 = subRow.CreateCell(1);
//            subCell1.SetCellValue("← KONKURENT TAŃSZY (masz wyższe ceny)");
//            subCell1.CellStyle = s.SubHeaderRed;
//            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 1, 11));

//            var subCellEq = subRow.CreateCell(12);
//            subCellEq.SetCellValue("REMIS");
//            subCellEq.CellStyle = s.SubHeaderBlue;

//            var subCell2 = subRow.CreateCell(13);
//            subCell2.SetCellValue("KONKURENT DROŻSZY (masz niższe ceny) →");
//            subCell2.CellStyle = s.SubHeaderGreen;
//            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 13, 23));

//            var headerRow = sheet.CreateRow(r++);
//            string[] cols = {
//                "Sklep",
//                ">50%", "45-50%", "40-45%", "35-40%", "30-35%", "25-30%", "20-25%", "15-20%", "10-15%", "5-10%", "0-5%",
//                "0%",
//                "0-5%", "5-10%", "10-15%", "15-20%", "20-25%", "25-30%", "30-35%", "35-40%", "40-45%", "45-50%", ">50%",
//                "Wspólne"
//            };

//            for (int i = 0; i < cols.Length; i++)
//            {
//                var cell = headerRow.CreateCell(i);
//                cell.SetCellValue(cols[i]);
//                cell.CellStyle = s.HeaderDark;
//            }

//            foreach (var comp in data)
//            {
//                var row = sheet.CreateRow(r++);
//                int c = 0;

//                row.CreateCell(c++).SetCellValue(comp.StoreName);

//                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_50plus, s.CellRed11);
//                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_45_50, s.CellRed10);
//                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_40_45, s.CellRed9);
//                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_35_40, s.CellRed8);
//                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_30_35, s.CellRed7);
//                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_25_30, s.CellRed6);
//                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_20_25, s.CellRed5);
//                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_15_20, s.CellRed4);
//                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_10_15, s.CellRed3);
//                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_5_10, s.CellRed2);
//                SetDistCell(row.CreateCell(c++), comp.TheyCheaper_0_5, s.CellRed1);

//                SetDistCell(row.CreateCell(c++), comp.EqualCount, s.CellBlue);

//                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_0_5, s.CellGreen1);
//                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_5_10, s.CellGreen2);
//                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_10_15, s.CellGreen3);
//                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_15_20, s.CellGreen4);
//                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_20_25, s.CellGreen5);
//                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_25_30, s.CellGreen6);
//                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_30_35, s.CellGreen7);
//                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_35_40, s.CellGreen8);
//                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_40_45, s.CellGreen9);
//                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_45_50, s.CellGreen10);
//                SetDistCell(row.CreateCell(c++), comp.TheyExpensive_50plus, s.CellGreen11);

//                row.CreateCell(c++).SetCellValue(comp.OverlapCount);
//            }

//            for (int i = 0; i < cols.Length; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
//        }

//        private void WriteBrandAnalysisSheet(XSSFWorkbook wb, string sheetName,
//            List<BrandSummary> data, string scrapDate, ExportStyles s)
//        {
//            var sheet = wb.CreateSheet(sheetName);
//            int r = 0;

//            var infoRow = sheet.CreateRow(r++);
//            var infoCell = infoRow.CreateCell(0);
//            infoCell.SetCellValue($"Analiza pozycji cenowej wg marek — Analiza: {scrapDate}");
//            infoCell.CellStyle = s.InfoHeader;
//            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 8));

//            r++;
//            var headerRow = sheet.CreateRow(r++);
//            string[] headers = {
//                "Marka", "Produkty (szt.)",
//                "Śr. nasza cena", "Śr. cena rynku",
//                "Indeks cenowy (%)",
//                "Najtańsi (szt.)", "Najtańsi (%)",
//                "Najdrożsi (szt.)", "Najdrożsi (%)"
//            };

//            for (int i = 0; i < headers.Length; i++)
//            {
//                var cell = headerRow.CreateCell(i);
//                cell.SetCellValue(headers[i]);
//                cell.CellStyle = s.HeaderDark;
//            }

//            foreach (var brand in data)
//            {
//                var row = sheet.CreateRow(r++);
//                int c = 0;

//                row.CreateCell(c++).SetCellValue(brand.BrandName);
//                row.CreateCell(c++).SetCellValue(brand.ProductCount);

//                SetDecimalCell(row.CreateCell(c++), brand.AvgOurPrice, s.Currency);
//                SetDecimalCell(row.CreateCell(c++), brand.AvgMarketPrice, s.Currency);

//                var idxCell = row.CreateCell(c++);
//                idxCell.SetCellValue((double)brand.PriceIndexPercent);
//                idxCell.CellStyle = brand.PriceIndexPercent <= 100 ? s.PercentGreen : s.PercentRed;

//                SetIntCell(row.CreateCell(c++), brand.WeAreCheapestCount, brand.WeAreCheapestPercent > 50 ? s.CellGreenBg : s.Default);
//                SetPercentValueCell(row.CreateCell(c++), brand.WeAreCheapestPercent, s.Percent);

//                SetIntCell(row.CreateCell(c++), brand.WeAreMostExpensiveCount, brand.WeAreMostExpensivePercent > 50 ? s.CellRedBg : s.Default);
//                SetPercentValueCell(row.CreateCell(c++), brand.WeAreMostExpensivePercent, s.Percent);
//            }

//            for (int i = 0; i < headers.Length; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
//        }

//        // ====================================================================
//        // STYLE EXCELA
//        // ====================================================================

//        private class ExportStyles
//        {
//            public ICellStyle Header { get; set; }
//            public ICellStyle HeaderDark { get; set; }
//            public ICellStyle InfoHeader { get; set; }
//            public ICellStyle SubHeaderRed { get; set; }
//            public ICellStyle SubHeaderGreen { get; set; }
//            public ICellStyle Currency { get; set; }
//            public ICellStyle Percent { get; set; }
//            public ICellStyle PercentRed { get; set; }
//            public ICellStyle PercentGreen { get; set; }
//            public ICellStyle PriceGreen { get; set; }
//            public ICellStyle PriceLightGreen { get; set; }
//            public ICellStyle PriceRed { get; set; }
//            public ICellStyle Default { get; set; }
//            public ICellStyle CellRedBg { get; set; }
//            public ICellStyle CellGreenBg { get; set; }
//            public ICellStyle CellRed1 { get; set; }
//            public ICellStyle CellRed2 { get; set; }
//            public ICellStyle CellRed3 { get; set; }
//            public ICellStyle CellRed4 { get; set; }
//            public ICellStyle CellRed5 { get; set; }
//            public ICellStyle CellRed6 { get; set; }
//            public ICellStyle CellRed7 { get; set; }
//            public ICellStyle CellRed8 { get; set; }
//            public ICellStyle CellRed9 { get; set; }
//            public ICellStyle CellRed10 { get; set; }
//            public ICellStyle CellRed11 { get; set; }
//            public ICellStyle CellGreen1 { get; set; }
//            public ICellStyle CellGreen2 { get; set; }
//            public ICellStyle CellGreen3 { get; set; }
//            public ICellStyle CellGreen4 { get; set; }
//            public ICellStyle CellGreen5 { get; set; }
//            public ICellStyle CellGreen6 { get; set; }
//            public ICellStyle CellGreen7 { get; set; }
//            public ICellStyle CellGreen8 { get; set; }
//            public ICellStyle CellGreen9 { get; set; }
//            public ICellStyle CellGreen10 { get; set; }
//            public ICellStyle CellGreen11 { get; set; }
//            public ICellStyle SubHeaderBlue { get; set; }
//            public ICellStyle CellBlue { get; set; }
//        }

//        private ExportStyles CreateExportStyles(XSSFWorkbook wb)
//        {
//            var s = new ExportStyles();
//            var df = wb.CreateDataFormat();

//            s.Default = wb.CreateCellStyle();

//            s.Header = wb.CreateCellStyle();
//            var hf = wb.CreateFont(); hf.IsBold = true; s.Header.SetFont(hf);

//            s.HeaderDark = CreateColoredStyle(wb, new byte[] { 26, 39, 68 }, true, IndexedColors.White.Index);

//            s.InfoHeader = wb.CreateCellStyle();
//            var infoFont = wb.CreateFont(); infoFont.IsBold = true; infoFont.FontHeightInPoints = 12;
//            s.InfoHeader.SetFont(infoFont);

//            s.SubHeaderRed = CreateColoredStyle(wb, new byte[] { 220, 53, 69 }, true, IndexedColors.White.Index);
//            s.SubHeaderGreen = CreateColoredStyle(wb, new byte[] { 40, 167, 69 }, true, IndexedColors.White.Index);

//            s.Currency = wb.CreateCellStyle();
//            s.Currency.DataFormat = df.GetFormat("#,##0.00");

//            s.Percent = wb.CreateCellStyle();
//            s.Percent.DataFormat = df.GetFormat("0.00");

//            s.PercentRed = wb.CreateCellStyle();
//            s.PercentRed.DataFormat = df.GetFormat("0.00");
//            var redFont = wb.CreateFont(); redFont.Color = IndexedColors.Red.Index; redFont.IsBold = true;
//            s.PercentRed.SetFont(redFont);

//            s.PercentGreen = wb.CreateCellStyle();
//            s.PercentGreen.DataFormat = df.GetFormat("0.00");
//            var greenFont = wb.CreateFont(); greenFont.Color = IndexedColors.Green.Index; greenFont.IsBold = true;
//            s.PercentGreen.SetFont(greenFont);

//            s.PriceGreen = wb.CreateCellStyle(); s.PriceGreen.CloneStyleFrom(s.Currency);
//            s.PriceGreen.FillForegroundColor = IndexedColors.LightGreen.Index;
//            s.PriceGreen.FillPattern = FillPattern.SolidForeground;

//            s.PriceLightGreen = wb.CreateCellStyle(); s.PriceLightGreen.CloneStyleFrom(s.Currency);
//            s.PriceLightGreen.FillForegroundColor = IndexedColors.LemonChiffon.Index;
//            s.PriceLightGreen.FillPattern = FillPattern.SolidForeground;

//            s.PriceRed = wb.CreateCellStyle(); s.PriceRed.CloneStyleFrom(s.Currency);
//            s.PriceRed.FillForegroundColor = IndexedColors.Rose.Index;
//            s.PriceRed.FillPattern = FillPattern.SolidForeground;

//            s.CellRedBg = CreateColoredStyle(wb, new byte[] { 248, 215, 218 }, false, 0);
//            s.CellGreenBg = CreateColoredStyle(wb, new byte[] { 212, 237, 218 }, false, 0);

//            s.CellRed1 = CreateColoredStyle(wb, new byte[] { 255, 240, 240 }, false, 0);
//            s.CellRed2 = CreateColoredStyle(wb, new byte[] { 255, 220, 220 }, false, 0);
//            s.CellRed3 = CreateColoredStyle(wb, new byte[] { 255, 200, 200 }, false, 0);
//            s.CellRed4 = CreateColoredStyle(wb, new byte[] { 255, 180, 180 }, false, 0);
//            s.CellRed5 = CreateColoredStyle(wb, new byte[] { 255, 160, 160 }, false, 0);
//            s.CellRed6 = CreateColoredStyle(wb, new byte[] { 255, 140, 140 }, false, 0);
//            s.CellRed7 = CreateColoredStyle(wb, new byte[] { 255, 115, 115 }, false, 0);
//            s.CellRed8 = CreateColoredStyle(wb, new byte[] { 255, 90, 90 }, false, 0);
//            s.CellRed9 = CreateColoredStyle(wb, new byte[] { 240, 60, 60 }, false, IndexedColors.White.Index);
//            s.CellRed10 = CreateColoredStyle(wb, new byte[] { 220, 40, 40 }, false, IndexedColors.White.Index);
//            s.CellRed11 = CreateColoredStyle(wb, new byte[] { 190, 20, 20 }, false, IndexedColors.White.Index);

//            s.CellGreen1 = CreateColoredStyle(wb, new byte[] { 240, 255, 240 }, false, 0);
//            s.CellGreen2 = CreateColoredStyle(wb, new byte[] { 220, 250, 220 }, false, 0);
//            s.CellGreen3 = CreateColoredStyle(wb, new byte[] { 200, 240, 200 }, false, 0);
//            s.CellGreen4 = CreateColoredStyle(wb, new byte[] { 180, 230, 180 }, false, 0);
//            s.CellGreen5 = CreateColoredStyle(wb, new byte[] { 160, 220, 160 }, false, 0);
//            s.CellGreen6 = CreateColoredStyle(wb, new byte[] { 140, 210, 140 }, false, 0);
//            s.CellGreen7 = CreateColoredStyle(wb, new byte[] { 120, 200, 120 }, false, 0);
//            s.CellGreen8 = CreateColoredStyle(wb, new byte[] { 100, 185, 100 }, false, 0);
//            s.CellGreen9 = CreateColoredStyle(wb, new byte[] { 75, 170, 75 }, false, IndexedColors.White.Index);
//            s.CellGreen10 = CreateColoredStyle(wb, new byte[] { 50, 150, 50 }, false, IndexedColors.White.Index);
//            s.CellGreen11 = CreateColoredStyle(wb, new byte[] { 30, 130, 30 }, false, IndexedColors.White.Index);

//            s.SubHeaderBlue = CreateColoredStyle(wb, new byte[] { 100, 180, 255 }, true, IndexedColors.White.Index);
//            s.CellBlue = CreateColoredStyle(wb, new byte[] { 230, 245, 255 }, false, 0);

//            return s;
//        }

//        private ICellStyle CreateColoredStyle(XSSFWorkbook wb, byte[] rgb, bool bold, short fontColorIndex)
//        {
//            var style = (XSSFCellStyle)wb.CreateCellStyle();

//            // NOWE PODEJŚCIE DLA NPOI 2.6.x / 2.7.x:
//            // Tworzymy domyślną mapę kolorów i przekazujemy ją do konstruktora XSSFColor
//            var colorMap = new DefaultIndexedColorMap();
//            style.SetFillForegroundColor(new XSSFColor(rgb, colorMap));

//            style.FillPattern = FillPattern.SolidForeground;

//            if (bold || fontColorIndex > 0)
//            {
//                var font = wb.CreateFont();
//                if (bold) font.IsBold = true;
//                if (fontColorIndex > 0) font.Color = fontColorIndex;
//                style.SetFont(font);
//            }

//            return style;
//        }
//        // ====================================================================
//        // METODY POMOCNICZE
//        // ====================================================================

//        private async Task SendExportProgress(string connectionId, object progress)
//        {
//            if (string.IsNullOrEmpty(connectionId)) return;
//            try
//            {
//                await _hubContext.Clients.Client(connectionId).SendAsync("ExportProgress", progress);
//            }
//            catch (Exception ex)
//            {
//                _logger.LogWarning(ex, "Nie udało się wysłać postępu eksportu.");
//            }
//        }

//        private static void SetDecimalCell(ICell cell, decimal? value, ICellStyle style)
//        {
//            if (value.HasValue)
//            {
//                cell.SetCellValue((double)value.Value);
//                cell.CellStyle = style;
//            }
//        }

//        private static void SetPercentValueCell(ICell cell, decimal value, ICellStyle style)
//        {
//            cell.SetCellValue((double)value);
//            cell.CellStyle = style;
//        }

//        private static void SetIntCell(ICell cell, int value, ICellStyle style)
//        {
//            cell.SetCellValue(value);
//            cell.CellStyle = style;
//        }

//        private static void SetDistCell(ICell cell, int value, ICellStyle style)
//        {
//            if (value > 0)
//            {
//                cell.SetCellValue(value);
//                cell.CellStyle = style;
//            }
//            else
//            {
//                cell.SetCellValue(0);
//            }
//        }

//        private static string StockText(bool? inStock)
//        {
//            if (inStock == true) return "Dostępny";
//            if (inStock == false) return "Niedostępny";
//            return "Brak danych";
//        }
//    }
//}










using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NPOI.OOXML.XSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using System.IO.Compression;

namespace PriceSafari.VSA.MassExporter
{
    public class MassExportService : IMassExportService
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private readonly ILogger<MassExportService> _logger;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTime> _exportCooldowns = new();
        private static readonly TimeSpan ExportCooldown = TimeSpan.FromMinutes(5);
        public const int MAX_SCRAPS = 90;

        public MassExportService(PriceSafariContext context, IHubContext<ScrapingHub> hubContext, ILogger<MassExportService> logger)
        {
            _context = context;
            _hubContext = hubContext;
            _logger = logger;
        }

        // ====================================================================
        // GET AVAILABLE SCRAPS
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

            if (!scraps.Any()) return new List<object>();

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

            if (!scraps.Any()) return new List<object>();

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
        // GŁÓWNA METODA — ROUTING
        // ====================================================================

        public async Task<(byte[] FileContent, string FileName, string ContentType)> GenerateExportAsync(
            int storeId, ExportMultiRequest request, string userId)
        {
            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (!hasAccess)
                throw new UnauthorizedAccessException("Brak dostępu do tego sklepu.");

            if (request.ScrapIds == null || !request.ScrapIds.Any())
                throw new InvalidOperationException("Nie wybrano żadnych analiz.");

            if (request.ScrapIds.Count > MAX_SCRAPS)
                throw new InvalidOperationException($"Maksymalnie {MAX_SCRAPS} analiz.");

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
            var exportType = request.ExportType ?? "prices";

            if (exportType == "priceChange")
                return await GeneratePriceChangeExportAsync(storeId, request, sourceType == "marketplace");

            if (sourceType == "marketplace")
                return await GenerateAllegroExportAsync(storeId, request);

            return await GenerateComparisonExportAsync(storeId, request);
        }

        // ====================================================================
        // FLAGI — helper do ładowania flag dla produktów (CSV per produkt)
        // ====================================================================

        private async Task<Dictionary<int, string>> LoadFlagsCsvForProductsAsync(
            int storeId, List<int> productIds, bool isMarketplace)
        {
            if (productIds == null || !productIds.Any())
                return new Dictionary<int, string>();

            var allFlags = await _context.Flags.AsNoTracking()
                .Where(f => f.StoreId == storeId)
                .ToDictionaryAsync(f => f.FlagId, f => f.FlagName);

            Dictionary<int, List<int>> productFlagIds;

            if (isMarketplace)
            {
                productFlagIds = await _context.ProductFlags.AsNoTracking()
                    .Where(pf => pf.AllegroProductId.HasValue && productIds.Contains(pf.AllegroProductId.Value))
                    .GroupBy(pf => pf.AllegroProductId.Value)
                    .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());
            }
            else
            {
                productFlagIds = await _context.ProductFlags.AsNoTracking()
                    .Where(pf => pf.ProductId.HasValue && productIds.Contains(pf.ProductId.Value))
                    .GroupBy(pf => pf.ProductId.Value)
                    .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());
            }

            var result = new Dictionary<int, string>();
            foreach (var kvp in productFlagIds)
            {
                var names = kvp.Value
                    .Where(id => allFlags.ContainsKey(id))
                    .Select(id => allFlags[id])
                    .OrderBy(n => n);
                result[kvp.Key] = string.Join(", ", names);
            }
            return result;
        }

        // ====================================================================
        // COMPARISON EXPORT (Ceneo / Google) + flagi
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

            // Flagi dla wszystkich produktów sklepu
            var allProductIds = await _context.Products.AsNoTracking()
                .Where(p => p.StoreId == storeId && p.IsScrapable)
                .Select(p => p.ProductId)
                .ToListAsync();
            var flagsDict = await LoadFlagsCsvForProductsAsync(storeId, allProductIds, false);

            using var workbook = new XSSFWorkbook();
            var styles = CreateExportStyles(workbook);

            int totalScraps = scraps.Count;
            int processedScraps = 0;
            int grandTotalPrices = 0;

            // Zapobieganie kolizji nazw arkuszy — jeśli mamy wiele scrapów tego samego dnia,
            // dla wszystkich z tej daty dodajemy godzinę do nazwy arkusza (spójnie w obrębie scrapu).
            var dateCounts = scraps.GroupBy(s => s.Date.Date).ToDictionary(g => g.Key, g => g.Count());

            foreach (var scrap in scraps)
            {
                bool ambiguousDate = dateCounts[scrap.Date.Date] > 1;
                var scrapDateStr = ambiguousDate
                    ? scrap.Date.ToString("dd.MM.yyyy HH-mm")
                    : scrap.Date.ToString("dd.MM.yyyy");
                var scrapDateShort = ambiguousDate
                    ? scrap.Date.ToString("dd.MM HH-mm")
                    : scrap.Date.ToString("dd.MM");

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
                    var sheet = workbook.CreateSheet(scrapDateStr);
                    WritePriceExportSheet(sheet, exportRows, flagsDict, styles);
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
        // ALLEGRO EXPORT + flagi
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

            var allProductIds = await _context.AllegroProducts.AsNoTracking()
                .Where(p => p.StoreId == storeId && p.IsScrapable)
                .Select(p => p.AllegroProductId)
                .ToListAsync();
            var flagsDict = await LoadFlagsCsvForProductsAsync(storeId, allProductIds, true);

            using var workbook = new XSSFWorkbook();
            var styles = CreateExportStyles(workbook);

            int totalScraps = scraps.Count;
            int processedScraps = 0;
            int grandTotalPrices = 0;

            // Zapobieganie kolizji nazw arkuszy — jeśli mamy wiele scrapów tego samego dnia,
            // dla wszystkich z tej daty dodajemy godzinę do nazwy arkusza (spójnie w obrębie scrapu).
            var dateCounts = scraps.GroupBy(s => s.Date.Date).ToDictionary(g => g.Key, g => g.Count());

            foreach (var scrap in scraps)
            {
                bool ambiguousDate = dateCounts[scrap.Date.Date] > 1;
                var scrapDateStr = ambiguousDate
                    ? scrap.Date.ToString("dd.MM.yyyy HH-mm")
                    : scrap.Date.ToString("dd.MM.yyyy");
                var scrapDateShort = ambiguousDate
                    ? scrap.Date.ToString("dd.MM HH-mm")
                    : scrap.Date.ToString("dd.MM");

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

                    var dedupedForCompetition = rawData
                        .GroupBy(x => !string.IsNullOrWhiteSpace(x.Ean)
                            ? $"ean:{x.Ean.Trim()}"
                            : $"pid:{x.AllegroProductId}")
                        .SelectMany(g =>
                        {
                            var items = g.ToList();
                            var result = new List<RawAllegroExportEntry>();

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
                        ProductId = x.AllegroProductId,
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
                    var sheet = workbook.CreateSheet(scrapDateStr);
                    WriteAllegroPriceExportSheet(sheet, exportRows, flagsDict, styles);
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
        // NOWY: PRICE CHANGE EXPORT — zip z 2 xlsx (flat + block)
        // Każdy zawiera 2 arkusze referencji: vs Oferta, vs Zakup + Podsumowanie
        // ====================================================================

        private async Task<(byte[] FileContent, string FileName, string ContentType)> GeneratePriceChangeExportAsync(
            int storeId, ExportMultiRequest request, bool isMarketplace)
        {
            var connectionId = request.ConnectionId;

            var store = await _context.Stores.AsNoTracking()
                .Where(s => s.StoreId == storeId)
                .Select(s => new { s.StoreName, s.StoreNameAllegro })
                .FirstOrDefaultAsync();

            var storeName = store?.StoreName ?? "";

            // Wczytaj scrapy chronologicznie
            List<(int Id, DateTime Date)> scraps;
            if (isMarketplace)
            {
                scraps = (await _context.AllegroScrapeHistories.AsNoTracking()
                    .Where(sh => request.ScrapIds.Contains(sh.Id) && sh.StoreId == storeId)
                    .OrderBy(sh => sh.Date)
                    .Select(sh => new { sh.Id, sh.Date })
                    .ToListAsync())
                    .Select(x => (x.Id, x.Date)).ToList();
            }
            else
            {
                scraps = (await _context.ScrapHistories.AsNoTracking()
                    .Where(sh => request.ScrapIds.Contains(sh.Id) && sh.StoreId == storeId)
                    .OrderBy(sh => sh.Date)
                    .Select(sh => new { sh.Id, sh.Date })
                    .ToListAsync())
                    .Select(x => (x.Id, x.Date)).ToList();
            }

            if (!scraps.Any())
                throw new InvalidOperationException("Nie znaleziono wybranych analiz.");

            await SendExportProgress(connectionId, new
            {
                step = "processing",
                currentIndex = 0,
                totalScraps = scraps.Count,
                scrapDate = "",
                priceCount = 0,
                grandTotalPrices = 0,
                percentComplete = 5
            });

            // Zbierz matrix danych (produkty × konkurenci × scrapy)
            List<PriceChangeProduct> matrix;
            if (isMarketplace)
                matrix = await BuildPriceChangeMatrixAllegroAsync(storeId, store?.StoreNameAllegro ?? "", scraps, connectionId);
            else
                matrix = await BuildPriceChangeMatrixComparisonAsync(storeId, storeName, scraps, connectionId);

            int grandTotalPrices = matrix.Sum(p => p.Competitors.Sum(c => c.PricePerScrap.Count(v => v.Value.HasValue)));

            await SendExportProgress(connectionId, new
            {
                step = "writing",
                currentIndex = scraps.Count,
                totalScraps = scraps.Count,
                scrapDate = "",
                priceCount = 0,
                grandTotalPrices,
                percentComplete = 80
            });

            // Zbuduj jeden plik xlsx z wieloma zakładkami
            var xlsxBytes = BuildPriceChangeWorkbookFlat(matrix, scraps, storeName, isMarketplace);

            var dateRange = scraps.Count == 1
                ? scraps[0].Date.ToString("yyyy-MM-dd")
                : $"{scraps.First().Date:yyyy-MM-dd}_do_{scraps.Last().Date:yyyy-MM-dd}";

            var label = isMarketplace ? "Allegro" : "Ceneo-Google";
            var fileName = $"Analiza_Zmian_Cen_{label}_{storeName}_{dateRange}.xlsx";

            await SendExportProgress(connectionId, new
            {
                step = "finalizing",
                currentIndex = scraps.Count,
                totalScraps = scraps.Count,
                scrapDate = "",
                priceCount = 0,
                grandTotalPrices,
                percentComplete = 100
            });

            return (xlsxBytes, fileName, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        }

        // ====================================================================
        // BUDOWA MATRIX — COMPARISON
        // ====================================================================

        private async Task<List<PriceChangeProduct>> BuildPriceChangeMatrixComparisonAsync(
            int storeId, string storeName, List<(int Id, DateTime Date)> scraps, string connectionId)
        {
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

            // Pobierz wszystkie produkty sklepu
            var products = await _context.Products.AsNoTracking()
                .Where(p => p.StoreId == storeId && p.IsScrapable)
                .Select(p => new
                {
                    p.ProductId,
                    p.ProductName,
                    p.Producer,
                    p.Ean,
                    p.CatalogNumber,
                    p.ExternalId,
                    p.MarginPrice
                })
                .ToListAsync();

            var productIds = products.Select(p => p.ProductId).ToList();
            var flagsDict = await LoadFlagsCsvForProductsAsync(storeId, productIds, false);

            var matrix = new Dictionary<int, PriceChangeProduct>();
            foreach (var p in products)
            {
                matrix[p.ProductId] = new PriceChangeProduct
                {
                    ProductId = p.ProductId,
                    ProductName = p.ProductName ?? "",
                    Producer = p.Producer ?? "",
                    Ean = p.Ean ?? "",
                    Sku = p.CatalogNumber ?? "",
                    ExternalId = p.ExternalId,
                    PurchasePrice = p.MarginPrice,
                    FlagsCsv = flagsDict.GetValueOrDefault(p.ProductId, ""),
                    OurPricePerScrap = new Dictionary<int, decimal?>(),
                    CompetitorsByKey = new Dictionary<string, PriceChangeCompetitor>(StringComparer.OrdinalIgnoreCase)
                };
                foreach (var s in scraps) matrix[p.ProductId].OurPricePerScrap[s.Id] = null;
            }

            int idx = 0;
            int runningPriceCount = 0;
            foreach (var scrap in scraps)
            {
                idx++;
                await SendExportProgress(connectionId, new
                {
                    step = "processing",
                    currentIndex = idx,
                    totalScraps = scraps.Count,
                    scrapDate = scrap.Date.ToString("dd.MM.yyyy HH:mm"),
                    priceCount = 0,
                    grandTotalPrices = runningPriceCount,
                    percentComplete = (int)(5 + (double)idx / scraps.Count * 70)
                });

                var query = from p in _context.Products.AsNoTracking()
                            join ph in _context.PriceHistories.AsNoTracking()
                                on p.ProductId equals ph.ProductId
                            where p.StoreId == storeId && p.IsScrapable && ph.ScrapHistoryId == scrap.Id
                            select new
                            {
                                p.ProductId,
                                ph.Price,
                                ph.StoreName,
                                ph.IsGoogle,
                                ph.ShippingCostNum
                            };

                if (activePreset != null)
                {
                    if (!activePreset.SourceGoogle) query = query.Where(x => x.IsGoogle != true);
                    if (!activePreset.SourceCeneo) query = query.Where(x => x.IsGoogle == true);
                }

                var rawList = await query.ToListAsync();
                runningPriceCount += rawList.Count;

                await SendExportProgress(connectionId, new
                {
                    step = "processing",
                    currentIndex = idx,
                    totalScraps = scraps.Count,
                    scrapDate = scrap.Date.ToString("dd.MM.yyyy HH:mm"),
                    priceCount = rawList.Count,
                    grandTotalPrices = runningPriceCount,
                    percentComplete = (int)(5 + (double)idx / scraps.Count * 70)
                });

                foreach (var row in rawList)
                {
                    if (!matrix.TryGetValue(row.ProductId, out var product)) continue;

                    bool isMe = row.StoreName != null && row.StoreName.ToLower().Trim() == myStoreNameLower;
                    decimal finalPrice = (priceValues.UsePriceWithDelivery && row.ShippingCostNum.HasValue)
                        ? row.Price + row.ShippingCostNum.Value : row.Price;

                    if (isMe)
                    {
                        // Moja cena — minimum ze źródeł (Google + Ceneo)
                        var current = product.OurPricePerScrap[scrap.Id];
                        if (!current.HasValue || finalPrice < current.Value)
                            product.OurPricePerScrap[scrap.Id] = finalPrice;
                        continue;
                    }

                    // Filtr presetu dla konkurenta
                    if (competitorItemsDict != null)
                    {
                        DataSourceType src = row.IsGoogle == true ? DataSourceType.Google : DataSourceType.Ceneo;
                        var key = (Store: (row.StoreName ?? "").ToLower().Trim(), Source: src);
                        bool keep;
                        if (competitorItemsDict.TryGetValue(key, out bool use)) keep = use;
                        else keep = activePreset.UseUnmarkedStores;
                        if (!keep) continue;
                    }

                    var displayName = row.StoreName ?? (row.IsGoogle ? "Google" : "Ceneo");
                    var compKey = displayName.ToLower().Trim();

                    if (!product.CompetitorsByKey.TryGetValue(compKey, out var comp))
                    {
                        comp = new PriceChangeCompetitor
                        {
                            StoreName = displayName,
                            PricePerScrap = new Dictionary<int, decimal?>()
                        };
                        foreach (var s in scraps) comp.PricePerScrap[s.Id] = null;
                        product.CompetitorsByKey[compKey] = comp;
                    }

                    // Minimum per scrap (Google/Ceneo dla tego samego sklepu)
                    var existing = comp.PricePerScrap[scrap.Id];
                    if (!existing.HasValue || finalPrice < existing.Value)
                        comp.PricePerScrap[scrap.Id] = finalPrice;
                }
            }

            // Ustaw LatestOurPrice
            foreach (var p in matrix.Values)
            {
                for (int i = scraps.Count - 1; i >= 0; i--)
                {
                    var pr = p.OurPricePerScrap[scraps[i].Id];
                    if (pr.HasValue) { p.LatestOurPrice = pr; break; }
                }
                p.Competitors = p.CompetitorsByKey.Values
                    .OrderBy(c => c.StoreName)
                    .ToList();
            }

            return matrix.Values
                .Where(p => p.OurPricePerScrap.Any(v => v.Value.HasValue) || p.Competitors.Any(c => c.PricePerScrap.Any(v => v.Value.HasValue)))
                .OrderBy(p => p.ProductName)
                .ToList();
        }

        // ====================================================================
        // BUDOWA MATRIX — ALLEGRO
        // ====================================================================

        private async Task<List<PriceChangeProduct>> BuildPriceChangeMatrixAllegroAsync(
            int storeId, string storeNameAllegro, List<(int Id, DateTime Date)> scraps, string connectionId)
        {
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

            bool includeNoDelivery = activePreset?.IncludeNoDeliveryInfo ?? true;
            int minDelivery = activePreset?.MinDeliveryDays ?? 0;
            int maxDelivery = activePreset?.MaxDeliveryDays ?? 31;

            var products = await _context.AllegroProducts.AsNoTracking()
                .Where(p => p.StoreId == storeId && p.IsScrapable)
                .Select(p => new
                {
                    p.AllegroProductId,
                    p.AllegroProductName,
                    p.Producer,
                    p.AllegroEan,
                    p.AllegroSku,
                    p.IdOnAllegro,
                    p.AllegroMarginPrice
                })
                .ToListAsync();

            var productIds = products.Select(p => p.AllegroProductId).ToList();
            var flagsDict = await LoadFlagsCsvForProductsAsync(storeId, productIds, true);

            // Grupuj po EAN (priorytet), fallback po ID
            var productGroups = products
                .GroupBy(p => !string.IsNullOrWhiteSpace(p.AllegroEan) ? $"ean:{p.AllegroEan.Trim()}" : $"pid:{p.AllegroProductId}")
                .ToList();

            var matrix = new Dictionary<string, PriceChangeProduct>();
            var productGroupKeyById = new Dictionary<int, string>();

            foreach (var g in productGroups)
            {
                var rep = g.First();
                var key = g.Key;
                matrix[key] = new PriceChangeProduct
                {
                    ProductId = rep.AllegroProductId,
                    ProductName = rep.AllegroProductName ?? "",
                    Producer = rep.Producer ?? "",
                    Ean = rep.AllegroEan ?? "",
                    Sku = rep.AllegroSku ?? "",
                    ExternalId = null,
                    PurchasePrice = rep.AllegroMarginPrice,
                    FlagsCsv = string.Join(", ", g.Select(p => flagsDict.GetValueOrDefault(p.AllegroProductId, ""))
                                                  .Where(s => !string.IsNullOrEmpty(s))
                                                  .Distinct()),
                    OurPricePerScrap = new Dictionary<int, decimal?>(),
                    CompetitorsByKey = new Dictionary<string, PriceChangeCompetitor>(StringComparer.OrdinalIgnoreCase)
                };
                foreach (var s in scraps) matrix[key].OurPricePerScrap[s.Id] = null;

                foreach (var p in g) productGroupKeyById[p.AllegroProductId] = key;
            }

            int idx = 0;
            int runningPriceCount = 0;
            foreach (var scrap in scraps)
            {
                idx++;
                await SendExportProgress(connectionId, new
                {
                    step = "processing",
                    currentIndex = idx,
                    totalScraps = scraps.Count,
                    scrapDate = scrap.Date.ToString("dd.MM.yyyy HH:mm"),
                    priceCount = 0,
                    grandTotalPrices = runningPriceCount,
                    percentComplete = (int)(5 + (double)idx / scraps.Count * 70)
                });

                var rawPrices = await _context.AllegroPriceHistories.AsNoTracking()
                    .Where(ph => ph.AllegroScrapeHistoryId == scrap.Id && productIds.Contains(ph.AllegroProductId))
                    .Select(ph => new
                    {
                        ph.AllegroProductId,
                        ph.Price,
                        ph.SellerName,
                        ph.DeliveryTime,
                        ph.IdAllegro
                    })
                    .ToListAsync();

                // Deduplikacja
                rawPrices = rawPrices
                    .GroupBy(x => new { x.AllegroProductId, x.IdAllegro })
                    .Select(g => g.First())
                    .ToList();

                runningPriceCount += rawPrices.Count;
                await SendExportProgress(connectionId, new
                {
                    step = "processing",
                    currentIndex = idx,
                    totalScraps = scraps.Count,
                    scrapDate = scrap.Date.ToString("dd.MM.yyyy HH:mm"),
                    priceCount = rawPrices.Count,
                    grandTotalPrices = runningPriceCount,
                    percentComplete = (int)(5 + (double)idx / scraps.Count * 70)
                });

                foreach (var row in rawPrices)
                {
                    if (!productGroupKeyById.TryGetValue(row.AllegroProductId, out var groupKey)) continue;
                    var product = matrix[groupKey];

                    bool isMe = !string.IsNullOrEmpty(row.SellerName) &&
                                row.SellerName.Equals(storeNameAllegro, StringComparison.OrdinalIgnoreCase);

                    if (isMe)
                    {
                        if (row.Price <= 0) continue;
                        var cur = product.OurPricePerScrap[scrap.Id];
                        if (!cur.HasValue || row.Price < cur.Value)
                            product.OurPricePerScrap[scrap.Id] = row.Price;
                        continue;
                    }

                    // Filtr presetu
                    if (activePreset != null)
                    {
                        if (row.DeliveryTime.HasValue)
                        {
                            if (row.DeliveryTime.Value < minDelivery || row.DeliveryTime.Value > maxDelivery) continue;
                        }
                        else if (!includeNoDelivery) continue;

                        if (competitorRules != null)
                        {
                            var sellerLower = (row.SellerName ?? "").ToLower().Trim();
                            if (competitorRules.TryGetValue(sellerLower, out bool use))
                            {
                                if (!use) continue;
                            }
                            else if (!activePreset.UseUnmarkedStores) continue;
                        }
                    }

                    if (row.Price <= 0) continue;

                    var compKey = (row.SellerName ?? "").ToLower().Trim();
                    if (string.IsNullOrEmpty(compKey)) continue;

                    if (!product.CompetitorsByKey.TryGetValue(compKey, out var comp))
                    {
                        comp = new PriceChangeCompetitor
                        {
                            StoreName = row.SellerName,
                            PricePerScrap = new Dictionary<int, decimal?>()
                        };
                        foreach (var s in scraps) comp.PricePerScrap[s.Id] = null;
                        product.CompetitorsByKey[compKey] = comp;
                    }

                    var existing = comp.PricePerScrap[scrap.Id];
                    if (!existing.HasValue || row.Price < existing.Value)
                        comp.PricePerScrap[scrap.Id] = row.Price;
                }
            }

            foreach (var p in matrix.Values)
            {
                for (int i = scraps.Count - 1; i >= 0; i--)
                {
                    var pr = p.OurPricePerScrap[scraps[i].Id];
                    if (pr.HasValue) { p.LatestOurPrice = pr; break; }
                }
                p.Competitors = p.CompetitorsByKey.Values
                    .OrderBy(c => c.StoreName)
                    .ToList();
            }

            return matrix.Values
                .Where(p => p.OurPricePerScrap.Any(v => v.Value.HasValue) || p.Competitors.Any(c => c.PricePerScrap.Any(v => v.Value.HasValue)))
                .OrderBy(p => p.ProductName)
                .ToList();
        }

        // ====================================================================
        // STATYSTYKI NA PRODUKT/KONKURENTA
        // ====================================================================

        private enum PriceChangeRef { Offer, Purchase }

        private class CompetitorStats
        {
            public int? FirstViolationScrapId { get; set; }
            public DateTime? FirstViolationScrapDate { get; set; }
            public int ViolationCount { get; set; }
            public decimal MaxViolationPercent { get; set; }
            public decimal AvgViolationPercent { get; set; }
        }

        private CompetitorStats ComputeCompetitorStats(
            PriceChangeProduct product, PriceChangeCompetitor comp,
            List<(int Id, DateTime Date)> scraps, PriceChangeRef refType)
        {
            var stats = new CompetitorStats();
            decimal sumPct = 0m;

            foreach (var scrap in scraps)
            {
                decimal? reference = GetReference(product, scrap.Id, refType);
                if (!reference.HasValue) continue;

                var price = comp.PricePerScrap[scrap.Id];
                if (!price.HasValue) continue;
                if (price.Value >= reference.Value) continue;

                stats.ViolationCount++;
                if (stats.FirstViolationScrapId == null)
                {
                    stats.FirstViolationScrapId = scrap.Id;
                    stats.FirstViolationScrapDate = scrap.Date;
                }
                if (reference.Value > 0)
                {
                    var violationPct = (reference.Value - price.Value) / reference.Value * 100m;
                    if (violationPct > stats.MaxViolationPercent)
                        stats.MaxViolationPercent = Math.Round(violationPct, 2);
                    sumPct += violationPct;
                }
            }

            if (stats.ViolationCount > 0)
                stats.AvgViolationPercent = Math.Round(sumPct / stats.ViolationCount, 2);

            return stats;
        }

        private decimal? GetReference(PriceChangeProduct product, int scrapId, PriceChangeRef refType)
        {
            if (refType == PriceChangeRef.Offer)
                return product.OurPricePerScrap[scrapId];
            return product.PurchasePrice;
        }

        /// <summary>
        /// Dla każdego produktu znajduje NAJWCZEŚNIEJSZY scrap w którym ktoś złamał referencję,
        /// a następnie oznacza wszystkich konkurentów którzy w tym scrapie byli poniżej referencji.
        /// Wynik: dictionary (productId, compKey) -> bool.
        /// </summary>
        private Dictionary<(int prodId, string compKey), bool> DetermineFirstBreakers(
            List<PriceChangeProduct> matrix, List<(int Id, DateTime Date)> scraps, PriceChangeRef refType)
        {
            var result = new Dictionary<(int, string), bool>();

            foreach (var p in matrix)
            {
                int? earliestScrapId = null;
                foreach (var scrap in scraps)
                {
                    decimal? reference = GetReference(p, scrap.Id, refType);
                    if (!reference.HasValue) continue;

                    bool anyBroke = false;
                    foreach (var c in p.Competitors)
                    {
                        var price = c.PricePerScrap[scrap.Id];
                        if (price.HasValue && price.Value < reference.Value)
                        {
                            anyBroke = true;
                            break;
                        }
                    }
                    if (anyBroke) { earliestScrapId = scrap.Id; break; }
                }

                if (!earliestScrapId.HasValue)
                {
                    foreach (var c in p.Competitors)
                        result[(p.ProductId, c.StoreName.ToLower().Trim())] = false;
                    continue;
                }

                decimal? refAtEarliest = GetReference(p, earliestScrapId.Value, refType);
                foreach (var c in p.Competitors)
                {
                    var pr = c.PricePerScrap[earliestScrapId.Value];
                    bool isFirst = pr.HasValue && refAtEarliest.HasValue && pr.Value < refAtEarliest.Value;
                    result[(p.ProductId, c.StoreName.ToLower().Trim())] = isFirst;
                }
            }

            return result;
        }

        // ====================================================================
        // ZAPIS — PŁASKI LAYOUT (workbook)
        // ====================================================================

        // ====================================================================
        // BUDOWA WORKBOOKA PRICE CHANGE — 5 ZAKŁADEK
        // vs Oferta, vs Zakup, Zbieżność, Podsumowanie, Legenda
        // ====================================================================

        private byte[] BuildPriceChangeWorkbookFlat(List<PriceChangeProduct> matrix, List<(int Id, DateTime Date)> scraps, string storeName, bool isMarketplace)
        {
            using var wb = new XSSFWorkbook();
            var styles = CreateExportStyles(wb);

            var firstBreakersOffer = DetermineFirstBreakers(matrix, scraps, PriceChangeRef.Offer);
            var firstBreakersPurchase = DetermineFirstBreakers(matrix, scraps, PriceChangeRef.Purchase);

            // Wstępnie wylicz stats per (produkt, konkurent, referencja) — wykorzystywane przez wszystkie zakładki
            var classMap = BuildClassificationMap(matrix, scraps);

            WritePriceChangeFlatSheet(wb, "vs Oferta", matrix, scraps, storeName,
                PriceChangeRef.Offer, firstBreakersOffer, classMap, styles, isMarketplace);

            WritePriceChangeFlatSheet(wb, "vs Zakup", matrix, scraps, storeName,
                PriceChangeRef.Purchase, firstBreakersPurchase, classMap, styles, isMarketplace);

            WritePriceChangeComplianceSheet(wb, "Zbieżność", matrix, scraps, classMap, styles, isMarketplace);

            WritePriceChangeSummarySheet(wb, "Podsumowanie", matrix, scraps,
                firstBreakersOffer, firstBreakersPurchase, classMap, styles);

            WritePriceChangeLegendSheet(wb, "Legenda", styles, isMarketplace);

            using var ms = new MemoryStream();
            wb.Write(ms);
            return ms.ToArray();
        }

        // ====================================================================
        // ZAKŁADKA: vs Oferta / vs Zakup — płaska siatka z dodatkową statystyką
        // ====================================================================

        private void WritePriceChangeFlatSheet(XSSFWorkbook wb, string sheetName,
            List<PriceChangeProduct> matrix, List<(int Id, DateTime Date)> scraps, string storeName,
            PriceChangeRef refType, Dictionary<(int prodId, string compKey), bool> firstBreakers,
            Dictionary<(int prodId, string compKey), ClassificationEntry> classMap,
            ExportStyles styles, bool isMarketplace)
        {
            var sheet = wb.CreateSheet(sheetName);

            // --------------------------------------------------------------------
            // Wstęp — intro box (3 wiersze), tłumaczenie co tu jest
            // --------------------------------------------------------------------
            var refLabel = refType == PriceChangeRef.Offer ? "cenę oferty" : "cenę zakupu";
            var refShortLabel = refType == PriceChangeRef.Offer ? "Oferta" : "Zakup";

            var introRow = sheet.CreateRow(0);
            introRow.HeightInPoints = 22;
            var introCell = introRow.CreateCell(0);
            introCell.SetCellValue($"Kto i kiedy złamał {refLabel} — {(isMarketplace ? "Allegro" : "Google/Ceneo")} / {storeName}");
            introCell.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 8 + scraps.Count + 6));

            var descRow = sheet.CreateRow(1);
            descRow.HeightInPoints = 30;
            var descCell = descRow.CreateCell(0);
            descCell.SetCellValue(
                "Każdy wiersz = konkurent dla danego produktu. Kolorowe komórki w kolumnach dat = konkurent miał cenę niższą od " + refLabel +
                ". Intensywność czerwieni = skala wyłamu (od 0% do 50%+). Złota komórka (🥇) = pierwszy, który złamał w całym oknie analizy. " +
                "Zobacz zakładki: [Zbieżność] — kto nas kopiuje / odstaje, [Podsumowanie] — ranking konkurentów, [Legenda] — opis kolorów i kolumn.");
            descCell.CellStyle = styles.Default;
            var wrap = wb.CreateCellStyle();
            wrap.CloneStyleFrom(styles.Default);
            wrap.WrapText = true;
            wrap.VerticalAlignment = VerticalAlignment.Top;
            descCell.CellStyle = wrap;
            sheet.AddMergedRegion(new CellRangeAddress(1, 1, 0, 8 + scraps.Count + 6));

            // Wiersz 2 — pusty
            int headerRowIdx = 3;

            // --------------------------------------------------------------------
            // Nagłówek
            // --------------------------------------------------------------------
            var headerRow = sheet.CreateRow(headerRowIdx);
            headerRow.HeightInPoints = 28;

            string[] metaHeaders = new[]
            {
                "Produkt", "EAN", "SKU", "Marka", "Flagi",
                $"Nasza cena ({refShortLabel})",
                "Sklep (konkurent)", "Typ źródła"
            };

            int col = 0;
            foreach (var h in metaHeaders)
            {
                var c = headerRow.CreateCell(col++);
                c.SetCellValue(h);
                c.CellStyle = styles.HeaderDark;
            }

            int scrapStartCol = col;
            foreach (var scrap in scraps)
            {
                var c = headerRow.CreateCell(col++);
                c.SetCellValue(scrap.Date.ToString("dd.MM HH:mm"));
                c.CellStyle = styles.HeaderDark;
            }
            int scrapEndCol = col - 1;

            string[] statHeaders = new[]
            {
                "Wyłomów (dni)",
                "Max % poniżej",
                "Średnie % poniżej",
                "Pierwszy wyłom (data)",
                "🥇 Pierwszy",
                "Status (ost. scrap)"
            };

            foreach (var h in statHeaders)
            {
                var c = headerRow.CreateCell(col++);
                c.SetCellValue(h);
                c.CellStyle = styles.HeaderDark;
            }
            int lastColIdx = col - 1;

            // --------------------------------------------------------------------
            // Wiersze danych
            // --------------------------------------------------------------------
            int rowIdx = headerRowIdx + 1;

            foreach (var product in matrix)
            {
                // Określ porządek konkurentów: 🥇 pierwsi, potem po dacie złamania, potem alfabetycznie
                var ordered = product.Competitors
                    .Select(c =>
                    {
                        var compKey = c.StoreName.ToLower().Trim();
                        bool isFirst = firstBreakers.GetValueOrDefault((product.ProductId, compKey), false);
                        var stats = ComputeCompetitorStats(product, c, scraps, refType);
                        return new { Comp = c, IsFirst = isFirst, Stats = stats, CompKey = compKey };
                    })
                    .OrderByDescending(x => x.IsFirst)
                    .ThenBy(x => x.Stats.FirstViolationScrapDate ?? DateTime.MaxValue)
                    .ThenBy(x => x.Comp.StoreName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var entry in ordered)
                {
                    var row = sheet.CreateRow(rowIdx++);
                    col = 0;

                    row.CreateCell(col++).SetCellValue(product.ProductName ?? "");
                    row.CreateCell(col++).SetCellValue(product.Ean ?? "");
                    row.CreateCell(col++).SetCellValue(product.Sku ?? "");
                    row.CreateCell(col++).SetCellValue(product.Producer ?? "");
                    row.CreateCell(col++).SetCellValue(product.FlagsCsv ?? "");

                    decimal? ourRef = null;
                    if (refType == PriceChangeRef.Offer) ourRef = product.LatestOurPrice;
                    else ourRef = product.PurchasePrice;

                    var refCell = row.CreateCell(col++);
                    if (ourRef.HasValue)
                    {
                        refCell.SetCellValue((double)ourRef.Value);
                        refCell.CellStyle = styles.BaselineCurrency;
                    }
                    else
                    {
                        refCell.SetCellValue("—");
                        refCell.CellStyle = styles.NoDataCell;
                    }

                    row.CreateCell(col++).SetCellValue(entry.Comp.StoreName ?? "");
                    row.CreateCell(col++).SetCellValue(isMarketplace ? "Marketplace (Allegro)" : "Porównywarka (Google/Ceneo)");

                    // Kolumny per scrap
                    foreach (var scrap in scraps)
                    {
                        var cell = row.CreateCell(col++);
                        var pr = entry.Comp.PricePerScrap.GetValueOrDefault(scrap.Id);
                        var reference = GetReference(product, scrap.Id, refType);

                        if (!pr.HasValue)
                        {
                            cell.SetCellValue("—");
                            cell.CellStyle = styles.NoDataCell;
                            continue;
                        }

                        cell.SetCellValue((double)pr.Value);

                        if (!reference.HasValue)
                        {
                            cell.CellStyle = styles.ComplianceCurrency;
                            continue;
                        }

                        if (pr.Value >= reference.Value)
                        {
                            // Konkurent >= nas — zgodność
                            cell.CellStyle = styles.ComplianceCurrency;
                        }
                        else
                        {
                            decimal pct = (reference.Value - pr.Value) / reference.Value * 100m;
                            bool isFirstScrapAtBreak = entry.IsFirst && entry.Stats.FirstViolationScrapId == scrap.Id;
                            cell.CellStyle = GetViolationStyle(styles, pct, isFirstScrapAtBreak);
                        }
                    }

                    // Kolumny statystyczne
                    var stats = entry.Stats;
                    row.CreateCell(col++).SetCellValue(stats.ViolationCount);

                    var maxCell = row.CreateCell(col++);
                    if (stats.ViolationCount > 0)
                    {
                        maxCell.SetCellValue((double)stats.MaxViolationPercent);
                        maxCell.CellStyle = styles.PercentRed;
                    }
                    else
                    {
                        maxCell.SetCellValue("—");
                    }

                    var avgCell = row.CreateCell(col++);
                    if (stats.ViolationCount > 0)
                    {
                        avgCell.SetCellValue((double)stats.AvgViolationPercent);
                        avgCell.CellStyle = styles.Percent;
                    }
                    else
                    {
                        avgCell.SetCellValue("—");
                    }

                    var firstDateCell = row.CreateCell(col++);
                    if (stats.FirstViolationScrapDate.HasValue)
                        firstDateCell.SetCellValue(stats.FirstViolationScrapDate.Value.ToString("dd.MM.yyyy HH:mm"));
                    else
                        firstDateCell.SetCellValue("—");

                    var firstCell = row.CreateCell(col++);
                    firstCell.SetCellValue(entry.IsFirst ? "🥇 TAK" : "");
                    if (entry.IsFirst) firstCell.CellStyle = styles.FirstBreakerMarker;

                    // Status — klasyfikacja na ostatnim scrapie wg wybranej referencji
                    var classKey = (product.ProductId, entry.CompKey);
                    var cls = classMap.GetValueOrDefault(classKey);
                    var statusCell = row.CreateCell(col++);
                    var statusInfo = refType == PriceChangeRef.Offer ? cls?.OfferLast : cls?.PurchaseLast;
                    statusCell.SetCellValue(statusInfo?.Label ?? "—");
                    if (statusInfo != null) statusCell.CellStyle = GetClassificationStyle(styles, statusInfo.Code);
                }
            }

            int lastDataRowIdx = rowIdx - 1;

            // --------------------------------------------------------------------
            // Freeze + AutoFilter
            // --------------------------------------------------------------------
            sheet.CreateFreezePane(scrapStartCol, headerRowIdx + 1);

            if (lastDataRowIdx >= headerRowIdx + 1)
            {
                sheet.SetAutoFilter(new CellRangeAddress(headerRowIdx, lastDataRowIdx, 0, lastColIdx));
            }

            // Szerokości kolumn
            sheet.SetColumnWidth(0, 40 * 256); // Produkt
            sheet.SetColumnWidth(1, 15 * 256); // EAN
            sheet.SetColumnWidth(2, 15 * 256); // SKU
            sheet.SetColumnWidth(3, 18 * 256); // Marka
            sheet.SetColumnWidth(4, 22 * 256); // Flagi
            sheet.SetColumnWidth(5, 16 * 256); // Nasza cena
            sheet.SetColumnWidth(6, 24 * 256); // Sklep
            sheet.SetColumnWidth(7, 22 * 256); // Typ

            for (int i = scrapStartCol; i <= scrapEndCol; i++)
                sheet.SetColumnWidth(i, 13 * 256);

            for (int i = scrapEndCol + 1; i <= lastColIdx; i++)
                sheet.SetColumnWidth(i, 17 * 256);
        }

        // ====================================================================
        // ZAKŁADKA: Zbieżność — kto nas kopiuje, kto odstaje, kto się zbliża
        // ====================================================================

        private void WritePriceChangeComplianceSheet(XSSFWorkbook wb, string sheetName,
            List<PriceChangeProduct> matrix, List<(int Id, DateTime Date)> scraps,
            Dictionary<(int prodId, string compKey), ClassificationEntry> classMap,
            ExportStyles styles, bool isMarketplace)
        {
            var sheet = wb.CreateSheet(sheetName);

            // Intro
            var introRow = sheet.CreateRow(0);
            introRow.HeightInPoints = 22;
            var introCell = introRow.CreateCell(0);
            introCell.SetCellValue("Zbieżność cen — jak blisko/daleko konkurenci poruszają się względem nas");
            introCell.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 14));

            var descRow = sheet.CreateRow(1);
            descRow.HeightInPoints = 45;
            var descCell = descRow.CreateCell(0);
            descCell.SetCellValue(
                "Dla każdej pary (produkt × konkurent) liczone są dni (scrapy), w których cena konkurenta była względem nas: " +
                "IDENTYCZNA (±0.5%), BLISKO (±2%), NIŻEJ (>2% poniżej), WYŻEJ (>2% powyżej). Procenty = udział dni z daną klasyfikacją. " +
                "Osobno dla referencji 'Oferta' (nasza cena na rynku) i 'Zakup' (nasza cena zakupowa — MAP). " +
                "Sortuj po kolumnie '% Identyczny (Oferta)' malejąco żeby zobaczyć kto nas najdokładniej kopiuje. " +
                "Sortuj po 'Max % niżej (Oferta)' żeby zobaczyć największych agresorów.");
            var wrap = wb.CreateCellStyle();
            wrap.CloneStyleFrom(styles.Default);
            wrap.WrapText = true;
            wrap.VerticalAlignment = VerticalAlignment.Top;
            descCell.CellStyle = wrap;
            sheet.AddMergedRegion(new CellRangeAddress(1, 1, 0, 14));

            int headerRowIdx = 3;
            var headerRow = sheet.CreateRow(headerRowIdx);
            headerRow.HeightInPoints = 28;

            string[] headers = new[]
            {
                "Produkt", "EAN", "SKU", "Marka",
                "Sklep (konkurent)",
                "% Identyczny (Oferta)",
                "% Blisko (Oferta)",
                "% Niżej (Oferta)",
                "% Wyżej (Oferta)",
                "Max % niżej (Oferta)",
                "% Identyczny (Zakup)",
                "% Blisko (Zakup)",
                "% Niżej (Zakup)",
                "Max % niżej (Zakup)",
                "Status ostatni (Oferta)"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = headerRow.CreateCell(i);
                c.SetCellValue(headers[i]);
                c.CellStyle = styles.HeaderDark;
            }

            int rowIdx = headerRowIdx + 1;

            // Posortujmy wiersze: najpierw po % Identyczny (Oferta) malejąco, potem Max % niżej malejąco
            var allRows = new List<(PriceChangeProduct Prod, PriceChangeCompetitor Comp, ClassificationEntry Cls)>();
            foreach (var product in matrix)
            {
                foreach (var comp in product.Competitors)
                {
                    var key = (product.ProductId, comp.StoreName.ToLower().Trim());
                    if (!classMap.TryGetValue(key, out var cls)) continue;
                    allRows.Add((product, comp, cls));
                }
            }

            foreach (var entry in allRows
                .OrderByDescending(x => x.Cls.OfferIdenticalPct)
                .ThenByDescending(x => x.Cls.OfferMaxBelowPct))
            {
                var row = sheet.CreateRow(rowIdx++);
                int col = 0;

                row.CreateCell(col++).SetCellValue(entry.Prod.ProductName ?? "");
                row.CreateCell(col++).SetCellValue(entry.Prod.Ean ?? "");
                row.CreateCell(col++).SetCellValue(entry.Prod.Sku ?? "");
                row.CreateCell(col++).SetCellValue(entry.Prod.Producer ?? "");
                row.CreateCell(col++).SetCellValue(entry.Comp.StoreName ?? "");

                WritePct(row.CreateCell(col++), entry.Cls.OfferIdenticalPct, styles);
                WritePct(row.CreateCell(col++), entry.Cls.OfferClosePct, styles);
                WritePct(row.CreateCell(col++), entry.Cls.OfferBelowPct, styles);
                WritePct(row.CreateCell(col++), entry.Cls.OfferAbovePct, styles);
                WritePct(row.CreateCell(col++), entry.Cls.OfferMaxBelowPct, styles, tintRed: entry.Cls.OfferMaxBelowPct > 0);

                WritePct(row.CreateCell(col++), entry.Cls.PurchaseIdenticalPct, styles);
                WritePct(row.CreateCell(col++), entry.Cls.PurchaseClosePct, styles);
                WritePct(row.CreateCell(col++), entry.Cls.PurchaseBelowPct, styles);
                WritePct(row.CreateCell(col++), entry.Cls.PurchaseMaxBelowPct, styles, tintRed: entry.Cls.PurchaseMaxBelowPct > 0);

                var lastCell = row.CreateCell(col++);
                var last = entry.Cls.OfferLast;
                lastCell.SetCellValue(last?.Label ?? "—");
                if (last != null) lastCell.CellStyle = GetClassificationStyle(styles, last.Code);
            }

            int lastDataRowIdx = rowIdx - 1;

            sheet.CreateFreezePane(5, headerRowIdx + 1);
            if (lastDataRowIdx >= headerRowIdx + 1)
                sheet.SetAutoFilter(new CellRangeAddress(headerRowIdx, lastDataRowIdx, 0, headers.Length - 1));

            sheet.SetColumnWidth(0, 40 * 256);
            sheet.SetColumnWidth(1, 15 * 256);
            sheet.SetColumnWidth(2, 15 * 256);
            sheet.SetColumnWidth(3, 18 * 256);
            sheet.SetColumnWidth(4, 24 * 256);
            for (int i = 5; i < headers.Length - 1; i++) sheet.SetColumnWidth(i, 15 * 256);
            sheet.SetColumnWidth(headers.Length - 1, 24 * 256);
        }

        private void WritePct(ICell cell, decimal value, ExportStyles styles, bool tintRed = false)
        {
            cell.SetCellValue((double)value);
            if (tintRed && value >= 5m)
                cell.CellStyle = styles.PercentRed;
            else
                cell.CellStyle = styles.Percent;
        }

        // ====================================================================
        // ZAKŁADKA: Podsumowanie — ranking konkurentów wg naruszeń
        // ====================================================================

        private void WritePriceChangeSummarySheet(XSSFWorkbook wb, string sheetName,
            List<PriceChangeProduct> matrix, List<(int Id, DateTime Date)> scraps,
            Dictionary<(int prodId, string compKey), bool> firstBreakersOffer,
            Dictionary<(int prodId, string compKey), bool> firstBreakersPurchase,
            Dictionary<(int prodId, string compKey), ClassificationEntry> classMap,
            ExportStyles styles)
        {
            var sheet = wb.CreateSheet(sheetName);

            var introRow = sheet.CreateRow(0);
            introRow.HeightInPoints = 22;
            var introCell = introRow.CreateCell(0);
            introCell.SetCellValue("Ranking konkurentów — kto najczęściej łamie cenę");
            introCell.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 9));

            var descRow = sheet.CreateRow(1);
            descRow.HeightInPoints = 45;
            var descCell = descRow.CreateCell(0);
            descCell.SetCellValue(
                "Agregat per konkurent — ile produktów ma, w ilu łamie cenę oferty, w ilu łamie cenę zakupu (MAP), " +
                "jak często był pierwszym łamaczem (🥇) oraz ogólne % zbieżności z naszą ceną oferty. " +
                "Sortuj malejąco po 'Produkty z wyłomem Oferty' żeby zobaczyć największych agresorów, " +
                "albo po '% Identyczny (Oferta)' żeby znaleźć tych którzy nas najbardziej śledzą.");
            var wrap = wb.CreateCellStyle();
            wrap.CloneStyleFrom(styles.Default);
            wrap.WrapText = true;
            wrap.VerticalAlignment = VerticalAlignment.Top;
            descCell.CellStyle = wrap;
            sheet.AddMergedRegion(new CellRangeAddress(1, 1, 0, 9));

            int headerRowIdx = 3;
            var headerRow = sheet.CreateRow(headerRowIdx);
            headerRow.HeightInPoints = 28;

            string[] headers = new[]
            {
                "Sklep (konkurent)",
                "Produkty (łącznie)",
                "Produkty z wyłomem Oferty",
                "Produkty z wyłomem Zakupu",
                "🥇 Pierwszy łamacz (Oferta)",
                "🥇 Pierwszy łamacz (Zakup)",
                "Avg % Identyczny (Oferta)",
                "Avg % Blisko (Oferta)",
                "Avg % Niżej (Oferta)",
                "Avg Max % niżej (Oferta)"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = headerRow.CreateCell(i);
                c.SetCellValue(headers[i]);
                c.CellStyle = styles.HeaderDark;
            }

            // Agregacja per konkurent
            var agg = new Dictionary<string, SummaryAgg>(StringComparer.OrdinalIgnoreCase);

            foreach (var product in matrix)
            {
                foreach (var comp in product.Competitors)
                {
                    var compKey = comp.StoreName.ToLower().Trim();
                    if (!agg.TryGetValue(compKey, out var a))
                    {
                        a = new SummaryAgg { StoreName = comp.StoreName };
                        agg[compKey] = a;
                    }
                    a.ProductsTotal++;

                    var statsOffer = ComputeCompetitorStats(product, comp, scraps, PriceChangeRef.Offer);
                    var statsPurchase = ComputeCompetitorStats(product, comp, scraps, PriceChangeRef.Purchase);

                    if (statsOffer.ViolationCount > 0) a.ProductsWithOfferBreach++;
                    if (statsPurchase.ViolationCount > 0) a.ProductsWithPurchaseBreach++;

                    if (firstBreakersOffer.GetValueOrDefault((product.ProductId, compKey), false))
                        a.FirstBreakerOfferCount++;
                    if (firstBreakersPurchase.GetValueOrDefault((product.ProductId, compKey), false))
                        a.FirstBreakerPurchaseCount++;

                    if (classMap.TryGetValue((product.ProductId, compKey), out var cls))
                    {
                        a.SumIdentical += cls.OfferIdenticalPct;
                        a.SumClose += cls.OfferClosePct;
                        a.SumBelow += cls.OfferBelowPct;
                        a.SumMaxBelow += cls.OfferMaxBelowPct;
                        a.ClassCount++;
                    }
                }
            }

            int rowIdx = headerRowIdx + 1;
            foreach (var a in agg.Values
                .OrderByDescending(x => x.ProductsWithOfferBreach)
                .ThenByDescending(x => x.ProductsWithPurchaseBreach)
                .ThenBy(x => x.StoreName, StringComparer.OrdinalIgnoreCase))
            {
                var row = sheet.CreateRow(rowIdx++);
                int col = 0;

                row.CreateCell(col++).SetCellValue(a.StoreName ?? "");
                row.CreateCell(col++).SetCellValue(a.ProductsTotal);
                row.CreateCell(col++).SetCellValue(a.ProductsWithOfferBreach);
                row.CreateCell(col++).SetCellValue(a.ProductsWithPurchaseBreach);
                row.CreateCell(col++).SetCellValue(a.FirstBreakerOfferCount);
                row.CreateCell(col++).SetCellValue(a.FirstBreakerPurchaseCount);

                decimal avgIdent = a.ClassCount > 0 ? a.SumIdentical / a.ClassCount : 0;
                decimal avgClose = a.ClassCount > 0 ? a.SumClose / a.ClassCount : 0;
                decimal avgBelow = a.ClassCount > 0 ? a.SumBelow / a.ClassCount : 0;
                decimal avgMaxBelow = a.ClassCount > 0 ? a.SumMaxBelow / a.ClassCount : 0;

                WritePct(row.CreateCell(col++), avgIdent, styles);
                WritePct(row.CreateCell(col++), avgClose, styles);
                WritePct(row.CreateCell(col++), avgBelow, styles);
                WritePct(row.CreateCell(col++), avgMaxBelow, styles, tintRed: avgMaxBelow >= 5m);
            }

            int lastDataRowIdx = rowIdx - 1;

            sheet.CreateFreezePane(1, headerRowIdx + 1);
            if (lastDataRowIdx >= headerRowIdx + 1)
                sheet.SetAutoFilter(new CellRangeAddress(headerRowIdx, lastDataRowIdx, 0, headers.Length - 1));

            sheet.SetColumnWidth(0, 26 * 256);
            for (int i = 1; i < headers.Length; i++)
                sheet.SetColumnWidth(i, 20 * 256);
        }

        private class SummaryAgg
        {
            public string StoreName { get; set; }
            public int ProductsTotal { get; set; }
            public int ProductsWithOfferBreach { get; set; }
            public int ProductsWithPurchaseBreach { get; set; }
            public int FirstBreakerOfferCount { get; set; }
            public int FirstBreakerPurchaseCount { get; set; }
            public decimal SumIdentical { get; set; }
            public decimal SumClose { get; set; }
            public decimal SumBelow { get; set; }
            public decimal SumMaxBelow { get; set; }
            public int ClassCount { get; set; }
        }

        // ====================================================================
        // ZAKŁADKA: Legenda — tłumaczenie kolorów i kolumn
        // ====================================================================

        private void WritePriceChangeLegendSheet(XSSFWorkbook wb, string sheetName, ExportStyles styles, bool isMarketplace)
        {
            var sheet = wb.CreateSheet(sheetName);

            int r = 0;
            var title = sheet.CreateRow(r++);
            title.HeightInPoints = 24;
            var titleCell = title.CreateCell(0);
            titleCell.SetCellValue("Legenda — jak czytać ten raport");
            titleCell.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 2));

            r++;

            // Sekcja 1 — zakładki
            WriteLegendHeader(sheet, r++, "Zakładki w tym pliku", styles);
            WriteLegendPair(sheet, r++, "vs Oferta", "Porównanie cen konkurentów z naszą aktualną ceną oferty (na rynku). Każdy wiersz = konkurent dla produktu.", styles);
            WriteLegendPair(sheet, r++, "vs Zakup", "Porównanie z ceną zakupu (koszt dla nas / MAP). Wyłom = ktoś sprzedaje taniej niż nam kosztuje → alarm.", styles);
            WriteLegendPair(sheet, r++, "Zbieżność", "Klasyfikacja zachowania konkurenta: % dni z identyczną/bliską/niższą/wyższą ceną. Pokazuje kto nas kopiuje, kto odstaje, kto nas podgryza.", styles);
            WriteLegendPair(sheet, r++, "Podsumowanie", "Ranking konkurentów — ilu produktów dotyczy, ile razy byli pierwsi w łamaniu, jak blisko nas oscylują.", styles);
            WriteLegendPair(sheet, r++, "Legenda", "Ten arkusz.", styles);

            r++;

            // Sekcja 2 — Kolory w komórkach cenowych
            WriteLegendHeader(sheet, r++, "Kolory komórek z cenami (kolumny scrapów w 'vs Oferta' / 'vs Zakup')", styles);

            WriteLegendColor(sheet, r++, styles.BaselineCurrency, "Nasza cena referencyjna", "Cena oferty (ostatnia znana) lub cena zakupu — do tej ceny porównywani są wszyscy konkurenci.", styles);
            WriteLegendColor(sheet, r++, styles.ComplianceCurrency, "Zgodność — konkurent NIE taniej od nas", "Cena konkurenta równa lub wyższa od referencji. Nic się nie dzieje, MAP OK.", styles);
            WriteLegendColor(sheet, r++, styles.NoDataCell, "Brak danych", "W tym scrapie konkurent nie miał oferty dla tego produktu.", styles);

            WriteLegendColor(sheet, r++, styles.CellRed1, "Wyłom 0-2%", "Konkurent delikatnie taniej (do 2% poniżej referencji).", styles);
            WriteLegendColor(sheet, r++, styles.CellRed3, "Wyłom 5-8%", "Wyraźna agresja.", styles);
            WriteLegendColor(sheet, r++, styles.CellRed6, "Wyłom 16-20%", "Silny undercut.", styles);
            WriteLegendColor(sheet, r++, styles.CellRed9, "Wyłom 30-40%", "Bardzo agresywne.", styles);
            WriteLegendColor(sheet, r++, styles.CellRed11, "Wyłom 50%+", "Drastyczny — często błąd w cenie albo wyprzedaż.", styles);

            WriteLegendColor(sheet, r++, styles.FirstBreakerViolation, "🥇 Pierwszy łamacz", "To ten konkurent złamał cenę jako pierwszy w całym oknie analizy. Kluczowy sygnał — śledzić uważnie.", styles);

            r++;

            // Sekcja 3 — Klasyfikacje (Status)
            WriteLegendHeader(sheet, r++, "Klasyfikacja statusu (kolumna 'Status (ost. scrap)' w 'vs Oferta' / 'vs Zakup' i w 'Zbieżność')", styles);
            WriteLegendColor(sheet, r++, GetClassificationStyle(styles, ClassCode.Identical), "🎯 Identyczny", "Cena konkurenta w przedziale ±0.5% od naszej. Prawdopodobnie nas kopiuje (skrypt lub ręcznie).", styles);
            WriteLegendColor(sheet, r++, GetClassificationStyle(styles, ClassCode.Close), "🟢 Blisko", "Cena w przedziale ±2% od naszej (ale poza ±0.5%). Świadomy konkurent dopasowuje się do naszej strategii.", styles);
            WriteLegendColor(sheet, r++, GetClassificationStyle(styles, ClassCode.SlightlyBelow), "🔵 Nieco niżej (2-5%)", "Konkurent taniej o 2-5%. Delikatna agresja — warto monitorować.", styles);
            WriteLegendColor(sheet, r++, GetClassificationStyle(styles, ClassCode.Below), "🟠 Niżej (5-15%)", "Wyraźny undercut — tracisz pozycję cenową.", styles);
            WriteLegendColor(sheet, r++, GetClassificationStyle(styles, ClassCode.DeepBelow), "🔴 Mocno niżej (>15%)", "Bardzo agresywna cena. Alarm — zweryfikuj czy to nie błąd feed'a albo celowa wyprzedaż.", styles);
            WriteLegendColor(sheet, r++, GetClassificationStyle(styles, ClassCode.Above), "🟡 Powyżej (>2%)", "Konkurent drożej od nas — mamy przewagę cenową.", styles);
            WriteLegendColor(sheet, r++, GetClassificationStyle(styles, ClassCode.NoData), "— Brak danych", "Brak ceny w ostatnim scrapie.", styles);

            r++;

            // Sekcja 4 — Kolumny statystyczne
            WriteLegendHeader(sheet, r++, "Kolumny statystyczne (koniec wiersza w 'vs Oferta' / 'vs Zakup')", styles);
            WriteLegendPair(sheet, r++, "Wyłomów (dni)", "W ilu scrapach z wybranego okna konkurent miał cenę niższą od naszej referencji.", styles);
            WriteLegendPair(sheet, r++, "Max % poniżej", "Największe odchylenie w dół w całym oknie. Przydatne do wyłapania jednostkowych alarmów.", styles);
            WriteLegendPair(sheet, r++, "Średnie % poniżej", "Średnie odchylenie, liczone TYLKO z dni w których był wyłom (nie rozcieńcza średniej dniami zgodnymi).", styles);
            WriteLegendPair(sheet, r++, "Pierwszy wyłom (data)", "Data pierwszego scrapu w którym pojawił się wyłom dla tego konkurenta.", styles);
            WriteLegendPair(sheet, r++, "🥇 Pierwszy", "Czy ten konkurent był pierwszym który w ogóle złamał cenę tego produktu w całym oknie (zwycięzca wyścigu w dół).", styles);
            WriteLegendPair(sheet, r++, "Status (ost. scrap)", "Klasyfikacja konkurenta na ostatnim scrapie — patrz sekcja 'Klasyfikacja statusu' wyżej.", styles);

            r++;

            // Sekcja 5 — Zbieżność
            WriteLegendHeader(sheet, r++, "Kolumny w zakładce 'Zbieżność'", styles);
            WriteLegendPair(sheet, r++, "% Identyczny (Oferta/Zakup)", "Odsetek scrapów w których cena konkurenta była w ±0.5% od naszej. Wysokie = kopiuje nas.", styles);
            WriteLegendPair(sheet, r++, "% Blisko", "Odsetek scrapów w których cena była w ±2% (ale poza ±0.5%). Aktywny follower.", styles);
            WriteLegendPair(sheet, r++, "% Niżej", "Odsetek scrapów w których cena konkurenta była niżej od naszej (>2% poniżej).", styles);
            WriteLegendPair(sheet, r++, "% Wyżej", "Odsetek scrapów powyżej naszej ceny (>2% wyżej).", styles);
            WriteLegendPair(sheet, r++, "Max % niżej", "Najniższy punkt do którego konkurent zszedł w stosunku do nas — skala najgorszego wyłomu.", styles);

            r++;

            // Sekcja 6 — Tips
            WriteLegendHeader(sheet, r++, "Wskazówki użycia", styles);
            WriteLegendPair(sheet, r++, "Sortowanie", "Kliknij strzałkę przy nagłówku dowolnej kolumny — autofiltr jest już włączony.", styles);
            WriteLegendPair(sheet, r++, "Filtrowanie po konkurencie", "W 'vs Oferta' użyj filtra kolumny 'Sklep (konkurent)' żeby zobaczyć co robi jeden gracz w całym asortymencie.", styles);
            WriteLegendPair(sheet, r++, "Znajdź kopiujących", "Sortuj 'Zbieżność' malejąco po '% Identyczny (Oferta)' — górna część listy to sklepy kopiujące twoje ceny.", styles);
            WriteLegendPair(sheet, r++, "Znajdź agresorów", "W 'Podsumowanie' sortuj po 'Produkty z wyłomem Oferty' malejąco.", styles);
            WriteLegendPair(sheet, r++, "MAP enforcement", "Zakładka 'vs Zakup' — każda czerwona komórka oznacza sklep sprzedający poniżej ceny zakupu, co zwykle narusza politykę MAP (Minimum Advertised Price).", styles);

            sheet.SetColumnWidth(0, 38 * 256);
            sheet.SetColumnWidth(1, 34 * 256);
            sheet.SetColumnWidth(2, 80 * 256);
        }

        private void WriteLegendHeader(ISheet sheet, int r, string text, ExportStyles styles)
        {
            var row = sheet.CreateRow(r);
            row.HeightInPoints = 22;
            var cell = row.CreateCell(0);
            cell.SetCellValue(text);
            cell.CellStyle = styles.ProductBlockHeader;
            sheet.AddMergedRegion(new CellRangeAddress(r, r, 0, 2));
        }

        private void WriteLegendPair(ISheet sheet, int r, string what, string desc, ExportStyles styles)
        {
            var row = sheet.CreateRow(r);
            var c1 = row.CreateCell(0);
            c1.SetCellValue(what);
            c1.CellStyle = styles.ProductBlockSubHeader;
            var c2 = row.CreateCell(1);
            c2.SetCellValue(desc);
            var wrap = sheet.Workbook.CreateCellStyle();
            wrap.WrapText = true;
            wrap.VerticalAlignment = VerticalAlignment.Top;
            c2.CellStyle = wrap;
            sheet.AddMergedRegion(new CellRangeAddress(r, r, 1, 2));
            row.HeightInPoints = 30;
        }

        private void WriteLegendColor(ISheet sheet, int r, ICellStyle sample, string what, string desc, ExportStyles styles)
        {
            var row = sheet.CreateRow(r);
            row.HeightInPoints = 24;

            var cSample = row.CreateCell(0);
            cSample.SetCellValue("PRZYKŁAD");
            cSample.CellStyle = sample;

            var cWhat = row.CreateCell(1);
            cWhat.SetCellValue(what);
            cWhat.CellStyle = styles.ProductBlockSubHeader;

            var cDesc = row.CreateCell(2);
            cDesc.SetCellValue(desc);
            var wrap = sheet.Workbook.CreateCellStyle();
            wrap.WrapText = true;
            wrap.VerticalAlignment = VerticalAlignment.Top;
            cDesc.CellStyle = wrap;
        }

        // ====================================================================
        // KLASYFIKACJA — kto jak blisko nas stoi
        // ====================================================================

        private enum ClassCode { NoData, Identical, Close, SlightlyBelow, Below, DeepBelow, Above }

        private class ClassificationStatus
        {
            public ClassCode Code { get; set; }
            public string Label { get; set; }
        }

        private class ClassificationEntry
        {
            // Per referencja (Offer/Purchase): % dni identycznych / bliskich / niżej / wyżej + max % niżej
            public decimal OfferIdenticalPct { get; set; }
            public decimal OfferClosePct { get; set; }
            public decimal OfferBelowPct { get; set; }
            public decimal OfferAbovePct { get; set; }
            public decimal OfferMaxBelowPct { get; set; }

            public decimal PurchaseIdenticalPct { get; set; }
            public decimal PurchaseClosePct { get; set; }
            public decimal PurchaseBelowPct { get; set; }
            public decimal PurchaseMaxBelowPct { get; set; }

            // Status na ostatnim scrapie
            public ClassificationStatus OfferLast { get; set; }
            public ClassificationStatus PurchaseLast { get; set; }
        }

        private Dictionary<(int, string), ClassificationEntry> BuildClassificationMap(
            List<PriceChangeProduct> matrix, List<(int Id, DateTime Date)> scraps)
        {
            var result = new Dictionary<(int, string), ClassificationEntry>();

            foreach (var product in matrix)
            {
                foreach (var comp in product.Competitors)
                {
                    var key = (product.ProductId, comp.StoreName.ToLower().Trim());
                    var e = new ClassificationEntry();

                    // ==== OFERTA ====
                    int oDays = 0, oIdent = 0, oClose = 0, oBelow = 0, oAbove = 0;
                    decimal oMaxBelow = 0m;
                    ClassificationStatus oLast = new ClassificationStatus { Code = ClassCode.NoData, Label = "—" };

                    foreach (var scrap in scraps)
                    {
                        var pr = comp.PricePerScrap.GetValueOrDefault(scrap.Id);
                        var refPrice = GetReference(product, scrap.Id, PriceChangeRef.Offer);
                        if (!pr.HasValue || !refPrice.HasValue || refPrice.Value == 0) continue;

                        oDays++;
                        decimal diffPct = (pr.Value - refPrice.Value) / refPrice.Value * 100m;
                        var cls = ClassifyDelta(diffPct);

                        if (cls.Code == ClassCode.Identical) oIdent++;
                        else if (cls.Code == ClassCode.Close) oClose++;
                        else if (cls.Code == ClassCode.SlightlyBelow || cls.Code == ClassCode.Below || cls.Code == ClassCode.DeepBelow) oBelow++;
                        else if (cls.Code == ClassCode.Above) oAbove++;

                        if (diffPct < 0 && -diffPct > oMaxBelow) oMaxBelow = -diffPct;
                        oLast = cls; // przy iteracji rosnąco — ostatni nadpisuje
                    }

                    if (oDays > 0)
                    {
                        e.OfferIdenticalPct = (decimal)oIdent / oDays * 100m;
                        e.OfferClosePct = (decimal)oClose / oDays * 100m;
                        e.OfferBelowPct = (decimal)oBelow / oDays * 100m;
                        e.OfferAbovePct = (decimal)oAbove / oDays * 100m;
                        e.OfferMaxBelowPct = oMaxBelow;
                        e.OfferLast = oLast;
                    }
                    else
                    {
                        e.OfferLast = new ClassificationStatus { Code = ClassCode.NoData, Label = "—" };
                    }

                    // ==== ZAKUP ====
                    int pDays = 0, pIdent = 0, pClose = 0, pBelow = 0;
                    decimal pMaxBelow = 0m;
                    ClassificationStatus pLast = new ClassificationStatus { Code = ClassCode.NoData, Label = "—" };

                    foreach (var scrap in scraps)
                    {
                        var pr = comp.PricePerScrap.GetValueOrDefault(scrap.Id);
                        var refPrice = GetReference(product, scrap.Id, PriceChangeRef.Purchase);
                        if (!pr.HasValue || !refPrice.HasValue || refPrice.Value == 0) continue;

                        pDays++;
                        decimal diffPct = (pr.Value - refPrice.Value) / refPrice.Value * 100m;
                        var cls = ClassifyDelta(diffPct);

                        if (cls.Code == ClassCode.Identical) pIdent++;
                        else if (cls.Code == ClassCode.Close) pClose++;
                        else if (cls.Code == ClassCode.SlightlyBelow || cls.Code == ClassCode.Below || cls.Code == ClassCode.DeepBelow) pBelow++;

                        if (diffPct < 0 && -diffPct > pMaxBelow) pMaxBelow = -diffPct;
                        pLast = cls;
                    }

                    if (pDays > 0)
                    {
                        e.PurchaseIdenticalPct = (decimal)pIdent / pDays * 100m;
                        e.PurchaseClosePct = (decimal)pClose / pDays * 100m;
                        e.PurchaseBelowPct = (decimal)pBelow / pDays * 100m;
                        e.PurchaseMaxBelowPct = pMaxBelow;
                        e.PurchaseLast = pLast;
                    }
                    else
                    {
                        e.PurchaseLast = new ClassificationStatus { Code = ClassCode.NoData, Label = "—" };
                    }

                    result[key] = e;
                }
            }

            return result;
        }

        private ClassificationStatus ClassifyDelta(decimal diffPct)
        {
            // diffPct = (konkurent - my) / my * 100
            // ujemne => konkurent taniej, dodatnie => droższy
            decimal abs = Math.Abs(diffPct);
            if (abs <= 0.5m) return new ClassificationStatus { Code = ClassCode.Identical, Label = "🎯 Identyczny" };
            if (abs <= 2m) return new ClassificationStatus { Code = ClassCode.Close, Label = "🟢 Blisko" };

            if (diffPct > 0) return new ClassificationStatus { Code = ClassCode.Above, Label = "🟡 Powyżej" };

            // Ujemne — jak głęboko?
            if (abs <= 5m) return new ClassificationStatus { Code = ClassCode.SlightlyBelow, Label = "🔵 Nieco niżej" };
            if (abs <= 15m) return new ClassificationStatus { Code = ClassCode.Below, Label = "🟠 Niżej" };
            return new ClassificationStatus { Code = ClassCode.DeepBelow, Label = "🔴 Mocno niżej" };
        }

        private ICellStyle GetClassificationStyle(ExportStyles s, ClassCode code)
        {
            switch (code)
            {
                case ClassCode.Identical: return s.ClsIdentical;
                case ClassCode.Close: return s.ClsClose;
                case ClassCode.SlightlyBelow: return s.ClsSlightlyBelow;
                case ClassCode.Below: return s.ClsBelow;
                case ClassCode.DeepBelow: return s.ClsDeepBelow;
                case ClassCode.Above: return s.ClsAbove;
                default: return s.NoDataCell;
            }
        }


        // ====================================================================
        // GRADIENT NARUSZEŃ — wybór stylu per %
        // ====================================================================

        private ICellStyle GetViolationStyle(ExportStyles s, decimal pct, bool isFirst)
        {
            if (isFirst) return s.FirstBreakerViolation;

            decimal v = Math.Abs(pct);
            if (v <= 2) return s.CellRed1;
            if (v <= 5) return s.CellRed2;
            if (v <= 8) return s.CellRed3;
            if (v <= 12) return s.CellRed4;
            if (v <= 16) return s.CellRed5;
            if (v <= 20) return s.CellRed6;
            if (v <= 25) return s.CellRed7;
            if (v <= 30) return s.CellRed8;
            if (v <= 40) return s.CellRed9;
            if (v <= 50) return s.CellRed10;
            return s.CellRed11;
        }

        // ====================================================================
        // MODELE (PriceChange)
        // ====================================================================

        private class PriceChangeProduct
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string Producer { get; set; }
            public string Ean { get; set; }
            public string Sku { get; set; }
            public int? ExternalId { get; set; }
            public decimal? PurchasePrice { get; set; }
            public decimal? LatestOurPrice { get; set; }
            public string FlagsCsv { get; set; }
            public Dictionary<int, decimal?> OurPricePerScrap { get; set; }
            public Dictionary<string, PriceChangeCompetitor> CompetitorsByKey { get; set; }
            public List<PriceChangeCompetitor> Competitors { get; set; } = new();
        }

        private class PriceChangeCompetitor
        {
            public string StoreName { get; set; }
            public Dictionary<int, decimal?> PricePerScrap { get; set; }
        }

        // ====================================================================
        // MODELE POMOCNICZE — Comparison
        // ====================================================================

        private class RawExportEntry
        {
            public int ProductId { get; set; }
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
            public int ProductId { get; set; }
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
        // MODELE POMOCNICZE — Allegro
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
                            p.ProductId,
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
                    ProductId = x.ProductId,
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
                if (!productDict.TryGetValue(ph.AllegroProductId, out var product)) continue;

                bool isMe = ph.SellerName.Equals(storeNameAllegro, StringComparison.OrdinalIgnoreCase);

                if (!isMe && activePreset != null)
                {
                    if (ph.DeliveryTime.HasValue)
                    {
                        if (ph.DeliveryTime.Value < minDelivery || ph.DeliveryTime.Value > maxDelivery) continue;
                    }
                    else if (!includeNoDelivery) continue;

                    if (competitorRules != null)
                    {
                        var sellerLower = (ph.SellerName ?? "").ToLower().Trim();
                        if (competitorRules.TryGetValue(sellerLower, out bool useCompetitor))
                        {
                            if (!useCompetitor) continue;
                        }
                        else if (!activePreset.UseUnmarkedStores) continue;
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
                .GroupBy(x => new { x.ProductId, x.ProductName, x.Producer, x.Ean, x.CatalogNumber, x.ExternalId, x.MarginPrice })
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
                        ProductId = g.Key.ProductId,
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
            return rawData
                .GroupBy(x => !string.IsNullOrWhiteSpace(x.Ean)
                    ? $"ean:{x.Ean.Trim()}"
                    : $"pid:{x.AllegroProductId}")
                .Select(g =>
                {
                    var all = g.ToList();
                    var representative = all.First();

                    var myEntries = all.Where(x => x.IsMe && x.Price > 0).ToList();
                    RawAllegroExportEntry myOffer = null;

                    if (myEntries.Any())
                    {
                        foreach (var entry in myEntries)
                        {
                            if (long.TryParse(entry.IdOnAllegro, out var tid) && entry.IdAllegro == tid)
                            {
                                myOffer = entry;
                                break;
                            }
                        }
                        myOffer ??= myEntries.OrderBy(x => x.Price).First();
                    }

                    var competitors = all
                        .Where(x => !x.IsMe && x.Price > 0)
                        .GroupBy(x => x.SellerName.ToLower().Trim())
                        .Select(sg => sg.OrderBy(x => x.Price).First())
                        .OrderBy(x => x.Price)
                        .ToList();

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
        // ZAPIS ARKUSZA — Comparison (z flagami)
        // ====================================================================

        private void WritePriceExportSheet(ISheet sheet, List<ExportProductRow> data, Dictionary<int, string> flagsDict, ExportStyles s)
        {
            var headerRow = sheet.CreateRow(0);
            int col = 0;

            string[] headers = {
                "ID Produktu", "Nazwa Produktu", "Producent", "EAN", "SKU", "Flagi",
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
                row.CreateCell(col++).SetCellValue(flagsDict.GetValueOrDefault(item.ProductId, ""));

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

            // Freeze pierwszego wiersza + 6 meta kolumn, autofilter na meta+stats
            sheet.CreateFreezePane(6, 1);
            if (rowIdx > 1)
            {
                sheet.SetAutoFilter(new CellRangeAddress(0, rowIdx - 1, 0, 17));
            }

            for (int i = 0; i < 18; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
        }

        // ====================================================================
        // ZAPIS ARKUSZA — Allegro (z flagami)
        // ====================================================================

        private void WriteAllegroPriceExportSheet(ISheet sheet, List<ExportAllegroProductRow> data, Dictionary<int, string> flagsDict, ExportStyles s)
        {
            var headerRow = sheet.CreateRow(0);
            int col = 0;

            string[] headers = {
                "ID Produktu", "Nazwa Produktu", "Producent", "EAN", "SKU Allegro", "Flagi",
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
                row.CreateCell(col++).SetCellValue(flagsDict.GetValueOrDefault(item.AllegroProductId, ""));

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

            for (int i = 0; i < 16; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
        }

        // ====================================================================
        // BUDOWANIE DANYCH KONKURENCJI
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
        // ARKUSZE KONKURENCJI
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
            public ICellStyle SubHeaderBlue { get; set; }
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
            public ICellStyle CellBlue { get; set; }

            // Price change
            public ICellStyle ComplianceCurrency { get; set; }
            public ICellStyle ComplianceStatus { get; set; }
            public ICellStyle NoDataCell { get; set; }
            public ICellStyle NoDataCurrency { get; set; }
            public ICellStyle BaselineCell { get; set; }
            public ICellStyle BaselineCurrency { get; set; }
            public ICellStyle FirstBreakerMarker { get; set; }
            public ICellStyle FirstBreakerViolation { get; set; }
            public ICellStyle ProductBlockHeader { get; set; }
            public ICellStyle ProductBlockSubHeader { get; set; }

            // Klasyfikacje statusu (Zbieżność / Status w ostatnim scrapie)
            public ICellStyle ClsIdentical { get; set; }
            public ICellStyle ClsClose { get; set; }
            public ICellStyle ClsSlightlyBelow { get; set; }
            public ICellStyle ClsBelow { get; set; }
            public ICellStyle ClsDeepBelow { get; set; }
            public ICellStyle ClsAbove { get; set; }
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
            s.SubHeaderBlue = CreateColoredStyle(wb, new byte[] { 100, 180, 255 }, true, IndexedColors.White.Index);

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

            s.CellRed1 = CreateColoredStyle(wb, new byte[] { 255, 240, 240 }, false, 0, s.Currency);
            s.CellRed2 = CreateColoredStyle(wb, new byte[] { 255, 220, 220 }, false, 0, s.Currency);
            s.CellRed3 = CreateColoredStyle(wb, new byte[] { 255, 200, 200 }, false, 0, s.Currency);
            s.CellRed4 = CreateColoredStyle(wb, new byte[] { 255, 180, 180 }, false, 0, s.Currency);
            s.CellRed5 = CreateColoredStyle(wb, new byte[] { 255, 160, 160 }, false, 0, s.Currency);
            s.CellRed6 = CreateColoredStyle(wb, new byte[] { 255, 140, 140 }, false, 0, s.Currency);
            s.CellRed7 = CreateColoredStyle(wb, new byte[] { 255, 115, 115 }, false, 0, s.Currency);
            s.CellRed8 = CreateColoredStyle(wb, new byte[] { 255, 90, 90 }, false, 0, s.Currency);
            s.CellRed9 = CreateColoredStyle(wb, new byte[] { 240, 60, 60 }, false, IndexedColors.White.Index, s.Currency);
            s.CellRed10 = CreateColoredStyle(wb, new byte[] { 220, 40, 40 }, false, IndexedColors.White.Index, s.Currency);
            s.CellRed11 = CreateColoredStyle(wb, new byte[] { 190, 20, 20 }, false, IndexedColors.White.Index, s.Currency);

            s.CellGreen1 = CreateColoredStyle(wb, new byte[] { 240, 255, 240 }, false, 0, s.Currency);
            s.CellGreen2 = CreateColoredStyle(wb, new byte[] { 220, 250, 220 }, false, 0, s.Currency);
            s.CellGreen3 = CreateColoredStyle(wb, new byte[] { 200, 240, 200 }, false, 0, s.Currency);
            s.CellGreen4 = CreateColoredStyle(wb, new byte[] { 180, 230, 180 }, false, 0, s.Currency);
            s.CellGreen5 = CreateColoredStyle(wb, new byte[] { 160, 220, 160 }, false, 0, s.Currency);
            s.CellGreen6 = CreateColoredStyle(wb, new byte[] { 140, 210, 140 }, false, 0, s.Currency);
            s.CellGreen7 = CreateColoredStyle(wb, new byte[] { 120, 200, 120 }, false, 0, s.Currency);
            s.CellGreen8 = CreateColoredStyle(wb, new byte[] { 100, 185, 100 }, false, 0, s.Currency);
            s.CellGreen9 = CreateColoredStyle(wb, new byte[] { 75, 170, 75 }, false, IndexedColors.White.Index, s.Currency);
            s.CellGreen10 = CreateColoredStyle(wb, new byte[] { 50, 150, 50 }, false, IndexedColors.White.Index, s.Currency);
            s.CellGreen11 = CreateColoredStyle(wb, new byte[] { 30, 130, 30 }, false, IndexedColors.White.Index, s.Currency);

            s.CellBlue = CreateColoredStyle(wb, new byte[] { 230, 245, 255 }, false, 0);

            // Price change styles
            s.ComplianceCurrency = CreateColoredStyle(wb, new byte[] { 225, 248, 225 }, false, 0, s.Currency);
            s.ComplianceStatus = CreateColoredStyle(wb, new byte[] { 200, 240, 200 }, true, 0);
            s.NoDataCell = CreateColoredStyle(wb, new byte[] { 235, 235, 235 }, false, 0);
            s.NoDataCurrency = CreateColoredStyle(wb, new byte[] { 235, 235, 235 }, false, 0, s.Currency);
            s.BaselineCell = CreateColoredStyle(wb, new byte[] { 220, 228, 240 }, true, 0);
            s.BaselineCurrency = CreateColoredStyle(wb, new byte[] { 220, 228, 240 }, true, 0, s.Currency);

            s.FirstBreakerMarker = CreateColoredStyle(wb, new byte[] { 255, 215, 80 }, true, 0);
            s.FirstBreakerViolation = CreateColoredStyle(wb, new byte[] { 255, 165, 0 }, true, 0, s.Currency);

            s.ProductBlockHeader = CreateColoredStyle(wb, new byte[] { 26, 39, 68 }, true, IndexedColors.White.Index);
            s.ProductBlockSubHeader = CreateColoredStyle(wb, new byte[] { 222, 228, 240 }, true, 0);

            // Klasyfikacje (używane w 'Zbieżność' i kolumnie 'Status' w vs Oferta / vs Zakup)
            s.ClsIdentical = CreateColoredStyle(wb, new byte[] { 196, 230, 252 }, true, 0);  // jasny niebieski — kopiuje nas
            s.ClsClose = CreateColoredStyle(wb, new byte[] { 212, 237, 218 }, true, 0);  // jasna zieleń — blisko
            s.ClsSlightlyBelow = CreateColoredStyle(wb, new byte[] { 255, 240, 200 }, true, 0);  // jasny żółty — niewielkie zagrożenie
            s.ClsBelow = CreateColoredStyle(wb, new byte[] { 255, 200, 140 }, true, 0);  // pomarańczowy — wyraźne zagrożenie
            s.ClsDeepBelow = CreateColoredStyle(wb, new byte[] { 220, 53, 69 }, true, IndexedColors.White.Index); // czerwony alarm
            s.ClsAbove = CreateColoredStyle(wb, new byte[] { 230, 230, 230 }, true, 0);  // szary — mamy przewagę

            return s;
        }

        private ICellStyle CreateColoredStyle(XSSFWorkbook wb, byte[] rgb, bool bold, short fontColorIndex, ICellStyle cloneFrom = null)
        {
            var style = (XSSFCellStyle)wb.CreateCellStyle();

            if (cloneFrom != null) style.CloneStyleFrom(cloneFrom);

            var colorMap = new DefaultIndexedColorMap();
            style.SetFillForegroundColor(new XSSFColor(rgb, colorMap));
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