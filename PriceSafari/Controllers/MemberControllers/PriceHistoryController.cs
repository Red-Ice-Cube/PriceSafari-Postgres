using AngleSharp.Dom;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NPOI.SS.Formula.Functions;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PriceSafari.Models.DTOs;
using PriceSafari.Models.ViewModels;
using PriceSafari.Services.ScheduleService;
using PriceSafari.ViewModels;
using Schema.NET;
using System.Collections.Concurrent;
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
        private readonly StorePriceBridgeService _priceBridgeService;
  
        private static readonly ConcurrentDictionary<int, DateTime> _exportCooldowns = new();
        private static readonly TimeSpan ExportCooldown = TimeSpan.FromMinutes(5);
        public PriceHistoryController(
            PriceSafariContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<PriceHistoryController> logger,
            UserManager<PriceSafariUser> userManager,
            IHubContext<ScrapingHub> hubContext,
            StorePriceBridgeService priceBridgeService)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _userManager = userManager;
            _hubContext = hubContext;
            _priceBridgeService = priceBridgeService;
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

            var storeDetails = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => new
                {
                    s.StoreName,
                    s.StoreLogoUrl,
                    s.IsStorePriceBridgeActive

                })
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

            ViewBag.IsStorePriceBridgeActive = storeDetails?.IsStorePriceBridgeActive ?? false;

            ViewBag.StoreName = storeDetails?.StoreName;
            ViewBag.StoreLogo = storeDetails?.StoreLogoUrl;

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
                .AsNoTracking()
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

            var previousScrapId = await _context.ScrapHistories
                .AsNoTracking()
                .Where(sh => sh.StoreId == storeId && sh.Date < latestScrap.Date)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            var storeName = await _context.Stores
                .AsNoTracking()
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            var priceValues = await _context.PriceValues
                .AsNoTracking()
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
                    pv.UsePriceWithDelivery,
                    pv.PriceIndexTargetPercent
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
                    UsePriceWithDelivery = false,
                    PriceIndexTargetPercent = 100.00m
                };

            // ── Preset konkurencji ──
            var activePreset = await _context.CompetitorPresets
                .AsNoTracking()
                .Include(x => x.CompetitorItems)
                .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse && cp.Type == PresetType.PriceComparison);

            string activePresetName = null;

            var baseQuery = from p in _context.Products.AsNoTracking()
                            where p.StoreId == storeId && p.IsScrapable
                            join ph in _context.PriceHistories.AsNoTracking()
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
                                ShippingCostNum = (ph != null ? ph.ShippingCostNum : (decimal?)null),
                                AddedDate = p.AddedDate
                            };

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

            // ── Committed changes — przeniesione po productIds, ograniczone filtrem ──
            var bridgeItems = await _context.PriceBridgeItems
                .AsNoTracking()
                .Include(i => i.Batch)
                .Where(i => i.Batch.StoreId == storeId.Value
                         && i.Batch.ScrapHistoryId == latestScrap.Id
                         && productIds.Contains(i.ProductId))
                .ToListAsync();

            var committedLookup = bridgeItems
                .GroupBy(i => i.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(i => i.Batch.ExecutionDate).First()
                );

            // ── Extended info — ograniczone do aktywnych produktów ──
            var extendedInfoData = await _context.PriceHistoryExtendedInfos
                .AsNoTracking()
                .Where(e => e.ScrapHistoryId == latestScrap.Id && productIds.Contains(e.ProductId))
                .ToListAsync();
            var extendedInfoDict = extendedInfoData.ToDictionary(e => e.ProductId);

            var previousExtendedInfoData = new Dictionary<int, PriceHistoryExtendedInfoClass>();
            if (previousScrapId > 0)
            {
                previousExtendedInfoData = await _context.PriceHistoryExtendedInfos
                    .AsNoTracking()
                    .Where(e => e.ScrapHistoryId == previousScrapId && productIds.Contains(e.ProductId))
                    .ToDictionaryAsync(e => e.ProductId);
            }

            var productFlagsDictionary = await _context.ProductFlags
                .AsNoTracking()
                .Where(pf => pf.ProductId.HasValue && productIds.Contains(pf.ProductId.Value))
                .GroupBy(pf => pf.ProductId.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            var productsWithExternalInfo = await _context.Products
                .AsNoTracking()
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

            var automationLookup = await _context.AutomationProductAssignments
                .AsNoTracking()
                .Include(a => a.AutomationRule)
                .Where(a => a.AutomationRule.StoreId == storeId.Value
                         && a.ProductId != null
                         && a.AutomationRule.SourceType == AutomationSourceType.PriceComparison)
                .Select(a => new
                {
                    ProductId = a.ProductId.Value,
                    RuleName = a.AutomationRule.Name,
                    RuleColor = a.AutomationRule.ColorHex,
                    IsActive = a.AutomationRule.IsActive,
                    RuleId = a.AutomationRule.Id,
                    IsTimeLimited = a.AutomationRule.IsTimeLimited,
                    StartDate = a.AutomationRule.ScheduledStartDate,
                    EndDate = a.AutomationRule.ScheduledEndDate
                })
                .ToDictionaryAsync(a => a.ProductId);

            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

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
                       var autoRule = automationLookup.GetValueOrDefault(g.Key);
                       bool isAutomationPaused = false;
                       if (autoRule != null && autoRule.IsActive && autoRule.IsTimeLimited)
                       {
                           var today = DateTime.Today;

                           bool isScheduledForFuture = autoRule.StartDate.HasValue && today < autoRule.StartDate.Value.Date;
                           bool isExpiredInPast = autoRule.EndDate.HasValue && today > autoRule.EndDate.Value.Date;

                           if (isScheduledForFuture || isExpiredInPast)
                           {
                               isAutomationPaused = true;
                           }
                       }

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
                       if (myPriceEntry != null) presetFilteredValidPrices.Add(myPriceEntry);

                       bool onlyMe = !presetFilteredCompetitorPrices.Any() && myPriceEntry != null;

                       var bestCompetitorPriceEntry = presetFilteredCompetitorPrices
                           .OrderBy(x => x.Price)
                           .ThenBy(x => x.StoreName)
                           .ThenByDescending(x => x.IsGoogle == false)
                           .FirstOrDefault();
                       var bestCompetitorPrice = bestCompetitorPriceEntry?.Price;

                       PriceRowDto finalBestPriceEntry = bestCompetitorPriceEntry;
                       decimal? finalBestPrice = bestCompetitorPrice;

                       decimal? marketAveragePrice = null;
                       decimal? marketPriceIndex = null;
                       string marketBucket = "market-neutral";

                       var competitorPricesOnly = presetFilteredCompetitorPrices
                           .Where(x => x.Price.HasValue && x.Price.Value > 0)
                           .Select(x => x.Price.Value)
                           .ToList();

                       if (competitorPricesOnly.Count > 0)
                       {
                           marketAveragePrice = CalculateMedian(competitorPricesOnly);

                           if (marketAveragePrice.HasValue && myPrice.HasValue && myPrice.Value > 0 && marketAveragePrice.Value > 0)
                           {
                               decimal diff = myPrice.Value - marketAveragePrice.Value;
                               marketPriceIndex = Math.Round((diff / marketAveragePrice.Value) * 100, 2);

                               if (marketPriceIndex < -15)
                                   marketBucket = "market-deep-discount";
                               else if (marketPriceIndex >= -15 && marketPriceIndex < -2)
                                   marketBucket = "market-below-average";
                               else if (marketPriceIndex >= -2 && marketPriceIndex <= 2)
                                   marketBucket = "market-average";
                               else if (marketPriceIndex > 2 && marketPriceIndex <= 15)
                                   marketBucket = "market-above-average";
                               else
                                   marketBucket = "market-overpriced";
                           }
                       }
                       else if (onlyMe)
                       {
                           marketBucket = "market-solo";
                       }

                       bool iAmEffectivelyTheBest = false;
                       if (myPrice.HasValue)
                       {
                           if (!bestCompetitorPrice.HasValue) iAmEffectivelyTheBest = true;
                           else if (myPrice.Value <= bestCompetitorPrice.Value) iAmEffectivelyTheBest = true;
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
                       else if (totalValidOffers > 0) myPricePositionString = $"N/A / {totalValidOffers}";

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
                               if (bestCompetitorPrice.HasValue) savings = Math.Round(bestCompetitorPrice.Value - myPrice.Value, 2);
                           }
                       }

                       bool? bestEntryStockStatus = finalBestPriceEntry != null ? (finalBestPriceEntry.IsGoogle == true ? finalBestPriceEntry.GoogleInStock : finalBestPriceEntry.CeneoInStock) : null;
                       bool? myEntryStockStatus = myPriceEntry != null ? (myPriceEntry.IsGoogle == true ? myPriceEntry.GoogleInStock : myPriceEntry.CeneoInStock) : null;

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
                           if (myPrice.HasValue && myPrice.Value < bestCompetitorPrice.Value) externalBestPriceCount = 0;
                           else if (myPrice.HasValue && myPrice.Value == bestCompetitorPrice.Value) externalBestPriceCount = presetFilteredValidPrices.Count(x => x.Price.HasValue && x.Price.Value == bestCompetitorPrice.Value);
                           else externalBestPriceCount = presetFilteredCompetitorPrices.Count(x => x.Price.HasValue && x.Price.Value == bestCompetitorPrice.Value);
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
                               bool isMyStoreSoleCheapest = (singleCheapestEntry.StoreName != null && singleCheapestEntry.StoreName.ToLower().Trim() == storeNameLower);

                               if (!isMyStoreSoleCheapest)
                               {
                                   var secondLowestPrice = validPresetPrices.Where(x => x.Price.Value > absoluteLowestPrice).Select(x => x.Price.Value).OrderBy(x => x).FirstOrDefault();
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
                           },
                           MarketAveragePrice = marketAveragePrice,
                           MarketPriceIndex = marketPriceIndex,
                           MarketBucket = marketBucket,
                           AutomationRuleName = autoRule?.RuleName,
                           AutomationRuleColor = autoRule?.RuleColor,
                           IsAutomationActive = autoRule?.IsActive,
                           AutomationRuleId = autoRule?.RuleId,
                           IsAutomationPaused = isAutomationPaused,
                           IsNew = product.AddedDate >= sevenDaysAgo
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
                priceIndexTarget = priceValues.PriceIndexTargetPercent,
                presetName = activePresetName ?? "PriceSafari",
                latestScrapId = latestScrap?.Id
            });
        }





        //[HttpGet]
        //public async Task<IActionResult> GetPrices(int? storeId)
        //{
        //    if (storeId == null)
        //    {
        //        return Json(new
        //        {
        //            productCount = 0,
        //            priceCount = 0,
        //            myStoreName = "",
        //            prices = new List<object>(),
        //            missedProductsCount = 0,
        //            setPrice1 = 2.00m,
        //            setPrice2 = 2.00m
        //        });
        //    }

        //    if (!await UserHasAccessToStore(storeId.Value))
        //    {
        //        return Json(new { error = "Nie ma takiego sklepu" });
        //    }

        //    var latestScrap = await _context.ScrapHistories
        //        .Where(sh => sh.StoreId == storeId)
        //        .OrderByDescending(sh => sh.Date)
        //        .Select(sh => new { sh.Id, sh.Date })
        //        .FirstOrDefaultAsync();

        //    if (latestScrap == null)
        //    {
        //        return Json(new
        //        {
        //            productCount = 0,
        //            priceCount = 0,
        //            myStoreName = "",
        //            prices = new List<object>(),
        //            missedProductsCount = 0,
        //            setPrice1 = 2.00m,
        //            setPrice2 = 2.00m
        //        });
        //    }

        //    var bridgeItems = await _context.PriceBridgeItems
        //.Include(i => i.Batch)
        //.Where(i => i.Batch.StoreId == storeId.Value &&
        //            i.Batch.ScrapHistoryId == latestScrap.Id)
        //.ToListAsync();

        //    var committedLookup = bridgeItems
        //        .GroupBy(i => i.ProductId)
        //        .ToDictionary(
        //            g => g.Key,
        //            g => g.OrderByDescending(i => i.Batch.ExecutionDate).First()
        //        );

        //    var previousScrapId = await _context.ScrapHistories
        //        .Where(sh => sh.StoreId == storeId && sh.Date < latestScrap.Date)
        //        .OrderByDescending(sh => sh.Date)
        //        .Select(sh => sh.Id)
        //        .FirstOrDefaultAsync();

        //    var storeName = await _context.Stores
        //        .Where(s => s.StoreId == storeId)
        //        .Select(s => s.StoreName)
        //        .FirstOrDefaultAsync();

        //    var priceValues = await _context.PriceValues
        //        .Where(pv => pv.StoreId == storeId)
        //        .Select(pv => new
        //        {
        //            pv.SetPrice1,
        //            pv.SetPrice2,
        //            pv.PriceStep,
        //            pv.UsePriceDiff,
        //            pv.IdentifierForSimulation,
        //            pv.UseMarginForSimulation,
        //            pv.EnforceMinimalMargin,
        //            pv.MinimalMarginPercent,
        //            pv.UsePriceWithDelivery,
        //            pv.PriceIndexTargetPercent
        //        })
        //        .FirstOrDefaultAsync() ?? new
        //        {
        //            SetPrice1 = 2.00m,
        //            SetPrice2 = 2.00m,
        //            PriceStep = 2.00m,
        //            UsePriceDiff = true,
        //            IdentifierForSimulation = "EAN",
        //            UseMarginForSimulation = true,
        //            EnforceMinimalMargin = true,
        //            MinimalMarginPercent = 0.00m,
        //            UsePriceWithDelivery = false,
        //            PriceIndexTargetPercent = 100.00m
        //        };

        //    var baseQuery = from p in _context.Products
        //                    where p.StoreId == storeId && p.IsScrapable
        //                    join ph in _context.PriceHistories
        //                        .Where(ph => ph.ScrapHistoryId == latestScrap.Id)
        //                        on p.ProductId equals ph.ProductId into phGroup
        //                    from ph in phGroup.DefaultIfEmpty()
        //                    select new PriceRowDto
        //                    {
        //                        ProductId = p.ProductId,
        //                        ProductName = p.ProductName,
        //                        Producer = p.Producer,
        //                        Price = (ph != null ? ph.Price : (decimal?)null),
        //                        StoreName = (ph != null ? ph.StoreName : null),
        //                        ScrapHistoryId = (ph != null ? ph.ScrapHistoryId : (int?)null),
        //                        Position = (ph != null ? ph.Position : (int?)null),
        //                        IsBidding = ph != null ? ph.IsBidding : null,
        //                        IsGoogle = (ph != null ? ph.IsGoogle : (bool?)null),

        //                        CeneoInStock = (ph != null ? ph.CeneoInStock : (bool?)null),
        //                        GoogleInStock = (ph != null ? ph.GoogleInStock : (bool?)null),
        //                        IsRejected = p.IsRejected,
        //                        ShippingCostNum = (ph != null ? ph.ShippingCostNum : (decimal?)null),
        //                        AddedDate = p.AddedDate
        //                    };

        //    var activePreset = await _context.CompetitorPresets
        //        .Include(x => x.CompetitorItems)
        //        .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse && cp.Type == PresetType.PriceComparison);

        //    string activePresetName = null;

        //    if (activePreset != null)
        //    {
        //        activePresetName = activePreset.PresetName;
        //        if (!activePreset.SourceGoogle) baseQuery = baseQuery.Where(p => p.IsGoogle != true);
        //        if (!activePreset.SourceCeneo) baseQuery = baseQuery.Where(p => p.IsGoogle == true);
        //    }

        //    var rawPrices = await baseQuery.ToListAsync();

        //    if (priceValues.UsePriceWithDelivery)
        //    {
        //        foreach (var row in rawPrices)
        //        {
        //            if (row.Price.HasValue && row.ShippingCostNum.HasValue)
        //            {
        //                row.Price = row.Price.Value + row.ShippingCostNum.Value;
        //            }
        //        }
        //    }

        //    var productIds = rawPrices.Select(p => p.ProductId).Distinct().ToList();

        //    var extendedInfoData = await _context.PriceHistoryExtendedInfos
        //        .Where(e => e.ScrapHistoryId == latestScrap.Id && productIds.Contains(e.ProductId))
        //        .ToListAsync();
        //    var extendedInfoDict = extendedInfoData.ToDictionary(e => e.ProductId);

        //    var previousExtendedInfoData = new Dictionary<int, PriceHistoryExtendedInfoClass>();
        //    if (previousScrapId > 0)
        //    {
        //        previousExtendedInfoData = await _context.PriceHistoryExtendedInfos
        //            .Where(e => e.ScrapHistoryId == previousScrapId && productIds.Contains(e.ProductId))
        //            .ToDictionaryAsync(e => e.ProductId);
        //    }

        //    var productFlagsDictionary = await _context.ProductFlags
        //       .Where(pf => pf.ProductId.HasValue && productIds.Contains(pf.ProductId.Value))
        //       .GroupBy(pf => pf.ProductId.Value)
        //       .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

        //    var productsWithExternalInfo = await _context.Products
        //       .Where(p => p.StoreId == storeId && productIds.Contains(p.ProductId))
        //       .Select(p => new {
        //           p.ProductId,
        //           p.ExternalId,
        //           p.MainUrl,
        //           p.MarginPrice,
        //           p.Ean,
        //           p.ProducerCode
        //       })
        //       .ToListAsync();
        //    var productExternalInfoDictionary = productsWithExternalInfo.ToDictionary(
        //        p => p.ProductId,
        //        p => new { p.ExternalId, p.MainUrl, p.MarginPrice, p.Ean, p.ProducerCode }
        //    );

        //    Dictionary<(string Store, DataSourceType Source), bool> competitorItemsDict = null;
        //    var storeNameLower = storeName?.ToLower().Trim() ?? "";

        //    if (activePreset != null && activePreset.Type == PresetType.PriceComparison)
        //    {
        //        competitorItemsDict = activePreset.CompetitorItems.ToDictionary(
        //            ci => (Store: ci.StoreName.ToLower().Trim(), Source: ci.DataSource),
        //            ci => ci.UseCompetitor
        //        );
        //    }

        //    var automationLookup = await _context.AutomationProductAssignments
        //        .Include(a => a.AutomationRule)
        //        .Where(a => a.AutomationRule.StoreId == storeId.Value
        //                 && a.ProductId != null

        //                 && a.AutomationRule.SourceType == AutomationSourceType.PriceComparison)
        //        .Select(a => new
        //        {
        //            ProductId = a.ProductId.Value,
        //            RuleName = a.AutomationRule.Name,
        //            RuleColor = a.AutomationRule.ColorHex,
        //            IsActive = a.AutomationRule.IsActive,
        //            RuleId = a.AutomationRule.Id,
        //            IsTimeLimited = a.AutomationRule.IsTimeLimited,
        //            StartDate = a.AutomationRule.ScheduledStartDate,
        //            EndDate = a.AutomationRule.ScheduledEndDate
        //        })
        //    .ToDictionaryAsync(a => a.ProductId);
        //    var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
        //    var allPrices = rawPrices
        //           .GroupBy(p => p.ProductId)
        //           .Select(g =>
        //           {
        //               var productGroup = g.ToList();
        //               var product = productGroup.First();
        //               var validPrices = productGroup.Where(x => x.Price.HasValue).ToList();

        //               var myPriceEntries = validPrices.Where(x => x.StoreName != null && x.StoreName.ToLower() == storeNameLower).ToList();
        //               var myPriceEntry = myPriceEntries.OrderByDescending(x => x.IsGoogle == false).FirstOrDefault();
        //               var myPrice = myPriceEntry?.Price;

        //               var allCompetitorEntries = validPrices.Where(x => x.StoreName != null && x.StoreName.ToLower() != storeNameLower).ToList();
        //               var presetFilteredCompetitorPrices = new List<PriceRowDto>();
        //               var committedItem = committedLookup.GetValueOrDefault(g.Key);
        //               var autoRule = automationLookup.GetValueOrDefault(g.Key);
        //               bool isAutomationPaused = false;
        //               if (autoRule != null && autoRule.IsActive && autoRule.IsTimeLimited)
        //               {
        //                   var today = DateTime.Today; // Porównujemy z dzisiejszą datą, tak jak w modelu

        //                   bool isScheduledForFuture = autoRule.StartDate.HasValue && today < autoRule.StartDate.Value.Date;
        //                   bool isExpiredInPast = autoRule.EndDate.HasValue && today > autoRule.EndDate.Value.Date;

        //                   if (isScheduledForFuture || isExpiredInPast)
        //                   {
        //                       isAutomationPaused = true;
        //                   }
        //               }
        //               if (competitorItemsDict != null)
        //               {
        //                   foreach (var row in allCompetitorEntries)
        //                   {
        //                       DataSourceType currentSource = row.IsGoogle == true ? DataSourceType.Google : DataSourceType.Ceneo;
        //                       var key = (Store: (row.StoreName ?? "").ToLower().Trim(), Source: currentSource);

        //                       if (competitorItemsDict.TryGetValue(key, out bool useCompetitor))
        //                       {
        //                           if (useCompetitor) presetFilteredCompetitorPrices.Add(row);
        //                       }
        //                       else if (activePreset.UseUnmarkedStores)
        //                       {
        //                           presetFilteredCompetitorPrices.Add(row);
        //                       }
        //                   }
        //               }
        //               else
        //               {
        //                   presetFilteredCompetitorPrices = allCompetitorEntries;
        //               }

        //               var presetFilteredValidPrices = new List<PriceRowDto>(presetFilteredCompetitorPrices);
        //               if (myPriceEntry != null) presetFilteredValidPrices.Add(myPriceEntry);

        //               bool onlyMe = !presetFilteredCompetitorPrices.Any() && myPriceEntry != null;

        //               var bestCompetitorPriceEntry = presetFilteredCompetitorPrices
        //                   .OrderBy(x => x.Price)
        //                   .ThenBy(x => x.StoreName)
        //                   .ThenByDescending(x => x.IsGoogle == false)
        //                   .FirstOrDefault();
        //               var bestCompetitorPrice = bestCompetitorPriceEntry?.Price;

        //               PriceRowDto finalBestPriceEntry = bestCompetitorPriceEntry;
        //               decimal? finalBestPrice = bestCompetitorPrice;

        //               decimal? marketAveragePrice = null;

        //               decimal? marketPriceIndex = null;

        //               string marketBucket = "market-neutral";

        //               var competitorPricesOnly = presetFilteredCompetitorPrices
        //                   .Where(x => x.Price.HasValue && x.Price.Value > 0)
        //                   .Select(x => x.Price.Value)
        //                   .ToList();

        //               if (competitorPricesOnly.Count > 0)
        //               {

        //                   marketAveragePrice = CalculateMedian(competitorPricesOnly);

        //                   if (marketAveragePrice.HasValue && myPrice.HasValue && myPrice.Value > 0 && marketAveragePrice.Value > 0)
        //                   {

        //                       decimal diff = myPrice.Value - marketAveragePrice.Value;
        //                       marketPriceIndex = Math.Round((diff / marketAveragePrice.Value) * 100, 2);

        //                       if (marketPriceIndex < -15)
        //                           marketBucket = "market-deep-discount";

        //                       else if (marketPriceIndex >= -15 && marketPriceIndex < -2)
        //                           marketBucket = "market-below-average";

        //                       else if (marketPriceIndex >= -2 && marketPriceIndex <= 2)
        //                           marketBucket = "market-average";

        //                       else if (marketPriceIndex > 2 && marketPriceIndex <= 15)
        //                           marketBucket = "market-above-average";

        //                       else
        //                           marketBucket = "market-overpriced";

        //                   }
        //               }
        //               else if (onlyMe)
        //               {
        //                   marketBucket = "market-solo";

        //               }

        //               bool iAmEffectivelyTheBest = false;
        //               if (myPrice.HasValue)
        //               {
        //                   if (!bestCompetitorPrice.HasValue) iAmEffectivelyTheBest = true;
        //                   else if (myPrice.Value <= bestCompetitorPrice.Value) iAmEffectivelyTheBest = true;
        //               }

        //               string myPricePositionString = "N/A / 0";
        //               var totalValidOffers = presetFilteredValidPrices.Count();
        //               if (myPrice.HasValue && totalValidOffers > 0)
        //               {
        //                   var myStorePriceValue = myPrice.Value;
        //                   int pricesLower = presetFilteredValidPrices.Count(vp => vp.Price.HasValue && vp.Price.Value < myStorePriceValue);
        //                   int pricesEqual = presetFilteredValidPrices.Count(vp => vp.Price.HasValue && vp.Price.Value == myStorePriceValue);
        //                   int rankStart = pricesLower + 1; int rankEnd = pricesLower + pricesEqual;
        //                   myPricePositionString = (rankStart == rankEnd) ? $"{rankStart}/{totalValidOffers}" : $"{rankStart}-{rankEnd}/{totalValidOffers}";
        //               }
        //               else if (totalValidOffers > 0) myPricePositionString = $"N/A / {totalValidOffers}";

        //               decimal? priceDifference = null;
        //               decimal? percentageDifference = null;
        //               decimal? savings = null;
        //               bool isUniqueBestPrice = false;
        //               bool isRejectedDueToZeroPrice = false;
        //               int? myPosition = myPriceEntry?.Position;

        //               if (product.IsRejected || (myPrice.HasValue && myPrice.Value == 0) || (bestCompetitorPrice.HasValue && bestCompetitorPrice.Value == 0))
        //               {
        //                   isRejectedDueToZeroPrice = true;
        //               }

        //               if (myPrice.HasValue && bestCompetitorPrice.HasValue && bestCompetitorPrice.Value <= myPrice.Value)
        //               {
        //                   priceDifference = Math.Round(myPrice.Value - bestCompetitorPrice.Value, 2);
        //                   if (bestCompetitorPrice.Value > 0)
        //                   {
        //                       percentageDifference = Math.Round((myPrice.Value - bestCompetitorPrice.Value) / bestCompetitorPrice.Value * 100, 2);
        //                   }
        //               }

        //               if (iAmEffectivelyTheBest && myPrice.HasValue && !isRejectedDueToZeroPrice)
        //               {
        //                   bool amIUniquelyTheBest = myPrice.HasValue && (!bestCompetitorPrice.HasValue || myPrice.Value < bestCompetitorPrice.Value);
        //                   if (amIUniquelyTheBest)
        //                   {
        //                       isUniqueBestPrice = true;
        //                       if (bestCompetitorPrice.HasValue) savings = Math.Round(bestCompetitorPrice.Value - myPrice.Value, 2);
        //                   }
        //               }

        //               bool? bestEntryStockStatus = finalBestPriceEntry != null ? (finalBestPriceEntry.IsGoogle == true ? finalBestPriceEntry.GoogleInStock : finalBestPriceEntry.CeneoInStock) : null;
        //               bool? myEntryStockStatus = myPriceEntry != null ? (myPriceEntry.IsGoogle == true ? myPriceEntry.GoogleInStock : myPriceEntry.CeneoInStock) : null;

        //               var storeCount = presetFilteredValidPrices
        //                   .Where(s => s != null && s.StoreName != null)
        //                   .Select(x => new { StoreName = x.StoreName.ToLower().Trim(), Source = x.IsGoogle ?? false })
        //                   .Distinct().Count();

        //               bool sourceGoogle = productGroup.Any(x => x.IsGoogle == true);
        //               bool sourceCeneo = productGroup.Any(x => x.IsGoogle == false);

        //               bool? bestPriceIncludesDeliveryFlag = null; bool? myPriceIncludesDeliveryFlag = null;
        //               extendedInfoDict.TryGetValue(g.Key, out var extendedInfo);
        //               if (priceValues.UsePriceWithDelivery)
        //               {
        //                   bestPriceIncludesDeliveryFlag = finalBestPriceEntry?.ShippingCostNum.HasValue;
        //                   myPriceIncludesDeliveryFlag = myPriceEntry?.ShippingCostNum.HasValue;
        //               }

        //               int externalBestPriceCount = 0;
        //               if (bestCompetitorPrice.HasValue)
        //               {
        //                   if (myPrice.HasValue && myPrice.Value < bestCompetitorPrice.Value) externalBestPriceCount = 0;
        //                   else if (myPrice.HasValue && myPrice.Value == bestCompetitorPrice.Value) externalBestPriceCount = presetFilteredValidPrices.Count(x => x.Price.HasValue && x.Price.Value == bestCompetitorPrice.Value);
        //                   else externalBestPriceCount = presetFilteredCompetitorPrices.Count(x => x.Price.HasValue && x.Price.Value == bestCompetitorPrice.Value);
        //               }

        //               productFlagsDictionary.TryGetValue(g.Key, out var flagIds); flagIds ??= new List<int>();
        //               productExternalInfoDictionary.TryGetValue(g.Key, out var extInfo);

        //               SalesTrendStatus salesTrendStatus = SalesTrendStatus.NoData;
        //               int? salesDifference = null; decimal? salesPercentageChange = null;
        //               previousExtendedInfoData.TryGetValue(g.Key, out var previousExtendedInfo);
        //               if (extendedInfo?.CeneoSalesCount != null && previousExtendedInfo?.CeneoSalesCount != null)
        //               {
        //                   int currentSales = extendedInfo.CeneoSalesCount.Value; int previousSales = previousExtendedInfo.CeneoSalesCount.Value;
        //                   salesDifference = currentSales - previousSales;
        //                   if (previousSales > 0) salesPercentageChange = Math.Round(((decimal)salesDifference.Value / previousSales) * 100, 2);
        //                   else if (salesDifference > 0) salesPercentageChange = 100m;

        //                   const decimal smallChangeThreshold = 10.0m;
        //                   if (salesDifference == 0) salesTrendStatus = SalesTrendStatus.NoChange;
        //                   else if (salesDifference > 0) salesTrendStatus = (salesPercentageChange.HasValue && Math.Abs(salesPercentageChange.Value) > smallChangeThreshold) ? SalesTrendStatus.SalesUpBig : SalesTrendStatus.SalesUpSmall;
        //                   else salesTrendStatus = (salesPercentageChange.HasValue && Math.Abs(salesPercentageChange.Value) > smallChangeThreshold) ? SalesTrendStatus.SalesDownBig : SalesTrendStatus.SalesDownSmall;
        //               }

        //               decimal? singleBestCheaperDiff = null;
        //               decimal? singleBestCheaperDiffPerc = null;
        //               var validPresetPrices = presetFilteredValidPrices.Where(x => x.Price.HasValue && x.Price.Value > 0).ToList();
        //               if (validPresetPrices.Any())
        //               {
        //                   decimal absoluteLowestPrice = validPresetPrices.Select(x => x.Price.Value).Min();
        //                   var lowestPriceEntries = validPresetPrices.Where(x => x.Price.Value == absoluteLowestPrice).ToList();
        //                   int absoluteLowestPriceCount = lowestPriceEntries.Count;

        //                   if (absoluteLowestPriceCount == 1)
        //                   {
        //                       var singleCheapestEntry = lowestPriceEntries.First();
        //                       bool isMyStoreSoleCheapest = (singleCheapestEntry.StoreName != null && singleCheapestEntry.StoreName.ToLower().Trim() == storeNameLower);

        //                       if (!isMyStoreSoleCheapest)
        //                       {
        //                           var secondLowestPrice = validPresetPrices.Where(x => x.Price.Value > absoluteLowestPrice).Select(x => x.Price.Value).OrderBy(x => x).FirstOrDefault();
        //                           decimal? actualSecondLowest = (secondLowestPrice == 0) ? null : (decimal?)secondLowestPrice;
        //                           if (actualSecondLowest.HasValue)
        //                           {
        //                               singleBestCheaperDiff = Math.Round(actualSecondLowest.Value - absoluteLowestPrice, 2);
        //                               var diffPercent = ((actualSecondLowest.Value - absoluteLowestPrice) / actualSecondLowest.Value) * 100;
        //                               singleBestCheaperDiffPerc = Math.Round(diffPercent, 2);
        //                           }
        //                       }
        //                   }
        //               }

        //               return new
        //               {
        //                   ProductId = product.ProductId,
        //                   ProductName = product.ProductName,
        //                   Producer = product.Producer,
        //                   LowestPrice = finalBestPrice,
        //                   StoreName = finalBestPriceEntry?.StoreName,
        //                   MyPrice = myPrice,
        //                   ScrapId = latestScrap.Id,
        //                   PriceDifference = priceDifference,
        //                   PercentageDifference = percentageDifference,
        //                   Savings = savings,
        //                   IsSharedBestPrice = (iAmEffectivelyTheBest && !isUniqueBestPrice && myPrice.HasValue),
        //                   IsUniqueBestPrice = isUniqueBestPrice,
        //                   OnlyMe = onlyMe,
        //                   ExternalBestPriceCount = externalBestPriceCount,
        //                   IsBidding = finalBestPriceEntry?.IsBidding,
        //                   IsGoogle = finalBestPriceEntry?.IsGoogle,
        //                   Position = finalBestPriceEntry?.Position,
        //                   MyIsBidding = myPriceEntry?.IsBidding,
        //                   MyIsGoogle = myPriceEntry?.IsGoogle,
        //                   MyPosition = myPosition,
        //                   FlagIds = flagIds,
        //                   BestEntryInStock = bestEntryStockStatus,
        //                   MyEntryInStock = myEntryStockStatus,
        //                   ExternalId = extInfo?.ExternalId,
        //                   MarginPrice = extInfo?.MarginPrice,
        //                   ImgUrl = extInfo?.MainUrl,
        //                   Ean = extInfo?.Ean,
        //                   ProducerCode = extInfo?.ProducerCode,
        //                   IsRejected = product.IsRejected || isRejectedDueToZeroPrice,
        //                   StoreCount = storeCount,
        //                   SourceGoogle = sourceGoogle,
        //                   SourceCeneo = sourceCeneo,
        //                   SingleBestCheaperDiff = singleBestCheaperDiff,
        //                   SingleBestCheaperDiffPerc = singleBestCheaperDiffPerc,
        //                   BestPriceIncludesDelivery = bestPriceIncludesDeliveryFlag,
        //                   MyPriceIncludesDelivery = myPriceIncludesDeliveryFlag,
        //                   BestPriceDeliveryCost = priceValues.UsePriceWithDelivery ? finalBestPriceEntry?.ShippingCostNum : null,
        //                   MyPriceDeliveryCost = priceValues.UsePriceWithDelivery ? myPriceEntry?.ShippingCostNum : null,
        //                   CeneoSalesCount = extendedInfo?.CeneoSalesCount,
        //                   SalesTrendStatus = salesTrendStatus.ToString(),
        //                   SalesDifference = salesDifference,
        //                   SalesPercentageChange = salesPercentageChange,
        //                   ExternalApiPrice = extendedInfo?.ExtendedDataApiPrice,
        //                   MyPricePosition = myPricePositionString,
        //                   Committed = committedItem == null ? null : new
        //                   {
        //                       NewPrice = committedItem.PriceAfter,
        //                       NewGoogleRanking = committedItem.RankingGoogleAfterSimulated,
        //                       NewCeneoRanking = committedItem.RankingCeneoAfterSimulated
        //                   },

        //                   MarketAveragePrice = marketAveragePrice,

        //                   MarketPriceIndex = marketPriceIndex,

        //                   MarketBucket = marketBucket,
        //                   AutomationRuleName = autoRule?.RuleName,
        //                   AutomationRuleColor = autoRule?.RuleColor,
        //                   IsAutomationActive = autoRule?.IsActive,
        //                   AutomationRuleId = autoRule?.RuleId,
        //                   IsAutomationPaused = isAutomationPaused,
        //                   IsNew = product.AddedDate >= sevenDaysAgo
        //               };
        //           })
        //           .Where(p => p != null)
        //           .ToList();

        //    var missedProductsCount = allPrices.Count(p => p.IsRejected);

        //    return Json(new
        //    {
        //        productCount = allPrices.Count,
        //        priceCount = rawPrices.Count,
        //        myStoreName = storeName,
        //        prices = allPrices,
        //        missedProductsCount = missedProductsCount,
        //        setPrice1 = priceValues.SetPrice1,
        //        setPrice2 = priceValues.SetPrice2,
        //        stepPrice = priceValues.PriceStep,
        //        usePriceDiff = priceValues.UsePriceDiff,
        //        useMarginForSimulation = priceValues.UseMarginForSimulation,
        //        enforceMinimalMargin = priceValues.EnforceMinimalMargin,
        //        minimalMarginPercent = priceValues.MinimalMarginPercent,
        //        identifierForSimulation = priceValues.IdentifierForSimulation,
        //        usePriceWithDelivery = priceValues.UsePriceWithDelivery,
        //        priceIndexTarget = priceValues.PriceIndexTargetPercent,
        //        presetName = activePresetName ?? "PriceSafari",
        //        latestScrapId = latestScrap?.Id
        //    });
        //}


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
            public DateTime AddedDate { get; set; }
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





        private decimal? CalculateMedian(List<decimal> prices)
        {
            if (prices == null || prices.Count == 0) return null;

            var sortedPrices = prices.OrderBy(x => x).ToList();
            int count = sortedPrices.Count;

            if (count % 2 == 0)
            {
                // Parzysta liczba elementów - średnia z dwóch środkowych
                return (sortedPrices[count / 2 - 1] + sortedPrices[count / 2]) / 2m;
            }
            else
            {
                // Nieparzysta liczba - środkowy element
                return sortedPrices[count / 2];
            }
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
                    UsePriceDiff = model.usePriceDiff,
                    PriceIndexTargetPercent = model.PriceIndexTargetPercent
                };
                _context.PriceValues.Add(priceValues);
            }
            else
            {
                priceValues.SetPrice1 = model.SetPrice1;
                priceValues.SetPrice2 = model.SetPrice2;
                priceValues.PriceStep = model.PriceStep;
                priceValues.UsePriceDiff = model.usePriceDiff;
                priceValues.PriceIndexTargetPercent = model.PriceIndexTargetPercent;
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
                    offerCount = p.GoogleOfferPerStoreCount,
                    googleOfferUrl = p.GoogleOfferUrl
                })
            );

            // ── Flagi dla tego produktu ──
            var allFlags = await _context.Flags
                .Where(f => f.StoreId == storeId && f.IsMarketplace == false)
                .Select(f => new FlagViewModel
                {
                    FlagId = f.FlagId,
                    FlagName = f.FlagName,
                    FlagColor = f.FlagColor,
                    IsMarketplace = f.IsMarketplace
                })
                .ToListAsync();

            var productFlagIds = await _context.ProductFlags
                .Where(pf => pf.ProductId == productId)
                .Select(pf => pf.FlagId)
                .ToListAsync();

            // ── Automatyzacja dla tego produktu ──
            var automationAssignment = await _context.AutomationProductAssignments
                .Include(a => a.AutomationRule)
                .Where(a => a.ProductId == productId
                         && a.AutomationRule.StoreId == storeId
                         && a.AutomationRule.SourceType == AutomationSourceType.PriceComparison)
                .Select(a => new
                {
                    RuleName = a.AutomationRule.Name,
                    RuleColor = a.AutomationRule.ColorHex,
                    IsActive = a.AutomationRule.IsActive,
                    RuleId = a.AutomationRule.Id,
                    IsTimeLimited = a.AutomationRule.IsTimeLimited,
                    StartDate = a.AutomationRule.ScheduledStartDate,
                    EndDate = a.AutomationRule.ScheduledEndDate
                })
                .FirstOrDefaultAsync();

            bool isAutomationPaused = false;
            if (automationAssignment != null && automationAssignment.IsActive && automationAssignment.IsTimeLimited)
            {
                var today = DateTime.Today;
                bool isScheduledForFuture = automationAssignment.StartDate.HasValue && today < automationAssignment.StartDate.Value.Date;
                bool isExpiredInPast = automationAssignment.EndDate.HasValue && today > automationAssignment.EndDate.Value.Date;
                if (isScheduledForFuture || isExpiredInPast)
                    isAutomationPaused = true;
            }

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
            ViewBag.Flags = allFlags;
            ViewBag.ProductFlagIds = productFlagIds;
            ViewBag.AutomationRuleName = automationAssignment?.RuleName;
            ViewBag.AutomationRuleColor = automationAssignment?.RuleColor;
            ViewBag.AutomationRuleIsActive = automationAssignment?.IsActive ?? false;
            ViewBag.AutomationRuleId = automationAssignment?.RuleId;
            ViewBag.IsAutomationPaused = isAutomationPaused;

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
        public async Task<IActionResult> GetPriceTrendData(int productId, int limit = 30) // 1. Dodany parametr
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

            // 2. Walidacja limitu
            if (limit <= 0) limit = 30;

            var lastScraps = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(limit) // 3. Użycie zmiennej limit
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
        public async Task<IActionResult> LogExportAsChange(int storeId, string exportType, [FromBody] List<PriceBridgeItemRequest> items)
        {
            // 1. Walidacja Uprawnień (Autoryzacja zostaje w kontrolerze)
            if (!await UserHasAccessToStore(storeId))
            {
                return Forbid();
            }

            // 2. Pobranie danych kontekstowych (UserId)
            var userId = _userManager.GetUserId(User);

            try
            {
                // 3. Wywołanie logiki biznesowej z serwisu
                var count = await _priceBridgeService.LogExportAsChangeAsync(storeId, exportType, userId, items);

                // 4. Zwrócenie wyniku HTTP
                return Json(new { success = true, count = count });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas zapisywania eksportu cen.");
                return StatusCode(500, "Wystąpił błąd wewnętrzny serwera.");
            }
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

                string sql = $@"
            SELECT ""ProductId"", ""Ean"", ""MarginPrice"", ""ExternalId"", ""ProducerCode"", ""Producer""
            FROM ""Products""
            WHERE ""ProductId"" IN ({inClause})
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
                        Producer = p.Producer
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
                if (subset.Count == 0) continue;

                var inClause = string.Join(",", subset);

                string sql = $@"
            SELECT ""ProductId"", ""Price"", ""IsGoogle"", ""StoreName"", ""ShippingCostNum""
            FROM ""PriceHistories""
            WHERE ""ScrapHistoryId"" = {scrapId}
              AND ""ProductId"" IN ({inClause})
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

                result.AddRange(partial.Select(x => (x.ProductId, x.Price, x.IsGoogle, x.StoreName, x.ShippingCostNum)));
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

        [HttpPost]
        public async Task<IActionResult> ExecuteStorePriceChange(int storeId, [FromBody] List<PriceBridgeItemRequest> items)
        {

            if (items == null || !items.Any()) return BadRequest("Brak danych.");

            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null) return NotFound("Sklep nie istnieje.");

            if (!store.IsStorePriceBridgeActive)
            {
                return BadRequest("Zmiana cen przez API jest wyłączona w ustawieniach sklepu.");
            }

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            int scrapHistoryId = latestScrap != 0 ? latestScrap : 0;
            var userId = _userManager.GetUserId(User);

            try
            {

                var result = await _priceBridgeService.ExecuteStorePriceChangesAsync(storeId, scrapHistoryId, userId, items);

                return Json(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd w ExecuteStorePriceChange.");
                return StatusCode(500, "Wystąpił błąd podczas komunikacji ze sklepem.");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetApiExportSettings(int storeId)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => new {
                    s.IsApiExportEnabled,
                    s.ApiExportToken
                })
                .FirstOrDefaultAsync();

            return Json(store);
        }

        public class ApiExportSettingsDto
        {
            public bool IsEnabled { get; set; }
            public string Token { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> SaveApiExportSettings(int storeId, [FromBody] ApiExportSettingsDto dto)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores.FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null) return NotFound("Sklep nie istnieje");

            store.IsApiExportEnabled = dto.IsEnabled;
            store.ApiExportToken = dto.Token;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Ustawienia API zostały zapisane." });
        }

       





        [HttpGet]
        public async Task<IActionResult> GetScrapPriceChangeHistory(int storeId, int scrapHistoryId)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            if (scrapHistoryId == 0) return BadRequest("Invalid Scrap History ID.");

            var batches = await _context.PriceBridgeBatches
                .AsNoTracking()
                .Where(b => b.StoreId == storeId && b.ScrapHistoryId == scrapHistoryId)
                .Include(b => b.User)

                .Include(b => b.AutomationRule)
                .Include(b => b.BridgeItems)
                    .ThenInclude(i => i.Product)
                .OrderByDescending(b => b.ExecutionDate)
                .ToListAsync();

            var result = batches.Select(b => new
            {
                executionDate = b.ExecutionDate,

                userName = b.User?.UserName ?? (b.IsAutomation ? "Automat Cenowy" : "System/Nieznany"),

                isAutomation = b.IsAutomation,
                automationRuleName = b.AutomationRule?.Name,
                automationRuleColor = b.AutomationRule?.ColorHex,

                successfulCount = b.SuccessfulCount,
                exportMethod = b.ExportMethod.ToString(),
                items = b.BridgeItems.Select(i => new
                {
                    productId = i.ProductId,
                    productName = i.Product?.ProductName ?? "Produkt usunięty lub nieznany",
                    ean = i.Product?.Ean,
                    priceBefore = i.PriceBefore,
                    priceAfter_Verified = i.PriceAfter,
                    marginPrice = i.MarginPrice,
                    rankingGoogleBefore = i.RankingGoogleBefore,
                    rankingCeneoBefore = i.RankingCeneoBefore,
                    rankingGoogleAfter = i.RankingGoogleAfterSimulated,
                    rankingCeneoAfter = i.RankingCeneoAfterSimulated,
                    mode = i.Mode,
                    priceIndexTarget = i.PriceIndexTarget,
                    stepPriceApplied = i.StepPriceApplied,
                    success = i.Success
                }).ToList()
            }).ToList();

            return Ok(result);
        }
















        public class ExportMultiRequest
        {
            public List<int> ScrapIds { get; set; }
            public string ConnectionId { get; set; }
            public string ExportType { get; set; } // "prices" | "competition"
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

            // Competitor cheaper than us (we're losing)
            // Competitor cheaper than us (we're losing)
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

            // Competitor more expensive than us (we're winning)
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

            // Per brand: Dictionary<BrandName, (theyCheaper, theyExpensive, equal)>
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


        // ═══════════════════════════════════════════════════════════════════
        //  ENDPOINT: Lista dostępnych analiz
        // ═══════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> GetAvailableScraps(int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
                return Forbid();

            var scraps = await _context.ScrapHistories
                .AsNoTracking()
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(360)
                .Select(sh => new { sh.Id, sh.Date })
                .ToListAsync();

            if (!scraps.Any())
                return Json(new List<object>());

            var scrapIds = scraps.Select(s => s.Id).ToList();

            var priceCounts = await _context.PriceHistories
                .AsNoTracking()
                .Where(ph => scrapIds.Contains(ph.ScrapHistoryId))
                .GroupBy(ph => ph.ScrapHistoryId)
                .Select(g => new { ScrapId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ScrapId, x => x.Count);

            var result = scraps.Select(s => new
            {
                id = s.Id,
                date = s.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                priceCount = priceCounts.GetValueOrDefault(s.Id, 0)
            }).ToList();

            return Json(result);
        }


        // ═══════════════════════════════════════════════════════════════════
        //  ENDPOINT: Eksport multi-scrap (dane cenowe LUB raport konkurencji)
        // ═══════════════════════════════════════════════════════════════════

        [HttpPost]
        public async Task<IActionResult> ExportMultiScraps(int storeId, [FromBody] ExportMultiRequest request)
        {
            if (request?.ScrapIds == null || !request.ScrapIds.Any())
                return BadRequest("Nie wybrano żadnych analiz.");
            if (request.ScrapIds.Count > 12)
                return BadRequest("Maksymalnie 12 analiz.");
            if (!await UserHasAccessToStore(storeId))
                return Forbid();

            // ── Rate limit ──
            var now = DateTime.UtcNow;
            if (_exportCooldowns.TryGetValue(storeId, out var lastExport))
            {
                var remaining = ExportCooldown - (now - lastExport);
                if (remaining > TimeSpan.Zero)
                {
                    var secondsLeft = (int)Math.Ceiling(remaining.TotalSeconds);
                    return BadRequest($"Eksport będzie dostępny za {(secondsLeft > 60 ? $"{(int)Math.Ceiling(remaining.TotalMinutes)} min" : $"{secondsLeft} sek")}.");
                }
            }
            _exportCooldowns[storeId] = now;

            var connectionId = request.ConnectionId;
            var exportType = request.ExportType ?? "prices";

            // ── Dane wspólne ──
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
                return BadRequest("Nie znaleziono wybranych analiz.");

            // ── Excel ──
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

                // ── Pobierz surowe dane ──
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

            return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }


        // ═══════════════════════════════════════════════════════════════════
        //  HELPER: SignalR progress
        // ═══════════════════════════════════════════════════════════════════

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

            // Filtr presetowy
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


        // ═══════════════════════════════════════════════════════════════════
        //  EKSPORT CENOWY: Buduj wiersze
        // ═══════════════════════════════════════════════════════════════════

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

                    // Stock: nasze
                    bool? myGoogle = myOffer?.IsGoogle == true ? myOffer.GoogleInStock : null;
                    bool? myCeneo = myOffer != null && myOffer.IsGoogle == false ? myOffer.CeneoInStock : null;
                    // Jeśli mamy obie – nadpisz
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


        // ═══════════════════════════════════════════════════════════════════
        //  EKSPORT CENOWY: Zapisz arkusz
        // ═══════════════════════════════════════════════════════════════════

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


        // ═══════════════════════════════════════════════════════════════════
        //  RAPORT KONKURENCJI: Buduj dane
        // ═══════════════════════════════════════════════════════════════════

        private (List<CompetitorSummary> competitors, List<BrandSummary> brands) BuildCompetitionData(
            List<RawExportEntry> rawData, string myStoreNameLower)
        {
            // ── Grupuj po produkcie ──
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

                // ── Brand stats ──
                string brand = g.Key.Producer ?? "Brak producenta";
                decimal bestCompPrice = compEntries.Min(x => x.FinalPrice);
                decimal worstCompPrice = compEntries.Max(x => x.FinalPrice);
                bool isCheapest = myPrice <= bestCompPrice;
                bool isMostExpensive = myPrice >= worstCompPrice && compEntries.Count > 0;

                if (!brandStats.ContainsKey(brand)) brandStats[brand] = new();
                brandStats[brand].Add((myPrice, bestCompPrice, isCheapest, isMostExpensive));

                // ── Per-competitor analysis ──
                // Deduplikuj po StoreName (bierzemy najniższą cenę per sklep)
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

                    // diffPercent: how much cheaper competitor is (positive = they're cheaper = we're losing)
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
                    else if (diffPct > 0) // they're cheaper
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
                    else // they're more expensive (we're winning)
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

            // Oblicz średnią i medianę
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

            // ── Brand summaries ──
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


        // ═══════════════════════════════════════════════════════════════════
        //  RAPORT: Arkusz "Przegląd Konkurencji"
        // ═══════════════════════════════════════════════════════════════════

        private void WriteCompetitionOverviewSheet(XSSFWorkbook wb, string sheetName,
            List<CompetitorSummary> data, string scrapDate, string storeName, string presetName, ExportStyles s)
        {
            var sheet = wb.CreateSheet(sheetName);

            // ── Nagłówek informacyjny ──
            int r = 0;
            var infoRow = sheet.CreateRow(r++);
            var infoCell = infoRow.CreateCell(0);
            infoCell.SetCellValue($"Raport Konkurencji — {storeName} — Analiza: {scrapDate} — Preset: {presetName ?? "Domyślny"}");
            infoCell.CellStyle = s.InfoHeader;
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 9));

            r++; // pusty wiersz

            // ── Nagłówki tabeli ──
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

            // ── Dane ──
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

                // Pozycja cenowa — podsumowanie
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
            sheet.AddMergedRegion(new CellRangeAddress(0, 0, 0, 24)); // Poszerzamy o jedną kolumnę na 0%

            // ── Sub-header ──
            r++;
            var subRow = sheet.CreateRow(r++);

            var subCell1 = subRow.CreateCell(1);
            subCell1.SetCellValue("← KONKURENT TAŃSZY (masz wyższe ceny)");
            subCell1.CellStyle = s.SubHeaderRed;
            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 1, 11)); // Czerwone

            var subCellEq = subRow.CreateCell(12);
            subCellEq.SetCellValue("REMIS");
            subCellEq.CellStyle = s.SubHeaderBlue;// Nasz nowy żółty nagłówek na środku

            var subCell2 = subRow.CreateCell(13);
            subCell2.SetCellValue("KONKURENT DROŻSZY (masz niższe ceny) →");
            subCell2.CellStyle = s.SubHeaderGreen;
            sheet.AddMergedRegion(new CellRangeAddress(r - 1, r - 1, 13, 23)); // Zielone

            // ── Nagłówki ──
            var headerRow = sheet.CreateRow(r++);
            string[] cols = {
        "Sklep",
        // Czerwona sekcja
        ">50%", "45-50%", "40-45%", "35-40%", "30-35%", "25-30%", "20-25%", "15-20%", "10-15%", "5-10%", "0-5%",
        // Sekcja remisowa
        "0%", 
        // Zielona sekcja
        "0-5%", "5-10%", "10-15%", "15-20%", "20-25%", "25-30%", "30-35%", "35-40%", "40-45%", "45-50%", ">50%",
        "Wspólne"
    };

            for (int i = 0; i < cols.Length; i++)
            {
                var cell = headerRow.CreateCell(i);
                cell.SetCellValue(cols[i]);
                cell.CellStyle = s.HeaderDark;
            }

            // ── Dane ──
            foreach (var comp in data)
            {
                var row = sheet.CreateRow(r++);
                int c = 0;

                row.CreateCell(c++).SetCellValue(comp.StoreName);

                // Tańsi (czerwone)
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

                // REMIS / Równa cena (żółte)
                // REMIS / Równa cena (błękitne)
                SetDistCell(row.CreateCell(c++), comp.EqualCount, s.CellBlue); // <-- Zmiana tutaj

                // Drożsi (zielone)
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

            // ── Default ──
            s.Default = wb.CreateCellStyle();

            // ── Header (prosty, bold) ──
            s.Header = wb.CreateCellStyle();
            var hf = wb.CreateFont(); hf.IsBold = true; s.Header.SetFont(hf);

            // ── Header Dark (navy, biały tekst) ──
            s.HeaderDark = CreateColoredStyle(wb, new byte[] { 26, 39, 68 }, true, IndexedColors.White.Index);

            // ── Info Header ──
            s.InfoHeader = wb.CreateCellStyle();
            var infoFont = wb.CreateFont(); infoFont.IsBold = true; infoFont.FontHeightInPoints = 12;
            s.InfoHeader.SetFont(infoFont);

            // ── Sub-headers ──
            s.SubHeaderRed = CreateColoredStyle(wb, new byte[] { 220, 53, 69 }, true, IndexedColors.White.Index);
            s.SubHeaderGreen = CreateColoredStyle(wb, new byte[] { 40, 167, 69 }, true, IndexedColors.White.Index);

            // ── Currency ──
            s.Currency = wb.CreateCellStyle();
            s.Currency.DataFormat = df.GetFormat("#,##0.00");

            // ── Percent ──
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



            // ── Ceny z kolorami ──
            s.PriceGreen = wb.CreateCellStyle(); s.PriceGreen.CloneStyleFrom(s.Currency);
            s.PriceGreen.FillForegroundColor = IndexedColors.LightGreen.Index;
            s.PriceGreen.FillPattern = FillPattern.SolidForeground;

            s.PriceLightGreen = wb.CreateCellStyle(); s.PriceLightGreen.CloneStyleFrom(s.Currency);
            s.PriceLightGreen.FillForegroundColor = IndexedColors.LemonChiffon.Index;
            s.PriceLightGreen.FillPattern = FillPattern.SolidForeground;

            s.PriceRed = wb.CreateCellStyle(); s.PriceRed.CloneStyleFrom(s.Currency);
            s.PriceRed.FillForegroundColor = IndexedColors.Rose.Index;
            s.PriceRed.FillPattern = FillPattern.SolidForeground;

            // ── Tło komórek (raport) ──
            s.CellRedBg = CreateColoredStyle(wb, new byte[] { 248, 215, 218 }, false, 0);
            s.CellGreenBg = CreateColoredStyle(wb, new byte[] { 212, 237, 218 }, false, 0);

            // ── Gradient: czerwony (tańsi = my tracimy) co 5% ──
            s.CellRed1 = CreateColoredStyle(wb, new byte[] { 255, 240, 240 }, false, 0); // 0-5%
            s.CellRed2 = CreateColoredStyle(wb, new byte[] { 255, 220, 220 }, false, 0); // 5-10%
            s.CellRed3 = CreateColoredStyle(wb, new byte[] { 255, 200, 200 }, false, 0); // 10-15%
            s.CellRed4 = CreateColoredStyle(wb, new byte[] { 255, 180, 180 }, false, 0); // 15-20%
            s.CellRed5 = CreateColoredStyle(wb, new byte[] { 255, 160, 160 }, false, 0); // 20-25%
            s.CellRed6 = CreateColoredStyle(wb, new byte[] { 255, 140, 140 }, false, 0); // 25-30%
            s.CellRed7 = CreateColoredStyle(wb, new byte[] { 255, 115, 115 }, false, 0); // 30-35%
            s.CellRed8 = CreateColoredStyle(wb, new byte[] { 255, 90, 90 }, false, 0); // 35-40%
                                                                                       // Dla najciemniejszych dajemy białą czcionkę (IndexedColors.White.Index) żeby liczby były czytelne
            s.CellRed9 = CreateColoredStyle(wb, new byte[] { 240, 60, 60 }, false, IndexedColors.White.Index); // 40-45%
            s.CellRed10 = CreateColoredStyle(wb, new byte[] { 220, 40, 40 }, false, IndexedColors.White.Index); // 45-50%
            s.CellRed11 = CreateColoredStyle(wb, new byte[] { 190, 20, 20 }, false, IndexedColors.White.Index); // >50%

            // ── Gradient: zielony (drożsi = my wygrywamy) co 5% ──
            s.CellGreen1 = CreateColoredStyle(wb, new byte[] { 240, 255, 240 }, false, 0); // 0-5%
            s.CellGreen2 = CreateColoredStyle(wb, new byte[] { 220, 250, 220 }, false, 0); // 5-10%
            s.CellGreen3 = CreateColoredStyle(wb, new byte[] { 200, 240, 200 }, false, 0); // 10-15%
            s.CellGreen4 = CreateColoredStyle(wb, new byte[] { 180, 230, 180 }, false, 0); // 15-20%
            s.CellGreen5 = CreateColoredStyle(wb, new byte[] { 160, 220, 160 }, false, 0); // 20-25%
            s.CellGreen6 = CreateColoredStyle(wb, new byte[] { 140, 210, 140 }, false, 0); // 25-30%
            s.CellGreen7 = CreateColoredStyle(wb, new byte[] { 120, 200, 120 }, false, 0); // 30-35%
            s.CellGreen8 = CreateColoredStyle(wb, new byte[] { 100, 185, 100 }, false, 0); // 35-40%
                                                                                           // Biała czcionka dla ciemnej zieleni
            s.CellGreen9 = CreateColoredStyle(wb, new byte[] { 75, 170, 75 }, false, IndexedColors.White.Index); // 40-45%
            s.CellGreen10 = CreateColoredStyle(wb, new byte[] { 50, 150, 50 }, false, IndexedColors.White.Index); // 45-50%
            s.CellGreen11 = CreateColoredStyle(wb, new byte[] { 30, 130, 30 }, false, IndexedColors.White.Index); // >50%


            // ── Kolor dla Remisu / Równej ceny (Jasny błękit) ──
            s.SubHeaderBlue = CreateColoredStyle(wb, new byte[] { 100, 180, 255 }, true, IndexedColors.White.Index); // Głęboki błękit na nagłówek (biała czcionka)
            s.CellBlue = CreateColoredStyle(wb, new byte[] { 230, 245, 255 }, false, 0); // Bardzo delikatny, jasny błękit do komórek z danymi

            return s;
        }

        private ICellStyle CreateColoredStyle(XSSFWorkbook wb, byte[] rgb, bool bold, short fontColorIndex)
        {
            var style = (XSSFCellStyle)wb.CreateCellStyle();

            // ZMIANA: Tworzenie CT_Color zamiast bezpośredniego przekazywania byte[]
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