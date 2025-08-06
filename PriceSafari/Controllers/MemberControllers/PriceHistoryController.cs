using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.ViewModels;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using PriceSafari.Hubs;
using PriceSafari.Models.ViewModels;
using AngleSharp.Dom;
using NPOI.SS.Formula.Functions;

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
               .Select(f => new FlagViewModel // Projekcja na prosty ViewModel
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
                    pv.UsePriceWithDelivery,
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
                                AvailabilityNum = (ph != null ? ph.AvailabilityNum : (int?)null),
                                IsRejected = p.IsRejected,

                                ShippingCostNum = (ph != null ? ph.ShippingCostNum : (decimal?)null)
                            };

            var activePreset = await _context.CompetitorPresets
                .Include(x => x.CompetitorItems)
                .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse);

            string activePresetName = null;

            if (activePreset != null)
            {
                activePresetName = activePreset.PresetName;

                if (!activePreset.SourceGoogle)
                {
                    baseQuery = baseQuery.Where(p => p.IsGoogle != true);
                }

                if (!activePreset.SourceCeneo)
                {
                    baseQuery = baseQuery.Where(p => p.IsGoogle == true);
                }
            }
            else
            {
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
            else
            {

            }

            if (activePreset != null)
            {
                var competitorItemsDict = activePreset.CompetitorItems
                    .GroupBy(ci => new
                    {
                        Store = ci.StoreName.ToLower().Trim(),
                        Source = ci.IsGoogle
                    })
                    .Select(g => g.First())
                    .ToDictionary(
                        x => new
                        {
                            Store = x.StoreName.ToLower().Trim(),
                            Source = x.IsGoogle
                        },
                        x => x.UseCompetitor
                    );

                var storeNameLower = storeName.ToLower().Trim();
                var filteredPrices = new List<PriceRowDto>();

                foreach (var row in rawPrices)
                {
                    if (row.StoreName != null &&
                        row.StoreName.ToLower().Trim() == storeNameLower)
                    {
                        filteredPrices.Add(row);
                        continue;
                    }

                    bool googleFlag = (row.IsGoogle == true);
                    var key = new
                    {
                        Store = (row.StoreName ?? "").ToLower().Trim(),
                        Source = googleFlag
                    };

                    if (competitorItemsDict.TryGetValue(key, out bool useCompetitor))
                    {
                        if (useCompetitor) filteredPrices.Add(row);
                    }
                    else
                    {
                        if (activePreset.UseUnmarkedStores)
                            filteredPrices.Add(row);
                    }
                }

                rawPrices = filteredPrices;
            }

            // Nowy, wydajny kod
            var productIds = rawPrices.Select(p => p.ProductId).ToList();

            var productFlagsDictionary = await _context.ProductFlags
                .Where(pf => pf.ProductId.HasValue && productIds.Contains(pf.ProductId.Value))
                .GroupBy(pf => pf.ProductId.Value)
                .ToDictionaryAsync(
                    g => g.Key,
                    g => g.Select(pf => pf.FlagId).ToList()
                );

            var productsWithExternalInfo = await _context.Products
                .Where(p => p.StoreId == storeId && p.ExternalId.HasValue)
                .Select(p => new
                {
                    p.ProductId,
                    p.ExternalId,
                    p.ExternalPrice,
                    p.MainUrl,
                    p.MarginPrice,
                    p.Ean,
                    p.ProducerCode
                })
                .ToListAsync();

            var productExternalInfoDictionary = productsWithExternalInfo
                .ToDictionary(
                    p => p.ProductId,
                    p => new
                    {
                        p.ExternalId,
                        p.ExternalPrice,
                        p.MainUrl,
                        p.MarginPrice,
                        p.Ean,
                        p.ProducerCode
                    }
                );

            var allPrices = rawPrices
                .GroupBy(p => p.ProductId)
                .Select(g =>
                {
                    var product = g.First();
                    var storeCount = g
                        .Select(x => x.StoreName)
                        .Where(s => s != null)
                        .Distinct()
                        .Count();

                    bool sourceGoogle = g.Any(x => x.IsGoogle == true);
                    bool sourceCeneo = g.Any(x => x.IsGoogle == false);

                    var myPriceEntries = g.Where(x =>
                        x.StoreName != null &&
                        x.StoreName.ToLower() == storeName.ToLower()
                    );
                    var myPriceEntry = myPriceEntries
                        .OrderByDescending(x => x.IsGoogle == false)
                        .FirstOrDefault();

                    var validPrices = g.Where(x => x.Price.HasValue).ToList();

                    bool onlyMe = validPrices.Count() > 0 &&
                                  validPrices.All(x =>
                                      x.StoreName != null &&
                                      x.StoreName.ToLower() == storeName.ToLower()
                                  );

                    var bestPriceEntry = validPrices
                        .OrderBy(x => x.Price)
                        .ThenBy(x => x.StoreName)
                        .ThenByDescending(x => x.IsGoogle == false)
                        .FirstOrDefault();

                    bool? bestPriceIncludesDeliveryFlag = null;
                    bool? myPriceIncludesDeliveryFlag = null;

                    if (priceValues.UsePriceWithDelivery)
                    {

                        bestPriceIncludesDeliveryFlag = bestPriceEntry?.ShippingCostNum.HasValue;

                        myPriceIncludesDeliveryFlag = myPriceEntry?.ShippingCostNum.HasValue;
                    }

                    decimal? bestPrice = bestPriceEntry?.Price;
                    decimal? myPrice = myPriceEntry?.Price ?? bestPrice;
                    decimal? priceDifference = null;
                    decimal? percentageDifference = null;
                    decimal? savings = null;
                    bool isUniqueBestPrice = false;
                    int? myPosition = myPriceEntry?.Position;
                    int? myDelivery = myPriceEntry?.AvailabilityNum;
                    bool isRejectedDueToZeroPrice = false;

                    if (product.IsRejected || bestPrice == 0 || myPrice == 0)
                    {
                        percentageDifference = null;
                        priceDifference = null;
                        savings = null;
                        isUniqueBestPrice = false;
                        isRejectedDueToZeroPrice = true;
                    }
                    else if (bestPrice.HasValue && myPrice.HasValue)
                    {
                        var secondBestPrice = validPrices
                            .Where(x => x.Price > myPrice)
                            .OrderBy(x => x.Price)
                            .FirstOrDefault()?.Price ?? myPrice;

                        var bestPriceEntries = validPrices
                            .Where(x => x.Price == bestPrice)
                            .ToList();

                        bool allBestFromMyStore = bestPriceEntries.All(x =>
                            x.StoreName != null &&
                            x.StoreName.ToLower() == storeName.ToLower()
                        );

                        if (myPrice == bestPrice)
                        {
                            if (allBestFromMyStore || bestPriceEntries.Count() == 1)
                            {
                                isUniqueBestPrice = true;
                                if (secondBestPrice > myPrice)
                                {
                                    var secondBestPriceEntry = validPrices
                                        .FirstOrDefault(x => x.Price == secondBestPrice);
                                    if (secondBestPriceEntry != null)
                                    {
                                        bestPrice = secondBestPrice;
                                        bestPriceEntry = secondBestPriceEntry;
                                        savings = Math.Round(secondBestPrice.Value - myPrice.Value, 2);
                                        percentageDifference = Math.Round(
                                            (secondBestPrice.Value - myPrice.Value) / myPrice.Value * 100, 2
                                        );
                                        priceDifference = Math.Round(myPrice.Value - secondBestPrice.Value, 2);
                                    }
                                    else
                                    {
                                        savings = Math.Round(secondBestPrice.Value - bestPrice.Value, 2);
                                        percentageDifference = Math.Round(
                                            (secondBestPrice.Value - myPrice.Value) / myPrice.Value * 100, 2
                                        );
                                        priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
                                    }
                                }
                                else
                                {
                                    savings = null;
                                    percentageDifference = 0;
                                    priceDifference = 0;
                                }
                            }
                            else
                            {
                                isUniqueBestPrice = false;
                                savings = null;
                                priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
                                if (bestPrice.Value > 0)
                                {
                                    percentageDifference = Math.Round(
                                        (myPrice.Value - bestPrice.Value) / bestPrice.Value * 100, 2
                                    );
                                }
                            }
                        }
                        else
                        {
                            isUniqueBestPrice = false;
                            priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
                            if (bestPrice.Value > 0)
                            {
                                percentageDifference = Math.Round(
                                    (myPrice.Value - bestPrice.Value) / bestPrice.Value * 100, 2
                                );
                            }
                            savings = null;
                        }

                        var tiedBestPriceEntries = validPrices
                            .Where(x => x.Price == bestPrice)
                            .ToList();

                        if (tiedBestPriceEntries.Count() > 1 &&
                            tiedBestPriceEntries.Any(e => e.StoreName != null && e.StoreName.ToLower() == storeName.ToLower()))
                        {
                            var nonMyBest = tiedBestPriceEntries.FirstOrDefault(x => x.StoreName != null && x.StoreName.ToLower() != storeName.ToLower());
                            if (nonMyBest != null)
                            {
                                bestPriceEntry = nonMyBest;
                            }
                        }
                    }

                    if (onlyMe)
                    {
                        isUniqueBestPrice = false;
                    }

                    productFlagsDictionary.TryGetValue(g.Key, out var flagIds);
                    flagIds = flagIds ?? new List<int>();

                    var finalBestPriceEntries = validPrices
                        .Where(x => x.Price == bestPrice)
                        .ToList();

                    int externalBestPriceCount = finalBestPriceEntries.Count();
                    if (myPrice < bestPrice)
                    {
                        externalBestPriceCount = 0;
                    }

                    decimal? singleBestCheaperDiff = null;
                    decimal? singleBestCheaperDiffPerc = null;

                    decimal? absoluteLowestPrice = validPrices
                        .Where(x => x.Price.HasValue)
                        .Select(x => x.Price.Value)
                        .DefaultIfEmpty(0m)
                        .Min();

                    if (myPrice.HasValue && myPrice.Value > 0 && absoluteLowestPrice > 0)
                    {
                        if (myPrice.Value > absoluteLowestPrice)
                        {
                            int countStoresWithAbsoluteLowest = validPrices
                                .Count(x => x.Price.HasValue && x.Price.Value == absoluteLowestPrice);

                            if (countStoresWithAbsoluteLowest == 1)
                            {
                                var secondLowestPrice = validPrices
                                    .Where(x => x.Price.HasValue && x.Price.Value > absoluteLowestPrice)
                                    .Select(x => x.Price.Value)
                                    .OrderBy(x => x)
                                    .FirstOrDefault();

                                if (secondLowestPrice > 0)
                                {
                                    singleBestCheaperDiff = Math.Round(
                                        secondLowestPrice - absoluteLowestPrice.Value, 2
                                    );
                                    var diffPercent = (
                                        (secondLowestPrice - absoluteLowestPrice.Value)
                                        / secondLowestPrice
                                    ) * 100;
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
                        LowestPrice = bestPrice,
                        StoreName = bestPriceEntry?.StoreName,
                        MyPrice = myPriceEntry?.Price,
                        ScrapId = bestPriceEntry?.ScrapHistoryId,
                        PriceDifference = priceDifference,
                        PercentageDifference = percentageDifference,
                        Savings = savings,
                        IsSharedBestPrice = (
                            myPrice == bestPrice &&
                            validPrices.Count(x => x.Price == bestPrice) > 1 &&
                            !validPrices
                                .Where(x => x.Price == bestPrice)
                                .All(x =>
                                    x.StoreName != null &&
                                    x.StoreName.ToLower() == storeName.ToLower()
                                )
                        ),
                        IsUniqueBestPrice = isUniqueBestPrice,
                        OnlyMe = onlyMe,
                        ExternalBestPriceCount = externalBestPriceCount,
                        IsBidding = bestPriceEntry?.IsBidding,
                        IsGoogle = bestPriceEntry?.IsGoogle,
                        Position = bestPriceEntry?.Position,
                        MyIsBidding = myPriceEntry?.IsBidding,
                        MyIsGoogle = myPriceEntry?.IsGoogle,
                        MyPosition = myPosition,
                        FlagIds = flagIds,
                        Delivery = bestPriceEntry?.AvailabilityNum,
                        MyDelivery = myDelivery,
                        ExternalId = productExternalInfoDictionary.ContainsKey(g.Key)
                            ? productExternalInfoDictionary[g.Key].ExternalId
                            : null,
                        ExternalPrice = productExternalInfoDictionary.ContainsKey(g.Key)
                            ? productExternalInfoDictionary[g.Key].ExternalPrice
                            : null,
                        MarginPrice = productExternalInfoDictionary.ContainsKey(g.Key)
                            ? productExternalInfoDictionary[g.Key].MarginPrice
                            : null,
                        ImgUrl = productExternalInfoDictionary.ContainsKey(g.Key)
                            ? productExternalInfoDictionary[g.Key].MainUrl
                            : null,
                        Ean = productExternalInfoDictionary.ContainsKey(g.Key)
                            ? productExternalInfoDictionary[g.Key].Ean
                            : null,
                        ProducerCode = productExternalInfoDictionary.ContainsKey(g.Key)
                            ? productExternalInfoDictionary[g.Key].ProducerCode
                            : null,
                        IsRejected = product.IsRejected || isRejectedDueToZeroPrice,
                        StoreCount = storeCount,
                        SourceGoogle = sourceGoogle,
                        SourceCeneo = sourceCeneo,
                        SingleBestCheaperDiff = singleBestCheaperDiff,
                        SingleBestCheaperDiffPerc = singleBestCheaperDiffPerc,
                        BestPriceIncludesDelivery = bestPriceIncludesDeliveryFlag,
                        MyPriceIncludesDelivery = myPriceIncludesDeliveryFlag,
                        BestPriceDeliveryCost = priceValues.UsePriceWithDelivery ? bestPriceEntry?.ShippingCostNum : null,
                        MyPriceDeliveryCost = priceValues.UsePriceWithDelivery ? myPriceEntry?.ShippingCostNum : null
                    };
                })
                .Where(p => p != null)
                .ToList();

            var missedProductsCount = allPrices.Count(p => p.IsRejected);

            return Json(new
            {
                productCount = allPrices.Count(),
                priceCount = rawPrices.Count(),
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

                presetName = activePresetName ?? "PriceSafari"
            });
        }

        public class PriceRowDto
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public string? Producer { get; set; }
            public decimal? Price { get; set; }
            public string StoreName { get; set; }
            public int? ScrapHistoryId { get; set; }
            public int? Position { get; set; }
            public string IsBidding { get; set; }

            public bool? IsGoogle { get; set; }
            public int? AvailabilityNum { get; set; }
            public bool IsRejected { get; set; }
            public decimal? ShippingCostNum { get; set; }
        }


      
        


        [HttpGet]
        public async Task<IActionResult> GetPresets(int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return BadRequest("Nie ma takiego sklepu lub brak dostępu.");
            }

            var presets = await _context.CompetitorPresets
                .Where(p => p.StoreId == storeId)
                .Select(p => new
                {
                    p.PresetId,
                    p.PresetName,
                    p.NowInUse
                })
                .ToListAsync();

            return Json(presets);
        }

        [HttpGet]
        public async Task<IActionResult> GetPresetDetails(int presetId)
        {
            var preset = await _context.CompetitorPresets
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.PresetId == presetId);

            if (preset == null)
                return NotFound("Preset nie istnieje.");

            if (!await UserHasAccessToStore(preset.StoreId))
                return BadRequest("Brak dostępu do sklepu.");

            var result = new
            {
                presetId = preset.PresetId,
                presetName = preset.PresetName,
                nowInUse = preset.NowInUse,
                sourceGoogle = preset.SourceGoogle,
                sourceCeneo = preset.SourceCeneo,
                useUnmarkedStores = preset.UseUnmarkedStores,
                competitorItems = preset.CompetitorItems
                    .Select(ci => new
                    {
                        ci.StoreName,
                        ci.IsGoogle,
                        ci.UseCompetitor
                    }).ToList()
            };

            return Json(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetCompetitorStoresData(int storeId, string ourSource = "All")
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return Json(new { error = "Nie ma takiego sklepu" });
            }

            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return Json(new { data = new List<object>() });
            }

            var myPricesQuery = _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id
                          && ph.StoreName.ToLower() == storeName.ToLower());

            switch (ourSource?.ToLower())
            {
                case "google":
                    myPricesQuery = myPricesQuery.Where(ph => ph.IsGoogle == true);
                    break;

                case "ceneo":
                    myPricesQuery = myPricesQuery.Where(ph => ph.IsGoogle == false || ph.IsGoogle == null);
                    break;

                case "all":
                default:

                    break;
            }

            var myProductIds = await myPricesQuery
                .Select(ph => ph.ProductId)
                .Distinct()
                .ToListAsync();

            var competitorPrices = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id
                          && ph.StoreName.ToLower() != storeName.ToLower())
                .ToListAsync();

            var competitors = competitorPrices
                .GroupBy(ph => new { NormalizedName = ph.StoreName.ToLower(), ph.IsGoogle })
                .Select(g =>
                {
                    var storeNameInGroup = g.First().StoreName;
                    bool isGoogle = g.Key.IsGoogle;

                    var competitorProductIds = g
                        .Select(x => x.ProductId)
                        .Distinct();

                    int commonProductsCount = myProductIds
                        .Count(pid => competitorProductIds.Contains(pid));

                    return new
                    {
                        StoreName = storeNameInGroup,
                        DataSource = isGoogle ? "Google" : "Ceneo",
                        CommonProductsCount = commonProductsCount
                    };
                })
                .Where(c => c.CommonProductsCount >= 1)
                .OrderByDescending(c => c.CommonProductsCount)
                .ToList();

            return Json(new { data = competitors });
        }

        [HttpPost]
        public async Task<IActionResult> SaveOrUpdatePreset([FromBody] CompetitorPresetViewModel model)
        {
            if (!await UserHasAccessToStore(model.StoreId))
            {
                return BadRequest("Brak dostępu do sklepu.");
            }

            CompetitorPresetClass preset;
            if (model.PresetId == 0)
            {
                preset = new CompetitorPresetClass
                {
                    StoreId = model.StoreId,
                };
                _context.CompetitorPresets.Add(preset);
            }
            else
            {
                preset = await _context.CompetitorPresets
                    .Include(p => p.CompetitorItems)
                    .FirstOrDefaultAsync(p => p.PresetId == model.PresetId);

                if (preset == null)
                    return BadRequest("Taki preset nie istnieje.");

                if (preset.StoreId != model.StoreId)
                    return BadRequest("Błędny storeId dla tego presetu.");
            }

            preset.PresetName = string.IsNullOrWhiteSpace(model.PresetName)
                ? "No Name"
                : model.PresetName.Trim();

            if (model.NowInUse)
            {
                var others = await _context.CompetitorPresets
                    .Where(p => p.StoreId == model.StoreId && p.PresetId != model.PresetId && p.NowInUse)
                    .ToListAsync();

                foreach (var o in others)
                    o.NowInUse = false;
            }
            preset.NowInUse = model.NowInUse;

            preset.SourceGoogle = model.SourceGoogle;
            preset.SourceCeneo = model.SourceCeneo;
            preset.UseUnmarkedStores = model.UseUnmarkedStores;

            if (model.Competitors != null)
            {
                preset.CompetitorItems.Clear();

                foreach (var c in model.Competitors)
                {
                    preset.CompetitorItems.Add(new CompetitorPresetItem
                    {
                        StoreName = c.StoreName,
                        IsGoogle = c.IsGoogle,
                        UseCompetitor = c.UseCompetitor
                    });
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                presetId = preset.PresetId
            });
        }

        [HttpPost]
        public async Task<IActionResult> DeactivateAllPresets(int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return BadRequest("Brak dostępu do sklepu.");
            }

            var activePresets = await _context.CompetitorPresets
                .Where(p => p.StoreId == storeId && p.NowInUse)
                .ToListAsync();

            foreach (var preset in activePresets)
            {
                preset.NowInUse = false;
            }

            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> DeletePreset(int presetId)
        {
            var preset = await _context.CompetitorPresets
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.PresetId == presetId);

            if (preset == null)
                return NotFound("Preset nie istnieje.");

            if (!await UserHasAccessToStore(preset.StoreId))
                return BadRequest("Brak dostępu do sklepu.");

            _context.CompetitorPresetItems.RemoveRange(preset.CompetitorItems);

            _context.CompetitorPresets.Remove(preset);

            await _context.SaveChangesAsync();

            return Ok(new { success = true });
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
                .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse);

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

            if (activePreset != null)
            {
                var competitorItemsDict = activePreset.CompetitorItems
                        .GroupBy(ci => new
                        {
                            Store = ci.StoreName.ToLower().Trim(),
                            Source = ci.IsGoogle
                        })
                        .Select(g => g.First())
                        .ToDictionary(
                            x => new
                            {
                                Store = x.StoreName.ToLower().Trim(),
                                Source = x.IsGoogle
                            },
                            x => x.UseCompetitor
                        );

                var storeNameLower = storeName.ToLower().Trim();
                filteredPrices = new List<PriceHistoryClass>();

                foreach (var priceEntry in rawPrices)
                {
                    if (priceEntry.StoreName != null &&
                        priceEntry.StoreName.ToLower().Trim() == storeNameLower)
                    {
                        filteredPrices.Add(priceEntry);
                        continue;
                    }

                    bool googleFlag = (priceEntry.IsGoogle == true);
                    var key = new
                    {
                        Store = (priceEntry.StoreName ?? "").ToLower().Trim(),
                        Source = googleFlag
                    };

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
                        isGoogle = p.IsGoogle
                    })
              );

            ViewBag.ScrapHistory = scrapHistory;
            ViewBag.ProductName = product.ProductName;
            ViewBag.Url = product.OfferUrl;
            ViewBag.StoreId = storeId;
            ViewBag.GoogleUrl = product.GoogleUrl;
            ViewBag.StoreName = storeName;
            ViewBag.SetPrice1 = priceValues.SetPrice1;
            ViewBag.SetPrice2 = priceValues.SetPrice2;
            ViewBag.ProductId = productId;
            ViewBag.ExternalId = product.ExternalId;
            ViewBag.ExternalPrice = product.ExternalPrice;
            ViewBag.Img = product.MainUrl;
            ViewBag.Ean = product.Ean;
            ViewBag.CatalogNum = product.CatalogNumber;
            ViewBag.ExternalUrl = product.Url;
            ViewBag.ApiId = product.ExternalId;
            ViewBag.PricesDataJson = pricesDataJson;
            ViewBag.ActivePresetName = activePresetName;

            return View("~/Views/Panel/PriceHistory/Details.cshtml", prices);
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
                .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse);

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

            if (activePreset != null)
            {
                var competitorItemsDict = activePreset.CompetitorItems
                    .GroupBy(ci => new { Store = ci.StoreName.ToLower().Trim(), Source = ci.IsGoogle })
                    .Select(g => g.First())
                    .ToDictionary(
                        x => new { Store = x.StoreName.ToLower().Trim(), Source = x.IsGoogle },
                        x => x.UseCompetitor
                    );

                var storeNameLower = storeName.ToLower().Trim();
                finalFilteredHistories = new List<PriceHistoryClass>();

                foreach (var priceEntry in rawFilteredHistories)
                {
                    if (priceEntry.StoreName != null &&
                        priceEntry.StoreName.ToLower().Trim() == storeNameLower)
                    {
                        finalFilteredHistories.Add(priceEntry);
                        continue;
                    }

                    bool googleFlag = (priceEntry.IsGoogle == true);

                    var key = new { Store = (priceEntry.StoreName ?? "").ToLower().Trim(), Source = googleFlag };

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
                        newMarginValue = (decimal?)null
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
            SELECT ProductId, Ean, MarginPrice, ExternalId, ProducerCode
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
                        ProducerCode = p.ProducerCode
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



    }
}