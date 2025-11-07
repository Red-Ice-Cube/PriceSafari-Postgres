using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using PriceSafari.Services.AllegroServices;
using PriceSafari.ViewModels;
using System.Security.Claims;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    public class AllegroPriceHistoryController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly AllegroPriceBridgeService _priceBridgeService;

        public AllegroPriceHistoryController(
            PriceSafariContext context,
            UserManager<PriceSafariUser> userManager,
            AllegroPriceBridgeService priceBridgeService)
        {
            _context = context;
            _userManager = userManager;
            _priceBridgeService = priceBridgeService;
        }

        private async Task<bool> UserHasAccessToStore(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "Manager"))
            {
                return true;
            }
            return await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
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

            ViewBag.StoreId = store.StoreId;
            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreLogo = store.StoreLogoUrl;
            ViewBag.LatestScrap = latestScrap;
            ViewBag.Flags = flags;

            ViewBag.IsAllegroPriceBridgeActive = store.IsAllegroPriceBridgeActive;

            return View("~/Views/Panel/AllegroPriceHistory/Index.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetAllegroPrices(int? storeId)
        {
            if (storeId == null) return BadRequest();
            if (!await UserHasAccessToStore(storeId.Value)) return Forbid();

            var store = await _context.Stores.FindAsync(storeId.Value);
            var priceSettings = await _context.PriceValues
                .FirstOrDefaultAsync(pv => pv.StoreId == storeId.Value);

            var latestScrap = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .FirstOrDefaultAsync();

            if (latestScrap == null || store == null)
            {
                return Json(new { myStoreName = store?.StoreNameAllegro, prices = new List<object>() });
            }

            var committedChanges = await _context.AllegroPriceBridgeItems
                .Include(i => i.PriceBridgeBatch)
                .Where(i => i.PriceBridgeBatch.StoreId == storeId.Value && i.PriceBridgeBatch.AllegroScrapeHistoryId == latestScrap.Id && i.Success)
                .ToListAsync();

          
            var committedLookup = committedChanges
                .GroupBy(i => i.AllegroProductId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.PriceBridgeBatch.ExecutionDate).First());
           

            var activePreset = await _context.CompetitorPresets
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.StoreId == storeId.Value && p.NowInUse && p.Type == PresetType.Marketplace);

            string activePresetName = activePreset?.PresetName;

            var competitorRules = activePreset?.CompetitorItems
                .Where(ci => ci.DataSource == DataSourceType.Allegro)
                .ToDictionary(
                    ci => ci.StoreName.ToLower().Trim(),
                    ci => ci.UseCompetitor
                );

            var priceData = await _context.AllegroPriceHistories
                .Where(aph => aph.AllegroScrapeHistoryId == latestScrap.Id && aph.AllegroProduct.IsScrapable)
                .Include(aph => aph.AllegroProduct)
                .ToListAsync();

            var productIds = priceData.Select(p => p.AllegroProductId).Distinct().ToList();
            var productFlagsDictionary = await _context.ProductFlags
                .Where(pf => productIds.Contains(pf.AllegroProductId.Value))
                .GroupBy(pf => pf.AllegroProductId.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            var allExtendedInfo = await _context.AllegroPriceHistoryExtendedInfos
                .Where(e => e.ScrapHistoryId == latestScrap.Id)
                .ToListAsync();

            var extendedInfoDictionary = allExtendedInfo
                .GroupBy(e => e.AllegroProductId)
                .ToDictionary(g => g.Key, g => g.First());

            var groupedData = priceData
                .GroupBy(aph => aph.AllegroProduct)
                .Select(g =>
                {
                    var product = g.Key;
                    long? targetOfferId = null;
                    if (!string.IsNullOrEmpty(product.AllegroOfferUrl))
                    {
                        var idString = product.AllegroOfferUrl.Split('-').LastOrDefault();
                        if (long.TryParse(idString, out var parsedId))
                        {
                            targetOfferId = parsedId;
                        }
                    }

                    var myOffer = targetOfferId.HasValue
                        ? g.FirstOrDefault(p => p.IdAllegro == targetOfferId.Value && p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                        : g.FirstOrDefault(p => p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase));

                    List<AllegroPriceHistory> filteredCompetitors;
                    if (activePreset != null && competitorRules != null)
                    {
                        filteredCompetitors = g.Where(p =>
                        {
                            if (p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                            {
                                return false;
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

                    var allMyOffersInGroup = g.Where(p => p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase));
                    var myOffersGroupKey = string.Join(",", allMyOffersInGroup.Select(o => o.IdAllegro).OrderBy(id => id));

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
                    return new
                    {
                        ProductId = product.AllegroProductId,
                        ProductName = product.AllegroProductName,
                        Producer = (string)null,
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
                        MyIdAllegro = myOffer?.IdAllegro,
                        MyOffersGroupKey = myOffersGroupKey,
                        MyDeliveryTime = myOffer?.DeliveryTime,
                        MyIsSuperSeller = myOffer?.SuperSeller ?? false,
                        MyIsSmart = myOffer?.Smart ?? false,
                        MyIsBestPriceGuarantee = myOffer?.IsBestPriceGuarantee ?? false,
                        MyIsTopOffer = myOffer?.TopOffer ?? false,
                        MyIsSuperPrice = myOffer?.SuperPrice ?? false,
                        MyIsPromoted = myOffer?.Promoted ?? false,
                        MyIsSponsored = myOffer?.Sponsored ?? false,
                        IsRejected = false,
                        OnlyMe = (myOffer != null && !filteredCompetitors.Any()),
                        Savings = (myOffer != null && bestCompetitor != null && myOffer.Price < bestCompetitor.Price) ? bestCompetitor.Price - myOffer.Price : (decimal?)null,
                        PriceDifference = (myOffer != null && bestCompetitor != null) ? myOffer.Price - bestCompetitor.Price : (decimal?)null,

                        PercentageDifference = (myOffer != null && bestCompetitor != null && bestCompetitor.Price > 0) ? Math.Round(((myOffer.Price - bestCompetitor.Price) / bestCompetitor.Price) * 100, 2) : (decimal?)null,

                        IsUniqueBestPrice = (myOffer != null && bestCompetitor != null && myOffer.Price < bestCompetitor.Price),
                        IsSharedBestPrice = (myOffer != null && bestCompetitor != null && myOffer.Price == bestCompetitor.Price),
                        FlagIds = productFlagsDictionary.GetValueOrDefault(product.AllegroProductId, new List<int>()),

                        Ean = product.AllegroEan,

                        ExternalId = (int?)null,

                        MarginPrice = product.AllegroMarginPrice,

                        ImgUrl = (string)null,
                        ApiAllegroPrice = extendedInfo?.ApiAllegroPrice,
                        ApiAllegroPriceFromUser = extendedInfo?.ApiAllegroPriceFromUser,
                        ApiAllegroCommission = extendedInfo?.ApiAllegroCommission,
                        AnyPromoActive = extendedInfo?.AnyPromoActive,
                        Committed = committed == null ? null : new
                        {
                            // Preferujemy cenę zweryfikowaną, jeśli jest dostępna, w przeciwnym razie symulowaną
                            NewPrice = committed.PriceAfter_Verified ?? committed.PriceAfter_Simulated,
                            // Nowa prowizja (jeśli dostępna)
                            NewCommission = committed.CommissionAfter_Verified ?? (committed.IncludeCommissionInMargin ? (decimal?)null : null), // Możesz tu dodać logikę symulacji prowizji jeśli chcesz
                                                                                                                                                 // Nowa pozycja (ranking)
                            NewPosition = committed.RankingAfter_Simulated
                        }
                    };
                }).ToList();

            return Json(new
            {
                myStoreName = store.StoreNameAllegro,
                prices = groupedData,
                priceCount = priceData.Count,
                setPrice1 = priceSettings?.AllegroSetPrice1 ?? 2.00m,
                setPrice2 = priceSettings?.AllegroSetPrice2 ?? 2.00m,
                stepPrice = priceSettings?.AllegroPriceStep ?? 2.00m,
                usePriceDifference = priceSettings?.AllegroUsePriceDiff ?? true,
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

        public class PriceSettingsViewModel
        {
            public int StoreId { get; set; }
            public decimal SetPrice1 { get; set; }
            public decimal SetPrice2 { get; set; }
            public decimal PriceStep { get; set; }
            public bool UsePriceDifference { get; set; }
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
            allegroOfferUrl = p.AllegroOfferUrl // <-- DODAJ TĘ LINIĘ
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
        public async Task<IActionResult> Details(int storeId, int productId)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var product = await _context.AllegroProducts.FindAsync(productId);
            if (product == null) return NotFound("Product not found.");

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound("Store not found.");

            ViewBag.StoreId = storeId;
            ViewBag.ProductId = productId;
            ViewBag.ProductName = product.AllegroProductName;
            ViewBag.StoreName = store.StoreNameAllegro;
            ViewBag.AllegroOfferUrl = product.AllegroOfferUrl;

            return View("~/Views/Panel/AllegroPriceHistory/Details.cshtml");
        }
        [HttpGet]
        public async Task<IActionResult> GetProductPriceDetails(int storeId, int productId)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound("Store not found.");

            var product = await _context.AllegroProducts.FindAsync(productId);
            if (product == null) return NotFound("Product not found.");

            var latestScrap = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .FirstOrDefaultAsync();

            if (latestScrap == null)
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

            var allOffersForProduct = await _context.AllegroPriceHistories
                .Where(aph => aph.AllegroScrapeHistoryId == latestScrap.Id &&
                                aph.AllegroProductId == productId &&
                                aph.Price > 0)
                .ToListAsync();

            List<AllegroPriceHistory> filteredOffers;
            if (activePreset != null && competitorRules != null)
            {
                var ourStoreNameLower = store.StoreNameAllegro.ToLower().Trim();
                filteredOffers = allOffersForProduct.Where(p =>
                {
                    if (p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
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
                filteredOffers = allOffersForProduct;
            }

            filteredOffers = filteredOffers.OrderBy(o => o.Price).ToList();

            var myOfferIdsInList = filteredOffers
                .Where(aph => aph.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                .Select(aph => aph.IdAllegro)
                .ToList();

            var allMyProducts = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId)
                .Select(p => new { p.AllegroProductId, p.AllegroOfferUrl })
                .ToListAsync();

            var navigationMap = new Dictionary<long, int>();
            foreach (var offerId in myOfferIdsInList)
            {
                var matchingProduct = allMyProducts.FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.AllegroOfferUrl) && p.AllegroOfferUrl.EndsWith($"-{offerId}")
                );
                if (matchingProduct != null)
                {
                    navigationMap[offerId] = matchingProduct.AllegroProductId;
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
                TargetProductId = navigationMap.ContainsKey(aph.IdAllegro) ? navigationMap[aph.IdAllegro] : (int?)null
            }).ToList();

            long? mainOfferId = null;
            if (!string.IsNullOrEmpty(product.AllegroOfferUrl))
            {
                var idString = product.AllegroOfferUrl.Split('-').LastOrDefault();
                if (long.TryParse(idString, out var parsedId))
                {
                    mainOfferId = parsedId;
                }
            }

            var priceSettings = await _context.PriceValues.FirstOrDefaultAsync(pv => pv.StoreId == storeId);
            var totalPopularity = filteredOffers.Sum(o => o.Popularity ?? 0);

            return Json(new
            {
                mainOfferId,
                data = dataForJson,
                totalProductPopularity = totalPopularity,
                lastScrapeDate = latestScrap.Date,
                setPrice1 = priceSettings?.AllegroSetPrice1 ?? 0.01m,
                setPrice2 = priceSettings?.AllegroSetPrice2 ?? 2.00m,
                activePresetName = activePresetName
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetPriceTrendData(int productId)
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
            if (!string.IsNullOrEmpty(product.AllegroOfferUrl))
            {
                var idString = product.AllegroOfferUrl.Split('-').LastOrDefault();
                if (long.TryParse(idString, out var parsedId))
                {
                    mainOfferId = parsedId;
                }
            }

            var activePreset = await _context.CompetitorPresets
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.StoreId == storeId && p.NowInUse && p.Type == PresetType.Marketplace);

            var competitorRules = activePreset?.CompetitorItems
                .Where(ci => ci.DataSource == DataSourceType.Allegro)
                .ToDictionary(ci => ci.StoreName.ToLower().Trim(), ci => ci.UseCompetitor);

            var lastScraps = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Take(30)
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

            var timelineData = lastScraps.Select(scrap => {
                var allDailyOffers = priceHistories
                    .Where(ph => ph.AllegroScrapeHistoryId == scrap.Id);

                List<AllegroPriceHistory> filteredDailyOffers;
                if (activePreset != null && competitorRules != null)
                {
                    filteredDailyOffers = allDailyOffers.Where(p =>
                    {
                        if (p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                            return true;

                        var sellerNameLower = (p.SellerName ?? "").ToLower().Trim();
                        if (competitorRules.TryGetValue(sellerNameLower, out bool useCompetitor))
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

            var viewModel = new AllegroTrendDataViewModel
            {
                ProductName = product.AllegroProductName,
                TimelineData = timelineData,
                MainOfferId = mainOfferId
            };

            return Json(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> ExecutePriceChange(int storeId, [FromBody] List<AllegroPriceBridgeItemRequest> items)
        {
            if (!await UserHasAccessToStore(storeId)) return Forbid();
            if (items == null || !items.Any()) return BadRequest("Brak zmian do przetworzenia.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var latestScrap = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            if (latestScrap == 0)
            {
                return BadRequest("Nie znaleziono historii analizy dla tego sklepu.");
            }

            var priceSettings = await _context.PriceValues.FirstOrDefaultAsync(pv => pv.StoreId == storeId);
            bool includeCommissionInMargin = priceSettings?.AllegroIncludeCommisionInPriceChange ?? false;

            var result = await _priceBridgeService.ExecutePriceChangesAsync(
                storeId,
                latestScrap,
                userId,
                includeCommissionInMargin,
                items
            );

            return Json(result);
        }

        public class AllegroPriceBridgeItemRequest
        {

            public int ProductId { get; set; }
            public string OfferId { get; set; }
            public decimal? MarginPrice { get; set; }
            public bool IncludeCommissionInMargin { get; set; }

            public decimal PriceBefore { get; set; }
            public decimal? CommissionBefore { get; set; }
            public string RankingBefore { get; set; }

            public decimal PriceAfter_Simulated { get; set; }
            public string RankingAfter_Simulated { get; set; }
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

            if (!hasAccess)
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
                .Include(b => b.BridgeItems)
                    .ThenInclude(i => i.AllegroProduct)
                .OrderByDescending(b => b.ExecutionDate)
                .ToListAsync();

            var result = batches.Select(b => new
            {
                executionDate = b.ExecutionDate,
                userName = b.User?.UserName ?? "Nieznany",
                successfulCount = b.SuccessfulCount,
                failedCount = b.FailedCount,
                items = b.BridgeItems.Select(i => new
                {

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
                    commissionAfter_Verified = i.CommissionAfter_Verified
                }).ToList()
            }).ToList();

            return Ok(result);
        }

    }
}