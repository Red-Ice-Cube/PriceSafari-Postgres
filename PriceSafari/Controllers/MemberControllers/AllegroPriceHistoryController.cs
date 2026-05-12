using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.DTOs;
using PriceSafari.Models.ViewModels;
using PriceSafari.Services.AllegroServices;
using PriceSafari.ViewModels;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    public class AllegroPriceHistoryController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly AllegroPriceBridgeService _priceBridgeService;
        private readonly ILogger<AllegroPriceHistoryController> _logger;

        public AllegroPriceHistoryController(
            PriceSafariContext context,
            UserManager<PriceSafariUser> userManager,
            AllegroPriceBridgeService priceBridgeService,
            ILogger<AllegroPriceHistoryController> logger)

        {
            _context = context;
            _userManager = userManager;
            _priceBridgeService = priceBridgeService;
            _logger = logger;
        }
        private async Task<bool> UserHasAccessToStore(int storeId)
        {
           
            if (User.IsInRole("Admin") || User.IsInRole("Manager")) return true;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    
            return await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
        }


        private async Task<bool> CurrentUserUsesProducerView()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return false;

            return await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.UseProducerViewForMarketplace)
                .FirstOrDefaultAsync();
        }

        public async Task<IActionResult> Index(int? storeId)
        {
            if (storeId == null) return BadRequest("Store ID is required.");
            if (!await UserHasAccessToStore(storeId.Value)) return Forbid();

            var store = await _context.Stores.FindAsync(storeId.Value);
            if (store == null) return NotFound("Store not found.");

            var latestScrap = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .FirstOrDefaultAsync();

            var flags = await _context.Flags
                .Where(f => f.StoreId == storeId.Value && f.IsMarketplace == true)
                .Select(f => new FlagViewModel
                {
                    FlagId = f.FlagId,
                    FlagName = f.FlagName,
                    FlagColor = f.FlagColor,
                    IsMarketplace = f.IsMarketplace
                })
                .ToListAsync();

            bool useProducerView = await CurrentUserUsesProducerView();

            ViewBag.StoreId = store.StoreId;
            ViewBag.StoreName = store.StoreNameAllegro;
            ViewBag.StoreLogo = store.StoreLogoUrl;
            ViewBag.LatestScrap = latestScrap;
            ViewBag.Flags = flags;
            ViewBag.IsAllegroPriceBridgeActive = store.IsAllegroPriceBridgeActive;
            ViewBag.IsProducerOnAllegro = useProducerView;   

            if (useProducerView)  
                return View("~/Views/Panel/AllegroPriceHistory/IndexProducer.cshtml");

            return View("~/Views/Panel/AllegroPriceHistory/Index.cshtml");

        }

        //aktualizacja id w bazie dla allegro

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetDuplicateProductsByIdOnAllegro()
        {

            var duplicateKeys = await _context.AllegroProducts
                .AsNoTracking()
                .Where(p => p.IdOnAllegro != null)
                .GroupBy(p => p.IdOnAllegro)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToListAsync();

            if (!duplicateKeys.Any())
            {
                return Ok("Brak produktów ze zduplikowanym IdOnAllegro.");
            }

            var products = await _context.AllegroProducts
                .AsNoTracking()
                .Include(p => p.Store)

                .Where(p => duplicateKeys.Contains(p.IdOnAllegro))
                .ToListAsync();

            var productIds = products.Select(p => p.AllegroProductId).Distinct().ToList();

            var pricesRaw = await _context.AllegroPriceHistories
                .AsNoTracking()
                .Include(ph => ph.AllegroScrapeHistory)
                .Where(ph => productIds.Contains(ph.AllegroProductId))
                .Select(ph => new
                {
                    ph.AllegroProductId,
                    ph.Price,
                    Date = ph.AllegroScrapeHistory.Date,
                    ph.SellerName

                })
                .ToListAsync();

            var latestPricesDict = pricesRaw
                .GroupBy(x => x.AllegroProductId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.Date).Select(x => x.Price).FirstOrDefault()
                );

            var result = products
                .GroupBy(p => p.IdOnAllegro)
                .Select(g => new
                {
                    CommonIdOnAllegro = g.Key,
                    DuplicateCount = g.Count(),
                    Products = g.Select(p => new
                    {
                        InternalDbId = p.AllegroProductId,
                        StoreName = p.Store?.StoreNameAllegro ?? "Nieznany sklep",
                        ProductName = p.AllegroProductName,
                        Ean = p.AllegroEan,
                        Url = p.AllegroOfferUrl,
                        LatestScrapedPrice = latestPricesDict.ContainsKey(p.AllegroProductId)
                            ? latestPricesDict[p.AllegroProductId]
                            : (decimal?)null
                    }).ToList()
                })
                .ToList();

            return Json(result);
        }









        [HttpGet]
        public async Task<IActionResult> GetAllegroPrices(int? storeId)
        {
            if (storeId == null) return BadRequest();
            if (!await UserHasAccessToStore(storeId.Value)) return Forbid();

            var store = await _context.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.StoreId == storeId.Value);
            var priceSettings = await _context.PriceValues
                .AsNoTracking()
                .FirstOrDefaultAsync(pv => pv.StoreId == storeId.Value);

            var latestScrap = await _context.AllegroScrapeHistories
                .AsNoTracking()
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .FirstOrDefaultAsync();

            if (latestScrap == null || store == null)
            {
                return Json(new { myStoreName = store?.StoreNameAllegro, prices = new List<object>() });
            }

            // ── 1. Załaduj WSZYSTKIE aktywne produkty NAJPIERW ──
            var allScrapableProducts = await _context.AllegroProducts
                .AsNoTracking()
                .Where(p => p.StoreId == storeId.Value && p.IsScrapable)
                .ToListAsync();

            var productIds = allScrapableProducts.Select(p => p.AllegroProductId).ToList();
            var productDictionary = allScrapableProducts.ToDictionary(p => p.AllegroProductId);

            // ── 2. Historia cen BEZ Include (produkt ze słownika) ──
            var priceData = await _context.AllegroPriceHistories
                .AsNoTracking()
                .Where(aph => aph.AllegroScrapeHistoryId == latestScrap.Id
                            && productIds.Contains(aph.AllegroProductId))
                .ToListAsync();

            // ── Deduplikacja: jeden rekord per produkt + oferta ──
            priceData = priceData
                .GroupBy(aph => new { aph.AllegroProductId, aph.IdAllegro })
                .Select(g => g.First())
                .ToList();

            // ── 3. Committed changes — ograniczone do aktywnych produktów ──
            var committedChanges = await _context.AllegroPriceBridgeItems
                .AsNoTracking()
                .Include(i => i.PriceBridgeBatch)
                .Where(i => i.PriceBridgeBatch.StoreId == storeId.Value
                         && i.PriceBridgeBatch.AllegroScrapeHistoryId == latestScrap.Id
                         && i.Success
                         && productIds.Contains(i.AllegroProductId))
                .ToListAsync();

            var committedLookup = committedChanges
                .GroupBy(i => i.AllegroProductId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.PriceBridgeBatch.ExecutionDate).First());

            // ── 4. Preset konkurencji ──
            var activePreset = await _context.CompetitorPresets
                .AsNoTracking()
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.StoreId == storeId.Value && p.NowInUse && p.Type == PresetType.Marketplace);

            string activePresetName = activePreset?.PresetName;

            var competitorRules = activePreset?.CompetitorItems
                .Where(ci => ci.DataSource == DataSourceType.Allegro)
                .ToDictionary(
                    ci => ci.StoreName.ToLower().Trim(),
                    ci => ci.UseCompetitor
                );

            // ── 5. Flagi — ograniczone do aktywnych produktów ──
            var productFlagsDictionary = await _context.ProductFlags
                .AsNoTracking()
                .Where(pf => productIds.Contains(pf.AllegroProductId.Value))
                .GroupBy(pf => pf.AllegroProductId.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            // ── 6. Automatyzacja — ograniczona do aktywnych produktów ──
            var automationLookup = await _context.AutomationProductAssignments
                .AsNoTracking()
                .Include(a => a.AutomationRule)
                .Where(a => a.AutomationRule.StoreId == storeId.Value
                         && a.AllegroProductId != null
                         && a.AutomationRule.SourceType == AutomationSourceType.Marketplace)
                .Select(a => new
                {
                    AllegroProductId = a.AllegroProductId.Value,
                    RuleName = a.AutomationRule.Name,
                    RuleColor = a.AutomationRule.ColorHex,
                    IsActive = a.AutomationRule.IsActive,
                    RuleId = a.AutomationRule.Id,
                    IsTimeLimited = a.AutomationRule.IsTimeLimited,
                    StartDate = a.AutomationRule.ScheduledStartDate,
                    EndDate = a.AutomationRule.ScheduledEndDate
                })
                .ToDictionaryAsync(a => a.AllegroProductId);

            // ── 7. Extended info — ograniczone do aktywnych produktów ──
            var allExtendedInfo = await _context.AllegroPriceHistoryExtendedInfos
                .AsNoTracking()
                .Where(e => e.ScrapHistoryId == latestScrap.Id
                         && productIds.Contains(e.AllegroProductId))
                .ToListAsync();

            var extendedInfoDictionary = allExtendedInfo
                .GroupBy(e => e.AllegroProductId)
                .ToDictionary(g => g.Key, g => g.First());

            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

            // ── 8. Grupowanie po AllegroProductId (produkt ze słownika) ──
            var groupedData = priceData
                .GroupBy(aph => aph.AllegroProductId)
                .Select(g =>
                {
                    var product = productDictionary[g.Key];

                    long? targetOfferId = null;
                    if (long.TryParse(product.IdOnAllegro, out var parsedId))
                    {
                        targetOfferId = parsedId;
                    }

                    var myOffer = targetOfferId.HasValue
                        ? g.FirstOrDefault(p => p.IdAllegro == targetOfferId.Value && p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                        : g.FirstOrDefault(p => p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase));

                    List<AllegroPriceHistory> filteredCompetitors;

                    bool includeNoDelivery = activePreset?.IncludeNoDeliveryInfo ?? true;
                    int minDelivery = activePreset?.MinDeliveryDays ?? 0;
                    int maxDelivery = activePreset?.MaxDeliveryDays ?? 31;

                    if (activePreset != null && competitorRules != null)
                    {
                        filteredCompetitors = g.Where(p =>
                        {
                            if (p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
                            }

                            if (p.DeliveryTime.HasValue)
                            {
                                if (p.DeliveryTime.Value < minDelivery || p.DeliveryTime.Value > maxDelivery)
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                if (!includeNoDelivery)
                                {
                                    return false;
                                }
                            }

                            var sellerNameLower = (p.SellerName ?? "").ToLower().Trim();
                            if (competitorRules.TryGetValue(sellerNameLower, out bool useCompetitor))
                            {
                                return useCompetitor;
                            }
                            return activePreset.UseUnmarkedStores;
                        }).ToList();
                    }
                    else
                    {
                        filteredCompetitors = g.Where(p => !p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase)).ToList();
                    }

                    var bestCompetitor = filteredCompetitors.OrderBy(p => p.Price).FirstOrDefault();

                    decimal? marketAveragePrice = null;
                    decimal? marketPriceIndex = null;
                    string marketBucket = "market-neutral";

                    var competitorPricesOnly = filteredCompetitors
                        .Where(x => x.Price > 0)
                        .Select(x => x.Price)
                        .ToList();

                    if (competitorPricesOnly.Count > 0)
                    {
                        marketAveragePrice = CalculateMedian(competitorPricesOnly);

                        if (marketAveragePrice.HasValue && myOffer != null && myOffer.Price > 0 && marketAveragePrice.Value > 0)
                        {
                            decimal diff = myOffer.Price - marketAveragePrice.Value;
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
                    else if (myOffer != null)
                    {
                        marketBucket = "market-solo";
                    }

                    var allMyOffersInGroup = g.Where(p => p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase));
                    var myOfferIdsList = allMyOffersInGroup.Select(o => o.IdAllegro).ToList();

                    if (targetOfferId.HasValue && !myOfferIdsList.Contains(targetOfferId.Value))
                    {
                        myOfferIdsList.Add(targetOfferId.Value);
                    }

                    var myOffersGroupKey = string.Join(",", myOfferIdsList.OrderBy(id => id));

                    var totalPopularity = g.Sum(p => p.Popularity ?? 0);
                    var myPopularity = g.Where(p => p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                        .Sum(p => p.Popularity ?? 0);

                    var marketSharePercentage = (totalPopularity > 0) ? Math.Round(((decimal)myPopularity / totalPopularity) * 100, 2) : 0;

                    var visibleSellers = new HashSet<string>(filteredCompetitors.Select(c => c.SellerName));
                    if (myOffer != null)
                    {
                        visibleSellers.Add(myOffer.SellerName);
                    }
                    var visibleOfferCount = filteredCompetitors.Count() + (myOffer != null ? 1 : 0);

                    var allVisibleOffers = new List<AllegroPriceHistory>(filteredCompetitors);
                    if (myOffer != null)
                    {
                        allVisibleOffers.Add(myOffer);
                    }

                    var sortedOffers = allVisibleOffers.OrderBy(p => p.Price).ToList();

                    string myPricePosition = null;
                    if (myOffer != null)
                    {
                        int zeroBasedIndex = sortedOffers.FindIndex(p => p.IdAllegro == myOffer.IdAllegro);

                        if (zeroBasedIndex != -1)
                        {
                            int myRank = zeroBasedIndex + 1;
                            myPricePosition = $"{myRank}/{visibleOfferCount}";
                        }
                    }

                    var extendedInfo = extendedInfoDictionary.GetValueOrDefault(product.AllegroProductId);
                    var committed = committedLookup.GetValueOrDefault(product.AllegroProductId);
                    var autoRule = automationLookup.GetValueOrDefault(product.AllegroProductId);

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

                    return new
                    {
                        ProductId = product.AllegroProductId,
                        ProductName = product.AllegroProductName,
                        Producer = product.Producer,
                        MyPrice = myOffer?.Price,
                        LowestPrice = bestCompetitor?.Price,
                        StoreName = bestCompetitor?.SellerName,
                        StoreCount = visibleSellers.Count,
                        TotalOfferCount = visibleOfferCount,
                        MyPricePosition = myPricePosition,
                        TotalPopularity = totalPopularity,
                        MyTotalPopularity = myPopularity,
                        MarketSharePercentage = marketSharePercentage,
                        DeliveryTime = bestCompetitor?.DeliveryTime,
                        IsSuperSeller = bestCompetitor?.SuperSeller ?? false,
                        IsSmart = bestCompetitor?.Smart ?? false,
                        IsBestPriceGuarantee = bestCompetitor?.IsBestPriceGuarantee ?? false,
                        IsTopOffer = bestCompetitor?.TopOffer ?? false,
                        IsSuperPrice = bestCompetitor?.SuperPrice ?? false,
                        IsPromoted = bestCompetitor?.Promoted ?? false,
                        IsSponsored = bestCompetitor?.Sponsored ?? false,
                        MyIdAllegro = myOffer?.IdAllegro ?? (targetOfferId.HasValue ? targetOfferId : null),
                        MyOffersGroupKey = myOffersGroupKey,
                        MyDeliveryTime = myOffer?.DeliveryTime,
                        MyIsSuperSeller = myOffer?.SuperSeller ?? false,
                        IsNew = product.AddedDate >= sevenDaysAgo,
                        MyIsSmart = myOffer?.Smart ?? false,
                        MyIsBestPriceGuarantee = myOffer?.IsBestPriceGuarantee ?? false,
                        MyIsTopOffer = myOffer?.TopOffer ?? false,
                        MyIsSuperPrice = myOffer?.SuperPrice ?? false,
                        MyIsPromoted = myOffer?.Promoted ?? false,
                        MyIsSponsored = myOffer?.Sponsored ?? false,
                        IsRejected = product.IsRejected,
                        OnlyMe = (myOffer != null && !filteredCompetitors.Any()),
                        Savings = (myOffer != null && bestCompetitor != null && myOffer.Price < bestCompetitor.Price) ? bestCompetitor.Price - myOffer.Price : (decimal?)null,
                        PriceDifference = (myOffer != null && bestCompetitor != null) ? myOffer.Price - bestCompetitor.Price : (decimal?)null,
                        PercentageDifference = (myOffer != null && bestCompetitor != null && bestCompetitor.Price > 0) ? Math.Round(((myOffer.Price - bestCompetitor.Price) / bestCompetitor.Price) * 100, 2) : (decimal?)null,
                        IsUniqueBestPrice = (myOffer != null && bestCompetitor != null && myOffer.Price < bestCompetitor.Price),
                        IsSharedBestPrice = (myOffer != null && bestCompetitor != null && myOffer.Price == bestCompetitor.Price),
                        FlagIds = productFlagsDictionary.GetValueOrDefault(product.AllegroProductId, new List<int>()),
                        Ean = product.AllegroEan,
                        AllegroSku = product.AllegroSku,
                        ExternalId = (int?)null,
                        MarginPrice = product.AllegroMarginPrice,
                        MarketAveragePrice = marketAveragePrice,
                        MarketPriceIndex = marketPriceIndex,
                        MarketBucket = marketBucket,
                        ImgUrl = (string)null,
                        ApiAllegroPrice = extendedInfo?.ApiAllegroPrice,
                        ApiAllegroPriceFromUser = extendedInfo?.ApiAllegroPriceFromUser,
                        ApiAllegroCommission = extendedInfo?.ApiAllegroCommission,
                        AnyPromoActive = extendedInfo?.AnyPromoActive,
                        IsSubsidyActive = extendedInfo?.IsSubsidyActive,
                        AutomationRuleName = autoRule?.RuleName,
                        AutomationRuleColor = autoRule?.RuleColor,
                        IsAutomationActive = autoRule?.IsActive,
                        AutomationRuleId = autoRule?.RuleId,
                        IsAutomationPaused = isAutomationPaused,
                        Committed = committed == null ? null : new
                        {
                            NewPrice = committed.PriceAfter_Verified ?? committed.PriceAfter_Simulated,
                            NewCommission = committed.CommissionAfter_Verified ?? (committed.IncludeCommissionInMargin ? (decimal?)null : null),
                            NewPosition = committed.RankingAfter_Simulated
                        }
                    };
                }).Cast<object>().ToList();

            // ── 9. Produkty aktywne, ale bez historii cen w ostatnim scrapie ──
            var coveredProductIds = new HashSet<int>(
                priceData.Select(p => p.AllegroProductId).Distinct());

            var missingProducts = allScrapableProducts
                .Where(p => !coveredProductIds.Contains(p.AllegroProductId))
                .ToList();

            if (missingProducts.Any())
            {
                var missingEntries = missingProducts.Select(product =>
                {
                    long? targetOfferId = null;
                    if (long.TryParse(product.IdOnAllegro, out var pid))
                        targetOfferId = pid;

                    var autoRule = automationLookup.GetValueOrDefault(product.AllegroProductId);
                    bool isAutoPaused = false;
                    if (autoRule != null && autoRule.IsActive && autoRule.IsTimeLimited)
                    {
                        var today = DateTime.Today;
                        if ((autoRule.StartDate.HasValue && today < autoRule.StartDate.Value.Date) ||
                            (autoRule.EndDate.HasValue && today > autoRule.EndDate.Value.Date))
                            isAutoPaused = true;
                    }

                    return (object)new
                    {
                        ProductId = product.AllegroProductId,
                        ProductName = product.AllegroProductName,
                        Producer = product.Producer,
                        MyPrice = (decimal?)null,
                        LowestPrice = (decimal?)null,
                        StoreName = (string)null,
                        StoreCount = 0,
                        TotalOfferCount = 0,
                        MyPricePosition = (string)null,
                        TotalPopularity = 0,
                        MyTotalPopularity = 0,
                        MarketSharePercentage = 0m,
                        DeliveryTime = (int?)null,
                        IsSuperSeller = false,
                        IsSmart = false,
                        IsBestPriceGuarantee = false,
                        IsTopOffer = false,
                        IsSuperPrice = false,
                        IsPromoted = false,
                        IsSponsored = false,
                        MyIdAllegro = targetOfferId,
                        MyOffersGroupKey = targetOfferId.HasValue ? targetOfferId.Value.ToString() : "",
                        MyDeliveryTime = (int?)null,
                        MyIsSuperSeller = false,
                        IsNew = product.AddedDate >= sevenDaysAgo,
                        MyIsSmart = false,
                        MyIsBestPriceGuarantee = false,
                        MyIsTopOffer = false,
                        MyIsSuperPrice = false,
                        MyIsPromoted = false,
                        MyIsSponsored = false,
                        IsRejected = product.IsRejected,
                        OnlyMe = false,
                        Savings = (decimal?)null,
                        PriceDifference = (decimal?)null,
                        PercentageDifference = (decimal?)null,
                        IsUniqueBestPrice = false,
                        IsSharedBestPrice = false,
                        FlagIds = productFlagsDictionary.GetValueOrDefault(product.AllegroProductId, new List<int>()),
                        Ean = product.AllegroEan,
                        AllegroSku = product.AllegroSku,
                        ExternalId = (int?)null,
                        MarginPrice = product.AllegroMarginPrice,
                        MarketAveragePrice = (decimal?)null,
                        MarketPriceIndex = (decimal?)null,
                        MarketBucket = "market-neutral",
                        ImgUrl = (string)null,
                        ApiAllegroPrice = (decimal?)null,
                        ApiAllegroPriceFromUser = (decimal?)null,
                        ApiAllegroCommission = (decimal?)null,
                        AnyPromoActive = (bool?)null,
                        IsSubsidyActive = (bool?)null,
                        AutomationRuleName = autoRule?.RuleName,
                        AutomationRuleColor = autoRule?.RuleColor,
                        IsAutomationActive = autoRule?.IsActive,
                        AutomationRuleId = autoRule?.RuleId,
                        IsAutomationPaused = isAutoPaused,
                        Committed = (object)null
                    };
                }).ToList();

                groupedData.AddRange(missingEntries);
            }

            return Json(new
            {
                myStoreName = store.StoreNameAllegro,
                prices = groupedData,
                priceCount = groupedData.Count,
                setPrice1 = priceSettings?.AllegroSetPrice1 ?? 2.00m,
                setPrice2 = priceSettings?.AllegroSetPrice2 ?? 2.00m,
                stepPrice = priceSettings?.AllegroPriceStep ?? 2.00m,
                usePriceDifference = priceSettings?.AllegroUsePriceDiff ?? true,
                allegroPriceIndexTarget = priceSettings?.AllegroPriceIndexTargetPercent ?? 100.00m,
                presetName = activePresetName ?? "PriceSafari",
                latestScrapId = latestScrap?.Id,
                allegroIdentifierForSimulation = priceSettings?.AllegroIdentifierForSimulation ?? "EAN",
                allegroUseMarginForSimulation = priceSettings?.AllegroUseMarginForSimulation ?? true,
                allegroEnforceMinimalMargin = priceSettings?.AllegroEnforceMinimalMargin ?? true,
                allegroMinimalMarginPercent = priceSettings?.AllegroMinimalMarginPercent ?? 0.00m,
                allegroIncludeCommisionInPriceChange = priceSettings?.AllegroIncludeCommisionInPriceChange ?? false,
                allegroChangePriceForBagdeSuperPrice = priceSettings?.AllegroChangePriceForBagdeSuperPrice ?? false,
                allegroChangePriceForBagdeTopOffer = priceSettings?.AllegroChangePriceForBagdeTopOffer ?? false,
                allegroChangePriceForBagdeBestPriceGuarantee = priceSettings?.AllegroChangePriceForBagdeBestPriceGuarantee ?? false,
                allegroChangePriceForBagdeInCampaign = priceSettings?.AllegroChangePriceForBagdeInCampaign ?? false
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetAllegroPricesForProducer(int? storeId)
        {
            var swTotal = System.Diagnostics.Stopwatch.StartNew();

            if (storeId == null) return BadRequest();
            if (!await UserHasAccessToStore(storeId.Value)) return Forbid();

            var store = await _context.Stores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.StoreId == storeId.Value);
            if (store == null) return NotFound();

            var storeNameLower = (store.StoreNameAllegro ?? "").ToLower().Trim();

            var latestScrap = await _context.AllegroScrapeHistories.AsNoTracking()
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            if (latestScrap == null)
                return Json(new { productCount = 0, myStoreName = store.StoreNameAllegro, prices = new List<object>() });

            // ─── Ustawienia producenta — TYLKO ścieżka Allegro ───
            var pv = await _context.PriceValues.AsNoTracking()
                .FirstOrDefaultAsync(x => x.StoreId == storeId.Value);

            var settings = pv != null ? new AllegroProducerSettings
            {
                IdentifierForSimulation = pv.AllegroIdentifierForSimulation ?? "EAN",
                ComparisonSource = pv.AllegroProducerComparisonSource,
                UseAmount = pv.AllegroProducerUseAmount,
                RedDarkPct = pv.AllegroProducerThresholdRedDarkPercent,
                RedPct = pv.AllegroProducerThresholdRedPercent,
                RedLightPct = pv.AllegroProducerThresholdRedLightPercent,
                GreenLightPct = pv.AllegroProducerThresholdGreenLightPercent,
                GreenPct = pv.AllegroProducerThresholdGreenPercent,
                GreenDarkPct = pv.AllegroProducerThresholdGreenDarkPercent,
                RedDarkAmt = pv.AllegroProducerThresholdRedDarkAmount,
                RedAmt = pv.AllegroProducerThresholdRedAmount,
                RedLightAmt = pv.AllegroProducerThresholdRedLightAmount,
                GreenLightAmt = pv.AllegroProducerThresholdGreenLightAmount,
                GreenAmt = pv.AllegroProducerThresholdGreenAmount,
                GreenDarkAmt = pv.AllegroProducerThresholdGreenDarkAmount
            } : new AllegroProducerSettings();

            var allScrapableProducts = await _context.AllegroProducts.AsNoTracking()
                .Where(p => p.StoreId == storeId.Value && p.IsScrapable)
                .ToListAsync();

            var productIds = allScrapableProducts.Select(p => p.AllegroProductId).ToList();
            var productDict = allScrapableProducts.ToDictionary(p => p.AllegroProductId);

            // ─── Dane bieżącego scrapu + deduplikacja per (productId, offerId) ───
            var priceData = await _context.AllegroPriceHistories.AsNoTracking()
                .Where(aph => aph.AllegroScrapeHistoryId == latestScrap.Id
                           && productIds.Contains(aph.AllegroProductId))
                .ToListAsync();

            priceData = priceData
                .GroupBy(aph => new { aph.AllegroProductId, aph.IdAllegro })
                .Select(g => g.First())
                .ToList();

            var offersByProductId = priceData
                .GroupBy(aph => aph.AllegroProductId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var activePreset = await _context.CompetitorPresets.AsNoTracking()
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.StoreId == storeId.Value && p.NowInUse && p.Type == PresetType.Marketplace);

            var competitorRules = activePreset?.CompetitorItems
                .Where(ci => ci.DataSource == DataSourceType.Allegro)
                .ToDictionary(ci => ci.StoreName.ToLower().Trim(), ci => ci.UseCompetitor);

            bool includeNoDelivery = activePreset?.IncludeNoDeliveryInfo ?? true;
            int minDelivery = activePreset?.MinDeliveryDays ?? 0;
            int maxDelivery = activePreset?.MaxDeliveryDays ?? 31;

            // Extended info — dwa lookupy: po (productId, offerId) i fallback per produkt
            var allExtInfo = await _context.AllegroPriceHistoryExtendedInfos.AsNoTracking()
                .Where(e => e.ScrapHistoryId == latestScrap.Id && productIds.Contains(e.AllegroProductId))
                .ToListAsync();

            var extInfoByProduct = allExtInfo
                .GroupBy(e => e.AllegroProductId)
                .ToDictionary(g => g.Key, g => g.First());

            var extInfoByProductAndOffer = allExtInfo
                .Where(e => e.IdAllegro.HasValue)
                .GroupBy(e => (e.AllegroProductId, e.IdAllegro.Value))
                .ToDictionary(g => g.Key, g => g.First());

            var productFlagsDict = await _context.ProductFlags.AsNoTracking()
                .Where(pf => pf.AllegroProductId.HasValue && productIds.Contains(pf.AllegroProductId.Value))
                .GroupBy(pf => pf.AllegroProductId.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            // ═══ Okno historii ═══
            var windowStart = latestScrap.Date.AddDays(-7);
            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7); // osobno — tylko dla flagi IsNew

            var historyScraps = await _context.AllegroScrapeHistories.AsNoTracking()
                .Where(sh => sh.StoreId == storeId && sh.Date >= windowStart && sh.Id != latestScrap.Id)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .ToListAsync();
            var historyScrapIds = historyScraps.Select(s => s.Id).ToList();

            var historyPricesRaw = historyScrapIds.Any()
                ? await _context.AllegroPriceHistories.AsNoTracking()
                    .Where(ph => historyScrapIds.Contains(ph.AllegroScrapeHistoryId)
                              && productIds.Contains(ph.AllegroProductId)
                              && ph.Price > 0)
                    .Select(ph => new {
                        ph.AllegroProductId,
                        ph.AllegroScrapeHistoryId,
                        ph.IdAllegro,
                        ph.Price,
                        ph.SellerName,
                        ph.DeliveryTime
                    })
                    .ToListAsync()
                : new List<dynamic>().Select(x => new { AllegroProductId = 0, AllegroScrapeHistoryId = 0, IdAllegro = 0L, Price = 0m, SellerName = "", DeliveryTime = (int?)null }).ToList();

            var historyMapSnapshots = historyScrapIds.Any()
                ? await _context.AllegroPriceHistoryExtendedInfos.AsNoTracking()
                    .Where(e => historyScrapIds.Contains(e.ScrapHistoryId)
                             && productIds.Contains(e.AllegroProductId)
                             && e.AllegroMapPriceSnapshot.HasValue)
                    .Select(e => new { e.AllegroProductId, e.ScrapHistoryId, e.AllegroMapPriceSnapshot })
                    .ToListAsync()
                : new List<dynamic>().Select(x => new { AllegroProductId = 0, ScrapHistoryId = 0, AllegroMapPriceSnapshot = (decimal?)null }).ToList();

            // GroupBy — chroni przed duplikatami klucza gdy producent ma >1 ofertę
            var historyMapSnapshotLookup = historyMapSnapshots
                .GroupBy(e => (e.AllegroProductId, e.ScrapHistoryId))
                .ToDictionary(g => g.Key, g => g.First().AllegroMapPriceSnapshot);


            var historyMyOfferPricesByProductOfferScrap = historyPricesRaw
                .Where(x => x.SellerName != null && x.SellerName.ToLower().Trim() == storeNameLower)
                .GroupBy(x => new { x.AllegroProductId, x.IdAllegro, x.AllegroScrapeHistoryId })
                .ToDictionary(
                    g => (g.Key.AllegroProductId, g.Key.IdAllegro, g.Key.AllegroScrapeHistoryId),
                    g => g.OrderBy(x => x.Price).First().Price
                );
            // Min cena konkurencji per scrap, po filtrze presetu — bez naszych ofert
            var historyMinCompetitorByProductScrap = new Dictionary<(int, int), decimal>();
            foreach (var ph in historyPricesRaw)
            {
                if (ph.SellerName != null && ph.SellerName.ToLower().Trim() == storeNameLower) continue;

                // Filtry presetu — TYLKO gdy preset istnieje
                if (activePreset != null)
                {
                    if (ph.DeliveryTime.HasValue)
                    {
                        if (ph.DeliveryTime.Value < minDelivery || ph.DeliveryTime.Value > maxDelivery) continue;
                    }
                    else
                    {
                        if (!includeNoDelivery) continue;
                    }

                    if (competitorRules != null)
                    {
                        var key = (ph.SellerName ?? "").ToLower().Trim();
                        if (competitorRules.TryGetValue(key, out bool use))
                        {
                            if (!use) continue;
                        }
                        else if (!activePreset.UseUnmarkedStores) continue;
                    }
                }

                var dictKey = (ph.AllegroProductId, ph.AllegroScrapeHistoryId);
                if (!historyMinCompetitorByProductScrap.TryGetValue(dictKey, out var existing) || ph.Price < existing)
                    historyMinCompetitorByProductScrap[dictKey] = ph.Price;
            }
            decimal redLightThreshold = settings.UseAmount ? settings.RedLightAmt : settings.RedLightPct;
            decimal greenLightThreshold = settings.UseAmount ? settings.GreenLightAmt : settings.GreenLightPct;

            // true  → konkurent łamie próg
            // false → mamy dane i NIE łamie  (zrywa sekwencję)
            // null  → brak danych dla tego scrapu (nieudany scrap / brak naszej oferty) — pomiń
            bool? WasViolatingAtScrap(int productId, long? targetOfferId, int scrapId)
            {
                decimal? historicalReference;
                if (settings.ComparisonSource == ProducerComparisonSource.StorePrice)
                {
                    if (targetOfferId.HasValue &&
                        historyMyOfferPricesByProductOfferScrap.TryGetValue(
                            (productId, targetOfferId.Value, scrapId), out var offerPrice))
                    {
                        historicalReference = offerPrice;
                    }
                    else
                    {
                        return null; // naszej oferty nie było w tym scrapie → nie wiemy
                    }
                }
                else // MapPrice
                {
                    if (historyMapSnapshotLookup.TryGetValue((productId, scrapId), out var histMap)
                        && histMap.HasValue && histMap.Value > 0)
                    {
                        historicalReference = histMap;
                    }
                    else
                    {
                        return null; // brak snapshotu MAP → nie wiemy
                    }
                }

                if (!historicalReference.HasValue || historicalReference.Value <= 0)
                    return null;

                if (!historyMinCompetitorByProductScrap.TryGetValue((productId, scrapId), out var minComp))
                {
                    // mamy referencję, ale żaden konkurent nie przeszedł filtra presetu
                    // — to twarde "brak naruszenia"
                    return false;
                }

                decimal hDelta = minComp - historicalReference.Value;
                decimal hDiff = settings.UseAmount
                    ? hDelta
                    : (hDelta / historicalReference.Value) * 100m;

                return hDiff <= -redLightThreshold;
            }

            var allPrices = productIds.Select(currentProductId =>
            {
                var product = productDict[currentProductId];
                var offers = offersByProductId.GetValueOrDefault(currentProductId, new List<AllegroPriceHistory>());

                // ═════════════════════════════════════════════════════════════
                //  KAŻDY rekord = jeden konkretny produkt z AllegroProducts.
                //  Jeśli producent ma w katalogu 2 oferty, ma 2 wpisy w AllegroProducts
                //  (z różnymi IdOnAllegro) → 2 rekordy w odpowiedzi.
                //  Frontend grupuje je przez MyOffersGroupKey i wybiera lidera.
                // ═════════════════════════════════════════════════════════════

                long? targetOfferId = null;
                if (long.TryParse(product.IdOnAllegro, out var parsedId))
                    targetOfferId = parsedId;

                // Reprezentatywna oferta TEGO REKORDU = oferta o targetOfferId,
                // fallback: pierwsza nasza oferta w katalogu (deterministycznie po IdAllegro)
                var myOffer = targetOfferId.HasValue
                    ? offers.FirstOrDefault(p => p.IdAllegro == targetOfferId.Value
                                               && p.SellerName != null
                                               && p.SellerName.ToLower().Trim() == storeNameLower)
                    : null;

                if (myOffer == null)
                {
                    myOffer = offers
                        .Where(p => p.SellerName != null && p.SellerName.ToLower().Trim() == storeNameLower)
                        .OrderBy(p => p.IdAllegro)
                        .FirstOrDefault();
                }

                // Wszystkie nasze oferty w tym katalogu — do MyOffersGroupKey + sumy popularity
                var allMyOffersInGroup = offers
                    .Where(p => p.SellerName != null && p.SellerName.ToLower().Trim() == storeNameLower)
                    .ToList();

                var myOfferIdsList = allMyOffersInGroup.Select(o => o.IdAllegro).Distinct().ToList();
                if (targetOfferId.HasValue && !myOfferIdsList.Contains(targetOfferId.Value))
                    myOfferIdsList.Add(targetOfferId.Value);

                var myOffersGroupKey = myOfferIdsList.Any()
                    ? string.Join(",", myOfferIdsList.OrderBy(id => id))
                    : "";

                // ═══ Extended info — preferuj rekord przypisany do MOJEJ oferty (po IdAllegro) ═══
                AllegroPriceHistoryExtendedInfoClass extInfo = null;
                if (myOffer != null && extInfoByProductAndOffer.TryGetValue((currentProductId, myOffer.IdAllegro), out var specific))
                    extInfo = specific;
                else
                    extInfoByProduct.TryGetValue(currentProductId, out extInfo);

                decimal? mapPrice = extInfo?.AllegroMapPriceSnapshot ?? product.AllegroMapPrice;

                // ─── Cena referencyjna ───
                // W trybie StorePrice = cena TEJ KONKRETNEJ oferty (nie najtańszej z naszych)
                decimal? referencePrice = null;
                string referenceSource = "none";
                if (settings.ComparisonSource == ProducerComparisonSource.MapPrice)
                {
                    if (mapPrice.HasValue && mapPrice.Value > 0)
                    {
                        referencePrice = mapPrice;
                        referenceSource = "map";
                    }
                }
                else // StorePrice
                {
                    if (myOffer != null && myOffer.Price > 0)
                    {
                        referencePrice = myOffer.Price;
                        referenceSource = "store";
                    }
                }
                // ─── KONKURENCJA ───
                // Filtr SellerName wyklucza WSZYSTKIE nasze oferty (każdą z naszych N ofert).
                var filteredCompetitors = offers.Where(p =>
                {
                    // 1. Zawsze odrzucamy nasze własne oferty
                    if (p.SellerName != null && p.SellerName.ToLower().Trim() == storeNameLower) return false;

                    // 2. Aplikujemy filtry presetu TYLKO, jeśli preset jest włączony
                    if (activePreset != null)
                    {
                        if (p.DeliveryTime.HasValue)
                        {
                            if (p.DeliveryTime.Value < minDelivery || p.DeliveryTime.Value > maxDelivery) return false;
                        }
                        else
                        {
                            if (!includeNoDelivery) return false;
                        }

                        if (competitorRules != null)
                        {
                            var key = (p.SellerName ?? "").ToLower().Trim();
                            if (competitorRules.TryGetValue(key, out bool use)) return use;
                            return activePreset.UseUnmarkedStores;
                        }
                    }

                    // Jeśli preset jest wyłączony (lub sklep nie był na liście reguł), akceptujemy ofertę
                    return true;
                
            }).ToList();

                var bestCompetitor = filteredCompetitors
                    .Where(x => x.Price > 0)
                    .OrderBy(x => x.Price)
                    .ThenBy(x => x.SellerName)
                    .FirstOrDefault();

                string bucket;
                decimal? deltaAbsolute = null;
                decimal? deltaPercent = null;
                if (referencePrice == null) bucket = "producer-no-reference";
                else if (bestCompetitor == null) bucket = "producer-no-competition";
                else
                {
                    deltaAbsolute = bestCompetitor.Price - referencePrice.Value;
                    if (referencePrice.Value > 0)
                        deltaPercent = Math.Round((deltaAbsolute.Value / referencePrice.Value) * 100m, 2);
                    bucket = ResolveAllegroProducerBucket(deltaAbsolute.Value, deltaPercent ?? 0m, settings);
                }

                int storesBelowReference = 0;
                int storesAtReference = 0;
                int storesAboveReference = 0;
                decimal? worstViolation = null;

                if (referencePrice.HasValue && referencePrice.Value > 0)
                {
                    foreach (var c in filteredCompetitors.Where(x => x.Price > 0))
                    {
                        decimal compDelta = c.Price - referencePrice.Value;
                        decimal compDiff = settings.UseAmount
                            ? compDelta
                            : (compDelta / referencePrice.Value) * 100m;

                        // Spójne z ResolveAllegroProducerBucket:
                        //   <= -redLightThreshold → naruszenie (poniżej)
                        //   >=  greenLightThreshold → powyżej
                        if (compDiff <= -redLightThreshold)
                        {
                            storesBelowReference++;
                            if (!worstViolation.HasValue || compDelta < worstViolation.Value)
                                worstViolation = compDelta;
                        }
                        else if (compDiff >= greenLightThreshold)
                        {
                            storesAboveReference++;
                        }
                        else
                        {
                            storesAtReference++;
                        }
                    }
                }

                // ─── Status naruszenia + czas trwania ───
                bool isCurrentlyViolating = bucket == "producer-deep-violation"
                                         || bucket == "producer-violation"
                                         || bucket == "producer-minor-below";

                decimal? violationDurationHours = null;
                bool isFreshViolation = false;
                bool reachedMaxWindow = false;

                bool wasRecentlyViolated = false;
                decimal? lastViolationEndedHoursAgo = null;
                decimal? lastViolationDurationHours = null;

                if (isCurrentlyViolating)
                {
                    DateTime violationStartDate = latestScrap.Date;
                    bool sequenceBroken = false;
                    bool prevScrapWasViolation = false;
                    bool prevScrapEvaluated = false;
                    foreach (var hs in historyScraps)
                    {
                        var result = WasViolatingAtScrap(currentProductId, targetOfferId, hs.Id);

                        if (result == null) continue; // pomiń nieudane scrapy — nie zrywaj łańcucha

                        bool wasViolating = result.Value;

                        if (!prevScrapEvaluated)
                        {
                            prevScrapWasViolation = wasViolating;
                            prevScrapEvaluated = true;
                        }

                        if (!sequenceBroken)
                        {
                            if (wasViolating)
                                violationStartDate = hs.Date;
                            else
                                sequenceBroken = true;
                        }
                    }

                    isFreshViolation = !prevScrapWasViolation;

                    decimal hours = (decimal)(latestScrap.Date - violationStartDate).TotalHours;

                    if (!sequenceBroken && historyScraps.Any())
                    {
                        var earliestScrap = historyScraps.Last(); // last w DESC = najstarszy
                        if ((earliestScrap.Date - windowStart).TotalHours < 24)
                            reachedMaxWindow = true;
                    }

                    if (hours > 168m) hours = 168m;
                    if (reachedMaxWindow) hours = 168m;

                    violationDurationHours = Math.Round(hours, 2);
                }
                else
                {
                    int? lastViolatingIdx = null;
                    for (int i = 0; i < historyScraps.Count; i++)
                    {
                        if (WasViolatingAtScrap(currentProductId, targetOfferId, historyScraps[i].Id) == true)
                        {
                            lastViolatingIdx = i;
                            break;
                        }
                    }

                    if (lastViolatingIdx.HasValue)
                    {
                        wasRecentlyViolated = true;
                        var lastViolationDate = historyScraps[lastViolatingIdx.Value].Date;
                        lastViolationEndedHoursAgo = Math.Round(
                            (decimal)(latestScrap.Date - lastViolationDate).TotalHours, 2);

                        DateTime pastViolationStart = lastViolationDate;
                        for (int i = lastViolatingIdx.Value + 1; i < historyScraps.Count; i++)
                        {
                            var r = WasViolatingAtScrap(currentProductId, targetOfferId, historyScraps[i].Id);
                            if (r == null) continue;            // pomiń unknowns
                            if (r == true)
                                pastViolationStart = historyScraps[i].Date;
                            else
                                break;
                        }
                        decimal pastDurHrs = (decimal)(lastViolationDate - pastViolationStart).TotalHours;
                        if (pastDurHrs > 168m) pastDurHrs = 168m;
                        lastViolationDurationHours = Math.Round(pastDurHrs, 2);
                    }
                }

                productFlagsDict.TryGetValue(currentProductId, out var flagIds);
                flagIds ??= new List<int>();

                // ─── Sprzedaż 30 dni ───
                // Tak jak w GetAllegroPrices: myPopularity = suma wszystkich naszych ofert
                // (każdy rekord pokazuje TAKĄ SAMĄ wartość — frontend wyświetla ją tylko na liderze)
                int totalPopularity = offers.Sum(o => o.Popularity ?? 0);
                int myPopularity = allMyOffersInGroup.Sum(o => o.Popularity ?? 0);
                decimal marketSharePercentage = (totalPopularity > 0)
                    ? Math.Round(((decimal)myPopularity / totalPopularity) * 100m, 2)
                    : 0m;

                return new
                {
                    ProductId = product.AllegroProductId,
                    ProductName = product.AllegroProductName,
                    Producer = product.Producer,
                    AllegroSku = product.AllegroSku,
                    Ean = product.AllegroEan,
                    AllegroOfferUrl = product.AllegroOfferUrl,
                    IdOnAllegro = product.IdOnAllegro,

                    // Catalog grouping
                    MyIdAllegro = myOffer?.IdAllegro ?? targetOfferId,
                    MyOffersGroupKey = myOffersGroupKey,
                    MyOfferCount = allMyOffersInGroup.Count,

                    ReferencePrice = referencePrice,
                    ReferenceSource = referenceSource,
                    MapPrice = mapPrice,
                    MyPrice = myOffer?.Price,

                    // Konkurent (najtańszy nie-nasz, po filtrze presetu)
                    BestCompetitorPrice = bestCompetitor?.Price,
                    BestCompetitorSellerName = bestCompetitor?.SellerName,
                    BestCompetitorDeliveryTime = bestCompetitor?.DeliveryTime,
                    BestCompetitorSuperSeller = bestCompetitor?.SuperSeller ?? false,
                    BestCompetitorSuperPrice = bestCompetitor?.SuperPrice ?? false,
                    BestCompetitorTopOffer = bestCompetitor?.TopOffer ?? false,
                    BestCompetitorIsBestPriceGuarantee = bestCompetitor?.IsBestPriceGuarantee ?? false,
                    BestCompetitorPromoted = bestCompetitor?.Promoted ?? false,
                    BestCompetitorSponsored = bestCompetitor?.Sponsored ?? false,
                    BestCompetitorSmart = bestCompetitor?.Smart ?? false,
                    BestCompetitorIdAllegro = bestCompetitor?.IdAllegro,

                    // MOJA oferta (TA KONKRETNA z IdOnAllegro)
                    MyDeliveryTime = myOffer?.DeliveryTime,
                    MyIsSmart = myOffer?.Smart ?? false,
                    MyIsSuperSeller = myOffer?.SuperSeller ?? false,
                    MyIsSuperPrice = myOffer?.SuperPrice ?? false,
                    MyIsTopOffer = myOffer?.TopOffer ?? false,
                    MyIsBestPriceGuarantee = myOffer?.IsBestPriceGuarantee ?? false,
                    MyIsPromoted = myOffer?.Promoted ?? false,
                    MyIsSponsored = myOffer?.Sponsored ?? false,

                    ProducerBucket = bucket,
                    DeltaAbsolute = deltaAbsolute,
                    DeltaPercent = deltaPercent,

                    StoresBelowReference = storesBelowReference,
                    StoresAtReference = storesAtReference,
                    StoresAboveReference = storesAboveReference,
                    WorstViolation = worstViolation,

                    // Naruszenia
                    IsCurrentlyViolating = isCurrentlyViolating,
                    IsFreshViolation = isFreshViolation,
                    ReachedMaxWindow = reachedMaxWindow,
                    ViolationDurationHours = violationDurationHours,

                    WasRecentlyViolated = wasRecentlyViolated,
                    LastViolationEndedHoursAgo = lastViolationEndedHoursAgo,
                    LastViolationDurationHours = lastViolationDurationHours,

                    FlagIds = flagIds,
                    IsRejected = product.IsRejected,
                    AddedDate = product.AddedDate,
                    IsNew = product.AddedDate >= sevenDaysAgo,

                    CompetitorCount = filteredCompetitors.Count,
                    ApiAllegroPrice = extInfo?.ApiAllegroPrice,
                    ApiAllegroCommission = extInfo?.ApiAllegroCommission,
                    AnyPromoActive = extInfo?.AnyPromoActive,
                    IsSubsidyActive = extInfo?.IsSubsidyActive,

                    // Sprzedaż 30 dni
                    TotalPopularity = totalPopularity,
                    MyTotalPopularity = myPopularity,
                    MarketSharePercentage = marketSharePercentage
                };
            }).ToList();

            swTotal.Stop();
            _logger.LogWarning("[PERF-ALG-PROD] Store {StoreId} | TOTAL: {ms}ms (products={Count})",
                storeId, swTotal.ElapsedMilliseconds, allPrices.Count);

            return Json(new
            {
                productCount = allPrices.Count,
                myStoreName = store.StoreNameAllegro,
                prices = allPrices,
                presetName = activePreset?.PresetName ?? "PriceSafari",
                latestScrapId = latestScrap.Id,
                latestScrapDate = latestScrap.Date,
                producerSettings = new
                {
                    comparisonSource = (int)settings.ComparisonSource,
                    useAmount = settings.UseAmount,
                    identifierForSimulation = settings.IdentifierForSimulation,
                    thresholds = new
                    {
                        redDarkPct = settings.RedDarkPct,
                        redPct = settings.RedPct,
                        redLightPct = settings.RedLightPct,
                        greenLightPct = settings.GreenLightPct,
                        greenPct = settings.GreenPct,
                        greenDarkPct = settings.GreenDarkPct,
                        redDarkAmt = settings.RedDarkAmt,
                        redAmt = settings.RedAmt,
                        redLightAmt = settings.RedLightAmt,
                        greenLightAmt = settings.GreenLightAmt,
                        greenAmt = settings.GreenAmt,
                        greenDarkAmt = settings.GreenDarkAmt
                    }
                }
            });
        }

        public class PriceSettingsViewModel
        {
            public int StoreId { get; set; }
            public decimal SetPrice1 { get; set; }
            public decimal SetPrice2 { get; set; }
            public decimal PriceStep { get; set; }
            public bool UsePriceDifference { get; set; }
            public decimal PriceIndexTargetPercent { get; set; }
        }

        private decimal? CalculateMedian(List<decimal> prices)
        {
            if (prices == null || prices.Count == 0) return null;

            var sortedPrices = prices.OrderBy(x => x).ToList();
            int count = sortedPrices.Count;

            if (count % 2 == 0)
            {

                return (sortedPrices[count / 2 - 1] + sortedPrices[count / 2]) / 2m;
            }
            else
            {

                return sortedPrices[count / 2];
            }
        }

        [HttpPost]
        public async Task<IActionResult> SavePriceValues([FromBody] PriceSettingsViewModel model)
        {
            if (model == null || model.StoreId <= 0) return BadRequest();
            if (!await UserHasAccessToStore(model.StoreId)) return Forbid();

            var priceValues = await _context.PriceValues
                .FirstOrDefaultAsync(pv => pv.StoreId == model.StoreId);

            if (priceValues == null)
            {
                priceValues = new PriceValueClass { StoreId = model.StoreId };
                _context.PriceValues.Add(priceValues);
            }

            priceValues.AllegroSetPrice1 = model.SetPrice1;
            priceValues.AllegroSetPrice2 = model.SetPrice2;
            priceValues.AllegroPriceStep = model.PriceStep;
            priceValues.AllegroUsePriceDiff = model.UsePriceDifference;
            priceValues.AllegroPriceIndexTargetPercent = model.PriceIndexTargetPercent;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> SaveAllegroMarginSettings([FromBody] AllegroPriceMarginSettingsViewModel model)
        {
            if (model == null || model.StoreId <= 0) return BadRequest("Invalid data.");
            if (!await UserHasAccessToStore(model.StoreId)) return Forbid();

            var priceValues = await _context.PriceValues
                .FirstOrDefaultAsync(pv => pv.StoreId == model.StoreId);

            if (priceValues == null)
            {
                priceValues = new PriceValueClass { StoreId = model.StoreId };
                _context.PriceValues.Add(priceValues);
            }

            priceValues.AllegroIdentifierForSimulation = model.AllegroIdentifierForSimulation;
            priceValues.AllegroUseMarginForSimulation = model.AllegroUseMarginForSimulation;
            priceValues.AllegroEnforceMinimalMargin = model.AllegroEnforceMinimalMargin;
            priceValues.AllegroMinimalMarginPercent = model.AllegroMinimalMarginPercent;
            priceValues.AllegroIncludeCommisionInPriceChange = model.AllegroIncludeCommisionInPriceChange;

            priceValues.AllegroChangePriceForBagdeSuperPrice = model.AllegroChangePriceForBagdeSuperPrice;
            priceValues.AllegroChangePriceForBagdeTopOffer = model.AllegroChangePriceForBagdeTopOffer;
            priceValues.AllegroChangePriceForBagdeBestPriceGuarantee = model.AllegroChangePriceForBagdeBestPriceGuarantee;
            priceValues.AllegroChangePriceForBagdeInCampaign = model.AllegroChangePriceForBagdeInCampaign;
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpPost]
        public IActionResult GetPriceChangeDetails([FromBody] List<int> productIds)
        {
            if (productIds == null || productIds.Count == 0)
                return Json(new List<object>());

            var products = _context.AllegroProducts
                .Where(p => productIds.Contains(p.AllegroProductId))
               .Select(p => new
               {
                   productId = p.AllegroProductId,
                   productName = p.AllegroProductName,
                   imageUrl = (string)null,
                   ean = p.AllegroEan,
                   allegroOfferUrl = p.AllegroOfferUrl
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
            var firstProduct = await _context.AllegroProducts
             .Include(p => p.Store)
             .FirstOrDefaultAsync(p => p.AllegroProductId == firstProductId);

            if (firstProduct == null) return NotFound("Produkt nie znaleziony.");
            if (!await UserHasAccessToStore(firstProduct.StoreId)) return Unauthorized("Brak dostępu do sklepu.");

            int storeId = firstProduct.StoreId;
            string ourStoreName = firstProduct.Store?.StoreNameAllegro ?? "";

            var priceValues = await _context.PriceValues
              .FirstOrDefaultAsync(pv => pv.StoreId == storeId);

            var latestScrap = await _context.AllegroScrapeHistories
              .Where(sh => sh.StoreId == storeId)
              .OrderByDescending(sh => sh.Date)
              .Select(sh => new { sh.Id })
              .FirstOrDefaultAsync();

            if (latestScrap == null) return BadRequest("Brak danych scrapowania dla sklepu.");

            int latestScrapId = latestScrap.Id;
            var productIds = simulationItems.Select(s => s.ProductId).Distinct().ToList();

            bool includeCommission = priceValues?.AllegroIncludeCommisionInPriceChange ?? false;

            var extendedInfosList = await _context.AllegroPriceHistoryExtendedInfos
              .Where(e => e.ScrapHistoryId == latestScrapId && productIds.Contains(e.AllegroProductId))
              .ToListAsync();

            var extendedInfos = extendedInfosList
              .GroupBy(e => e.AllegroProductId)
              .ToDictionary(g => g.Key, g => g.First());

            var productsData = await _context.AllegroProducts
              .Where(p => productIds.Contains(p.AllegroProductId))
              .Select(p => new {
                  p.AllegroProductId,
                  p.AllegroEan,
                  p.AllegroMarginPrice,
                  p.AllegroOfferUrl

              })
              .ToDictionaryAsync(p => p.AllegroProductId);

            var allPriceHistories = await _context.AllegroPriceHistories
              .Where(ph => ph.AllegroScrapeHistoryId == latestScrapId && productIds.Contains(ph.AllegroProductId))
              .ToListAsync();
            allPriceHistories = allPriceHistories
            .GroupBy(ph => new { ph.AllegroProductId, ph.IdAllegro })
            .Select(g => g.First())
            .ToList();
            var priceHistoriesByProduct = allPriceHistories
              .GroupBy(ph => ph.AllegroProductId)
              .ToDictionary(g => g.Key, g => g.ToList());

            string CalculateRanking(List<decimal> prices, decimal price)
            {
                prices.Sort();
                int firstIndex = prices.IndexOf(price);
                int lastIndex = prices.LastIndexOf(price);
                if (firstIndex == -1) return "-";
                return (firstIndex == lastIndex) ? (firstIndex + 1).ToString() : $"{firstIndex + 1}-{lastIndex + 1}";
            }

            var simulationResults = new List<object>();

            foreach (var sim in simulationItems)
            {
                if (!productsData.TryGetValue(sim.ProductId, out var product)) continue;
                if (!priceHistoriesByProduct.TryGetValue(sim.ProductId, out var allRecordsForProduct))
                {
                    allRecordsForProduct = new List<AllegroPriceHistory>();
                }

                extendedInfos.TryGetValue(sim.ProductId, out var extendedInfo);

                bool weAreInAllegro = allRecordsForProduct.Any(ph => ph.SellerName == ourStoreName);
                var competitorPrices = allRecordsForProduct
                  .Where(ph => ph.SellerName != ourStoreName && ph.Price > 0)
                  .Select(ph => ph.Price)
                  .ToList();

                var currentAllegroList = new List<decimal>(competitorPrices);
                var newAllegroList = new List<decimal>(competitorPrices);

                if (weAreInAllegro)
                {
                    currentAllegroList.Add(sim.CurrentPrice);
                    newAllegroList.Add(sim.NewPrice);
                }

                int totalAllegroOffers = currentAllegroList.Count;

                string currentAllegroRankingStr = weAreInAllegro && totalAllegroOffers > 0 ? CalculateRanking(currentAllegroList, sim.CurrentPrice) : "-";
                string newAllegroRankingStr = weAreInAllegro && totalAllegroOffers > 0 ? CalculateRanking(newAllegroList, sim.NewPrice) : "-";

                string currentAllegroRanking = (currentAllegroRankingStr != "-" && totalAllegroOffers > 0) ? $"{currentAllegroRankingStr} / {totalAllegroOffers}" : "-";
                string newAllegroRanking = (newAllegroRankingStr != "-" && totalAllegroOffers > 0) ? $"{newAllegroRankingStr} / {totalAllegroOffers}" : "-";

                decimal? currentMargin = null, newMargin = null, currentMarginValue = null, newMarginValue = null;
                if (product.AllegroMarginPrice.HasValue && product.AllegroMarginPrice.Value != 0)
                {
                    decimal commissionToDeduct = (includeCommission && extendedInfo?.ApiAllegroCommission.HasValue == true)
                                ? extendedInfo.ApiAllegroCommission.Value
                                : 0m;

                    decimal currentNetPrice = sim.CurrentPrice - commissionToDeduct;
                    decimal newNetPrice = sim.NewPrice - commissionToDeduct;

                    currentMarginValue = currentNetPrice - product.AllegroMarginPrice.Value;
                    newMarginValue = newNetPrice - product.AllegroMarginPrice.Value;

                    currentMargin = Math.Round((currentMarginValue.Value / product.AllegroMarginPrice.Value) * 100, 2);
                    newMargin = Math.Round((newMarginValue.Value / product.AllegroMarginPrice.Value) * 100, 2);
                }

                simulationResults.Add(new
                {
                    productId = product.AllegroProductId,
                    ean = product.AllegroEan,
                    allegroOfferUrl = product.AllegroOfferUrl,
                    producerCode = (string)null,
                    externalId = (long?)null,

                    baseCurrentPrice = sim.CurrentPrice,
                    baseNewPrice = sim.NewPrice,

                    currentAllegroRanking,
                    newAllegroRanking,
                    totalAllegroOffers = (totalAllegroOffers > 0 ? totalAllegroOffers : (int?)null),

                    allegroCurrentOffers = currentAllegroList.OrderBy(p => p).Select(p => new { Price = p, StoreName = "Konkurent" }).ToList(),
                    allegroNewOffers = newAllegroList.OrderBy(p => p).Select(p => new { Price = p, StoreName = "Konkurent" }).ToList(),

                    currentMargin,
                    newMargin,
                    currentMarginValue,
                    newMarginValue,

                    marginPrice = product.AllegroMarginPrice,
                    apiAllegroCommission = extendedInfo?.ApiAllegroCommission
                });
            }

            return Json(new
            {
                ourStoreName,
                simulationResults,
                usePriceWithDelivery = false,
                latestScrapId,
                allegroIncludeCommisionInPriceChange = includeCommission
            });
        }





        [HttpGet]
        public async Task<IActionResult> Details(int storeId, int productId, int? scrapId = null)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var product = await _context.AllegroProducts.FindAsync(productId);
            if (product == null) return NotFound("Product not found.");

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound("Store not found.");

            bool useProducerView = await CurrentUserUsesProducerView();

            // ── Flagi dla tego produktu ──
            var allFlags = await _context.Flags
                .Where(f => f.StoreId == storeId && f.IsMarketplace == true)
                .Select(f => new FlagViewModel
                {
                    FlagId = f.FlagId,
                    FlagName = f.FlagName,
                    FlagColor = f.FlagColor,
                    IsMarketplace = f.IsMarketplace
                })
                .ToListAsync();

            var productFlagIds = await _context.ProductFlags
                .Where(pf => pf.AllegroProductId == productId)
                .Select(pf => pf.FlagId)
                .ToListAsync();

            // ── Automatyzacja dla tego produktu ──
            var automationAssignment = await _context.AutomationProductAssignments
                .Include(a => a.AutomationRule)
                .Where(a => a.AllegroProductId == productId
                         && a.AutomationRule.StoreId == storeId
                         && a.AutomationRule.SourceType == AutomationSourceType.Marketplace)
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
                {
                    isAutomationPaused = true;
                }
            }

            ViewBag.StoreId = storeId;
            ViewBag.ProductId = productId;
            ViewBag.ProductName = product.AllegroProductName;
            ViewBag.OfferId = product.IdOnAllegro;
            ViewBag.Ean = product.AllegroEan;
            ViewBag.AllegroSku = product.AllegroSku;
            ViewBag.StoreName = store.StoreNameAllegro;
            ViewBag.AllegroOfferUrl = product.AllegroOfferUrl;
            ViewBag.Flags = allFlags;
            ViewBag.ProductFlagIds = productFlagIds;
            ViewBag.AutomationRuleName = automationAssignment?.RuleName;
            ViewBag.AutomationRuleColor = automationAssignment?.RuleColor;
            ViewBag.AutomationRuleIsActive = automationAssignment?.IsActive ?? false;
            ViewBag.AutomationRuleId = automationAssignment?.RuleId;
            ViewBag.IsAutomationPaused = isAutomationPaused;
            ViewBag.ScrapId = scrapId;

            if (useProducerView)
            {
                var pvFull = await _context.PriceValues.AsNoTracking()
                    .FirstOrDefaultAsync(pv => pv.StoreId == storeId);

                var producerSettings = new AllegroProducerSettings();
                if (pvFull != null)
                {
                    producerSettings.ComparisonSource = pvFull.AllegroProducerComparisonSource;
                    producerSettings.UseAmount = pvFull.AllegroProducerUseAmount;
                    producerSettings.IdentifierForSimulation = pvFull.AllegroIdentifierForSimulation ?? "EAN";
                    producerSettings.RedDarkPct = pvFull.AllegroProducerThresholdRedDarkPercent;
                    producerSettings.RedPct = pvFull.AllegroProducerThresholdRedPercent;
                    producerSettings.RedLightPct = pvFull.AllegroProducerThresholdRedLightPercent;
                    producerSettings.GreenLightPct = pvFull.AllegroProducerThresholdGreenLightPercent;
                    producerSettings.GreenPct = pvFull.AllegroProducerThresholdGreenPercent;
                    producerSettings.GreenDarkPct = pvFull.AllegroProducerThresholdGreenDarkPercent;
                    producerSettings.RedDarkAmt = pvFull.AllegroProducerThresholdRedDarkAmount;
                    producerSettings.RedAmt = pvFull.AllegroProducerThresholdRedAmount;
                    producerSettings.RedLightAmt = pvFull.AllegroProducerThresholdRedLightAmount;
                    producerSettings.GreenLightAmt = pvFull.AllegroProducerThresholdGreenLightAmount;
                    producerSettings.GreenAmt = pvFull.AllegroProducerThresholdGreenAmount;
                    producerSettings.GreenDarkAmt = pvFull.AllegroProducerThresholdGreenDarkAmount;
                }

                var lastScrap = await _context.AllegroScrapeHistories.AsNoTracking()
                    .Where(sh => sh.StoreId == storeId)
                    .OrderByDescending(sh => sh.Date)
                    .Select(sh => new { sh.Id })
                    .FirstOrDefaultAsync();

                AllegroPriceHistoryExtendedInfoClass extForProduct = null;
                AllegroPriceHistory myOffer = null;

                if (lastScrap != null)
                {
                    extForProduct = await _context.AllegroPriceHistoryExtendedInfos.AsNoTracking()
                        .FirstOrDefaultAsync(e => e.ScrapHistoryId == lastScrap.Id && e.AllegroProductId == productId);

                    // Moja oferta z najnowszego scrapu — potrzebna do trybu StorePrice
                    var storeNameLower = (store.StoreNameAllegro ?? "").ToLower().Trim();

                    // ZASTOSOWANA ZMIANA:
                    var allOffers = await _context.AllegroPriceHistories.AsNoTracking()
                        .Where(ph => ph.AllegroScrapeHistoryId == lastScrap.Id
                                  && ph.AllegroProductId == productId
                                  && ph.SellerName != null
                                  && ph.Price > 0)
                        .ToListAsync();

                    myOffer = allOffers
                        .Where(x => x.SellerName.ToLower().Trim() == storeNameLower)
                        .OrderBy(x => x.Price)
                        .FirstOrDefault();
                }
                // MAP = snapshot, fallback na bieżący AllegroMapPrice (NIE AllegroMarginPrice)
                decimal? mapPrice = extForProduct?.AllegroMapPriceSnapshot ?? product.AllegroMapPrice;

                decimal? referencePrice = null;
                string referenceSource = "none";
                if (producerSettings.ComparisonSource == ProducerComparisonSource.MapPrice)
                {
                    if (mapPrice.HasValue && mapPrice.Value > 0)
                    {
                        referencePrice = mapPrice;
                        referenceSource = "map";
                    }
                }
                else // StorePrice — pełna obsługa, producent może mieć własne konto marki na Allegro
                {
                    if (myOffer != null && myOffer.Price > 0)
                    {
                        referencePrice = myOffer.Price;
                        referenceSource = "store";
                    }
                }

                var thresholdsForChart = new
                {
                    useAmount = producerSettings.UseAmount,
                    referencePrice = referencePrice,
                    greenDarkPct = producerSettings.GreenDarkPct,
                    greenPct = producerSettings.GreenPct,
                    greenLightPct = producerSettings.GreenLightPct,
                    redLightPct = producerSettings.RedLightPct,
                    redPct = producerSettings.RedPct,
                    redDarkPct = producerSettings.RedDarkPct,
                    greenDarkAmt = producerSettings.GreenDarkAmt,
                    greenAmt = producerSettings.GreenAmt,
                    greenLightAmt = producerSettings.GreenLightAmt,
                    redLightAmt = producerSettings.RedLightAmt,
                    redAmt = producerSettings.RedAmt,
                    redDarkAmt = producerSettings.RedDarkAmt
                };

                ViewBag.ReferencePrice = referencePrice;
                ViewBag.ReferenceSource = referenceSource;
                ViewBag.MapPrice = mapPrice;
                ViewBag.MyPrice = myOffer?.Price;
                ViewBag.ProducerThresholdsJson = Newtonsoft.Json.JsonConvert.SerializeObject(thresholdsForChart);
                ViewBag.ProducerSettings = producerSettings;
                ViewBag.IsProducerOnAllegro = true;

                return View("~/Views/Panel/AllegroPriceHistory/DetailsProducer.cshtml");
            }

            return View("~/Views/Panel/AllegroPriceHistory/Details.cshtml");
        }





        [HttpGet]
        // DODANO: int? scrapId = null
        public async Task<IActionResult> GetProductPriceDetails(int storeId, int productId, int? scrapId = null)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound("Store not found.");

            var product = await _context.AllegroProducts.FindAsync(productId);
            if (product == null) return NotFound("Product not found.");

            // ZMIANA: Zamiast "latestScrap", szukamy konkretnego "selectedScrap"
            AllegroScrapeHistory selectedScrap;

            if (scrapId.HasValue && scrapId.Value > 0)
            {
                // Jeśli podano scrapId w URL, pobierz ten konkretny
                selectedScrap = await _context.AllegroScrapeHistories
                    .FirstOrDefaultAsync(sh => sh.Id == scrapId.Value && sh.StoreId == storeId);
            }
            else
            {
                // Jeśli nie podano (wejście z innego miejsca), pobierz najnowszy
                selectedScrap = await _context.AllegroScrapeHistories
                    .Where(sh => sh.StoreId == storeId)
                    .OrderByDescending(sh => sh.Date)
                    .FirstOrDefaultAsync();
            }

            if (selectedScrap == null)
            {
                return Json(new { data = new List<object>() });
            }

            var activePreset = await _context.CompetitorPresets
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.StoreId == storeId && p.NowInUse && p.Type == PresetType.Marketplace);

            string activePresetName = activePreset?.PresetName;

            var competitorRules = activePreset?.CompetitorItems
                .Where(ci => ci.DataSource == DataSourceType.Allegro)
                .ToDictionary(ci => ci.StoreName.ToLower().Trim(), ci => ci.UseCompetitor);

            // ZMIANA: Tutaj zamieniamy "latestScrap.Id" na "selectedScrap.Id"
            var allOffersForProduct = await _context.AllegroPriceHistories
            .Where(aph => aph.AllegroScrapeHistoryId == selectedScrap.Id &&
                            aph.AllegroProductId == productId &&
                            aph.Price > 0)
            .ToListAsync();

            // ── Deduplikacja: jedna oferta per IdAllegro ──
            allOffersForProduct = allOffersForProduct
                        .GroupBy(aph => aph.IdAllegro)
                        .Select(g => g.First())
                        .ToList();
            List<AllegroPriceHistory> filteredOffers;
            bool includeNoDelivery = activePreset?.IncludeNoDeliveryInfo ?? true;
            int minDelivery = activePreset?.MinDeliveryDays ?? 0;
            int maxDelivery = activePreset?.MaxDeliveryDays ?? 31;

            if (activePreset != null)
            {
                var ourStoreNameLower = store.StoreNameAllegro.ToLower().Trim();

                filteredOffers = allOffersForProduct.Where(p =>
                {

                    if (p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (p.DeliveryTime.HasValue)
                    {
                        if (p.DeliveryTime.Value < minDelivery || p.DeliveryTime.Value > maxDelivery)
                        {
                            return false;
                        }
                    }
                    else
                    {

                        if (!includeNoDelivery)
                        {
                            return false;
                        }
                    }

                    var sellerNameLower = (p.SellerName ?? "").ToLower().Trim();

                    if (competitorRules != null && competitorRules.TryGetValue(sellerNameLower, out bool useCompetitor))
                    {
                        return useCompetitor;
                    }

                    return activePreset.UseUnmarkedStores;
                }).ToList();
            }
            else
            {

                filteredOffers = allOffersForProduct;
            }

            filteredOffers = filteredOffers.OrderBy(o => o.Price).ToList();

            var myOfferIdsInList = filteredOffers
                .Where(aph => aph.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                .Select(aph => aph.IdAllegro)
                .ToList();

            var allMyProducts = await _context.AllegroProducts
                  .Where(p => p.StoreId == storeId)
                  .Select(p => new { p.AllegroProductId, p.IdOnAllegro })

                  .ToListAsync();

            var navigationMap = new Dictionary<long, int>();

            var myProductsLookup = new Dictionary<long, int>();

            foreach (var p in allMyProducts)
            {
                if (long.TryParse(p.IdOnAllegro, out var parsedId))
                {

                    if (!myProductsLookup.ContainsKey(parsedId))
                    {
                        myProductsLookup[parsedId] = p.AllegroProductId;
                    }
                }
            }
            foreach (var offerId in myOfferIdsInList)
            {
                if (myProductsLookup.TryGetValue(offerId, out var productIdTarget))
                {
                    navigationMap[offerId] = productIdTarget;
                }
            }

            var dataForJson = filteredOffers.Select(aph => new {
                aph.SellerName,
                aph.Price,
                aph.SuperSeller,
                aph.Smart,
                aph.DeliveryTime,
                aph.DeliveryCost,
                aph.Popularity,
                aph.IsBestPriceGuarantee,
                aph.TopOffer,
                aph.SuperPrice,
                aph.Promoted,
                aph.Sponsored,
                aph.IdAllegro,
                aph.RatingCount,
                aph.RatingPositivePercent,
                TargetProductId = navigationMap.ContainsKey(aph.IdAllegro) ? navigationMap[aph.IdAllegro] : (int?)null
            }).ToList();

            long? mainOfferId = null;
            if (long.TryParse(product.IdOnAllegro, out var parsedMainId))
            {
                mainOfferId = parsedMainId;
            }

            var priceSettings = await _context.PriceValues.FirstOrDefaultAsync(pv => pv.StoreId == storeId);
            var totalPopularity = filteredOffers.Sum(o => o.Popularity ?? 0);

            return Json(new
            {
                mainOfferId,
                data = dataForJson,
                totalProductPopularity = totalPopularity,
                lastScrapeDate = selectedScrap.Date,
                setPrice1 = priceSettings?.AllegroSetPrice1 ?? 0.01m,
                setPrice2 = priceSettings?.AllegroSetPrice2 ?? 2.00m,
                activePresetName = activePresetName
            });
        }








        [HttpGet]
        public async Task<IActionResult> GetPriceTrendData(int productId, int limit = 30)
        {
            var product = await _context.AllegroProducts.FindAsync(productId);
            if (product == null)
                return NotFound(new { Error = "Nie znaleziono produktu." });

            var storeId = product.StoreId;
            if (!await UserHasAccessToStore(storeId))
                return Unauthorized(new { Error = "Brak dostępu do sklepu." });

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound("Store not found.");

            long? mainOfferId = null;
            if (long.TryParse(product.IdOnAllegro, out var parsedId))
            {
                mainOfferId = parsedId;
            }

            var activePreset = await _context.CompetitorPresets
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.StoreId == storeId && p.NowInUse && p.Type == PresetType.Marketplace);

            var competitorRules = activePreset?.CompetitorItems
                .Where(ci => ci.DataSource == DataSourceType.Allegro)
                .ToDictionary(ci => ci.StoreName.ToLower().Trim(), ci => ci.UseCompetitor);

            if (limit <= 0) limit = 30;

            var lastScraps = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(limit)
                .OrderBy(sh => sh.Date)
                .ToListAsync();

            var scrapIds = lastScraps.Select(sh => sh.Id).ToList();

            if (!scrapIds.Any())
            {
                return Json(new AllegroTrendDataViewModel
                {
                    ProductName = product.AllegroProductName,
                    TimelineData = new List<DailyTrendPointViewModel>(),
                    MainOfferId = mainOfferId
                });
            }

            var priceHistories = await _context.AllegroPriceHistories
                .Where(aph => aph.AllegroProductId == productId && scrapIds.Contains(aph.AllegroScrapeHistoryId))
                .Where(aph => aph.Price > 0)
                .ToListAsync();

            // ── Deduplikacja: jeden rekord per scrap + oferta ──
            priceHistories = priceHistories
                .GroupBy(aph => new { aph.AllegroScrapeHistoryId, aph.IdAllegro })
                .Select(g => g.First())
                .ToList();

            var scrapIdsWithData = new HashSet<int>(
                priceHistories.Select(ph => ph.AllegroScrapeHistoryId).Distinct());

            lastScraps = lastScraps
                .Where(sh => scrapIdsWithData.Contains(sh.Id))
                .ToList();

            scrapIds = lastScraps.Select(sh => sh.Id).ToList();

            // ═══ FIX: Znajdź TYLKO nasze oferty (IdAllegro) które występują w tym katalogu ═══
            var ourOfferIdsInCatalog = priceHistories
                .Where(ph => ph.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                .Select(ph => ph.IdAllegro)
                .Distinct()
                .ToList();

            // Dodaj mainOfferId jeśli nie było w cenówkach (np. oferta tymczasowo nieaktywna)
            if (mainOfferId.HasValue && !ourOfferIdsInCatalog.Contains(mainOfferId.Value))
            {
                ourOfferIdsInCatalog.Add(mainOfferId.Value);
            }

            // Dla każdej naszej oferty w katalogu, znajdź produkt-właściciela (przez IdOnAllegro)
            var ourProducts = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId && p.IdOnAllegro != null)
                .Select(p => new { p.AllegroProductId, p.IdOnAllegro })
                .ToListAsync();

            var offerOwnerMap = new Dictionary<long, int>(); // IdAllegro -> AllegroProductId
            foreach (var p in ourProducts)
            {
                if (long.TryParse(p.IdOnAllegro, out var parsedOfferId) && !offerOwnerMap.ContainsKey(parsedOfferId))
                {
                    offerOwnerMap[parsedOfferId] = p.AllegroProductId;
                }
            }

            // Zbierz productId tylko tych ofert, które faktycznie są w katalogu
            var relevantProductIds = ourOfferIdsInCatalog
                .Where(offerId => offerOwnerMap.ContainsKey(offerId))
                .Select(offerId => offerOwnerMap[offerId])
                .Distinct()
                .ToList();

            // Upewnij się, że aktualny produkt też jest na liście
            if (!relevantProductIds.Contains(productId))
                relevantProductIds.Add(productId);

            // Pobieramy extended info TYLKO dla powiązanych produktów
            var visitsData = await _context.AllegroPriceHistoryExtendedInfos
                .Where(e => scrapIds.Contains(e.ScrapHistoryId)
                         && relevantProductIds.Contains(e.AllegroProductId))
                .Select(e => new { e.ScrapHistoryId, e.AllegroVisitsCount, e.IdAllegro, e.AllegroProductId })
                .ToListAsync();

            // Grupujemy po ScrapHistoryId, deduplikujemy per IdAllegro,
            // biorąc visits z produktu-właściciela oferty
            // i FILTRUJEMY tylko oferty które faktycznie są w katalogu
            var visitsByScrapId = visitsData
                .Where(v => v.IdAllegro.HasValue && ourOfferIdsInCatalog.Contains(v.IdAllegro.Value))
                .GroupBy(v => v.ScrapHistoryId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        return g
                            .GroupBy(v => v.IdAllegro.Value)
                            .Select(offerGroup =>
                            {
                                var offerId = offerGroup.Key;
                                // Preferuj rekord z produktu-właściciela tej oferty
                                var bestRecord = offerOwnerMap.TryGetValue(offerId, out var ownerId)
                                    ? offerGroup.FirstOrDefault(r => r.AllegroProductId == ownerId)
                                      ?? offerGroup.First()
                                    : offerGroup.First();

                                return new { bestRecord.IdAllegro, bestRecord.AllegroVisitsCount };
                            })
                            .ToList();
                    }
                );
            // ═══════════════════════════════════════════════════════════════════

            bool includeNoDelivery = activePreset?.IncludeNoDeliveryInfo ?? true;
            int minDelivery = activePreset?.MinDeliveryDays ?? 0;
            int maxDelivery = activePreset?.MaxDeliveryDays ?? 31;

            var timelineData = lastScraps.Select(scrap =>
            {
                var allDailyOffers = priceHistories
                    .Where(ph => ph.AllegroScrapeHistoryId == scrap.Id);

                List<AllegroPriceHistory> filteredDailyOffers;
                if (activePreset != null)
                {
                    filteredDailyOffers = allDailyOffers.Where(p =>
                    {
                        if (p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                            return true;

                        if (p.DeliveryTime.HasValue)
                        {
                            if (p.DeliveryTime.Value < minDelivery || p.DeliveryTime.Value > maxDelivery)
                                return false;
                        }
                        else
                        {
                            if (!includeNoDelivery) return false;
                        }

                        var sellerNameLower = (p.SellerName ?? "").ToLower().Trim();
                        if (competitorRules != null && competitorRules.TryGetValue(sellerNameLower, out bool useCompetitor))
                            return useCompetitor;

                        return activePreset.UseUnmarkedStores;
                    }).ToList();
                }
                else
                {
                    filteredDailyOffers = allDailyOffers.ToList();
                }

                var dailyOffersForJson = filteredDailyOffers
                    .Select(ph => new OfferTrendPointViewModel
                    {
                        StoreName = ph.SellerName,
                        Price = ph.Price,
                        Sales = ph.Popularity ?? 0,
                        Source = "allegro",
                        IdAllegro = ph.IdAllegro,
                        IsBestPriceGuarantee = ph.IsBestPriceGuarantee,
                        TopOffer = ph.TopOffer,
                        SuperPrice = ph.SuperPrice
                    })
                    .ToList();

                return new DailyTrendPointViewModel
                {
                    ScrapDate = scrap.Date.ToString("yyyy-MM-dd HH:00"),
                    TotalSales = dailyOffersForJson.Sum(o => o.Sales),
                    Offers = dailyOffersForJson
                };
            }).ToList();

            // ═══ Visits timeline — tylko nasze oferty z katalogu, z poprawnymi danymi ═══
            var visitsTimeline = lastScraps.Select(scrap =>
            {
                var offersForScrap = visitsByScrapId.GetValueOrDefault(scrap.Id);

                var offerVisits = offersForScrap?
                    .Where(o => o.IdAllegro.HasValue)
                    .Select(o => new { offerId = o.IdAllegro.Value, visits = o.AllegroVisitsCount })
                    .ToList() ?? new();

                int? totalVisits = offerVisits.Any(o => o.visits.HasValue)
                    ? offerVisits.Sum(o => o.visits ?? 0)
                    : null;

                return new
                {
                    scrapDate = scrap.Date.ToString("yyyy-MM-dd HH:00"),
                    totalVisits,
                    offers = offerVisits
                };
            }).ToList();
            // ══════════════════════════════════════════════════════════════════

            return Json(new
            {
                productName = product.AllegroProductName,
                timelineData = timelineData,
                mainOfferId = mainOfferId,
                visitsTimeline = visitsTimeline
            });
        }



        [HttpGet]
        public async Task<IActionResult> GetPriceTrendDataForProducer(int productId, int limit = 30)
        {
            var product = await _context.AllegroProducts.FindAsync(productId);
            if (product == null) return NotFound(new { Error = "Nie znaleziono produktu." });

            var storeId = product.StoreId;
            if (!await UserHasAccessToStore(storeId))
                return Unauthorized(new { Error = "Brak dostępu do sklepu." });

            var store = await _context.Stores.AsNoTracking()
                .FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null) return BadRequest(new { Error = "Sklep nie istnieje." });

            var storeNameLower = (store.StoreNameAllegro ?? "").ToLower().Trim();

            var pvFull = await _context.PriceValues.AsNoTracking()
                .FirstOrDefaultAsync(pv => pv.StoreId == storeId);

            bool isMapMode = (pvFull?.AllegroProducerComparisonSource ?? ProducerComparisonSource.MapPrice)
                             == ProducerComparisonSource.MapPrice;

            var activePreset = await _context.CompetitorPresets.AsNoTracking()
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.StoreId == storeId && p.NowInUse && p.Type == PresetType.Marketplace);

            var competitorRules = activePreset?.CompetitorItems
                .Where(ci => ci.DataSource == DataSourceType.Allegro)
                .ToDictionary(ci => ci.StoreName.ToLower().Trim(), ci => ci.UseCompetitor);

            bool includeNoDelivery = activePreset?.IncludeNoDeliveryInfo ?? true;
            int minDelivery = activePreset?.MinDeliveryDays ?? 0;
            int maxDelivery = activePreset?.MaxDeliveryDays ?? 31;

            if (limit <= 0) limit = 30;

            var lastScraps = await _context.AllegroScrapeHistories.AsNoTracking()
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(limit)
                .OrderBy(sh => sh.Date)
                .ToListAsync();

            var scrapIds = lastScraps.Select(sh => sh.Id).ToList();
            if (!scrapIds.Any())
            {
                return Json(new
                {
                    productName = product.AllegroProductName,
                    isMapMode,
                    timelineData = new List<object>()
                });
            }

            var priceHistories = await _context.AllegroPriceHistories.AsNoTracking()
                .Where(ph => ph.AllegroProductId == productId
                          && scrapIds.Contains(ph.AllegroScrapeHistoryId)
                          && ph.Price > 0)
                .ToListAsync();

            priceHistories = priceHistories
                .GroupBy(ph => new { ph.AllegroScrapeHistoryId, ph.IdAllegro })
                .Select(g => g.First())
                .ToList();

            // MAP snapshot per scrap
            var extendedInfos = await _context.AllegroPriceHistoryExtendedInfos.AsNoTracking()
                .Where(e => scrapIds.Contains(e.ScrapHistoryId) && e.AllegroProductId == productId)
                .Select(e => new { e.ScrapHistoryId, e.AllegroMapPriceSnapshot })
                .ToListAsync();

            var mapSnapshotByScrap = extendedInfos
                .GroupBy(e => e.ScrapHistoryId)
                .ToDictionary(g => g.Key, g => g.First().AllegroMapPriceSnapshot);

            var timelineData = lastScraps.Select(scrap =>
            {
                var scrapPrices = priceHistories.Where(ph => ph.AllegroScrapeHistoryId == scrap.Id).ToList();

                // Cena referencyjna dla tego scrapu
                decimal? referencePrice = null;
                if (isMapMode)
                {
                    mapSnapshotByScrap.TryGetValue(scrap.Id, out var snapshot);
                    referencePrice = snapshot;
                }
                else
                {
                    var myEntry = scrapPrices
                        .FirstOrDefault(ph => ph.SellerName != null && ph.SellerName.ToLower().Trim() == storeNameLower);
                    referencePrice = myEntry?.Price;
                }

                var filtered = scrapPrices.Where(ph =>
                {
                    // Wykluczenie naszej oferty w MAP mode — to logika producenta, nie presetu, zostaje
                    if (isMapMode && ph.SellerName != null
                        && ph.SellerName.ToLower().Trim() == storeNameLower)
                        return false;

                    // Filtry presetu — TYLKO gdy preset istnieje
                    if (activePreset != null)
                    {
                        if (ph.DeliveryTime.HasValue)
                        {
                            if (ph.DeliveryTime.Value < minDelivery || ph.DeliveryTime.Value > maxDelivery)
                                return false;
                        }
                        else
                        {
                            if (!includeNoDelivery) return false;
                        }

                        if (competitorRules != null)
                        {
                            var key = (ph.SellerName ?? "").ToLower().Trim();
                            if (competitorRules.TryGetValue(key, out bool use)) return use;
                            return activePreset.UseUnmarkedStores;
                        }
                    }
                    return true;
                })
                  .Select(ph => new
                {
                    sellerName = ph.SellerName,
                    price = ph.Price,
                    idAllegro = ph.IdAllegro,
                    deliveryTime = ph.DeliveryTime,
                    isBestPriceGuarantee = ph.IsBestPriceGuarantee,
                    topOffer = ph.TopOffer,
                    superPrice = ph.SuperPrice
                })
                .ToList();

                return new
                {
                    scrapDate = scrap.Date.ToString("yyyy-MM-dd HH:00"),
                    referencePrice,
                    offers = filtered
                };
            }).ToList();

            var thresholds = new
            {
                useAmount = pvFull?.AllegroProducerUseAmount ?? false,
                greenDarkPct = pvFull?.AllegroProducerThresholdGreenDarkPercent ?? 20m,
                greenPct = pvFull?.AllegroProducerThresholdGreenPercent ?? 10m,
                greenLightPct = pvFull?.AllegroProducerThresholdGreenLightPercent ?? 1m,
                redLightPct = pvFull?.AllegroProducerThresholdRedLightPercent ?? 1m,
                redPct = pvFull?.AllegroProducerThresholdRedPercent ?? 10m,
                redDarkPct = pvFull?.AllegroProducerThresholdRedDarkPercent ?? 20m,
                greenDarkAmt = pvFull?.AllegroProducerThresholdGreenDarkAmount ?? 50m,
                greenAmt = pvFull?.AllegroProducerThresholdGreenAmount ?? 20m,
                greenLightAmt = pvFull?.AllegroProducerThresholdGreenLightAmount ?? 5m,
                redLightAmt = pvFull?.AllegroProducerThresholdRedLightAmount ?? 5m,
                redAmt = pvFull?.AllegroProducerThresholdRedAmount ?? 20m,
                redDarkAmt = pvFull?.AllegroProducerThresholdRedDarkAmount ?? 50m
            };

            return Json(new
            {
                productName = product.AllegroProductName,
                isMapMode,
                producerThresholds = thresholds,
                timelineData
            });
        }



        [HttpPost]
        public async Task<IActionResult> ExecutePriceChange(int storeId, [FromBody] List<AllegroPriceBridgeItemRequest> items)
        {

            _logger.LogInformation($">>> [EXECUTE START] Próba ręcznej zmiany cen...");

            if (items == null)
            {

                _logger.LogError("!!! CRITICAL !!! Lista 'items' jest NULL. Frontend nie przesłał danych.");
            }
            else
            {

                _logger.LogInformation(">>> Otrzymano {Count} elementów do zmiany.", items.Count);

                foreach (var item in items)
                {

                    _logger.LogInformation("   -> ITEM: ProductId={ProductId}, OfferId (Allegro)={OfferId}, NewPrice={Price}, Mode={Mode}",
                        item.ProductId, item.OfferId, item.PriceAfter_Simulated, item.Mode);

                    if (string.IsNullOrEmpty(item.OfferId))
                    {

                        _logger.LogWarning("   !!! UWAGA !!! OfferId jest PUSTE dla produktu {ProductId}. To spowoduje błąd w serwisie!", item.ProductId);
                    }
                }
            }

            if (!await UserHasAccessToStore(storeId))
            {

                _logger.LogWarning("!!! ACCESS DENIED !!! Użytkownik nie ma dostępu do sklepu {StoreId}", storeId);
                return Forbid();
            }

            if (items == null || !items.Any())
            {

                _logger.LogWarning("!!! BAD REQUEST !!! Brak elementów do przetworzenia.");
                return BadRequest("Brak zmian do przetworzenia.");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var latestScrap = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            if (latestScrap == 0)
            {

                _logger.LogError("!!! ERROR !!! Nie znaleziono latestScrap (ID=0) dla sklepu {StoreId}.", storeId);
                return BadRequest("Nie znaleziono historii analizy dla tego sklepu.");
            }

            var priceSettings = await _context.PriceValues.FirstOrDefaultAsync(pv => pv.StoreId == storeId);
            bool includeCommissionInMargin = priceSettings?.AllegroIncludeCommisionInPriceChange ?? false;

            _logger.LogInformation(">>> Wywołuję serwis _priceBridgeService.ExecutePriceChangesAsync dla {Count} elementów...", items.Count);

            var result = await _priceBridgeService.ExecutePriceChangesAsync(
                storeId,
                latestScrap,
                userId,
                includeCommissionInMargin,
                items
            );

            _logger.LogInformation(">>> [EXECUTE END] Wynik serwisu: Success={Success}, Failed={Failed}", result.SuccessfulCount, result.FailedCount);

            if (result.Errors.Any())
            {
                foreach (var error in result.Errors)
                {
                    _logger.LogError("!!! PRZYCZYNA BŁĘDU !!! OfferId: {OfferId}, Message: {Message}", error.OfferId, error.Message);
                }
            }

            return Json(result);
        }


        [HttpGet]
        public async Task<IActionResult> GetAllegroApiExportSettings(int storeId)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => new
                {
                    isApiExportEnabled = s.AllegroIsApiExportEnabled,
                    apiExportToken = s.AllegroApiExportToken
                })
                .FirstOrDefaultAsync();

            if (store == null) return NotFound();
            return Json(store);
        }

        public class AllegroApiExportSettingsDto
        {
            public bool IsEnabled { get; set; }
            public string Token { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> SaveAllegroApiExportSettings(int storeId, [FromBody] AllegroApiExportSettingsDto dto)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores.FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null) return NotFound("Sklep nie istnieje");

            store.AllegroIsApiExportEnabled = dto.IsEnabled;
            store.AllegroApiExportToken = dto.Token;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Ustawienia Feed API Allegro zostały zapisane." });
        }
        public class PriceBridgeError
        {
            public string OfferId { get; set; }
            public string Message { get; set; }
        }

        public class PriceBridgeSuccessDetail
        {
            public string OfferId { get; set; }
            public decimal? FetchedNewPrice { get; set; }
            public decimal? FetchedNewCommission { get; set; }
        }

        public class PriceBridgeResult
        {
            public int SuccessfulCount { get; set; } = 0;
            public int FailedCount { get; set; } = 0;
            public List<PriceBridgeError> Errors { get; set; } = new List<PriceBridgeError>();

            public List<PriceBridgeSuccessDetail> SuccessfulChangesDetails { get; set; } = new List<PriceBridgeSuccessDetail>();
        }

        public class AllegroTrendDataViewModel
        {
            public string ProductName { get; set; }
            public long? MainOfferId { get; set; }
            public List<DailyTrendPointViewModel> TimelineData { get; set; }
        }

        public class DailyTrendPointViewModel
        {
            public string ScrapDate { get; set; }
            public int TotalSales { get; set; }
            public List<OfferTrendPointViewModel> Offers { get; set; }
        }

        public class OfferTrendPointViewModel
        {
            public string StoreName { get; set; }
            public decimal Price { get; set; }
            public int Sales { get; set; }
            public string Source { get; set; }
            public long IdAllegro { get; set; }
            public bool IsBestPriceGuarantee { get; set; }
            public bool TopOffer { get; set; }
            public bool SuperPrice { get; set; }
        }

        public class AllegroPriceMarginSettingsViewModel
        {
            public int StoreId { get; set; }
            public string AllegroIdentifierForSimulation { get; set; }
            public bool AllegroUseMarginForSimulation { get; set; }
            public bool AllegroEnforceMinimalMargin { get; set; }
            public decimal AllegroMinimalMarginPercent { get; set; }
            public bool AllegroIncludeCommisionInPriceChange { get; set; }
            public bool AllegroChangePriceForBagdeSuperPrice { get; set; }
            public bool AllegroChangePriceForBagdeTopOffer { get; set; }
            public bool AllegroChangePriceForBagdeBestPriceGuarantee { get; set; }
            public bool AllegroChangePriceForBagdeInCampaign { get; set; }
        }

        public class SimulationItem
        {
            public int ProductId { get; set; }
            public decimal CurrentPrice { get; set; }
            public decimal NewPrice { get; set; }
            public int StoreId { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> GetScrapPriceChangeHistory(int storeId, int allegroScrapeHistoryId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var hasAccess = await _context.Stores
                .AsNoTracking()
                .Where(s => s.StoreId == storeId)
                .AnyAsync(s => s.UserStores.Any(us => us.UserId == userId));

            if (!hasAccess && !User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return Forbid();
            }

            if (allegroScrapeHistoryId == 0)
            {
                return BadRequest("Invalid Scrap History ID.");
            }

            var batches = await _context.AllegroPriceBridgeBatches
                .AsNoTracking()
                .Where(b => b.StoreId == storeId && b.AllegroScrapeHistoryId == allegroScrapeHistoryId)
                .Include(b => b.User)

                .Include(b => b.AutomationRule)
                .Include(b => b.BridgeItems)
                    .ThenInclude(i => i.AllegroProduct)
                .OrderByDescending(b => b.ExecutionDate)
                .ToListAsync();

            var result = batches.Select(b => new
            {
                executionDate = b.ExecutionDate,

                userName = b.User != null ? b.User.UserName : (b.IsAutomation ? "Automat Cenowy" : "System/Nieznany"),

                isAutomation = b.IsAutomation,
                automationRuleName = b.AutomationRule?.Name,
                automationRuleColor = b.AutomationRule?.ColorHex,

                successfulCount = b.SuccessfulCount,
                failedCount = b.FailedCount,

                exportMethod = "Api",

                items = b.BridgeItems.Select(i => new
                {
                    productId = i.AllegroProductId,
                    productName = i.AllegroProduct?.AllegroProductName ?? "Produkt usunięty lub nieznany",
                    offerId = i.AllegroOfferId,
                    ean = i.AllegroProduct?.AllegroEan,
                    allegroOfferUrl = i.AllegroProduct?.AllegroOfferUrl,
                    success = i.Success,
                    errorMessage = i.ErrorMessage,

                    marginPrice = i.MarginPrice,
                    includeCommissionInMargin = i.IncludeCommissionInMargin,

                    priceBefore = i.PriceBefore,
                    commissionBefore = i.CommissionBefore,
                    rankingBefore = i.RankingBefore,

                    priceAfter_Simulated = i.PriceAfter_Simulated,
                    rankingAfter_Simulated = i.RankingAfter_Simulated,

                    priceAfter_Verified = i.PriceAfter_Verified,
                    commissionAfter_Verified = i.CommissionAfter_Verified,
                    mode = i.Mode,
                    priceIndexTarget = i.PriceIndexTarget,
                    stepPriceApplied = i.StepPriceApplied,
                }).ToList()
            }).ToList();

            return Ok(result);
        }


        [HttpGet]
        public async Task<IActionResult> GetAllegroProducerSettings(int storeId)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var pv = await _context.PriceValues.AsNoTracking()
                .FirstOrDefaultAsync(p => p.StoreId == storeId);

            if (pv == null)
            {
                return Json(new
                {
                    storeId,
                    comparisonSource = (int)ProducerComparisonSource.MapPrice,
                    useAmount = false,
                    identifierForSimulation = "EAN",
                    redDarkPct = 20.00m,
                    redPct = 10.00m,
                    redLightPct = 1.00m,
                    greenLightPct = 1.00m,
                    greenPct = 10.00m,
                    greenDarkPct = 20.00m,
                    redDarkAmt = 50.00m,
                    redAmt = 20.00m,
                    redLightAmt = 5.00m,
                    greenLightAmt = 5.00m,
                    greenAmt = 20.00m,
                    greenDarkAmt = 50.00m
                });
            }

            return Json(new
            {
                storeId,
                comparisonSource = (int)pv.AllegroProducerComparisonSource,
                useAmount = pv.AllegroProducerUseAmount,
                identifierForSimulation = pv.AllegroIdentifierForSimulation ?? "EAN",
                redDarkPct = pv.AllegroProducerThresholdRedDarkPercent,
                redPct = pv.AllegroProducerThresholdRedPercent,
                redLightPct = pv.AllegroProducerThresholdRedLightPercent,
                greenLightPct = pv.AllegroProducerThresholdGreenLightPercent,
                greenPct = pv.AllegroProducerThresholdGreenPercent,
                greenDarkPct = pv.AllegroProducerThresholdGreenDarkPercent,
                redDarkAmt = pv.AllegroProducerThresholdRedDarkAmount,
                redAmt = pv.AllegroProducerThresholdRedAmount,
                redLightAmt = pv.AllegroProducerThresholdRedLightAmount,
                greenLightAmt = pv.AllegroProducerThresholdGreenLightAmount,
                greenAmt = pv.AllegroProducerThresholdGreenAmount,
                greenDarkAmt = pv.AllegroProducerThresholdGreenDarkAmount
            });
        }

        [HttpPost]
        public async Task<IActionResult> SaveAllegroProducerSettings([FromBody] AllegroProducerSettingsViewModel model)
        {
            if (model == null || model.StoreId <= 0) return BadRequest("Invalid data.");
            if (!await UserHasAccessToStore(model.StoreId)) return Forbid();

            if (model.AllegroProducerThresholdRedDarkPercent < model.AllegroProducerThresholdRedPercent
                || model.AllegroProducerThresholdRedPercent < model.AllegroProducerThresholdRedLightPercent
                || model.AllegroProducerThresholdGreenDarkPercent < model.AllegroProducerThresholdGreenPercent
                || model.AllegroProducerThresholdGreenPercent < model.AllegroProducerThresholdGreenLightPercent
                || model.AllegroProducerThresholdRedDarkAmount < model.AllegroProducerThresholdRedAmount
                || model.AllegroProducerThresholdRedAmount < model.AllegroProducerThresholdRedLightAmount
                || model.AllegroProducerThresholdGreenDarkAmount < model.AllegroProducerThresholdGreenAmount
                || model.AllegroProducerThresholdGreenAmount < model.AllegroProducerThresholdGreenLightAmount)
            {
                return BadRequest("Niepoprawna kolejność progów.");
            }

            var pv = await _context.PriceValues.FirstOrDefaultAsync(p => p.StoreId == model.StoreId);
            bool isNew = pv == null;
            if (isNew)
            {
                pv = new PriceValueClass { StoreId = model.StoreId };
                _context.PriceValues.Add(pv);
            }

            pv.AllegroIdentifierForSimulation = model.AllegroIdentifierForSimulation ?? "EAN";
            pv.AllegroProducerComparisonSource = model.AllegroProducerComparisonSource;
            pv.AllegroProducerUseAmount = model.AllegroProducerUseAmount;

            pv.AllegroProducerThresholdRedDarkPercent = model.AllegroProducerThresholdRedDarkPercent;
            pv.AllegroProducerThresholdRedPercent = model.AllegroProducerThresholdRedPercent;
            pv.AllegroProducerThresholdRedLightPercent = model.AllegroProducerThresholdRedLightPercent;
            pv.AllegroProducerThresholdGreenLightPercent = model.AllegroProducerThresholdGreenLightPercent;
            pv.AllegroProducerThresholdGreenPercent = model.AllegroProducerThresholdGreenPercent;
            pv.AllegroProducerThresholdGreenDarkPercent = model.AllegroProducerThresholdGreenDarkPercent;

            pv.AllegroProducerThresholdRedDarkAmount = model.AllegroProducerThresholdRedDarkAmount;
            pv.AllegroProducerThresholdRedAmount = model.AllegroProducerThresholdRedAmount;
            pv.AllegroProducerThresholdRedLightAmount = model.AllegroProducerThresholdRedLightAmount;
            pv.AllegroProducerThresholdGreenLightAmount = model.AllegroProducerThresholdGreenLightAmount;
            pv.AllegroProducerThresholdGreenAmount = model.AllegroProducerThresholdGreenAmount;
            pv.AllegroProducerThresholdGreenDarkAmount = model.AllegroProducerThresholdGreenDarkAmount;

            if (!isNew) _context.PriceValues.Update(pv);
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Ustawienia producenta Allegro zapisane." });
        }

        public class AllegroProducerSettingsViewModel
        {
            public int StoreId { get; set; }
            public string AllegroIdentifierForSimulation { get; set; }
            public ProducerComparisonSource AllegroProducerComparisonSource { get; set; }
            public bool AllegroProducerUseAmount { get; set; }

            public decimal AllegroProducerThresholdRedDarkPercent { get; set; }
            public decimal AllegroProducerThresholdRedPercent { get; set; }
            public decimal AllegroProducerThresholdRedLightPercent { get; set; }
            public decimal AllegroProducerThresholdGreenLightPercent { get; set; }
            public decimal AllegroProducerThresholdGreenPercent { get; set; }
            public decimal AllegroProducerThresholdGreenDarkPercent { get; set; }

            public decimal AllegroProducerThresholdRedDarkAmount { get; set; }
            public decimal AllegroProducerThresholdRedAmount { get; set; }
            public decimal AllegroProducerThresholdRedLightAmount { get; set; }
            public decimal AllegroProducerThresholdGreenLightAmount { get; set; }
            public decimal AllegroProducerThresholdGreenAmount { get; set; }
            public decimal AllegroProducerThresholdGreenDarkAmount { get; set; }
        }



        private string ResolveAllegroProducerBucket(decimal deltaAbsolute, decimal deltaPercent, AllegroProducerSettings s)
        {
            decimal value = s.UseAmount ? deltaAbsolute : deltaPercent;

            decimal redDark = s.UseAmount ? s.RedDarkAmt : s.RedDarkPct;
            decimal red = s.UseAmount ? s.RedAmt : s.RedPct;
            decimal redLight = s.UseAmount ? s.RedLightAmt : s.RedLightPct;
            decimal greenLight = s.UseAmount ? s.GreenLightAmt : s.GreenLightPct;
            decimal green = s.UseAmount ? s.GreenAmt : s.GreenPct;
            decimal greenDark = s.UseAmount ? s.GreenDarkAmt : s.GreenDarkPct;

            if (value <= -redDark) return "producer-deep-violation";
            if (value <= -red) return "producer-violation";
            if (value <= -redLight) return "producer-minor-below";
            if (value < greenLight) return "producer-equal";
            if (value < green) return "producer-minor-above";
            if (value < greenDark) return "producer-above";
            return "producer-deep-above";
        }

        private class AllegroProducerSettings
        {
            public string IdentifierForSimulation { get; set; } = "EAN";
            public ProducerComparisonSource ComparisonSource { get; set; } = ProducerComparisonSource.MapPrice;
            public bool UseAmount { get; set; } = false;

            public decimal RedDarkPct { get; set; } = 20.00m;
            public decimal RedPct { get; set; } = 10.00m;
            public decimal RedLightPct { get; set; } = 1.00m;
            public decimal GreenLightPct { get; set; } = 1.00m;
            public decimal GreenPct { get; set; } = 10.00m;
            public decimal GreenDarkPct { get; set; } = 20.00m;

            public decimal RedDarkAmt { get; set; } = 50.00m;
            public decimal RedAmt { get; set; } = 20.00m;
            public decimal RedLightAmt { get; set; } = 5.00m;
            public decimal GreenLightAmt { get; set; } = 5.00m;
            public decimal GreenAmt { get; set; } = 20.00m;
            public decimal GreenDarkAmt { get; set; } = 50.00m;
        }
    }
}