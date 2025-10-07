using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using PriceSafari.ViewModels;
using System.Security.Claims;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    public class AllegroPriceHistoryController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        public AllegroPriceHistoryController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
        {
            _context = context;
            _userManager = userManager;
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

            if (latestScrap == null)
            {
                return Json(new { myStoreName = store?.StoreName, prices = new List<object>() });
            }

            var priceData = await _context.AllegroPriceHistories

                 .Where(aph => aph.AllegroScrapeHistoryId == latestScrap.Id && aph.AllegroProduct.IsScrapable)
                 .Include(aph => aph.AllegroProduct)
                 .ToListAsync();

            var productIds = priceData.Select(p => p.AllegroProductId).Distinct().ToList();
            var productFlagsDictionary = await _context.ProductFlags
                .Where(pf => productIds.Contains(pf.AllegroProductId.Value))
                .GroupBy(pf => pf.AllegroProductId.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

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
                        : null;

                    var competitors = g.Where(p => !p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase)).ToList();
                    var bestCompetitor = competitors.OrderBy(p => p.Price).FirstOrDefault();

                    var allMyOffersInGroup = g.Where(p => p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase));

                    var myOffersGroupKey = string.Join(",", allMyOffersInGroup.Select(o => o.IdAllegro).OrderBy(id => id));

                    var totalPopularity = g.Sum(p => p.Popularity ?? 0);

                    var myPopularity = g
                        .Where(p => p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase))
                        .Sum(p => p.Popularity ?? 0);

                    var marketSharePercentage = (totalPopularity > 0)
                        ? ((decimal)myPopularity / totalPopularity) * 100
                        : 0;

                    return new
                    {
                        ProductId = product.AllegroProductId,
                        ProductName = product.AllegroProductName,
                        Producer = (string)null,
                        MyPrice = myOffer?.Price,
                        LowestPrice = bestCompetitor?.Price,
                        StoreName = bestCompetitor?.SellerName,
                        StoreCount = g.Select(p => p.SellerName).Distinct().Count(),
                        TotalOfferCount = g.Count(),

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
                        OnlyMe = (myOffer != null && !competitors.Any()),
                        Savings = (myOffer != null && bestCompetitor != null && myOffer.Price < bestCompetitor.Price) ? bestCompetitor.Price - myOffer.Price : (decimal?)null,
                        PriceDifference = (myOffer != null && bestCompetitor != null) ? myOffer.Price - bestCompetitor.Price : (decimal?)null,
                        PercentageDifference = (myOffer != null && bestCompetitor != null && bestCompetitor.Price > 0) ? ((myOffer.Price - bestCompetitor.Price) / bestCompetitor.Price) * 100 : (decimal?)null,
                        IsUniqueBestPrice = (myOffer != null && bestCompetitor != null && myOffer.Price < bestCompetitor.Price),
                        IsSharedBestPrice = (myOffer != null && bestCompetitor != null && myOffer.Price == bestCompetitor.Price),
                        FlagIds = productFlagsDictionary.GetValueOrDefault(product.AllegroProductId, new List<int>()),

                        Ean = (string)null,
                        ExternalId = (int?)null,
                        MarginPrice = product.MarginPrice,
                        ImgUrl = (string)null,
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
                usePriceDifference = priceSettings?.AllegroUsePriceDiff ?? true
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

            var allOffersForProduct = await _context.AllegroPriceHistories
                .Where(aph => aph.AllegroScrapeHistoryId == latestScrap.Id &&
                              aph.AllegroProductId == productId &&
                              aph.Price > 0)
                .OrderBy(aph => aph.Price)
                .ToListAsync();

            var myOfferIdsInList = allOffersForProduct
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

            var dataForJson = allOffersForProduct.Select(aph => new {
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
            var totalPopularity = allOffersForProduct.Sum(o => o.Popularity ?? 0);

            return Json(new
            {
                mainOfferId = mainOfferId,
                data = dataForJson,
                totalProductPopularity = totalPopularity,
                lastScrapeDate = latestScrap.Date,
                setPrice1 = priceSettings?.AllegroSetPrice1 ?? 0.01m,
                setPrice2 = priceSettings?.AllegroSetPrice2 ?? 2.00m
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

            long? mainOfferId = null;
            if (!string.IsNullOrEmpty(product.AllegroOfferUrl))
            {
                var idString = product.AllegroOfferUrl.Split('-').LastOrDefault();
                if (long.TryParse(idString, out var parsedId))
                {
                    mainOfferId = parsedId;
                }
            }

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
                var dailyOffers = priceHistories
                    .Where(ph => ph.AllegroScrapeHistoryId == scrap.Id)
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
                    ScrapDate = scrap.Date.ToString("yyyy-MM-dd"),
                    TotalSales = dailyOffers.Sum(o => o.Sales),
                    Offers = dailyOffers
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
    }
}