using AngleSharp.Dom;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NPOI.SS.Formula.Functions;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using PriceSafari.ViewModels;
using Schema.NET;
using System.Security.Claims;


namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    public class PriceHistoryController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PriceHistoryController> _logger;
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly IHubContext<ScrapingHub> _hubContext;

        public PriceHistoryController(
            PriceSafariContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<PriceHistoryController> logger,
            UserManager<PriceSafariUser> userManager,
            IHubContext<ScrapingHub> hubContext)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _userManager = userManager;
            _hubContext = hubContext;
        }

        private async Task<bool> UserHasAccessToStore(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            var isAdminOrManager = await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "Manager");

            if (!isAdminOrManager)
            {
                var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
                return hasAccess;
            }

            return true;
        }

        public async Task<IActionResult> Index(int? storeId)
        {
            if (storeId == null)
            {
                return NotFound("Store ID not provided.");
            }

            if (!await UserHasAccessToStore(storeId.Value))
            {
                return Content("Nie ma takiego sklepu");
            }

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return View(new List<FlagsClass>());
            }

            var storeName = await _context.Stores
                .Where(sn => sn.StoreId == storeId)
                .Select(sn => sn.StoreName)
                .FirstOrDefaultAsync();

            var storeLogo = await _context.Stores
                .Where(sn => sn.StoreId == storeId)
                .Select(sn => sn.StoreLogoUrl)
                .FirstOrDefaultAsync();

            var scrapedproducts = await _context.Products
                .Where(p => p.StoreId == storeId && p.IsScrapable)
                .CountAsync();

            var flags = await _context.Flags
               .Where(f => f.StoreId == storeId.Value && f.IsMarketplace == false)
               .Select(f => new FlagViewModel
               {
                   FlagId = f.FlagId,
                   FlagName = f.FlagName,
                   FlagColor = f.FlagColor,
                   IsMarketplace = f.IsMarketplace
               })
               .ToListAsync();

            ViewBag.LatestScrap = latestScrap;
            ViewBag.StoreId = storeId;
            ViewBag.StoreName = storeName;
            ViewBag.StoreLogo = storeLogo;
            ViewBag.ScrapId = latestScrap.Id;
            ViewBag.Flags = flags;
            ViewBag.ScrapedProducts = scrapedproducts;

            return View("~/Views/Panel/PriceHistory/Index.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetPrices(int? storeId)
        {
            if (storeId == null)
            {
                return Json(new
                {
                    productCount = 0,
                    priceCount = 0,
                    myStoreName = "",
                    prices = new List<object>(),
                    missedProductsCount = 0,
                    setPrice1 = 2.00m,
                    setPrice2 = 2.00m
                });
            }

            if (!await UserHasAccessToStore(storeId.Value))
            {
                return Json(new { error = "Nie ma takiego sklepu" });
            }

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return Json(new
                {
                    productCount = 0,
                    priceCount = 0,
                    myStoreName = "",
                    prices = new List<object>(),
                    missedProductsCount = 0,
                    setPrice1 = 2.00m,
                    setPrice2 = 2.00m
                });
            }


            var bridgeItems = await _context.PriceBridgeItems
        .Include(i => i.Batch)
        .Where(i => i.Batch.StoreId == storeId.Value &&
                    i.Batch.ScrapHistoryId == latestScrap.Id)
        .ToListAsync();

            // Grupujemy po ProductId, aby wziąć tylko NAJNOWSZĄ zmianę dla danego produktu 
            // (w przypadku gdy użytkownik generował kilka plików eksportu po kolei)
            var committedLookup = bridgeItems
                .GroupBy(i => i.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(i => i.Batch.ExecutionDate).First()
                );

            var previousScrapId = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId && sh.Date < latestScrap.Date)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == storeId)
                .Select(pv => new
                {
                    pv.SetPrice1,
                    pv.SetPrice2,
                    pv.PriceStep,
                    pv.UsePriceDiff,
                    pv.IdentifierForSimulation,
                    pv.UseMarginForSimulation,
                    pv.EnforceMinimalMargin,
                    pv.MinimalMarginPercent,
                    pv.UsePriceWithDelivery
                })
                .FirstOrDefaultAsync() ?? new
                {
                    SetPrice1 = 2.00m,
                    SetPrice2 = 2.00m,
                    PriceStep = 2.00m,
                    UsePriceDiff = true,
                    IdentifierForSimulation = "EAN",
                    UseMarginForSimulation = true,
                    EnforceMinimalMargin = true,
                    MinimalMarginPercent = 0.00m,
                    UsePriceWithDelivery = false
                };

            var baseQuery = from p in _context.Products
                            where p.StoreId == storeId && p.IsScrapable
                            join ph in _context.PriceHistories
                                .Where(ph => ph.ScrapHistoryId == latestScrap.Id)
                                on p.ProductId equals ph.ProductId into phGroup
                            from ph in phGroup.DefaultIfEmpty()
                            select new PriceRowDto
                            {
                                ProductId = p.ProductId,
                                ProductName = p.ProductName,
                                Producer = p.Producer,
                                Price = (ph != null ? ph.Price : (decimal?)null),
                                StoreName = (ph != null ? ph.StoreName : null),
                                ScrapHistoryId = (ph != null ? ph.ScrapHistoryId : (int?)null),
                                Position = (ph != null ? ph.Position : (int?)null),
                                IsBidding = ph != null ? ph.IsBidding : null,
                                IsGoogle = (ph != null ? ph.IsGoogle : (bool?)null),

                                CeneoInStock = (ph != null ? ph.CeneoInStock : (bool?)null),
                                GoogleInStock = (ph != null ? ph.GoogleInStock : (bool?)null),
                                IsRejected = p.IsRejected,
                                ShippingCostNum = (ph != null ? ph.ShippingCostNum : (decimal?)null)
                            };

            var activePreset = await _context.CompetitorPresets
                .Include(x => x.CompetitorItems)
                .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse && cp.Type == PresetType.PriceComparison);

            string activePresetName = null;

            if (activePreset != null)
            {
                activePresetName = activePreset.PresetName;
                if (!activePreset.SourceGoogle) baseQuery = baseQuery.Where(p => p.IsGoogle != true);
                if (!activePreset.SourceCeneo) baseQuery = baseQuery.Where(p => p.IsGoogle == true);
            }

            var rawPrices = await baseQuery.ToListAsync();

            if (priceValues.UsePriceWithDelivery)
            {
                foreach (var row in rawPrices)
                {
                    if (row.Price.HasValue && row.ShippingCostNum.HasValue)
                    {
                        row.Price = row.Price.Value + row.ShippingCostNum.Value;
                    }
                }
            }

            var productIds = rawPrices.Select(p => p.ProductId).Distinct().ToList();

            var extendedInfoData = await _context.PriceHistoryExtendedInfos
                .Where(e => e.ScrapHistoryId == latestScrap.Id && productIds.Contains(e.ProductId))
                .ToListAsync();
            var extendedInfoDict = extendedInfoData.ToDictionary(e => e.ProductId);

            var previousExtendedInfoData = new Dictionary<int, PriceHistoryExtendedInfoClass>();
            if (previousScrapId > 0)
            {
                previousExtendedInfoData = await _context.PriceHistoryExtendedInfos
                    .Where(e => e.ScrapHistoryId == previousScrapId && productIds.Contains(e.ProductId))
                    .ToDictionaryAsync(e => e.ProductId);
            }

            var productFlagsDictionary = await _context.ProductFlags
               .Where(pf => pf.ProductId.HasValue && productIds.Contains(pf.ProductId.Value))
               .GroupBy(pf => pf.ProductId.Value)
               .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            var productsWithExternalInfo = await _context.Products
               .Where(p => p.StoreId == storeId && productIds.Contains(p.ProductId))
               .Select(p => new {
                   p.ProductId,
                   p.ExternalId,
                   p.MainUrl,
                   p.MarginPrice,
                   p.Ean,
                   p.ProducerCode
               })
               .ToListAsync();
            var productExternalInfoDictionary = productsWithExternalInfo.ToDictionary(
                p => p.ProductId,
                p => new { p.ExternalId, p.MainUrl, p.MarginPrice, p.Ean, p.ProducerCode }
            );

            Dictionary<(string Store, DataSourceType Source), bool> competitorItemsDict = null;
            var storeNameLower = storeName?.ToLower().Trim() ?? "";

            if (activePreset != null && activePreset.Type == PresetType.PriceComparison)
            {
                competitorItemsDict = activePreset.CompetitorItems.ToDictionary(
                    ci => (Store: ci.StoreName.ToLower().Trim(), Source: ci.DataSource),
                    ci => ci.UseCompetitor
                );
            }
            var allPrices = rawPrices
                .GroupBy(p => p.ProductId)
                .Select(g =>
                {
                    var productGroup = g.ToList();
                    var product = productGroup.First();
                    var validPrices = productGroup.Where(x => x.Price.HasValue).ToList();

                    var myPriceEntries = validPrices.Where(x => x.StoreName != null && x.StoreName.ToLower() == storeNameLower).ToList();
                    var myPriceEntry = myPriceEntries.OrderByDescending(x => x.IsGoogle == false).FirstOrDefault();
                    var myPrice = myPriceEntry?.Price;

                    var allCompetitorEntries = validPrices.Where(x => x.StoreName != null && x.StoreName.ToLower() != storeNameLower).ToList();
                    var presetFilteredCompetitorPrices = new List<PriceRowDto>();
                    var committedItem = committedLookup.GetValueOrDefault(g.Key);

                    if (competitorItemsDict != null)
                    {
                        foreach (var row in allCompetitorEntries)
                        {
                            DataSourceType currentSource = row.IsGoogle == true ? DataSourceType.Google : DataSourceType.Ceneo;
                            var key = (Store: (row.StoreName ?? "").ToLower().Trim(), Source: currentSource);

                            if (competitorItemsDict.TryGetValue(key, out bool useCompetitor))
                            {
                                if (useCompetitor) presetFilteredCompetitorPrices.Add(row);
                            }
                            else if (activePreset.UseUnmarkedStores)
                            {
                                presetFilteredCompetitorPrices.Add(row);
                            }
                        }
                    }
                    else
                    {
                        presetFilteredCompetitorPrices = allCompetitorEntries;
                    }

                    var presetFilteredValidPrices = new List<PriceRowDto>(presetFilteredCompetitorPrices);
                    if (myPriceEntry != null)
                    {
                        presetFilteredValidPrices.Add(myPriceEntry);
                    }
                    bool onlyMe = !presetFilteredCompetitorPrices.Any() && myPriceEntry != null;

                    var bestCompetitorPriceEntry = presetFilteredCompetitorPrices
                        .OrderBy(x => x.Price)
                        .ThenBy(x => x.StoreName)
                        .ThenByDescending(x => x.IsGoogle == false)
                        .FirstOrDefault();
                    var bestCompetitorPrice = bestCompetitorPriceEntry?.Price;

                    PriceRowDto finalBestPriceEntry = bestCompetitorPriceEntry;
                    decimal? finalBestPrice = bestCompetitorPrice;

                    bool iAmEffectivelyTheBest = false;
                    if (myPrice.HasValue)
                    {
                        if (!bestCompetitorPrice.HasValue)
                        {
                            iAmEffectivelyTheBest = true;
                        }

                        else if (myPrice.Value <= bestCompetitorPrice.Value)
                        {
                            iAmEffectivelyTheBest = true;
                        }
                    }

                    string myPricePositionString = "N/A / 0";
                    var totalValidOffers = presetFilteredValidPrices.Count();
                    if (myPrice.HasValue && totalValidOffers > 0)
                    {
                        var myStorePriceValue = myPrice.Value;
                        int pricesLower = presetFilteredValidPrices.Count(vp => vp.Price.HasValue && vp.Price.Value < myStorePriceValue);
                        int pricesEqual = presetFilteredValidPrices.Count(vp => vp.Price.HasValue && vp.Price.Value == myStorePriceValue);
                        int rankStart = pricesLower + 1; int rankEnd = pricesLower + pricesEqual;
                        myPricePositionString = (rankStart == rankEnd) ? $"{rankStart}/{totalValidOffers}" : $"{rankStart}-{rankEnd}/{totalValidOffers}";
                    }
                    else if (totalValidOffers > 0)
                    {
                        myPricePositionString = $"N/A / {totalValidOffers}";
                    }

                    decimal? priceDifference = null;
                    decimal? percentageDifference = null;
                    decimal? savings = null;
                    bool isUniqueBestPrice = false;
                    bool isRejectedDueToZeroPrice = false;
                    int? myPosition = myPriceEntry?.Position;

                    if (product.IsRejected || (myPrice.HasValue && myPrice.Value == 0) || (bestCompetitorPrice.HasValue && bestCompetitorPrice.Value == 0))
                    {
                        isRejectedDueToZeroPrice = true;
                    }

                    if (myPrice.HasValue && bestCompetitorPrice.HasValue && bestCompetitorPrice.Value <= myPrice.Value)
                    {
                        priceDifference = Math.Round(myPrice.Value - bestCompetitorPrice.Value, 2);
                        if (bestCompetitorPrice.Value > 0)
                        {
                            percentageDifference = Math.Round((myPrice.Value - bestCompetitorPrice.Value) / bestCompetitorPrice.Value * 100, 2);
                        }
                    }

                    if (iAmEffectivelyTheBest && myPrice.HasValue && !isRejectedDueToZeroPrice)
                    {

                        bool amIUniquelyTheBest = myPrice.HasValue && (!bestCompetitorPrice.HasValue || myPrice.Value < bestCompetitorPrice.Value);

                        if (amIUniquelyTheBest)
                        {
                            isUniqueBestPrice = true;

                            if (bestCompetitorPrice.HasValue)
                            {

                                savings = Math.Round(bestCompetitorPrice.Value - myPrice.Value, 2);
                            }

                        }

                    }

                    bool? bestEntryStockStatus = null;
                    if (finalBestPriceEntry != null)
                    {
                        bestEntryStockStatus = finalBestPriceEntry.IsGoogle == true ? finalBestPriceEntry.GoogleInStock : finalBestPriceEntry.CeneoInStock;
                    }
                    bool? myEntryStockStatus = null;
                    if (myPriceEntry != null)
                    {
                        myEntryStockStatus = myPriceEntry.IsGoogle == true ? myPriceEntry.GoogleInStock : myPriceEntry.CeneoInStock;
                    }

                    var storeCount = presetFilteredValidPrices
                        .Where(s => s != null && s.StoreName != null)
                        .Select(x => new { StoreName = x.StoreName.ToLower().Trim(), Source = x.IsGoogle ?? false })
                        .Distinct().Count();

                    bool sourceGoogle = productGroup.Any(x => x.IsGoogle == true);
                    bool sourceCeneo = productGroup.Any(x => x.IsGoogle == false);

                    bool? bestPriceIncludesDeliveryFlag = null; bool? myPriceIncludesDeliveryFlag = null;
                    extendedInfoDict.TryGetValue(g.Key, out var extendedInfo);
                    if (priceValues.UsePriceWithDelivery)
                    {
                        bestPriceIncludesDeliveryFlag = finalBestPriceEntry?.ShippingCostNum.HasValue;
                        myPriceIncludesDeliveryFlag = myPriceEntry?.ShippingCostNum.HasValue;
                    }

                    int externalBestPriceCount = 0;
                    if (bestCompetitorPrice.HasValue)
                    {

                        if (myPrice.HasValue && myPrice.Value < bestCompetitorPrice.Value)
                        {

                            externalBestPriceCount = 0;
                        }
                        else if (myPrice.HasValue && myPrice.Value == bestCompetitorPrice.Value)
                        {

                            externalBestPriceCount = presetFilteredValidPrices.Count(x => x.Price.HasValue && x.Price.Value == bestCompetitorPrice.Value);
                        }
                        else
                        {

                            externalBestPriceCount = presetFilteredCompetitorPrices.Count(x => x.Price.HasValue && x.Price.Value == bestCompetitorPrice.Value);
                        }
                    }

                    productFlagsDictionary.TryGetValue(g.Key, out var flagIds); flagIds ??= new List<int>();
                    productExternalInfoDictionary.TryGetValue(g.Key, out var extInfo);

                    SalesTrendStatus salesTrendStatus = SalesTrendStatus.NoData;
                    int? salesDifference = null; decimal? salesPercentageChange = null;
                    previousExtendedInfoData.TryGetValue(g.Key, out var previousExtendedInfo);
                    if (extendedInfo?.CeneoSalesCount != null && previousExtendedInfo?.CeneoSalesCount != null)
                    {
                        int currentSales = extendedInfo.CeneoSalesCount.Value; int previousSales = previousExtendedInfo.CeneoSalesCount.Value;
                        salesDifference = currentSales - previousSales;
                        if (previousSales > 0) salesPercentageChange = Math.Round(((decimal)salesDifference.Value / previousSales) * 100, 2);
                        else if (salesDifference > 0) salesPercentageChange = 100m;

                        const decimal smallChangeThreshold = 10.0m;
                        if (salesDifference == 0) salesTrendStatus = SalesTrendStatus.NoChange;
                        else if (salesDifference > 0) salesTrendStatus = (salesPercentageChange.HasValue && Math.Abs(salesPercentageChange.Value) > smallChangeThreshold) ? SalesTrendStatus.SalesUpBig : SalesTrendStatus.SalesUpSmall;
                        else salesTrendStatus = (salesPercentageChange.HasValue && Math.Abs(salesPercentageChange.Value) > smallChangeThreshold) ? SalesTrendStatus.SalesDownBig : SalesTrendStatus.SalesDownSmall;
                    }

                    decimal? singleBestCheaperDiff = null;
                    decimal? singleBestCheaperDiffPerc = null;

                    var validPresetPrices = presetFilteredValidPrices.Where(x => x.Price.HasValue && x.Price.Value > 0).ToList();

                    if (validPresetPrices.Any())
                    {

                        decimal absoluteLowestPrice = validPresetPrices.Select(x => x.Price.Value).Min();

                        var lowestPriceEntries = validPresetPrices.Where(x => x.Price.Value == absoluteLowestPrice).ToList();

                        int absoluteLowestPriceCount = lowestPriceEntries.Count;

                        if (absoluteLowestPriceCount == 1)
                        {
                            var singleCheapestEntry = lowestPriceEntries.First();

                            bool isMyStoreSoleCheapest = (singleCheapestEntry.StoreName != null &&
                             singleCheapestEntry.StoreName.ToLower().Trim() == storeNameLower);

                            if (!isMyStoreSoleCheapest)
                            {

                                var secondLowestPrice = validPresetPrices
                                  .Where(x => x.Price.Value > absoluteLowestPrice)
                                  .Select(x => x.Price.Value)
                                  .OrderBy(x => x)
                                  .FirstOrDefault();

                                decimal? actualSecondLowest = (secondLowestPrice == 0) ? null : (decimal?)secondLowestPrice;

                                if (actualSecondLowest.HasValue)
                                {
                                    singleBestCheaperDiff = Math.Round(actualSecondLowest.Value - absoluteLowestPrice, 2);

                                    var diffPercent = ((actualSecondLowest.Value - absoluteLowestPrice) / actualSecondLowest.Value) * 100;
                                    singleBestCheaperDiffPerc = Math.Round(diffPercent, 2);
                                }
                            }
                        }

                    }

                    return new
                    {
                        ProductId = product.ProductId,
                        ProductName = product.ProductName,
                        Producer = product.Producer,
                        LowestPrice = finalBestPrice,
                        StoreName = finalBestPriceEntry?.StoreName,
                        MyPrice = myPrice,
                        ScrapId = latestScrap.Id,
                        PriceDifference = priceDifference,
                        PercentageDifference = percentageDifference,
                        Savings = savings,
                        IsSharedBestPrice = (iAmEffectivelyTheBest && !isUniqueBestPrice && myPrice.HasValue),
                        IsUniqueBestPrice = isUniqueBestPrice,
                        OnlyMe = onlyMe,
                        ExternalBestPriceCount = externalBestPriceCount,
                        IsBidding = finalBestPriceEntry?.IsBidding,
                        IsGoogle = finalBestPriceEntry?.IsGoogle,
                        Position = finalBestPriceEntry?.Position,
                        MyIsBidding = myPriceEntry?.IsBidding,
                        MyIsGoogle = myPriceEntry?.IsGoogle,
                        MyPosition = myPosition,   
                        FlagIds = flagIds,
                        BestEntryInStock = bestEntryStockStatus,
                        MyEntryInStock = myEntryStockStatus,
                        ExternalId = extInfo?.ExternalId,
                        MarginPrice = extInfo?.MarginPrice,
                        ImgUrl = extInfo?.MainUrl,
                        Ean = extInfo?.Ean,
                        ProducerCode = extInfo?.ProducerCode,
                        IsRejected = product.IsRejected || isRejectedDueToZeroPrice,
                        StoreCount = storeCount,
                        SourceGoogle = sourceGoogle,
                        SourceCeneo = sourceCeneo,
                        SingleBestCheaperDiff = singleBestCheaperDiff,
                        SingleBestCheaperDiffPerc = singleBestCheaperDiffPerc,
                        BestPriceIncludesDelivery = bestPriceIncludesDeliveryFlag,
                        MyPriceIncludesDelivery = myPriceIncludesDeliveryFlag,
                        BestPriceDeliveryCost = priceValues.UsePriceWithDelivery ? finalBestPriceEntry?.ShippingCostNum : null,
                        MyPriceDeliveryCost = priceValues.UsePriceWithDelivery ? myPriceEntry?.ShippingCostNum : null,
                        CeneoSalesCount = extendedInfo?.CeneoSalesCount,
                        SalesTrendStatus = salesTrendStatus.ToString(),
                        SalesDifference = salesDifference,
                        SalesPercentageChange = salesPercentageChange,
                        ExternalApiPrice = extendedInfo?.ExtendedDataApiPrice,
                        MyPricePosition = myPricePositionString,
                        Committed = committedItem == null ? null : new
                        {
                            NewPrice = committedItem.PriceAfter,
                            NewGoogleRanking = committedItem.RankingGoogleAfterSimulated,
                            NewCeneoRanking = committedItem.RankingCeneoAfterSimulated
                        }
                    };
                })
                .Where(p => p != null)
                .ToList();

            var missedProductsCount = allPrices.Count(p => p.IsRejected);

            return Json(new
            {
                productCount = allPrices.Count,
                priceCount = rawPrices.Count,
                myStoreName = storeName,
                prices = allPrices,
                missedProductsCount = missedProductsCount,
                setPrice1 = priceValues.SetPrice1,
                setPrice2 = priceValues.SetPrice2,
                stepPrice = priceValues.PriceStep,
                usePriceDiff = priceValues.UsePriceDiff,
                useMarginForSimulation = priceValues.UseMarginForSimulation,
                enforceMinimalMargin = priceValues.EnforceMinimalMargin,
                minimalMarginPercent = priceValues.MinimalMarginPercent,
                identifierForSimulation = priceValues.IdentifierForSimulation,
                usePriceWithDelivery = priceValues.UsePriceWithDelivery,
                presetName = activePresetName ?? "PriceSafari",
                latestScrapId = latestScrap?.Id
            });
        }

        public class PriceRowDto
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string? Producer { get; set; }
            public decimal? Price { get; set; }
            public string? StoreName { get; set; }
            public int? ScrapHistoryId { get; set; }
            public int? Position { get; set; }
            public string? IsBidding { get; set; }
            public bool? IsGoogle { get; set; }
            public bool? CeneoInStock { get; set; }
            public bool? GoogleInStock { get; set; }
            public bool IsRejected { get; set; }
            public decimal? ShippingCostNum { get; set; }
        }

        public enum SalesTrendStatus
        {
            NoData,
            NoChange,
            SalesUpSmall,
            SalesUpBig,
            SalesDownSmall,
            SalesDownBig
        }

        [HttpPost]
        public async Task<IActionResult> SavePriceValues([FromBody] PriceValuesViewModel model)
        {
            if (model == null || model.StoreId <= 0)
            {
                return BadRequest("Invalid store ID or price values.");
            }

            if (!await UserHasAccessToStore(model.StoreId))
            {
                return BadRequest("Nie ma takiego sklepu");
            }

            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == model.StoreId)
                .FirstOrDefaultAsync();

            if (priceValues == null)
            {
                priceValues = new PriceValueClass
                {
                    StoreId = model.StoreId,
                    SetPrice1 = model.SetPrice1,
                    SetPrice2 = model.SetPrice2,
                    PriceStep = model.PriceStep,
                    UsePriceDiff = model.usePriceDiff
                };
                _context.PriceValues.Add(priceValues);
            }
            else
            {
                priceValues.SetPrice1 = model.SetPrice1;
                priceValues.SetPrice2 = model.SetPrice2;
                priceValues.PriceStep = model.PriceStep;
                priceValues.UsePriceDiff = model.usePriceDiff;
                _context.PriceValues.Update(priceValues);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Price values updated successfully." });
        }

        public async Task<IActionResult> Details(int scrapId, int productId)
        {
            var scrapHistory = await _context.ScrapHistories.FindAsync(scrapId);
            if (scrapHistory == null)
            {
                return NotFound("Nie znaleziono historii scrapowania.");
            }

            var storeId = scrapHistory.StoreId;

            if (!await UserHasAccessToStore(storeId))
            {
                return Content("Brak dostępu do sklepu lub sklep nie istnieje.");
            }

            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(storeName))
            {
                return Content("Nie można zidentyfikować nazwy sklepu.");
            }

            var activePreset = await _context.CompetitorPresets
               .Include(x => x.CompetitorItems)
               .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse && cp.Type == PresetType.PriceComparison);

            string activePresetName = null;

            IQueryable<PriceHistoryClass> query = _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == scrapId && ph.ProductId == productId);

            if (activePreset != null)
            {
                activePresetName = activePreset.PresetName;

                if (!activePreset.SourceGoogle)
                {
                    query = query.Where(ph => ph.IsGoogle != true);
                }
                if (!activePreset.SourceCeneo)
                {
                    query = query.Where(ph => ph.IsGoogle == true);
                }
            }

            var rawPrices = await query.Include(ph => ph.Product).ToListAsync();

            List<PriceHistoryClass> filteredPrices;

            if (activePreset != null && activePreset.Type == PresetType.PriceComparison)
            {
                var competitorItemsDict = activePreset.CompetitorItems
                    .ToDictionary(
                        ci => (Store: ci.StoreName.ToLower().Trim(), Source: ci.DataSource),
                        ci => ci.UseCompetitor
                    );

                var storeNameLower = storeName.ToLower().Trim();
                filteredPrices = new List<PriceHistoryClass>();

                foreach (var priceEntry in rawPrices)
                {
                    if (priceEntry.StoreName != null && priceEntry.StoreName.ToLower().Trim() == storeNameLower)
                    {
                        filteredPrices.Add(priceEntry);
                        continue;
                    }

                    DataSourceType currentSource = priceEntry.IsGoogle == true ? DataSourceType.Google : DataSourceType.Ceneo;

                    var key = (Store: (priceEntry.StoreName ?? "").ToLower().Trim(), Source: currentSource);

                    if (competitorItemsDict.TryGetValue(key, out bool useCompetitor))
                    {
                        if (useCompetitor)
                        {
                            filteredPrices.Add(priceEntry);
                        }
                    }
                    else
                    {
                        if (activePreset.UseUnmarkedStores)
                        {
                            filteredPrices.Add(priceEntry);
                        }
                    }
                }
            }
            else
            {
                filteredPrices = rawPrices;
            }

            var prices = filteredPrices
                            .OrderBy(p => p.Price)
                            .ThenBy(p => p.StoreName)
                            .ThenByDescending(p => p.IsGoogle == false)
                            .ToList();

            var product = prices.FirstOrDefault()?.Product;

            if (product == null && prices.Any())
            {
                product = await _context.Products.FindAsync(productId);
            }
            else if (!prices.Any())
            {
                product = await _context.Products.FindAsync(productId);
                if (product == null)
                {
                    return NotFound("Nie znaleziono produktu.");
                }
            }

            string? newGoogleUrl = null;
            if (!string.IsNullOrEmpty(product.GoogleUrl) && !string.IsNullOrEmpty(product.ProductName))
            {

                string? productIdCid = ExtractProductIdFromUrl(product.GoogleUrl);

                if (!string.IsNullOrEmpty(productIdCid))
                {

                    string productNameForUrl = System.Net.WebUtility.UrlEncode(product.ProductName);

                    newGoogleUrl = $"https://www.google.com/search?q={productNameForUrl}&udm=28#oshopproduct=cid:{productIdCid},pvt:hg,pvo:3&oshop=apv";
                }
            }

            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == storeId)
                .Select(pv => new { pv.SetPrice1, pv.SetPrice2 })
                .FirstOrDefaultAsync() ?? new { SetPrice1 = 2.00m, SetPrice2 = 2.00m };

            var pricesDataJson = JsonConvert.SerializeObject(
                prices.Select(p => new
                {
                    store = p.StoreName,
                    price = p.Price,
                    isBidding = p.IsBidding,
                    isGoogle = p.IsGoogle,
                    ceneoInStock = p.CeneoInStock,
                    googleInStock = p.GoogleInStock,
                    offerCount = p.GoogleOfferPerStoreCount
                })
            );

            ViewBag.ScrapHistory = scrapHistory;
            ViewBag.ProductName = product.ProductName;
            ViewBag.Url = product.OfferUrl;
            ViewBag.StoreId = storeId;
            ViewBag.GoogleUrl = newGoogleUrl ?? product.GoogleUrl;
            ViewBag.StoreName = storeName;
            ViewBag.SetPrice1 = priceValues.SetPrice1;
            ViewBag.SetPrice2 = priceValues.SetPrice2;
            ViewBag.ProductId = productId;
            ViewBag.ExternalId = product.ExternalId;
            ViewBag.Img = product.MainUrl;
            ViewBag.Ean = product.Ean;
            ViewBag.CatalogNum = product.CatalogNumber;
            ViewBag.ExternalUrl = product.Url;
            ViewBag.ApiId = product.ExternalId;
            ViewBag.PricesDataJson = pricesDataJson;
            ViewBag.ActivePresetName = activePresetName;

            return View("~/Views/Panel/PriceHistory/Details.cshtml", prices);
        }

        private string? ExtractProductIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(url, @"product/(\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        [HttpGet]
        public async Task<IActionResult> PriceTrend(int productId)
        {
            var product = await _context.Products.FindAsync(productId);
            if (product == null)
                return NotFound("Nie znaleziono produktu.");

            if (!await UserHasAccessToStore(product.StoreId))
                return Content("Nie ma takiego sklepu");

            return View("~/Views/Panel/PriceHistory/PriceTrend.cshtml", product);
        }

        [HttpGet]
        public async Task<IActionResult> GetPriceTrendData(int productId)
        {
            var product = await _context.Products.FindAsync(productId);
            if (product == null)
                return NotFound(new { Error = "Nie znaleziono produktu." });

            var storeId = product.StoreId;

            if (!await UserHasAccessToStore(storeId))
                return Unauthorized(new { Error = "Brak dostępu do sklepu." });

            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(storeName))
            {
                return BadRequest(new { Error = "Nie można zidentyfikować nazwy sklepu." });
            }

            var activePreset = await _context.CompetitorPresets
               .Include(x => x.CompetitorItems)
               .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse && cp.Type == PresetType.PriceComparison);

            var lastScraps = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(30)
                .OrderBy(sh => sh.Date)
                .ToListAsync();

            var scrapIds = lastScraps.Select(sh => sh.Id).ToList();

            if (!scrapIds.Any())
            {
                return Json(new
                {
                    ProductName = product.ProductName,
                    TimelineData = new List<object>()
                });
            }

            IQueryable<PriceHistoryClass> baseQuery = _context.PriceHistories
                .Where(ph => ph.ProductId == productId)
                .Where(ph => ph.Price > 0);

            if (activePreset != null)
            {
                if (!activePreset.SourceGoogle)
                {
                    baseQuery = baseQuery.Where(ph => ph.IsGoogle != true);
                }
                if (!activePreset.SourceCeneo)
                {
                    baseQuery = baseQuery.Where(ph => ph.IsGoogle == true);
                }
            }

            var allPotentialHistories = await baseQuery.ToListAsync();

            var rawFilteredHistories = allPotentialHistories
                .Where(ph => scrapIds.Contains(ph.ScrapHistoryId))
                .ToList();

            List<PriceHistoryClass> finalFilteredHistories;

            if (activePreset != null && activePreset.Type == PresetType.PriceComparison)
            {
                var competitorItemsDict = activePreset.CompetitorItems
                    .ToDictionary(
                        ci => (Store: ci.StoreName.ToLower().Trim(), Source: ci.DataSource),
                        ci => ci.UseCompetitor
                    );

                var storeNameLower = storeName.ToLower().Trim();
                finalFilteredHistories = new List<PriceHistoryClass>();

                foreach (var priceEntry in rawFilteredHistories)
                {
                    if (priceEntry.StoreName != null && priceEntry.StoreName.ToLower().Trim() == storeNameLower)
                    {
                        finalFilteredHistories.Add(priceEntry);
                        continue;
                    }

                    DataSourceType currentSource = priceEntry.IsGoogle == true ? DataSourceType.Google : DataSourceType.Ceneo;
                    var key = (Store: (priceEntry.StoreName ?? "").ToLower().Trim(), Source: currentSource);

                    if (competitorItemsDict.TryGetValue(key, out bool useCompetitor))
                    {
                        if (useCompetitor)
                        {
                            finalFilteredHistories.Add(priceEntry);
                        }
                    }
                    else
                    {
                        if (activePreset.UseUnmarkedStores)
                        {
                            finalFilteredHistories.Add(priceEntry);
                        }
                    }
                }
            }
            else
            {
                finalFilteredHistories = rawFilteredHistories;
            }

            var timelineData = lastScraps.Select(scrap => new
            {
                ScrapDate = scrap.Date.ToString("yyyy-MM-dd"),
                PricesByStore = finalFilteredHistories
                    .Where(ph => ph.ScrapHistoryId == scrap.Id)
                    .Select(ph => new
                    {
                        ph.StoreName,
                        ph.Price,
                        Source = (ph.IsGoogle == true) ? "google" : "ceneo"
                    })
                    .ToList()
            })
            .ToList();

            return Json(new
            {
                ProductName = product.ProductName,
                TimelineData = timelineData
            });
        }

        [HttpPost]
        public IActionResult GetPriceChangeDetails([FromBody] List<int> productIds)
        {
            if (productIds == null || productIds.Count == 0)
                return Json(new List<object>());

            var products = _context.Products
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new
                {
                    productId = p.ProductId,
                    productName = p.ProductName,
                    imageUrl = p.MainUrl
                })
                .ToList();

            return Json(products);
        }

        [HttpPost]
        public async Task<IActionResult> SimulatePriceChange([FromBody] List<SimulationItem> simulationItems)
        {

            if (simulationItems == null || simulationItems.Count == 0)
            {
                return Json(new List<object>());
            }

            int firstProductId = simulationItems.First().ProductId;
            var firstProduct = await _context.Products
                .Include(p => p.Store)
                .FirstOrDefaultAsync(p => p.ProductId == firstProductId);

            if (firstProduct == null)
            {
                return NotFound("Produkt nie znaleziony.");
            }

            if (!await UserHasAccessToStore(firstProduct.StoreId))
            {
                return Unauthorized("Brak dostępu do sklepu.");
            }

            int storeId = firstProduct.StoreId;
            string ourStoreName = firstProduct.Store?.StoreName ?? "";

            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == storeId)
                .Select(pv => new
                {
                    pv.UsePriceWithDelivery,

                })
                .FirstOrDefaultAsync() ?? new { UsePriceWithDelivery = false };

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id })
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return BadRequest("Brak danych scrapowania dla sklepu.");
            }
            int latestScrapId = latestScrap.Id;

            var productIds = simulationItems
                .Select(s => s.ProductId)
                .Distinct()
                .ToList();

            var productsData = await GetProductsInChunksAsync(productIds);

            var allPriceHistories = await GetPriceHistoriesInChunksAsync(productIds, latestScrapId);

            var priceHistoriesByProduct = allPriceHistories
                .GroupBy(ph => ph.ProductId)
                .ToDictionary(g => g.Key, g => g.ToList());

            string CalculateRanking(List<decimal> prices, decimal price)
            {
                prices.Sort();
                int firstIndex = prices.IndexOf(price);
                int lastIndex = prices.LastIndexOf(price);
                if (firstIndex == -1)
                    return "-";
                if (firstIndex == lastIndex)
                    return (firstIndex + 1).ToString();
                else
                    return $"{firstIndex + 1}-{lastIndex + 1}";
            }

            var simulationResults = new List<object>();

            foreach (var sim in simulationItems)
            {

                var product = productsData.FirstOrDefault(p => p.ProductId == sim.ProductId);
                if (product == null)
                {

                    continue;
                }

                priceHistoriesByProduct.TryGetValue(sim.ProductId, out var allRecordsForProduct);
                if (allRecordsForProduct == null)
                {

                    simulationResults.Add(new
                    {
                        productId = product.ProductId,
                        ean = product.Ean,
                        externalId = product.ExternalId,

                        producerCode = product.ProducerCode,
                        producer = product.Producer,
                        currentGoogleRanking = "-",
                        newGoogleRanking = "-",
                        totalGoogleOffers = (int?)null,
                        currentCeneoRanking = "-",
                        newCeneoRanking = "-",
                        totalCeneoOffers = (int?)null,
                        googleCurrentOffers = new List<object>(),
                        googleNewOffers = new List<object>(),
                        ceneoCurrentOffers = new List<object>(),
                        ceneoNewOffers = new List<object>(),

                        ourStoreShippingCost = (decimal?)null,
                        effectiveCurrentPrice = sim.CurrentPrice,
                        effectiveNewPrice = sim.NewPrice,
                        baseCurrentPrice = sim.CurrentPrice,
                        baseNewPrice = sim.NewPrice,

                        currentMargin = (decimal?)null,
                        newMargin = (decimal?)null,
                        currentMarginValue = (decimal?)null,
                        newMarginValue = (decimal?)null,
                        
                    });
                    continue;
                }

                var ourStoreCeneoRecord = allRecordsForProduct.FirstOrDefault(ph => ph.StoreName == ourStoreName && !ph.IsGoogle);

                var ourStoreGoogleRecord = allRecordsForProduct.FirstOrDefault(ph => ph.StoreName == ourStoreName && ph.IsGoogle);

                decimal? ourStoreShippingCost = null;

                if (ourStoreCeneoRecord.ProductId != 0 && ourStoreCeneoRecord.ShippingCostNum.HasValue)
                {
                    ourStoreShippingCost = ourStoreCeneoRecord.ShippingCostNum.Value;
                }

                else if (ourStoreGoogleRecord.ProductId != 0 && ourStoreGoogleRecord.ShippingCostNum.HasValue)
                {
                    ourStoreShippingCost = ourStoreGoogleRecord.ShippingCostNum.Value;
                }

                decimal effectiveCurrentPrice = sim.CurrentPrice;
                decimal effectiveNewPrice = sim.NewPrice;

                decimal baseCurrentPrice = effectiveCurrentPrice;
                decimal baseNewPrice = effectiveNewPrice;

                if (priceValues.UsePriceWithDelivery && ourStoreShippingCost.HasValue)
                {
                    baseCurrentPrice = effectiveCurrentPrice - ourStoreShippingCost.Value;
                    baseNewPrice = effectiveNewPrice - ourStoreShippingCost.Value;

                    if (baseCurrentPrice < 0) baseCurrentPrice = 0;
                    if (baseNewPrice < 0) baseNewPrice = 0;
                }

                decimal? currentMargin = null;
                decimal? newMargin = null;
                decimal? currentMarginValue = null;
                decimal? newMarginValue = null;

                if (product.MarginPrice.HasValue && product.MarginPrice.Value != 0)
                {
                    currentMarginValue = baseCurrentPrice - product.MarginPrice.Value;
                    newMarginValue = baseNewPrice - product.MarginPrice.Value;

                    if (product.MarginPrice.Value != 0)
                    {
                        currentMargin = Math.Round((currentMarginValue.Value / product.MarginPrice.Value) * 100, 2);
                        newMargin = Math.Round((newMarginValue.Value / product.MarginPrice.Value) * 100, 2);
                    }
                }

                bool weAreInGoogle = allRecordsForProduct.Any(ph => ph.StoreName == ourStoreName && ph.IsGoogle);
                bool weAreInCeneo = allRecordsForProduct.Any(ph => ph.StoreName == ourStoreName && !ph.IsGoogle);

                var competitorPrices = allRecordsForProduct.Where(ph => ph.StoreName != ourStoreName).ToList();

                var googleCompetitorEffectivePrices = competitorPrices
                    .Where(x => x.IsGoogle)
                    .Select(x => priceValues.UsePriceWithDelivery && x.ShippingCostNum.HasValue ? x.Price + x.ShippingCostNum.Value : x.Price)
                    .ToList();

                var currentGoogleList = new List<decimal>(googleCompetitorEffectivePrices);
                var newGoogleList = new List<decimal>(googleCompetitorEffectivePrices);

                if (weAreInGoogle)
                {
                    currentGoogleList.Add(effectiveCurrentPrice);
                    newGoogleList.Add(effectiveNewPrice);
                }

                int totalGoogleOffers = currentGoogleList.Count;
                string currentGoogleRanking, newGoogleRanking;
                if (totalGoogleOffers == 0)
                {
                    currentGoogleRanking = newGoogleRanking = "-";
                }
                else
                {

                    currentGoogleRanking = weAreInGoogle ? CalculateRanking(currentGoogleList, effectiveCurrentPrice) : "-";
                    newGoogleRanking = weAreInGoogle ? CalculateRanking(newGoogleList, effectiveNewPrice) : "-";
                }

                var googleCompetitorRecords = competitorPrices.Where(x => x.IsGoogle)
                     .Select(x => new { Price = priceValues.UsePriceWithDelivery && x.ShippingCostNum.HasValue ? x.Price + x.ShippingCostNum.Value : x.Price, x.StoreName }).ToList();
                var googleCurrentOffers = new List<object>(googleCompetitorRecords);
                if (weAreInGoogle)
                {
                    googleCurrentOffers.Add(new { Price = effectiveCurrentPrice, StoreName = ourStoreName });
                }
                var googleNewOffers = new List<object>(googleCompetitorRecords);
                if (weAreInGoogle)
                {
                    googleNewOffers.Add(new { Price = effectiveNewPrice, StoreName = ourStoreName });
                }

                googleCurrentOffers = googleCurrentOffers.OrderBy(x => ((dynamic)x).Price).ToList();
                googleNewOffers = googleNewOffers.OrderBy(x => ((dynamic)x).Price).ToList();

                var ceneoCompetitorEffectivePrices = competitorPrices
                    .Where(x => !x.IsGoogle && !string.IsNullOrEmpty(x.StoreName))
                    .Select(x => priceValues.UsePriceWithDelivery && x.ShippingCostNum.HasValue ? x.Price + x.ShippingCostNum.Value : x.Price)
                    .ToList();

                var currentCeneoList = new List<decimal>(ceneoCompetitorEffectivePrices);
                var newCeneoList = new List<decimal>(ceneoCompetitorEffectivePrices);

                if (weAreInCeneo)
                {
                    currentCeneoList.Add(effectiveCurrentPrice);
                    newCeneoList.Add(effectiveNewPrice);
                }

                int totalCeneoOffers = currentCeneoList.Count;
                string currentCeneoRanking, newCeneoRanking;
                if (totalCeneoOffers == 0)
                {
                    currentCeneoRanking = newCeneoRanking = "-";
                }
                else
                {

                    currentCeneoRanking = weAreInCeneo ? CalculateRanking(currentCeneoList, effectiveCurrentPrice) : "-";
                    newCeneoRanking = weAreInCeneo ? CalculateRanking(newCeneoList, effectiveNewPrice) : "-";
                }

                var ceneoCompetitorRecords = competitorPrices.Where(x => !x.IsGoogle && !string.IsNullOrEmpty(x.StoreName))
                     .Select(x => new { Price = priceValues.UsePriceWithDelivery && x.ShippingCostNum.HasValue ? x.Price + x.ShippingCostNum.Value : x.Price, x.StoreName }).ToList();
                var ceneoCurrentOffers = new List<object>(ceneoCompetitorRecords);
                if (weAreInCeneo)
                {
                    ceneoCurrentOffers.Add(new { Price = effectiveCurrentPrice, StoreName = ourStoreName });
                }
                var ceneoNewOffers = new List<object>(ceneoCompetitorRecords);
                if (weAreInCeneo)
                {
                    ceneoNewOffers.Add(new { Price = effectiveNewPrice, StoreName = ourStoreName });
                }

                ceneoCurrentOffers = ceneoCurrentOffers.OrderBy(x => ((dynamic)x).Price).ToList();
                ceneoNewOffers = ceneoNewOffers.OrderBy(x => ((dynamic)x).Price).ToList();

                simulationResults.Add(new
                {
                    productId = product.ProductId,
                    ean = product.Ean,
                    producerCode = product.ProducerCode,
                    producer = product.Producer,
                    externalId = product.ExternalId,

                    ourStoreShippingCost,
                    effectiveCurrentPrice,
                    effectiveNewPrice,
                    baseCurrentPrice,
                    baseNewPrice,

                    currentGoogleRanking,
                    newGoogleRanking,
                    totalGoogleOffers = (totalGoogleOffers > 0 ? totalGoogleOffers : (int?)null),
                    currentCeneoRanking,
                    newCeneoRanking,
                    totalCeneoOffers = (totalCeneoOffers > 0 ? totalCeneoOffers : (int?)null),
                    googleCurrentOffers,
                    googleNewOffers,
                    ceneoCurrentOffers,
                    ceneoNewOffers,

                    currentMargin,
                    newMargin,
                    currentMarginValue,
                    newMarginValue
                });
            }

            return Json(new
            {
                ourStoreName,
                simulationResults,
                usePriceWithDelivery = priceValues.UsePriceWithDelivery,
                latestScrapId
            });
        }

        public class SimulationItem
        {
            public int ProductId { get; set; }
            public decimal CurrentPrice { get; set; }
            public decimal NewPrice { get; set; }

            public int StoreId { get; set; }
        }

        private const int CHUNK_SIZE = 200;

        private async Task<List<ProductData>> GetProductsInChunksAsync(List<int> productIds)
        {
            var result = new List<ProductData>();
            for (int i = 0; i < productIds.Count; i += CHUNK_SIZE)
            {
                var subset = productIds.Skip(i).Take(CHUNK_SIZE).ToList();
                if (subset.Count == 0) continue;

                var inClause = string.Join(",", subset);

                // DODANO "Producer" DO LISTY PÓL W SELECT
                string sql = $@"
            SELECT ProductId, Ean, MarginPrice, ExternalId, ProducerCode, Producer
            FROM Products
            WHERE ProductId IN ({inClause})
        ";

                var partial = await _context.Products
                    .FromSqlRaw(sql)
                    .Select(p => new ProductData
                    {
                        ProductId = p.ProductId,
                        Ean = p.Ean,
                        MarginPrice = p.MarginPrice,
                        ExternalId = p.ExternalId,
                        ProducerCode = p.ProducerCode,
                        Producer = p.Producer // <--- DODAJ TĘ LINIJKĘ
                    })
                    .ToListAsync();

                result.AddRange(partial);
            }

            return result;
        }

        public class ProductData
        {
            public int ProductId { get; set; }
            public string Ean { get; set; }
            public decimal? MarginPrice { get; set; }
            public int? ExternalId { get; set; }
            public string? ProducerCode { get; set; }
            public string? Producer { get; set; }
        }

        private async Task<List<(int ProductId, decimal Price, bool IsGoogle, string StoreName, decimal? ShippingCostNum)>>
        GetPriceHistoriesInChunksAsync(List<int> productIds, int scrapId)
        {
            var result = new List<(int, decimal, bool, string, decimal?)>();

            for (int i = 0; i < productIds.Count; i += CHUNK_SIZE)
            {
                var subset = productIds.Skip(i).Take(CHUNK_SIZE).ToList();
                if (subset.Count == 0)
                    continue;

                var inClause = string.Join(",", subset);

                string sql = $@"
            SELECT ProductId, Price, IsGoogle, StoreName, ShippingCostNum -- Dodajemy ShippingCostNum do SELECT
            FROM PriceHistories
            WHERE ScrapHistoryId = {scrapId}
              AND ProductId IN ({inClause})
        ";

                var partial = await _context.PriceHistories
                    .FromSqlRaw(sql)
                    .Select(ph => new
                    {
                        ph.ProductId,
                        ph.Price,
                        ph.IsGoogle,
                        ph.StoreName,
                        ph.ShippingCostNum
                    })
                    .ToListAsync();

                result.AddRange(

                    partial.Select(x => (x.ProductId, x.Price, x.IsGoogle, x.StoreName, x.ShippingCostNum))
                );
            }

            return result;
        }

        [HttpPost]
        public async Task<IActionResult> SaveMarginSettings([FromBody] PriceMarginSettingsViewModel model)
        {
            if (model == null || model.StoreId <= 0)
            {
                return BadRequest("Invalid store ID or settings.");
            }

            if (!await UserHasAccessToStore(model.StoreId))
            {
                return BadRequest("Nie ma takiego sklepu.");
            }

            var priceValues = await _context.PriceValues
                .FirstOrDefaultAsync(pv => pv.StoreId == model.StoreId);

            if (priceValues == null)
            {
                priceValues = new PriceValueClass
                {
                    StoreId = model.StoreId,
                    IdentifierForSimulation = model.IdentifierForSimulation,
                    UsePriceWithDelivery = model.UsePriceWithDelivery,
                    UseMarginForSimulation = model.UseMarginForSimulation,
                    EnforceMinimalMargin = model.EnforceMinimalMargin,
                    MinimalMarginPercent = model.MinimalMarginPercent
                };
                _context.PriceValues.Add(priceValues);
            }
            else
            {
                priceValues.IdentifierForSimulation = model.IdentifierForSimulation;
                priceValues.UseMarginForSimulation = model.UseMarginForSimulation;
                priceValues.UsePriceWithDelivery = model.UsePriceWithDelivery;
                priceValues.EnforceMinimalMargin = model.EnforceMinimalMargin;
                priceValues.MinimalMarginPercent = model.MinimalMarginPercent;
                _context.PriceValues.Update(priceValues);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Ustawienia marży zostały zaktualizowane." });
        }

        public class PriceMarginSettingsViewModel
        {
            public int StoreId { get; set; }
            public string IdentifierForSimulation { get; set; }
            public bool UsePriceWithDelivery { get; set; }
            public bool UseMarginForSimulation { get; set; }
            public bool EnforceMinimalMargin { get; set; }
            public decimal MinimalMarginPercent { get; set; }
        }

        public class CompetitorKey
        {
            public string Store { get; set; }
            public bool Source { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> ExportToExcel(int? storeId)
        {
            if (storeId == null) return NotFound("Store ID not provided.");

            if (!await UserHasAccessToStore(storeId.Value))
            {
                return Content("Brak dostępu do sklepu");
            }

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            if (latestScrap == null) return Content("Brak danych scrapowania.");

            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            var myStoreNameLower = storeName?.ToLower().Trim() ?? "";

            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == storeId)
                .Select(pv => new { pv.UsePriceWithDelivery })
                .FirstOrDefaultAsync() ?? new { UsePriceWithDelivery = false };

            var rawData = await (from p in _context.Products
                                 join ph in _context.PriceHistories on p.ProductId equals ph.ProductId
                                 where p.StoreId == storeId && ph.ScrapHistoryId == latestScrap.Id
                                 select new
                                 {
                                     p.ProductName,
                                     p.Producer,

                                     p.Ean,
                                     p.ExternalId,
                                     p.MarginPrice,
                                     ph.Price,
                                     ph.StoreName,
                                     ph.IsGoogle,
                                     ph.ShippingCostNum
                                 }).ToListAsync();

            var groupedData = rawData
                .GroupBy(x => new { x.ProductName, x.Producer, x.Ean, x.ExternalId, x.MarginPrice })
                .Select(g =>
                {
                    var allOffers = g.Select(x => new
                    {
                        Store = x.StoreName ?? (x.IsGoogle == true ? "Google" : "Ceneo"),
                        FinalPrice = (priceValues.UsePriceWithDelivery && x.ShippingCostNum.HasValue)
                                     ? x.Price + x.ShippingCostNum.Value
                                     : x.Price,
                        IsMe = x.StoreName != null && x.StoreName.ToLower().Trim() == myStoreNameLower
                    }).ToList();

                    var myOffer = allOffers.FirstOrDefault(x => x.IsMe);
                    var competitors = allOffers.Where(x => !x.IsMe).OrderBy(x => x.FinalPrice).ToList();
                    decimal minMarketPrice = allOffers.Min(x => x.FinalPrice);

                    string positionString = "-";
                    int statusColorCode = 0;
                    decimal? diffToLowest = null;

                    if (myOffer != null)
                    {
                        int cheaperCount = allOffers.Count(x => x.FinalPrice < myOffer.FinalPrice);
                        int myRank = cheaperCount + 1;
                        int totalOffers = allOffers.Count;
                        positionString = $"{myRank} z {totalOffers}";

                        if (competitors.Any())
                        {
                            decimal lowestCompetitor = competitors.First().FinalPrice;
                            diffToLowest = myOffer.FinalPrice - lowestCompetitor;
                        }

                        if (myOffer.FinalPrice == minMarketPrice)
                        {
                            int othersWithSamePrice = allOffers.Count(x => x.FinalPrice == minMarketPrice && !x.IsMe);
                            if (othersWithSamePrice == 0) statusColorCode = 1;
                            else statusColorCode = 2;
                        }
                        else
                        {
                            statusColorCode = 3;
                        }
                    }

                    return new
                    {
                        Product = g.Key,
                        MyPrice = myOffer?.FinalPrice,
                        DiffToLowest = diffToLowest,
                        Position = positionString,
                        ColorCode = statusColorCode,
                        Competitors = competitors
                    };
                })
                .OrderBy(x => x.Product.ProductName)
                .ToList();

            using (var workbook = new XSSFWorkbook())
            {
                var sheet = workbook.CreateSheet("Monitoring Cen");

                var headerStyle = workbook.CreateCellStyle();
                var headerFont = workbook.CreateFont();
                headerFont.IsBold = true;
                headerStyle.SetFont(headerFont);

                var currencyStyle = workbook.CreateCellStyle();
                currencyStyle.DataFormat = workbook.CreateDataFormat().GetFormat("#,##0.00");

                var styleGreen = workbook.CreateCellStyle();
                styleGreen.CloneStyleFrom(currencyStyle);
                styleGreen.FillForegroundColor = IndexedColors.LightGreen.Index;
                styleGreen.FillPattern = FillPattern.SolidForeground;

                var styleLightGreen = workbook.CreateCellStyle();
                styleLightGreen.CloneStyleFrom(currencyStyle);
                styleLightGreen.FillForegroundColor = IndexedColors.LemonChiffon.Index;
                styleLightGreen.FillPattern = FillPattern.SolidForeground;

                var styleRed = workbook.CreateCellStyle();
                styleRed.CloneStyleFrom(currencyStyle);
                styleRed.FillForegroundColor = IndexedColors.Rose.Index;
                styleRed.FillPattern = FillPattern.SolidForeground;

                var headerRow = sheet.CreateRow(0);
                int colIndex = 0;

                string[] staticHeaders = { "ID Produktu", "Nazwa Produktu", "Producent", "EAN", "Cena Zakupu", "Twoja Cena", "Pozycja", "Różnica" };

                foreach (var h in staticHeaders) headerRow.CreateCell(colIndex++).SetCellValue(h);

                int maxCompetitors = 12;
                for (int i = 1; i <= maxCompetitors; i++)
                {
                    headerRow.CreateCell(colIndex++).SetCellValue($"Sklep {i}");
                    headerRow.CreateCell(colIndex++).SetCellValue($"Cena {i}");
                }

                for (int i = 0; i < colIndex; i++) headerRow.GetCell(i).CellStyle = headerStyle;

                int rowIndex = 1;
                foreach (var item in groupedData)
                {
                    var row = sheet.CreateRow(rowIndex++);
                    colIndex = 0;

                    row.CreateCell(colIndex++).SetCellValue(item.Product.ExternalId?.ToString() ?? "");

                    row.CreateCell(colIndex++).SetCellValue(item.Product.ProductName);

                    row.CreateCell(colIndex++).SetCellValue(item.Product.Producer ?? "");

                    row.CreateCell(colIndex++).SetCellValue(item.Product.Ean);

                    var cellMargin = row.CreateCell(colIndex++);
                    if (item.Product.MarginPrice.HasValue)
                    {
                        cellMargin.SetCellValue((double)item.Product.MarginPrice.Value);
                        cellMargin.CellStyle = currencyStyle;
                    }

                    var cellMyPrice = row.CreateCell(colIndex++);
                    if (item.MyPrice.HasValue)
                    {
                        cellMyPrice.SetCellValue((double)item.MyPrice.Value);
                        if (item.ColorCode == 1) cellMyPrice.CellStyle = styleGreen;
                        else if (item.ColorCode == 2) cellMyPrice.CellStyle = styleLightGreen;
                        else if (item.ColorCode == 3) cellMyPrice.CellStyle = styleRed;
                        else cellMyPrice.CellStyle = currencyStyle;
                    }
                    else
                    {
                        cellMyPrice.SetCellValue("-");
                    }

                    row.CreateCell(colIndex++).SetCellValue(item.Position);

                    var cellDiff = row.CreateCell(colIndex++);
                    if (item.DiffToLowest.HasValue)
                    {
                        cellDiff.SetCellValue((double)item.DiffToLowest.Value);
                        cellDiff.CellStyle = currencyStyle;
                    }
                    else
                    {
                        cellDiff.SetCellValue("");
                    }

                    for (int i = 0; i < maxCompetitors; i++)
                    {
                        if (i < item.Competitors.Count)
                        {
                            var comp = item.Competitors[i];
                            row.CreateCell(colIndex++).SetCellValue(comp.Store);

                            var cellCompPrice = row.CreateCell(colIndex++);
                            cellCompPrice.SetCellValue((double)comp.FinalPrice);
                            cellCompPrice.CellStyle = currencyStyle;
                        }
                        else
                        {
                            colIndex += 2;
                        }
                    }
                }

                sheet.AutoSizeColumn(0);

                sheet.AutoSizeColumn(1);

                sheet.AutoSizeColumn(2);

                sheet.AutoSizeColumn(3);

                sheet.AutoSizeColumn(5);

                sheet.AutoSizeColumn(6);

                for (int i = 8; i < colIndex; i++)
                {
                    sheet.SetColumnWidth(i, 4000);
                }

                using (var stream = new MemoryStream())
                {
                    workbook.Write(stream);
                    var content = stream.ToArray();
                    var fileName = $"Analiza_{storeName}_{DateTime.Now:yyyy-MM-dd_HHmm}.xlsx";
                    return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                }
            }
        }


        [HttpPost]
        public async Task<IActionResult> LogExportAsChange(int storeId, string exportType, [FromBody] List<PriceBridgeItemRequest> items)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();
            if (items == null || !items.Any()) return BadRequest("Brak danych do zapisu.");

            var userId = _userManager.GetUserId(User);

            // 1. Parsowanie typu eksportu (csv, excel, api) na Enum
            // Jeśli przyjdzie coś nieznanego, domyślnie ustawiamy np. Csv lub rzucamy błąd (tu fallback na Csv)
            PriceExportMethod method;
            if (!Enum.TryParse(exportType, true, out method))
            {
                method = PriceExportMethod.Csv; // Domyślna wartość w razie błędu
            }

            // Pobierz ID ostatniego scrapu dla spójności danych
            var latestScrapId = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            if (latestScrapId == 0) return BadRequest("Brak historii scrapowania.");

            // Tworzymy nową paczkę zmian
            var batch = new PriceBridgeBatch
            {
                StoreId = storeId,
                ScrapHistoryId = latestScrapId,
                UserId = userId,
                ExecutionDate = DateTime.Now,
                SuccessfulCount = items.Count,

                // 2. Zapisujemy metodę eksportu
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

                    // Informacyjne rankingi przed zmianą
                    RankingGoogleBefore = item.CurrentGoogleRanking,
                    RankingCeneoBefore = item.CurrentCeneoRanking,

                    // Symulowane rankingi po zmianie
                    RankingGoogleAfterSimulated = item.NewGoogleRanking,
                    RankingCeneoAfterSimulated = item.NewCeneoRanking,

                    Success = true
                });
            }

            _context.PriceBridgeBatches.Add(batch);
            await _context.SaveChangesAsync();

            return Json(new { success = true, count = items.Count });
        }
        // Klasa pomocnicza do odbierania danych z JS
        public class PriceBridgeItemRequest
        {
            public int ProductId { get; set; }
            public decimal CurrentPrice { get; set; }
            public decimal NewPrice { get; set; }
            public decimal? MarginPrice { get; set; }
            public string? CurrentGoogleRanking { get; set; }
            public string? CurrentCeneoRanking { get; set; }
            public string? NewGoogleRanking { get; set; }
            public string? NewCeneoRanking { get; set; }
        }


        [HttpGet]
        public async Task<IActionResult> GetScrapPriceChangeHistory(int storeId, int scrapHistoryId)
        {
            // 1. Sprawdzenie uprawnień
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            if (scrapHistoryId == 0) return BadRequest("Invalid Scrap History ID.");

            // 2. Pobranie paczek zmian z bazy (PriceBridgeBatches)
            var batches = await _context.PriceBridgeBatches
                .AsNoTracking()
                .Where(b => b.StoreId == storeId && b.ScrapHistoryId == scrapHistoryId)
                .Include(b => b.User)
                .Include(b => b.BridgeItems)
                    .ThenInclude(i => i.Product) // Dołączenie produktu, aby mieć jego nazwę
                .OrderByDescending(b => b.ExecutionDate)
                .ToListAsync();

            // 3. Mapowanie na prosty obiekt JSON dla widoku
            var result = batches.Select(b => new
            {
                executionDate = b.ExecutionDate,
                userName = b.User?.UserName ?? "Nieznany",
                successfulCount = b.SuccessfulCount,
                // Dodajemy metodę eksportu, żeby wyświetlić np. ikonkę Excela/CSV w historii
                exportMethod = b.ExportMethod.ToString(),
                items = b.BridgeItems.Select(i => new
                {
                    productId = i.ProductId,
                    // Upewnij się, że Product nie jest nullem (np. usunięty produkt)
                    productName = i.Product?.ProductName ?? "Produkt usunięty lub nieznany",
                    ean = i.Product?.Ean,

                    // Pola potrzebne do wyświetlenia tabeli w historii
                    priceBefore = i.PriceBefore,
                    priceAfter_Verified = i.PriceAfter, // Tutaj Verified to po prostu cena z eksportu

                    marginPrice = i.MarginPrice,

                    // Rankingi historyczne (opcjonalne do wyświetlenia)
                    rankingGoogleBefore = i.RankingGoogleBefore,
                    rankingCeneoBefore = i.RankingCeneoBefore,
                    rankingGoogleAfter = i.RankingGoogleAfterSimulated,
                    rankingCeneoAfter = i.RankingCeneoAfterSimulated,

                    success = i.Success
                }).ToList()
            }).ToList();

            return Ok(result);
        }
    }
}