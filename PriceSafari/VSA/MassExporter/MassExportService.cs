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
        // COMPARISON EXPORT (Ceneo / Google) + flagi  [BEZ ZMIAN]
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
        // ALLEGRO EXPORT + flagi  [BEZ ZMIAN]
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
        // PRICE CHANGE EXPORT — ANALIZA WYŁOMU
        // [ZMIANA: nazwa pliku Analiza_Wylomu_, wywołanie BuildPriceChangeWorkbook]
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

            var xlsxBytes = BuildPriceChangeWorkbook(matrix, scraps, storeName, isMarketplace);

            var dateRange = scraps.Count == 1
                ? scraps[0].Date.ToString("yyyy-MM-dd")
                : $"{scraps.First().Date:yyyy-MM-dd}_do_{scraps.Last().Date:yyyy-MM-dd}";

            var label = isMarketplace ? "Allegro" : "Ceneo-Google";
            var fileName = $"Analiza_Wylomu_{label}_{storeName}_{dateRange}.xlsx";

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
        // BUDOWA MATRIX — COMPARISON  [BEZ ZMIAN]
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
                        var current = product.OurPricePerScrap[scrap.Id];
                        if (!current.HasValue || finalPrice < current.Value)
                            product.OurPricePerScrap[scrap.Id] = finalPrice;
                        continue;
                    }

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

                    var existing = comp.PricePerScrap[scrap.Id];
                    if (!existing.HasValue || finalPrice < existing.Value)
                        comp.PricePerScrap[scrap.Id] = finalPrice;
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
        // BUDOWA MATRIX — ALLEGRO  [BEZ ZMIAN]
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
        // STATYSTYKI NA PRODUKT/KONKURENTA  [BEZ ZMIAN]
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
        // [NOWE] PRICE WAR TIMELINE — kluczowa logika kaskady
        // Dla produktu zwraca chronologiczną listę PIERWSZYCH zejść każdego
        // konkurenta poniżej naszej ceny referencyjnej.
        // ====================================================================

        private class PriceWarEvent
        {
            public int ScrapId { get; set; }
            public DateTime ScrapDate { get; set; }
            public string CompetitorKey { get; set; }
            public string CompetitorName { get; set; }
            public decimal Price { get; set; }
            public decimal OurRefPrice { get; set; }
            public decimal ViolationPct { get; set; }
            public int Rank { get; set; }                        // 1 = inicjator
            public double DelayHoursFromInitiator { get; set; }
            public double DelayHoursFromPrevious { get; set; }
        }

        private List<PriceWarEvent> BuildPriceWarTimelineForProduct(
            PriceChangeProduct product,
            List<(int Id, DateTime Date)> scraps,
            PriceChangeRef refType)
        {
            var firstBreakScrap = new Dictionary<string, (DateTime date, int scrapId, decimal price, decimal refPrice, string name)>();

            foreach (var scrap in scraps)
            {
                var reference = GetReference(product, scrap.Id, refType);
                if (!reference.HasValue) continue;

                foreach (var comp in product.Competitors)
                {
                    var compKey = comp.StoreName.ToLower().Trim();
                    if (firstBreakScrap.ContainsKey(compKey)) continue;

                    var price = comp.PricePerScrap[scrap.Id];
                    if (!price.HasValue) continue;
                    if (price.Value >= reference.Value) continue;

                    firstBreakScrap[compKey] = (scrap.Date, scrap.Id, price.Value, reference.Value, comp.StoreName);
                }
            }

            if (!firstBreakScrap.Any()) return new List<PriceWarEvent>();

            var ordered = firstBreakScrap
                .Select(kv => new { CompKey = kv.Key, Info = kv.Value })
                .OrderBy(x => x.Info.date)
                .ThenBy(x => x.Info.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var events = new List<PriceWarEvent>();
            var initiatorDate = ordered[0].Info.date;
            DateTime? prevDate = null;
            int rank = 0;

            foreach (var x in ordered)
            {
                rank++;
                var pct = x.Info.refPrice > 0
                    ? Math.Round((x.Info.refPrice - x.Info.price) / x.Info.refPrice * 100m, 2)
                    : 0m;

                events.Add(new PriceWarEvent
                {
                    ScrapId = x.Info.scrapId,
                    ScrapDate = x.Info.date,
                    CompetitorKey = x.CompKey,
                    CompetitorName = x.Info.name,
                    Price = x.Info.price,
                    OurRefPrice = x.Info.refPrice,
                    ViolationPct = pct,
                    Rank = rank,
                    DelayHoursFromInitiator = (x.Info.date - initiatorDate).TotalHours,
                    DelayHoursFromPrevious = prevDate.HasValue ? (x.Info.date - prevDate.Value).TotalHours : 0
                });
                prevDate = x.Info.date;
            }

            return events;
        }


        private byte[] BuildPriceChangeWorkbook(
            List<PriceChangeProduct> matrix,
            List<(int Id, DateTime Date)> scraps,
            string storeName,
            bool isMarketplace)
        {
            using var wb = new XSSFWorkbook();
            var styles = CreateExportStyles(wb);

            // --- Dwa zestawy first-breakerów ---
            var firstBreakersOffer = DetermineFirstBreakers(matrix, scraps, PriceChangeRef.Offer);
            var firstBreakersPurchase = DetermineFirstBreakers(matrix, scraps, PriceChangeRef.Purchase);

            // --- Dwa zestawy timeline'ów ---
            var timelinesOffer = new Dictionary<int, List<PriceWarEvent>>();
            var timelinesPurchase = new Dictionary<int, List<PriceWarEvent>>();
            foreach (var p in matrix)
            {
                timelinesOffer[p.ProductId] = BuildPriceWarTimelineForProduct(p, scraps, PriceChangeRef.Offer);
                timelinesPurchase[p.ProductId] = BuildPriceWarTimelineForProduct(p, scraps, PriceChangeRef.Purchase);
            }

            // 1–2: Płaskie tabele
            WritePriceChangeFlatSheet(wb, "vs Oferta", matrix, scraps, storeName,
                PriceChangeRef.Offer, firstBreakersOffer, timelinesOffer, styles, isMarketplace);

            WritePriceChangeFlatSheet(wb, "vs Zakup", matrix, scraps, storeName,
                PriceChangeRef.Purchase, firstBreakersPurchase, timelinesPurchase, styles, isMarketplace);

            // 3–4: Oś czasu wojny
            WritePriceWarTimelineSheet(wb, "Oś czasu vs Oferta", matrix, scraps,
                timelinesOffer, PriceChangeRef.Offer, styles, isMarketplace);

            WritePriceWarTimelineSheet(wb, "Oś czasu vs Zakup", matrix, scraps,
                timelinesPurchase, PriceChangeRef.Purchase, styles, isMarketplace);

            // 5–6: Liderzy per produkt
            WritePriceWarProductRankingSheet(wb, "Liderzy vs Oferta", matrix, scraps,
                timelinesOffer, PriceChangeRef.Offer, styles);

            WritePriceWarProductRankingSheet(wb, "Liderzy vs Zakup", matrix, scraps,
                timelinesPurchase, PriceChangeRef.Purchase, styles);

            // 7–8: Macierz lider→follower
            WritePriceWarLeaderFollowerMatrixSheet(wb, "Macierz L→F vs Oferta",
                matrix, timelinesOffer, PriceChangeRef.Offer, styles);

            WritePriceWarLeaderFollowerMatrixSheet(wb, "Macierz L→F vs Zakup",
                matrix, timelinesPurchase, PriceChangeRef.Purchase, styles);

            // 9–10: Mapa agresji
            WriteAggressionHeatmapSheet(wb, "Agresja vs Oferta", matrix, scraps,
                PriceChangeRef.Offer, styles);

            WriteAggressionHeatmapSheet(wb, "Agresja vs Zakup", matrix, scraps,
                PriceChangeRef.Purchase, styles);

            // 11: Legenda
            WritePriceChangeLegendSheet(wb, "Legenda", styles, isMarketplace);

            using var ms = new MemoryStream();
            wb.Write(ms);
            return ms.ToArray();
        }


        private void WritePriceChangeFlatSheet(XSSFWorkbook wb, string sheetName,
            List<PriceChangeProduct> matrix, List<(int Id, DateTime Date)> scraps, string storeName,
            PriceChangeRef refType, Dictionary<(int prodId, string compKey), bool> firstBreakers,
            Dictionary<int, List<PriceWarEvent>> timelines,  // <-- WŁAŚCIWY timeline per refType
            ExportStyles styles, bool isMarketplace)
        {
            var sheet = wb.CreateSheet(sheetName);

            var refLabel = refType == PriceChangeRef.Offer ? "cenę oferty" : "cenę zakupu";
            var refShortLabel = refType == PriceChangeRef.Offer ? "Oferta" : "Zakup";

            var introRow = sheet.CreateRow(0);
            introRow.HeightInPoints = 22;
            var introCell = introRow.CreateCell(0);
            introCell.SetCellValue($"Kto i kiedy złamał {refLabel} — {(isMarketplace ? "Allegro" : "Google/Ceneo")} / {storeName}");
            introCell.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 8 + scraps.Count + 4));

            var descRow = sheet.CreateRow(1);
            descRow.HeightInPoints = 42;
            var descCell = descRow.CreateCell(0);
            descCell.SetCellValue(
                "Każdy produkt = osobny blok (ciemny pasek = nagłówek z szybkimi statsami). Pod spodem wiersze konkurentów. " +
                "Kolumny z datami = cena konkurenta w danym scrapie. Czerwień = złamał " + refLabel + " (im głębiej, tym mocniejsza). " +
                "🥇 ZŁOTO = pierwszy kto złamał dla tego produktu. Kolumna 'Rola' mówi wprost: Inicjator / Follower +Δh / Spokojny. " +
                "Pełna kaskada wojny → zakładki 'Oś czasu', 'Liderzy', 'Macierz lider→follower'.");
            var wrap = wb.CreateCellStyle();
            wrap.CloneStyleFrom(styles.Default);
            wrap.WrapText = true;
            wrap.VerticalAlignment = VerticalAlignment.Top;
            descCell.CellStyle = wrap;
            sheet.AddMergedRegion(new CellRangeAddress(1, 1, 0, 8 + scraps.Count + 4));

            int headerRowIdx = 3;
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

            string[] statHeaders = new[] { "Wyłomów (dni)", "Max % poniżej", "Średnie % poniżej", "Pierwszy wyłom (data)", "Rola" };
            foreach (var h in statHeaders)
            {
                var c = headerRow.CreateCell(col++);
                c.SetCellValue(h);
                c.CellStyle = styles.HeaderDark;
            }
            int lastColIdx = col - 1;

            int rowIdx = headerRowIdx + 1;
            int productIndex = 0;

            foreach (var product in matrix)
            {
                if (!product.Competitors.Any(c => c.PricePerScrap.Any(v => v.Value.HasValue))) continue;

                decimal? ourRef = refType == PriceChangeRef.Offer ? product.LatestOurPrice : product.PurchasePrice;

                int violatorsCount = 0;
                decimal maxPctEver = 0m;
                PriceWarEvent initiatorEvent = null;
                var timeline = timelines.GetValueOrDefault(product.ProductId);
                if (timeline != null && timeline.Any()) initiatorEvent = timeline[0];

                foreach (var c in product.Competitors)
                {
                    var st = ComputeCompetitorStats(product, c, scraps, refType);
                    if (st.ViolationCount > 0)
                    {
                        violatorsCount++;
                        if (st.MaxViolationPercent > maxPctEver) maxPctEver = st.MaxViolationPercent;
                    }
                }

                // Pasek nagłówka produktu
                var prodHeaderRow = sheet.CreateRow(rowIdx);
                prodHeaderRow.HeightInPoints = 26;
                var phCell = prodHeaderRow.CreateCell(0);

                var initiatorText = initiatorEvent != null
                    ? $" | Inicjator: {initiatorEvent.CompetitorName} ({initiatorEvent.ScrapDate:dd.MM HH:mm}, -{initiatorEvent.ViolationPct}%)"
                    : "";
                var refText = ourRef.HasValue ? $"{ourRef.Value:N2} zł" : "brak";
                var flagsText = !string.IsNullOrEmpty(product.FlagsCsv) ? $" | Flagi: {product.FlagsCsv}" : "";
                var brandText = !string.IsNullOrEmpty(product.Producer) ? $" | {product.Producer}" : "";
                var violText = violatorsCount > 0
                    ? $" | 🔴 {violatorsCount} konkur. łamie, max -{maxPctEver}%"
                    : " | ✅ brak wyłomów";

                phCell.SetCellValue($"📦 {product.ProductName}{brandText}  |  EAN: {(string.IsNullOrEmpty(product.Ean) ? "—" : product.Ean)}  |  SKU: {(string.IsNullOrEmpty(product.Sku) ? "—" : product.Sku)}  |  Nasza {refShortLabel}: {refText}{violText}{initiatorText}{flagsText}");
                phCell.CellStyle = styles.ProductBlockHeader;
                sheet.AddMergedRegion(new CellRangeAddress(rowIdx, rowIdx, 0, lastColIdx));
                rowIdx++;

                // Wiersze konkurentów
                var ordered = product.Competitors
                    .Select(c =>
                    {
                        var compKey = c.StoreName.ToLower().Trim();
                        bool isFirst = firstBreakers.GetValueOrDefault((product.ProductId, compKey), false);
                        var st = ComputeCompetitorStats(product, c, scraps, refType);
                        return new { Comp = c, IsFirst = isFirst, Stats = st, CompKey = compKey };
                    })
                    .OrderByDescending(x => x.IsFirst)
                    .ThenBy(x => x.Stats.FirstViolationScrapDate ?? DateTime.MaxValue)
                    .ThenBy(x => x.Comp.StoreName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                bool bandRow = productIndex % 2 == 1;
                var baseStyle = bandRow ? styles.BandDefault : styles.Default;
                var basePercent = bandRow ? styles.BandPercent : styles.Percent;
                var baseNoData = bandRow ? styles.BandNoData : styles.NoDataCell;
                var baseBaseline = bandRow ? styles.BandBaselineCurrency : styles.BaselineCurrency;
                var baseCompliance = bandRow ? styles.BandComplianceCurrency : styles.ComplianceCurrency;

                foreach (var entry in ordered)
                {
                    var row = sheet.CreateRow(rowIdx++);
                    col = 0;

                    SetCell(row, col++, product.ProductName ?? "", baseStyle);
                    SetCell(row, col++, product.Ean ?? "", baseStyle);
                    SetCell(row, col++, product.Sku ?? "", baseStyle);
                    SetCell(row, col++, product.Producer ?? "", baseStyle);
                    SetCell(row, col++, product.FlagsCsv ?? "", baseStyle);

                    var refCell2 = row.CreateCell(col++);
                    if (ourRef.HasValue)
                    {
                        refCell2.SetCellValue((double)ourRef.Value);
                        refCell2.CellStyle = baseBaseline;
                    }
                    else
                    {
                        refCell2.SetCellValue("—");
                        refCell2.CellStyle = baseNoData;
                    }

                    SetCell(row, col++, entry.Comp.StoreName ?? "", baseStyle);
                    SetCell(row, col++, isMarketplace ? "Allegro" : "Google/Ceneo", baseStyle);

                    foreach (var scrap in scraps)
                    {
                        var cell = row.CreateCell(col++);
                        var pr = entry.Comp.PricePerScrap.GetValueOrDefault(scrap.Id);
                        var reference = GetReference(product, scrap.Id, refType);

                        if (!pr.HasValue)
                        {
                            cell.SetCellValue("—");
                            cell.CellStyle = baseNoData;
                            continue;
                        }

                        cell.SetCellValue((double)pr.Value);

                        if (!reference.HasValue || pr.Value >= reference.Value)
                        {
                            cell.CellStyle = baseCompliance;
                        }
                        else
                        {
                            decimal pct = (reference.Value - pr.Value) / reference.Value * 100m;
                            bool isFirstScrapAtBreak = entry.IsFirst && entry.Stats.FirstViolationScrapId == scrap.Id;
                            cell.CellStyle = GetViolationStyle(styles, pct, isFirstScrapAtBreak);
                        }
                    }

                    var stats = entry.Stats;
                    var vc = row.CreateCell(col++);
                    vc.SetCellValue(stats.ViolationCount);
                    vc.CellStyle = baseStyle;

                    var maxCell = row.CreateCell(col++);
                    if (stats.ViolationCount > 0)
                    {
                        maxCell.SetCellValue((double)stats.MaxViolationPercent);
                        maxCell.CellStyle = styles.PercentRed;
                    }
                    else { maxCell.SetCellValue("—"); maxCell.CellStyle = baseStyle; }

                    var avgCell = row.CreateCell(col++);
                    if (stats.ViolationCount > 0)
                    {
                        avgCell.SetCellValue((double)stats.AvgViolationPercent);
                        avgCell.CellStyle = basePercent;
                    }
                    else { avgCell.SetCellValue("—"); avgCell.CellStyle = baseStyle; }

                    var firstDateCell = row.CreateCell(col++);
                    firstDateCell.SetCellValue(stats.FirstViolationScrapDate.HasValue
                        ? stats.FirstViolationScrapDate.Value.ToString("dd.MM.yyyy HH:mm") : "—");
                    firstDateCell.CellStyle = baseStyle;

                    // Kolumna "Rola" — teraz z WŁAŚCIWEGO timeline'a per refType
                    var roleCell = row.CreateCell(col++);
                    if (stats.ViolationCount == 0)
                    {
                        roleCell.SetCellValue("😴 Spokojny");
                        roleCell.CellStyle = baseStyle;
                    }
                    else if (entry.IsFirst)
                    {
                        roleCell.SetCellValue("🥇 Inicjator");
                        roleCell.CellStyle = styles.FirstBreakerMarker;
                    }
                    else
                    {
                        double? delayHours = null;
                        if (timeline != null)
                        {
                            var myEvent = timeline.FirstOrDefault(e => e.CompetitorKey == entry.CompKey);
                            if (myEvent != null) delayHours = myEvent.DelayHoursFromInitiator;
                        }
                        string roleText;
                        if (delayHours.HasValue && delayHours.Value >= 0)
                        {
                            string delayStr = delayHours.Value < 1 ? "<1h" :
                                              delayHours.Value < 24 ? $"+{(int)delayHours.Value}h" :
                                              $"+{(int)(delayHours.Value / 24)}d";
                            roleText = $"🔄 Follower {delayStr}";
                        }
                        else { roleText = "🔄 Follower"; }
                        roleCell.SetCellValue(roleText);
                        roleCell.CellStyle = styles.FollowerMarker;
                    }
                }

                productIndex++;
            }

            int lastDataRowIdx = rowIdx - 1;
            sheet.CreateFreezePane(scrapStartCol, headerRowIdx + 1);
            if (lastDataRowIdx >= headerRowIdx + 1)
                sheet.SetAutoFilter(new CellRangeAddress(headerRowIdx, lastDataRowIdx, 0, lastColIdx));

            sheet.SetColumnWidth(0, 40 * 256);
            sheet.SetColumnWidth(1, 15 * 256);
            sheet.SetColumnWidth(2, 15 * 256);
            sheet.SetColumnWidth(3, 18 * 256);
            sheet.SetColumnWidth(4, 22 * 256);
            sheet.SetColumnWidth(5, 16 * 256);
            sheet.SetColumnWidth(6, 24 * 256);
            sheet.SetColumnWidth(7, 18 * 256);
            for (int i = scrapStartCol; i <= scrapEndCol; i++) sheet.SetColumnWidth(i, 13 * 256);
            for (int i = scrapEndCol + 1; i <= lastColIdx; i++) sheet.SetColumnWidth(i, 18 * 256);
        }

        private static void SetCell(IRow row, int col, string value, ICellStyle style)
        {
            var c = row.CreateCell(col);
            c.SetCellValue(value);
            c.CellStyle = style;
        }


        private void WritePriceWarTimelineSheet(XSSFWorkbook wb, string sheetName,
            List<PriceChangeProduct> matrix, List<(int Id, DateTime Date)> scraps,
            Dictionary<int, List<PriceWarEvent>> timelines,
            PriceChangeRef refType,  // <-- NOWE
            ExportStyles styles, bool isMarketplace)
        {
            var sheet = wb.CreateSheet(sheetName);

            var refLabel = refType == PriceChangeRef.Offer ? "cenę oferty" : "cenę zakupu (MAP)";
            var refShortLabel = refType == PriceChangeRef.Offer ? "Oferta" : "Zakup";

            var introRow = sheet.CreateRow(0);
            introRow.HeightInPoints = 22;
            var introCell = introRow.CreateCell(0);
            introCell.SetCellValue($"Oś czasu wojny cenowej vs {refShortLabel} — kto pierwszy złamał {refLabel}");
            introCell.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 8));

            var descRow = sheet.CreateRow(1);
            descRow.HeightInPoints = 55;
            var descCell = descRow.CreateCell(0);
            descCell.SetCellValue(
                $"Dla każdego produktu z wyłomem pokazana jest CHRONOLOGICZNA kolejność zejścia konkurentów poniżej naszej {refLabel}. " +
                "Pozycja #1 = INICJATOR. Kolejni = followerzy. 'Od inicjatora' = godziny po pierwszym wyłomie. " +
                "'Od poprzedniego' = odstęp do bezpośrednio poprzedzającego. " +
                "UWAGA: rozdzielczość zależy od częstotliwości scrapów — przy 1 scrapie dziennie 'szybki follower' wygląda jak +24h. " +
                "Sortowanie: najnowsze wojny na górze. Produkty bez wyłomu są pomijane.");
            var wrap = wb.CreateCellStyle();
            wrap.CloneStyleFrom(styles.Default);
            wrap.WrapText = true;
            wrap.VerticalAlignment = VerticalAlignment.Top;
            descCell.CellStyle = wrap;
            sheet.AddMergedRegion(new CellRangeAddress(1, 1, 0, 8));

            int headerRowIdx = 3;
            var headerRow = sheet.CreateRow(headerRowIdx);
            headerRow.HeightInPoints = 28;

            string[] headers = { "Pozycja", "Rola", "Konkurent", "Data wyłomu",
                         "Od inicjatora", "Od poprzedniego",
                         "Cena konkurenta", $"Nasza ref. ({refShortLabel})", "Wyłom %" };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = headerRow.CreateCell(i);
                c.SetCellValue(headers[i]);
                c.CellStyle = styles.HeaderDark;
            }

            int rowIdx = headerRowIdx + 1;
            int productIndex = 0;

            var productsSorted = matrix
                .Where(p => timelines.GetValueOrDefault(p.ProductId)?.Any() == true)
                .OrderByDescending(p => timelines[p.ProductId][0].ScrapDate)
                .ToList();

            foreach (var product in productsSorted)
            {
                var events = timelines[product.ProductId];

                // Cena referencyjna zależy od perspektywy
                decimal? refPrice = refType == PriceChangeRef.Offer
                    ? product.LatestOurPrice
                    : product.PurchasePrice;

                var headerP = sheet.CreateRow(rowIdx);
                headerP.HeightInPoints = 24;
                var hc = headerP.CreateCell(0);
                var brandTxt = !string.IsNullOrEmpty(product.Producer) ? $" | {product.Producer}" : "";
                var refTxt = refPrice.HasValue ? $"{refPrice.Value:N2} zł" : "brak";
                hc.SetCellValue($"📦 {product.ProductName}{brandTxt}  |  EAN: {(string.IsNullOrEmpty(product.Ean) ? "—" : product.Ean)}  |  Nasza {refShortLabel}: {refTxt}  |  Łamaczy: {events.Count}");
                hc.CellStyle = styles.ProductBlockHeader;
                sheet.AddMergedRegion(new CellRangeAddress(rowIdx, rowIdx, 0, headers.Length - 1));
                rowIdx++;

                bool bandRow = productIndex % 2 == 1;
                var baseStyle = bandRow ? styles.BandDefault : styles.Default;
                var baseCurrency = bandRow ? styles.BandCurrency : styles.Currency;
                var baseBaseline = bandRow ? styles.BandBaselineCurrency : styles.BaselineCurrency;

                foreach (var ev in events)
                {
                    var row = sheet.CreateRow(rowIdx++);
                    int col = 0;

                    var posCell = row.CreateCell(col++);
                    posCell.SetCellValue($"#{ev.Rank}");
                    posCell.CellStyle = ev.Rank == 1 ? styles.FirstBreakerMarker : styles.FollowerMarker;

                    var roleCell = row.CreateCell(col++);
                    if (ev.Rank == 1)
                    {
                        roleCell.SetCellValue("🥇 INICJATOR");
                        roleCell.CellStyle = styles.FirstBreakerMarker;
                    }
                    else
                    {
                        string delayStr = ev.DelayHoursFromInitiator < 1 ? "<1h" :
                                          ev.DelayHoursFromInitiator < 24 ? $"+{(int)ev.DelayHoursFromInitiator}h" :
                                          $"+{(int)(ev.DelayHoursFromInitiator / 24)}d";
                        roleCell.SetCellValue($"🔄 Follower ({delayStr})");
                        roleCell.CellStyle = styles.FollowerMarker;
                    }

                    SetCell(row, col++, ev.CompetitorName, baseStyle);

                    var dateCell = row.CreateCell(col++);
                    dateCell.SetCellValue(ev.ScrapDate.ToString("dd.MM.yyyy HH:mm"));
                    dateCell.CellStyle = baseStyle;

                    var fromInitCell = row.CreateCell(col++);
                    if (ev.Rank == 1) { fromInitCell.SetCellValue("— (start)"); fromInitCell.CellStyle = baseStyle; }
                    else
                    {
                        fromInitCell.SetCellValue(FormatDelay(ev.DelayHoursFromInitiator));
                        fromInitCell.CellStyle = GetDelayStyle(styles, ev.DelayHoursFromInitiator);
                    }

                    var fromPrevCell = row.CreateCell(col++);
                    if (ev.Rank == 1) { fromPrevCell.SetCellValue("— (start)"); fromPrevCell.CellStyle = baseStyle; }
                    else
                    {
                        fromPrevCell.SetCellValue(FormatDelay(ev.DelayHoursFromPrevious));
                        fromPrevCell.CellStyle = GetDelayStyle(styles, ev.DelayHoursFromPrevious);
                    }

                    var priceCell = row.CreateCell(col++);
                    priceCell.SetCellValue((double)ev.Price);
                    priceCell.CellStyle = baseCurrency;

                    var refCellVal = row.CreateCell(col++);
                    refCellVal.SetCellValue((double)ev.OurRefPrice);
                    refCellVal.CellStyle = baseBaseline;

                    var pctCell = row.CreateCell(col++);
                    pctCell.SetCellValue((double)ev.ViolationPct);
                    pctCell.CellStyle = GetViolationPctStyle(styles, ev.ViolationPct);
                }

                productIndex++;
            }

            sheet.CreateFreezePane(0, headerRowIdx + 1);
            sheet.SetColumnWidth(0, 10 * 256);
            sheet.SetColumnWidth(1, 22 * 256);
            sheet.SetColumnWidth(2, 28 * 256);
            sheet.SetColumnWidth(3, 18 * 256);
            sheet.SetColumnWidth(4, 15 * 256);
            sheet.SetColumnWidth(5, 16 * 256);
            sheet.SetColumnWidth(6, 16 * 256);
            sheet.SetColumnWidth(7, 14 * 256);
            sheet.SetColumnWidth(8, 12 * 256);
        }

        private string FormatDelay(double hours)
        {
            if (hours < 0) return "—";
            if (hours < 1) return "< 1 godz.";
            if (hours < 24) return $"{(int)hours} godz.";
            int days = (int)(hours / 24);
            int remH = (int)(hours - days * 24);
            return remH > 0 ? $"{days} dni {remH} godz." : $"{days} dni";
        }

        private ICellStyle GetDelayStyle(ExportStyles s, double hours)
        {
            if (hours < 1) return s.DelayInstant;
            if (hours < 12) return s.DelayFast;
            if (hours < 48) return s.DelayMedium;
            return s.DelaySlow;
        }

        private ICellStyle GetViolationPctStyle(ExportStyles s, decimal pct)
        {
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


        private void WritePriceWarProductRankingSheet(XSSFWorkbook wb, string sheetName,
            List<PriceChangeProduct> matrix, List<(int Id, DateTime Date)> scraps,
            Dictionary<int, List<PriceWarEvent>> timelines,
            PriceChangeRef refType,  // <-- NOWE
            ExportStyles styles)
        {
            var sheet = wb.CreateSheet(sheetName);

            var refShortLabel = refType == PriceChangeRef.Offer ? "Oferta" : "Zakup";
            var refLabel = refType == PriceChangeRef.Offer ? "cenę oferty" : "cenę zakupu (MAP)";

            var introRow = sheet.CreateRow(0);
            introRow.HeightInPoints = 22;
            var introCell = introRow.CreateCell(0);
            introCell.SetCellValue($"Liderzy per produkt vs {refShortLabel} — kto rozpoczął wojnę w każdym produkcie");
            introCell.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 11));

            var descRow = sheet.CreateRow(1);
            descRow.HeightInPoints = 45;
            var descCell = descRow.CreateCell(0);
            descCell.SetCellValue(
                $"Jeden wiersz = jeden produkt z wyłomem vs {refLabel}. INICJATOR + pierwszych 3 followerów z ich delay'ami. " +
                "Sortowanie: najnowsze inicjacje na górze. Filtr na kolumnie 'Inicjator' → które produkty startował konkretny konkurent. " +
                "'Łamaczy łącznie' ≥ 5 → spalony produkt (lepiej nie walczyć ceną, rozważ inną strategię).");
            var wrap = wb.CreateCellStyle();
            wrap.CloneStyleFrom(styles.Default);
            wrap.WrapText = true;
            wrap.VerticalAlignment = VerticalAlignment.Top;
            descCell.CellStyle = wrap;
            sheet.AddMergedRegion(new CellRangeAddress(1, 1, 0, 11));

            int headerRowIdx = 3;
            var headerRow = sheet.CreateRow(headerRowIdx);
            headerRow.HeightInPoints = 28;

            string[] headers = { "Produkt", "EAN", "Marka", $"Nasza cena ({refShortLabel})",
                         "🥇 Inicjator", "Data inicjacji", "Wyłom % (init.)",
                         "🥈 Follower 1 (+Δh)", "🥉 Follower 2 (+Δh)", "Follower 3 (+Δh)",
                         "Łamaczy łącznie", "Szybkość kaskady" };
            for (int i = 0; i < headers.Length; i++)
            {
                var c = headerRow.CreateCell(i);
                c.SetCellValue(headers[i]);
                c.CellStyle = styles.HeaderDark;
            }

            int rowIdx = headerRowIdx + 1;

            var productsSorted = matrix
                .Where(p => timelines.GetValueOrDefault(p.ProductId)?.Any() == true)
                .OrderByDescending(p => timelines[p.ProductId][0].ScrapDate)
                .ToList();

            foreach (var product in productsSorted)
            {
                var events = timelines[product.ProductId];
                var row = sheet.CreateRow(rowIdx++);
                int col = 0;

                SetCell(row, col++, product.ProductName ?? "", styles.Default);
                SetCell(row, col++, product.Ean ?? "", styles.Default);
                SetCell(row, col++, product.Producer ?? "", styles.Default);

                // Cena referencyjna zależy od perspektywy
                decimal? refPrice = refType == PriceChangeRef.Offer
                    ? product.LatestOurPrice
                    : product.PurchasePrice;

                var refCell = row.CreateCell(col++);
                if (refPrice.HasValue)
                {
                    refCell.SetCellValue((double)refPrice.Value);
                    refCell.CellStyle = styles.BaselineCurrency;
                }
                else { refCell.SetCellValue("—"); refCell.CellStyle = styles.NoDataCell; }

                var init = events[0];
                var initCell = row.CreateCell(col++);
                initCell.SetCellValue(init.CompetitorName);
                initCell.CellStyle = styles.FirstBreakerMarker;

                var initDateCell = row.CreateCell(col++);
                initDateCell.SetCellValue(init.ScrapDate.ToString("dd.MM.yyyy HH:mm"));
                initDateCell.CellStyle = styles.Default;

                var initPctCell = row.CreateCell(col++);
                initPctCell.SetCellValue((double)init.ViolationPct);
                initPctCell.CellStyle = GetViolationPctStyle(styles, init.ViolationPct);

                for (int fi = 1; fi <= 3; fi++)
                {
                    var fc = row.CreateCell(col++);
                    if (events.Count > fi)
                    {
                        var f = events[fi];
                        string delayStr = f.DelayHoursFromInitiator < 1 ? "<1h" :
                                          f.DelayHoursFromInitiator < 24 ? $"+{(int)f.DelayHoursFromInitiator}h" :
                                          $"+{(int)(f.DelayHoursFromInitiator / 24)}d";
                        fc.SetCellValue($"{f.CompetitorName} ({delayStr})");
                        fc.CellStyle = GetDelayStyle(styles, f.DelayHoursFromInitiator);
                    }
                    else { fc.SetCellValue("—"); fc.CellStyle = styles.NoDataCell; }
                }

                var totalCell = row.CreateCell(col++);
                totalCell.SetCellValue(events.Count);
                totalCell.CellStyle = events.Count >= 5 ? styles.CellRedBg : styles.Default;

                var cascadeCell = row.CreateCell(col++);
                if (events.Count <= 1)
                {
                    cascadeCell.SetCellValue("samotnik");
                    cascadeCell.CellStyle = styles.Default;
                }
                else
                {
                    var avgDelay = events.Skip(1).Average(e => e.DelayHoursFromInitiator);
                    string speedLabel;
                    ICellStyle speedStyle;
                    if (avgDelay < 24) { speedLabel = $"⚡ Błyskawica ({(int)avgDelay}h)"; speedStyle = styles.DelayInstant; }
                    else if (avgDelay < 72) { speedLabel = $"🔥 Szybka ({(int)(avgDelay / 24)}d)"; speedStyle = styles.DelayFast; }
                    else if (avgDelay < 168) { speedLabel = $"🐢 Umiarkowana ({(int)(avgDelay / 24)}d)"; speedStyle = styles.DelayMedium; }
                    else { speedLabel = $"🐌 Powolna ({(int)(avgDelay / 24)}d)"; speedStyle = styles.DelaySlow; }
                    cascadeCell.SetCellValue(speedLabel);
                    cascadeCell.CellStyle = speedStyle;
                }
            }

            int lastDataRowIdx = rowIdx - 1;
            sheet.CreateFreezePane(1, headerRowIdx + 1);
            if (lastDataRowIdx >= headerRowIdx + 1)
                sheet.SetAutoFilter(new CellRangeAddress(headerRowIdx, lastDataRowIdx, 0, headers.Length - 1));

            sheet.SetColumnWidth(0, 40 * 256);
            sheet.SetColumnWidth(1, 15 * 256);
            sheet.SetColumnWidth(2, 18 * 256);
            sheet.SetColumnWidth(3, 14 * 256);
            sheet.SetColumnWidth(4, 24 * 256);
            sheet.SetColumnWidth(5, 18 * 256);
            sheet.SetColumnWidth(6, 13 * 256);
            sheet.SetColumnWidth(7, 28 * 256);
            sheet.SetColumnWidth(8, 28 * 256);
            sheet.SetColumnWidth(9, 28 * 256);
            sheet.SetColumnWidth(10, 15 * 256);
            sheet.SetColumnWidth(11, 22 * 256);
        }

        // ====================================================================
        // [NOWE] ZAKŁADKA: MACIERZ LIDER→FOLLOWER
        // ====================================================================


        private void WritePriceWarLeaderFollowerMatrixSheet(XSSFWorkbook wb, string sheetName,
            List<PriceChangeProduct> matrix,
            Dictionary<int, List<PriceWarEvent>> timelines,
            PriceChangeRef refType,  // <-- NOWE
            ExportStyles styles)
        {
            var sheet = wb.CreateSheet(sheetName);

            var refShortLabel = refType == PriceChangeRef.Offer ? "Oferta" : "Zakup";
            var refLabel = refType == PriceChangeRef.Offer ? "cenę oferty" : "cenę zakupu (MAP)";

            var introRow = sheet.CreateRow(0);
            introRow.HeightInPoints = 22;
            var introCell = introRow.CreateCell(0);
            introCell.SetCellValue($"Macierz lider→follower vs {refShortLabel} — kto za kim podąża w wojnach cenowych");
            introCell.CellStyle = styles.HeaderDark;

            var lfCount = new Dictionary<(string leader, string follower), int>();
            var leaderCount = new Dictionary<string, int>();
            var followerCount = new Dictionary<string, int>();
            var compDisplay = new Dictionary<string, string>();

            foreach (var p in matrix)
            {
                var events = timelines.GetValueOrDefault(p.ProductId);
                if (events == null || events.Count == 0) continue;

                var init = events[0];
                leaderCount[init.CompetitorKey] = leaderCount.GetValueOrDefault(init.CompetitorKey, 0) + 1;
                compDisplay[init.CompetitorKey] = init.CompetitorName;

                for (int i = 1; i < events.Count; i++)
                {
                    var f = events[i];
                    followerCount[f.CompetitorKey] = followerCount.GetValueOrDefault(f.CompetitorKey, 0) + 1;
                    compDisplay[f.CompetitorKey] = f.CompetitorName;

                    var key = (init.CompetitorKey, f.CompetitorKey);
                    lfCount[key] = lfCount.GetValueOrDefault(key, 0) + 1;
                }
            }

            var allCompKeys = compDisplay.Keys.OrderBy(k => compDisplay[k], StringComparer.OrdinalIgnoreCase).ToList();

            if (!allCompKeys.Any())
            {
                sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 4));
                var emptyRow = sheet.CreateRow(2);
                emptyRow.CreateCell(0).SetCellValue($"Brak wojen cenowych vs {refLabel} w wybranym oknie analizy.");
                return;
            }

            int totalCols = allCompKeys.Count + 3;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, Math.Min(totalCols - 1, 15)));

            var descRow = sheet.CreateRow(1);
            descRow.HeightInPoints = 60;
            var descCell = descRow.CreateCell(0);
            descCell.SetCellValue(
                $"Perspektywa: vs {refLabel}. " +
                "Wiersz = INICJATOR. Kolumna = FOLLOWER. Wartość = liczba produktów, w których wiersz zainicjował obniżkę, a kolumna podążyła. " +
                "Im czerwieńsza, tym mocniejszy związek (ten follower regularnie reaguje na tego lidera). " +
                "Kolumna '∑ jako inicjator' = ile razy ten konkurent rozpoczął wojnę. Wiersz '∑ jako follower' na dole — ile razy podążył. " +
                "Szukaj par z wysoką wartością: jeśli X→Y = 15 produktów, to Y aktywnie monitoruje ceny X (możliwe że bot).");
            var wrap = wb.CreateCellStyle();
            wrap.CloneStyleFrom(styles.Default);
            wrap.WrapText = true;
            wrap.VerticalAlignment = VerticalAlignment.Top;
            descCell.CellStyle = wrap;
            sheet.AddMergedRegion(new CellRangeAddress(1, 1, 0, Math.Min(totalCols - 1, 15)));

            int headerRowIdx = 3;
            var hr = sheet.CreateRow(headerRowIdx);
            hr.HeightInPoints = 40;

            var corner = hr.CreateCell(0);
            corner.SetCellValue("INICJATOR ↓ / FOLLOWER →");
            corner.CellStyle = styles.HeaderDark;

            for (int i = 0; i < allCompKeys.Count; i++)
            {
                var c = hr.CreateCell(i + 1);
                c.SetCellValue(compDisplay[allCompKeys[i]]);
                c.CellStyle = styles.HeaderDark;
            }
            var sumInitCell = hr.CreateCell(allCompKeys.Count + 1);
            sumInitCell.SetCellValue("∑ jako inicjator");
            sumInitCell.CellStyle = styles.HeaderDark;

            int maxCount = lfCount.Values.DefaultIfEmpty(0).Max();

            int rowIdx = headerRowIdx + 1;
            foreach (var leaderKey in allCompKeys)
            {
                var row = sheet.CreateRow(rowIdx++);
                var nameCell = row.CreateCell(0);
                nameCell.SetCellValue(compDisplay[leaderKey]);
                nameCell.CellStyle = styles.ProductBlockSubHeader;

                for (int i = 0; i < allCompKeys.Count; i++)
                {
                    var followerKey = allCompKeys[i];
                    var cell = row.CreateCell(i + 1);
                    if (leaderKey == followerKey)
                    {
                        cell.SetCellValue("—");
                        cell.CellStyle = styles.DiagonalCell;
                        continue;
                    }
                    int cnt = lfCount.GetValueOrDefault((leaderKey, followerKey), 0);
                    cell.SetCellValue(cnt);
                    cell.CellStyle = GetMatrixCellStyle(styles, cnt, maxCount);
                }

                var sumCell = row.CreateCell(allCompKeys.Count + 1);
                sumCell.SetCellValue(leaderCount.GetValueOrDefault(leaderKey, 0));
                sumCell.CellStyle = styles.ProductBlockSubHeader;
            }

            var sumRow = sheet.CreateRow(rowIdx++);
            var sumRowHeader = sumRow.CreateCell(0);
            sumRowHeader.SetCellValue("∑ jako follower");
            sumRowHeader.CellStyle = styles.ProductBlockHeader;

            for (int i = 0; i < allCompKeys.Count; i++)
            {
                var cell = sumRow.CreateCell(i + 1);
                cell.SetCellValue(followerCount.GetValueOrDefault(allCompKeys[i], 0));
                cell.CellStyle = styles.ProductBlockSubHeader;
            }

            sheet.CreateFreezePane(1, headerRowIdx + 1);
            sheet.SetColumnWidth(0, 26 * 256);
            for (int i = 1; i <= allCompKeys.Count; i++) sheet.SetColumnWidth(i, 14 * 256);
            sheet.SetColumnWidth(allCompKeys.Count + 1, 16 * 256);
        }


        private ICellStyle GetMatrixCellStyle(ExportStyles s, int value, int max)
        {
            if (value == 0) return s.Default;
            if (max <= 0) return s.CellRed1;
            double ratio = (double)value / max;
            if (ratio <= 0.1) return s.CellRed1;
            if (ratio <= 0.2) return s.CellRed2;
            if (ratio <= 0.3) return s.CellRed3;
            if (ratio <= 0.4) return s.CellRed4;
            if (ratio <= 0.5) return s.CellRed5;
            if (ratio <= 0.6) return s.CellRed6;
            if (ratio <= 0.7) return s.CellRed7;
            if (ratio <= 0.8) return s.CellRed8;
            if (ratio <= 0.9) return s.CellRed9;
            return s.CellRed10;
        }


        private void WriteAggressionHeatmapSheet(XSSFWorkbook wb, string sheetName,
            List<PriceChangeProduct> matrix, List<(int Id, DateTime Date)> scraps,
            PriceChangeRef refType,  // <-- NOWE
            ExportStyles styles)
        {
            var sheet = wb.CreateSheet(sheetName);

            var refShortLabel = refType == PriceChangeRef.Offer ? "Oferta" : "Zakup";
            var refLabel = refType == PriceChangeRef.Offer ? "cenę oferty" : "cenę zakupu (MAP)";

            var introRow = sheet.CreateRow(0);
            introRow.HeightInPoints = 22;
            var introCell = introRow.CreateCell(0);
            introCell.SetCellValue($"Mapa agresji vs {refShortLabel} — kto na których markach najmocniej uderza");
            introCell.CellStyle = styles.HeaderDark;

            // Zbieramy dane: per (konkurent, marka) → lista wszystkich % wyłomów (avg, nie max!)
            // Każdy produkt w parze (comp, brand) daje jeden wpis = AvgViolationPercent
            var cellAvgViolations = new Dictionary<(string comp, string brand), List<decimal>>();
            var cellMaxViolations = new Dictionary<(string comp, string brand), decimal>();
            var cellProductCount = new Dictionary<(string comp, string brand), int>();
            var compDisplay = new Dictionary<string, string>();
            var allBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in matrix)
            {
                string brand = string.IsNullOrWhiteSpace(p.Producer) ? "(brak marki)" : p.Producer.Trim();
                allBrands.Add(brand);

                foreach (var comp in p.Competitors)
                {
                    var st = ComputeCompetitorStats(p, comp, scraps, refType);  // <-- refType zamiast hardcoded Offer
                    if (st.ViolationCount == 0) continue;

                    var compKey = comp.StoreName.ToLower().Trim();
                    compDisplay[compKey] = comp.StoreName;

                    var key = (compKey, brand);
                    if (!cellAvgViolations.ContainsKey(key))
                    {
                        cellAvgViolations[key] = new List<decimal>();
                        cellMaxViolations[key] = 0m;
                    }
                    cellAvgViolations[key].Add(st.AvgViolationPercent);
                    if (st.MaxViolationPercent > cellMaxViolations[key])
                        cellMaxViolations[key] = st.MaxViolationPercent;
                    cellProductCount[key] = cellProductCount.GetValueOrDefault(key, 0) + 1;
                }
            }

            if (!cellAvgViolations.Any())
            {
                sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 4));
                var emptyRow = sheet.CreateRow(2);
                emptyRow.CreateCell(0).SetCellValue($"Brak wyłomów vs {refLabel} w wybranym oknie analizy.");
                return;
            }

            var brandsList = allBrands
                .Where(b => cellAvgViolations.Keys.Any(k => k.brand == b))
                .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var compKeysList = compDisplay.Keys.ToList();

            // --- Kolumny: [Konkurent] + per marka 3 kolumny (Śr.%, Max%, Szt.) + [∑ produktów, Śr. agresja] ---
            int brandsColStart = 1;
            int colsPerBrand = 3; // Śr.%, Max%, Szt.
            int totalBrandCols = brandsList.Count * colsPerBrand;
            int summaryColStart = brandsColStart + totalBrandCols;
            int lastCol = summaryColStart + 1; // ∑ produktów, Śr. agresja

            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, Math.Min(lastCol, 15)));

            var descRow = sheet.CreateRow(1);
            descRow.HeightInPoints = 60;
            var descCell = descRow.CreateCell(0);
            descCell.SetCellValue(
                $"Perspektywa: vs {refLabel}. " +
                "Wiersze = konkurenci (posortowani od najbardziej agresywnego). Kolumny = marki z Twojego katalogu. " +
                "Per marka 3 kolumny: 'Śr.%' = średni wyłom w %, 'Max%' = najgorszy wyłom, 'Szt.' = ile produktów złamanych. " +
                "Intensywność czerwieni proporcjonalna do najgorszej średniej w pliku. " +
                "Pierwszy wiersz = Twój główny przeciwnik cenowy. Cała kolumna (marka) w czerwieni → kategoria pod ogniem.");
            var wrap = wb.CreateCellStyle();
            wrap.CloneStyleFrom(styles.Default);
            wrap.WrapText = true;
            wrap.VerticalAlignment = VerticalAlignment.Top;
            descCell.CellStyle = wrap;
            sheet.AddMergedRegion(new CellRangeAddress(1, 1, 0, Math.Min(lastCol, 15)));

            // --- Wiersz nagłówka marek (merged po 3 kolumny) ---
            int brandHeaderRowIdx = 3;
            var brandHeaderRow = sheet.CreateRow(brandHeaderRowIdx);
            brandHeaderRow.HeightInPoints = 22;

            var cornerCell = brandHeaderRow.CreateCell(0);
            cornerCell.SetCellValue("");
            cornerCell.CellStyle = styles.HeaderDark;

            for (int bi = 0; bi < brandsList.Count; bi++)
            {
                int startCol = brandsColStart + bi * colsPerBrand;
                var brandCell = brandHeaderRow.CreateCell(startCol);
                brandCell.SetCellValue(brandsList[bi]);
                brandCell.CellStyle = styles.HeaderDark;
                // Merge 3 kolumny pod nazwą marki
                if (colsPerBrand > 1)
                    sheet.AddMergedRegion(new CellRangeAddress(brandHeaderRowIdx, brandHeaderRowIdx,
                        startCol, startCol + colsPerBrand - 1));
            }

            var sumHeaderCell = brandHeaderRow.CreateCell(summaryColStart);
            sumHeaderCell.SetCellValue("∑ produktów");
            sumHeaderCell.CellStyle = styles.HeaderDark;

            var avgHeaderCell = brandHeaderRow.CreateCell(summaryColStart + 1);
            avgHeaderCell.SetCellValue("Śr. agresja %");
            avgHeaderCell.CellStyle = styles.HeaderDark;

            // --- Wiersz pod-nagłówka (Śr.%, Max%, Szt. per marka) ---
            int subHeaderRowIdx = brandHeaderRowIdx + 1;
            var subHeaderRow = sheet.CreateRow(subHeaderRowIdx);
            subHeaderRow.HeightInPoints = 20;

            var subCorner = subHeaderRow.CreateCell(0);
            subCorner.SetCellValue("KONKURENT ↓");
            subCorner.CellStyle = styles.ProductBlockSubHeader;

            for (int bi = 0; bi < brandsList.Count; bi++)
            {
                int startCol = brandsColStart + bi * colsPerBrand;
                var c1 = subHeaderRow.CreateCell(startCol);
                c1.SetCellValue("Śr.%");
                c1.CellStyle = styles.ProductBlockSubHeader;

                var c2 = subHeaderRow.CreateCell(startCol + 1);
                c2.SetCellValue("Max%");
                c2.CellStyle = styles.ProductBlockSubHeader;

                var c3 = subHeaderRow.CreateCell(startCol + 2);
                c3.SetCellValue("Szt.");
                c3.CellStyle = styles.ProductBlockSubHeader;
            }

            // --- Oblicz maxAvgAgg do skalowania koloru ---
            decimal maxAvgAgg = 0m;
            foreach (var kv in cellAvgViolations)
            {
                var avg = kv.Value.Average();
                if (avg > maxAvgAgg) maxAvgAgg = avg;
            }
            if (maxAvgAgg == 0) maxAvgAgg = 1;

            // --- Sortuj konkurentów od najbardziej agresywnego ---
            var compSorted = compKeysList
                .Select(k =>
                {
                    var totalProducts = brandsList.Sum(b => cellProductCount.GetValueOrDefault((k, b), 0));
                    var avgAgg = brandsList
                        .Select(b => cellAvgViolations.TryGetValue((k, b), out var list) ? list.Average() : 0m)
                        .Where(v => v > 0)
                        .DefaultIfEmpty(0m)
                        .Average();
                    return new { Key = k, TotalProducts = totalProducts, AvgAgg = avgAgg };
                })
                .OrderByDescending(x => x.AvgAgg)
                .ThenByDescending(x => x.TotalProducts)
                .ToList();

            // --- Wiersze danych ---
            int rowIdx = subHeaderRowIdx + 1;
            foreach (var compX in compSorted)
            {
                var row = sheet.CreateRow(rowIdx++);
                row.HeightInPoints = 22;

                var nc = row.CreateCell(0);
                nc.SetCellValue(compDisplay[compX.Key]);
                nc.CellStyle = styles.ProductBlockSubHeader;

                for (int bi = 0; bi < brandsList.Count; bi++)
                {
                    var brand = brandsList[bi];
                    int startCol = brandsColStart + bi * colsPerBrand;

                    if (cellAvgViolations.TryGetValue((compX.Key, brand), out var avgList))
                    {
                        var avg = Math.Round(avgList.Average(), 1);
                        var max = Math.Round(cellMaxViolations[(compX.Key, brand)], 1);
                        int cnt = cellProductCount[(compX.Key, brand)];

                        var avgCellVal = row.CreateCell(startCol);
                        avgCellVal.SetCellValue((double)avg);
                        avgCellVal.CellStyle = GetHeatmapStyleForAggression(styles, avg, maxAvgAgg);

                        var maxCellVal = row.CreateCell(startCol + 1);
                        maxCellVal.SetCellValue((double)max);
                        maxCellVal.CellStyle = GetHeatmapStyleForAggression(styles, max, maxAvgAgg);

                        var cntCellVal = row.CreateCell(startCol + 2);
                        cntCellVal.SetCellValue(cnt);
                        cntCellVal.CellStyle = cnt >= 5 ? styles.CellRedBg : styles.Default;
                    }
                    else
                    {
                        row.CreateCell(startCol).CellStyle = styles.Default;
                        row.CreateCell(startCol + 1).CellStyle = styles.Default;
                        row.CreateCell(startCol + 2).CellStyle = styles.Default;
                    }
                }

                var totalCellR = row.CreateCell(summaryColStart);
                totalCellR.SetCellValue(compX.TotalProducts);
                totalCellR.CellStyle = styles.ProductBlockSubHeader;

                var avgCellR = row.CreateCell(summaryColStart + 1);
                avgCellR.SetCellValue((double)Math.Round(compX.AvgAgg, 1));
                avgCellR.CellStyle = GetHeatmapStyleForAggression(styles, compX.AvgAgg, maxAvgAgg);
            }

            sheet.CreateFreezePane(1, subHeaderRowIdx + 1);
            sheet.SetColumnWidth(0, 26 * 256);
            for (int bi = 0; bi < brandsList.Count; bi++)
            {
                int startCol = brandsColStart + bi * colsPerBrand;
                sheet.SetColumnWidth(startCol, 10 * 256);     // Śr.%
                sheet.SetColumnWidth(startCol + 1, 10 * 256);  // Max%
                sheet.SetColumnWidth(startCol + 2, 7 * 256);   // Szt.
            }
            sheet.SetColumnWidth(summaryColStart, 13 * 256);
            sheet.SetColumnWidth(summaryColStart + 1, 14 * 256);
        }

        private ICellStyle GetHeatmapStyleForAggression(ExportStyles s, decimal value, decimal max)
        {
            if (value <= 0) return s.Default;
            double ratio = max > 0 ? (double)(value / max) : 0;
            if (ratio <= 0.1) return s.CellRed1;
            if (ratio <= 0.2) return s.CellRed2;
            if (ratio <= 0.3) return s.CellRed3;
            if (ratio <= 0.4) return s.CellRed4;
            if (ratio <= 0.5) return s.CellRed5;
            if (ratio <= 0.6) return s.CellRed6;
            if (ratio <= 0.7) return s.CellRed7;
            if (ratio <= 0.8) return s.CellRed8;
            if (ratio <= 0.9) return s.CellRed9;
            return s.CellRed10;
        }


        private void WritePriceChangeLegendSheet(XSSFWorkbook wb, string sheetName, ExportStyles styles, bool isMarketplace)
        {
            var sheet = wb.CreateSheet(sheetName);

            int r = 0;
            var title = sheet.CreateRow(r++);
            title.HeightInPoints = 26;
            var titleCell = title.CreateCell(0);
            titleCell.SetCellValue("Legenda — jak czytać raport wojen cenowych");
            titleCell.CellStyle = styles.HeaderDark;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 2));

            r++;

            WriteLegendHeader(sheet, r++, "Zakładki w tym pliku (jedenaście)", styles);
            WriteLegendPair(sheet, r++, "vs Oferta", "Płaska tabela: kolumny z datami scrapów × wiersze konkurentów, pogrupowane w bloki per produkt. Nasza cena oferty (na rynku) to referencja. Kolorowe komórki = ktoś zszedł poniżej.", styles);
            WriteLegendPair(sheet, r++, "vs Zakup", "Jak wyżej, ale referencja to cena zakupu (MAP). Czerwone komórki = ktoś sprzedaje poniżej Twojego kosztu — naruszenie MAP.", styles);
            WriteLegendPair(sheet, r++, "Oś czasu vs Oferta", "Kaskada per produkt (vs cena oferty): kto pierwszy zszedł poniżej nas, kto za nim, po ilu godzinach.", styles);
            WriteLegendPair(sheet, r++, "Oś czasu vs Zakup", "Jak wyżej, ale referencja to cena zakupu. Pokazuje kto łamie MAP i w jakiej kolejności.", styles);
            WriteLegendPair(sheet, r++, "Liderzy vs Oferta", "Jeden wiersz = jeden produkt. Kto był inicjatorem obniżki poniżej naszej oferty, kim byli followerzy, z delay'ami.", styles);
            WriteLegendPair(sheet, r++, "Liderzy vs Zakup", "Jak wyżej, ale referencja to cena zakupu. Kto inicjuje sprzedaż poniżej MAP.", styles);
            WriteLegendPair(sheet, r++, "Macierz L→F vs Oferta", "Krzyżowa tabela konkurent×konkurent (vs oferta): ile razy wiersz (lider) zainicjował obniżkę, a kolumna (follower) za nim podążyła.", styles);
            WriteLegendPair(sheet, r++, "Macierz L→F vs Zakup", "Jak wyżej, ale perspektywa vs cena zakupu (MAP).", styles);
            WriteLegendPair(sheet, r++, "Agresja vs Oferta", "Heatmapa konkurent × marka: który konkurent na której marce najbardziej podkopuje naszą cenę oferty.", styles);
            WriteLegendPair(sheet, r++, "Agresja vs Zakup", "Jak wyżej, ale perspektywa vs cena zakupu (MAP). Czerwień = ktoś sprzedaje poniżej naszego kosztu na tej marce.", styles);
            WriteLegendPair(sheet, r++, "Legenda", "Ten arkusz.", styles);

            r++;

            WriteLegendHeader(sheet, r++, "Dwie perspektywy — vs Oferta i vs Zakup", styles);
            WriteLegendPair(sheet, r++, "vs Oferta (cena rynkowa)", "Nasza aktualna cena sprzedaży na rynku. Pokazuje kto jest tańszy od nas i na ile. Przydatne do codziennego zarządzania cenami.", styles);
            WriteLegendPair(sheet, r++, "vs Zakup (MAP / cena zakupu)", "Nasza cena zakupu (koszt). Naruszenie = sprzedaż poniżej kosztu. Krytyczne dla ochrony marży i egzekwowania MAP z dostawcą. " +
                "Częsty scenariusz: produkt ma wyłomy vs Oferta, ale nie vs Zakup → konkurent obniżył cenę, ale nadal jest powyżej MAP. Odwrotnie: wyłom vs Zakup bez wyłomu vs Oferta → my sprzedajemy poniżej MAP, ale konkurent jest jeszcze droższy.", styles);

            r++;

            WriteLegendHeader(sheet, r++, "Role w wojnie cenowej", styles);
            WriteLegendColor(sheet, r++, styles.FirstBreakerMarker, "🥇 INICJATOR", "Konkurent który PIERWSZY dla danego produktu zszedł poniżej naszej referencji w oknie analizy. Osoba do obserwacji — to on testuje rynek lub atakuje.", styles);
            WriteLegendColor(sheet, r++, styles.FollowerMarker, "🔄 Follower (+Δh)", "Konkurent który zszedł poniżej PO inicjatorze. Liczba godzin obok pokazuje jak szybko zareagował.", styles);
            WriteLegendPair(sheet, r++, "😴 Spokojny", "Konkurent który w tym produkcie nie zszedł poniżej naszej referencji w całym oknie analizy.", styles);

            r++;

            WriteLegendHeader(sheet, r++, "Szybkość reakcji followera (kolory w kolumnach 'Od inicjatora' / 'Od poprzedniego')", styles);
            WriteLegendColor(sheet, r++, styles.DelayInstant, "⚡ < 1 godz.", "Błyskawiczna reakcja — prawdopodobnie automat (bot cenowy) śledzi lidera.", styles);
            WriteLegendColor(sheet, r++, styles.DelayFast, "🔥 1-12 godz.", "Szybka reakcja — aktywny monitoring (ręczny lub półautomatyczny).", styles);
            WriteLegendColor(sheet, r++, styles.DelayMedium, "🐢 12-48 godz.", "Umiarkowana — dzień-dwa zanim zauważył/zmienił.", styles);
            WriteLegendColor(sheet, r++, styles.DelaySlow, "🐌 > 48 godz.", "Powolna — kilka dni. Być może niezależna decyzja, nie follower.", styles);

            r++;

            WriteLegendHeader(sheet, r++, "Szybkość kaskady (kolumna w 'Liderzy')", styles);
            WriteLegendPair(sheet, r++, "⚡ Błyskawica (<24h)", "Wszyscy followerzy zareagowali w ciągu doby — gorący produkt, kilku botów go pilnuje.", styles);
            WriteLegendPair(sheet, r++, "🔥 Szybka (1-3 dni)", "Kaskada w ciągu 1-3 dni — bardzo aktywna kategoria.", styles);
            WriteLegendPair(sheet, r++, "🐢 Umiarkowana (3-7 dni)", "Rynek się powoli dostosowuje — cykliczny monitoring konkurentów.", styles);
            WriteLegendPair(sheet, r++, "🐌 Powolna (>7 dni)", "Wojna rozwija się tygodniami — być może nie kaskada, ale niezależne ruchy.", styles);

            r++;

            WriteLegendHeader(sheet, r++, "Kolory komórek cenowych (zakładki vs Oferta / vs Zakup)", styles);
            WriteLegendColor(sheet, r++, styles.BaselineCurrency, "Nasza cena ref.", "Nasza cena (oferty lub zakupu) — do niej porównywani są wszyscy konkurenci.", styles);
            WriteLegendColor(sheet, r++, styles.ComplianceCurrency, "Zgodność — nie taniej od nas", "Cena konkurenta równa/wyższa od naszej. Wszystko OK.", styles);
            WriteLegendColor(sheet, r++, styles.NoDataCell, "Brak danych", "Konkurent nie miał oferty w tym scrapie.", styles);
            WriteLegendColor(sheet, r++, styles.CellRed1, "Wyłom 0-2%", "Delikatnie taniej — w ramach szumu cenowego.", styles);
            WriteLegendColor(sheet, r++, styles.CellRed3, "Wyłom 5-8%", "Wyraźna agresja.", styles);
            WriteLegendColor(sheet, r++, styles.CellRed6, "Wyłom 16-20%", "Silny undercut — mocno pod naszą ceną.", styles);
            WriteLegendColor(sheet, r++, styles.CellRed9, "Wyłom 30-40%", "Bardzo agresywnie — wyprzedaż lub błąd cenowy.", styles);
            WriteLegendColor(sheet, r++, styles.CellRed11, "Wyłom 50%+", "Drastyczny — zazwyczaj błąd w feedzie lub totalna wyprzedaż.", styles);
            WriteLegendColor(sheet, r++, styles.FirstBreakerViolation, "🥇 Komórka złota", "Pierwszy wyłom tego konkurenta dla tego produktu (w dacie pierwszego zejścia poniżej nas).", styles);

            r++;

            WriteLegendHeader(sheet, r++, "Układ danych — jak czytać bloki produktów", styles);
            WriteLegendColor(sheet, r++, styles.ProductBlockHeader, "📦 Nagłówek produktu", "Gruby pasek nad wierszami konkurentów. Zawiera nazwę, EAN, SKU, marka, naszą cenę ref., inicjatora i liczbę łamaczy.", styles);
            WriteLegendColor(sheet, r++, styles.BandDefault, "Banding produktów", "Co drugi produkt ma lekko szare tło — żeby wizualnie oddzielić bloki. Biały produkt → szary produkt → biały produkt…", styles);

            r++;

            WriteLegendHeader(sheet, r++, "Mapa agresji — jak czytać", styles);
            WriteLegendPair(sheet, r++, "Kolumny per marka", "Każda marka ma 3 kolumny: 'Śr.%' = średni wyłom w % (po wszystkich produktach tej marki u tego konkurenta), 'Max%' = najgorszy wyłom, 'Szt.' = ile produktów złamanych.", styles);
            WriteLegendPair(sheet, r++, "Intensywność koloru", "Skalowana do najgorszej średniej agresji w całym pliku. Najczerwieńsza komórka = największy przeciwnik.", styles);
            WriteLegendPair(sheet, r++, "Sortowanie konkurentów", "Automatyczne od najbardziej agresywnego (najwyższa średnia) do najmniej. Pierwszy wiersz to Twój główny przeciwnik cenowy.", styles);
            WriteLegendPair(sheet, r++, "Szt. ≥ 5 (czerwone tło)", "Konkurent złamał cenę na 5+ produktach tej marki — aktywna agresja na kategorię.", styles);

            r++;

            WriteLegendHeader(sheet, r++, "Jak tego używać w praktyce", styles);
            WriteLegendPair(sheet, r++, "Zidentyfikuj agresora", "Zakładka 'Macierz L→F', kolumna '∑ jako inicjator'. Konkurent z najwyższą wartością to osoba do obserwacji.", styles);
            WriteLegendPair(sheet, r++, "Zidentyfikuj kopistów", "Wiersz '∑ jako follower' na dole macierzy. Wysokie liczby = reaktywni konkurenci.", styles);
            WriteLegendPair(sheet, r++, "Zidentyfikuj pary bot-lider", "W macierzy szukaj komórek z ciemną czerwienią. Np. 'X→Y = 25' = Y ma bota śledzi X.", styles);
            WriteLegendPair(sheet, r++, "Spalone produkty", "'Liderzy', kolumna 'Łamaczy łącznie'. Produkty ≥ 5 łamaczy — rozważ wycofanie z porównywarki.", styles);
            WriteLegendPair(sheet, r++, "Kategoria pod ogniem", "'Agresja' — całe kolumny (marki) w czerwieni → cała kategoria atakowana. Negocjuj MAP z dostawcą.", styles);
            WriteLegendPair(sheet, r++, "Porównaj Oferta vs Zakup", "Jeśli produkt ma wyłomy vs Oferta ale nie vs Zakup → konkurent jest tańszy od nas, ale powyżej MAP. " +
                "Jeśli vs Zakup też ma wyłomy → ktoś sprzedaje poniżej kosztu, narusza MAP — materiał do rozmowy z dostawcą.", styles);

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
            row.HeightInPoints = 32;
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
        // GRADIENT NARUSZEŃ — wybór stylu per %  [BEZ ZMIAN]
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
        // MODELE (PriceChange)  [BEZ ZMIAN]
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
        // MODELE POMOCNICZE — Comparison  [BEZ ZMIAN]
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
        // MODELE POMOCNICZE — Allegro  [BEZ ZMIAN]
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
        // ŁADOWANIE DANYCH — Comparison  [BEZ ZMIAN]
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
        // ŁADOWANIE DANYCH — Allegro  [BEZ ZMIAN]
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
        // BUDOWANIE WIERSZY — Comparison  [BEZ ZMIAN]
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
        // BUDOWANIE WIERSZY — Allegro  [BEZ ZMIAN]
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
        // ZAPIS ARKUSZA — Comparison (z flagami)  [BEZ ZMIAN]
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

            sheet.CreateFreezePane(6, 1);
            if (rowIdx > 1)
            {
                sheet.SetAutoFilter(new CellRangeAddress(0, rowIdx - 1, 0, 17));
            }

            for (int i = 0; i < 18; i++) { try { sheet.AutoSizeColumn(i); } catch { } }
        }

        // ====================================================================
        // ZAPIS ARKUSZA — Allegro (z flagami)  [BEZ ZMIAN]
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
        // BUDOWANIE DANYCH KONKURENCJI  [BEZ ZMIAN]
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
        // ARKUSZE KONKURENCJI  [BEZ ZMIAN]
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
        // STYLE EXCELA  [ZMIENIONE: usunięte Cls*, dodane Band*/Delay*/Follower/Diagonal]
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

            // Price change — zachowane z oryginału
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

            // [NOWE] Banding co drugi produkt — lekko szare tło
            public ICellStyle BandDefault { get; set; }
            public ICellStyle BandCurrency { get; set; }
            public ICellStyle BandPercent { get; set; }
            public ICellStyle BandNoData { get; set; }
            public ICellStyle BandBaselineCurrency { get; set; }
            public ICellStyle BandComplianceCurrency { get; set; }

            // [NOWE] Marker followera (turkusowy)
            public ICellStyle FollowerMarker { get; set; }

            // [NOWE] Skala szybkości reakcji (delay)
            public ICellStyle DelayInstant { get; set; }   // < 1h    — alarm czerwony
            public ICellStyle DelayFast { get; set; }      // 1-12h   — pomarańcz
            public ICellStyle DelayMedium { get; set; }    // 12-48h  — żółty
            public ICellStyle DelaySlow { get; set; }      // > 48h   — szary

            // [NOWE] Diagonal w macierzy (gdy wiersz = kolumna)
            public ICellStyle DiagonalCell { get; set; }
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

            // Price change — zachowane
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

            // [NOWE] Banding — co drugi produkt lekko szary
            s.BandDefault = CreateColoredStyle(wb, new byte[] { 247, 247, 250 }, false, 0);
            s.BandCurrency = CreateColoredStyle(wb, new byte[] { 247, 247, 250 }, false, 0, s.Currency);
            s.BandPercent = CreateColoredStyle(wb, new byte[] { 247, 247, 250 }, false, 0, s.Percent);
            s.BandNoData = CreateColoredStyle(wb, new byte[] { 247, 247, 250 }, false, 0);
            s.BandBaselineCurrency = CreateColoredStyle(wb, new byte[] { 227, 239, 252 }, true, 0, s.Currency);
            s.BandComplianceCurrency = CreateColoredStyle(wb, new byte[] { 232, 245, 233 }, false, 0, s.Currency);

            // [NOWE] Follower marker
            s.FollowerMarker = CreateColoredStyle(wb, new byte[] { 207, 229, 245 }, true, 0);

            // [NOWE] Delay scale — od alertu do spokojnego szarego
            s.DelayInstant = CreateColoredStyle(wb, new byte[] { 255, 138, 128 }, true, 0);   // alarm czerwony
            s.DelayFast = CreateColoredStyle(wb, new byte[] { 255, 183, 77 }, true, 0);       // pomarańcz
            s.DelayMedium = CreateColoredStyle(wb, new byte[] { 255, 224, 130 }, false, 0);   // żółty
            s.DelaySlow = CreateColoredStyle(wb, new byte[] { 207, 216, 220 }, false, 0);     // szary

            // [NOWE] Diagonal w macierzy
            s.DiagonalCell = CreateColoredStyle(wb, new byte[] { 224, 224, 224 }, false, 0);

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
        // POMOCNICZE  [BEZ ZMIAN]
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