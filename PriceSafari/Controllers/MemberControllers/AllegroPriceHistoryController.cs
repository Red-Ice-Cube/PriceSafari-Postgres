using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
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

        [HttpGet]
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

            ViewBag.StoreId = store.StoreId;
            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreLogo = store.StoreLogoUrl;
            ViewBag.LatestScrap = latestScrap;

            ViewBag.Flags = new List<FlagsClass>();

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
                .Where(aph => aph.AllegroScrapeHistoryId == latestScrap.Id)
                .Include(aph => aph.AllegroProduct)
                .ToListAsync();

            var groupedData = priceData
                .GroupBy(aph => aph.AllegroProduct)
                .Select(g => {
                    var product = g.Key;
                    var myOffer = g.FirstOrDefault(p => p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase));
                    var competitors = g.Where(p => !p.SellerName.Equals(store.StoreNameAllegro, StringComparison.OrdinalIgnoreCase)).ToList();
                    var bestCompetitor = competitors.OrderBy(p => p.Price).FirstOrDefault();

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

                        // ZMIANA: Dodanie pól promocyjnych dla najlepszej oferty konkurencji
                        CompetitorIsBestPriceGuarantee = bestCompetitor?.IsBestPriceGuarantee ?? false,
                        CompetitorIsTopOffer = bestCompetitor?.TopOffer ?? false,

                        MyDeliveryTime = myOffer?.DeliveryTime,
                        MyIsSuperSeller = myOffer?.SuperSeller ?? false,
                        MyIsSmart = myOffer?.Smart ?? false,

                        // ZMIANA: Dodanie pól promocyjnych dla Twojej oferty
                        MyIsBestPriceGuarantee = myOffer?.IsBestPriceGuarantee ?? false,
                        MyIsTopOffer = myOffer?.TopOffer ?? false,

                        IsRejected = false,
                        OnlyMe = (myOffer != null && !competitors.Any()),
                        Savings = (myOffer != null && bestCompetitor != null && myOffer.Price < bestCompetitor.Price) ? bestCompetitor.Price - myOffer.Price : (decimal?)null,
                        PriceDifference = (myOffer != null && bestCompetitor != null) ? myOffer.Price - bestCompetitor.Price : (decimal?)null,
                        PercentageDifference = (myOffer != null && bestCompetitor != null && bestCompetitor.Price > 0) ? ((myOffer.Price - bestCompetitor.Price) / bestCompetitor.Price) * 100 : (decimal?)null,
                        IsUniqueBestPrice = (myOffer != null && bestCompetitor != null && myOffer.Price < bestCompetitor.Price),
                        IsSharedBestPrice = (myOffer != null && bestCompetitor != null && myOffer.Price == bestCompetitor.Price),
                        FlagIds = new List<int>(),
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

            var priceSettings = await _context.PriceValues
                .FirstOrDefaultAsync(pv => pv.StoreId == storeId);

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
                .OrderBy(x => x.Price)
                .Select(aph => new {
                    aph.SellerName,
                    aph.Price,
                    aph.AllegroProduct.AllegroOfferUrl,
                    aph.SuperSeller,
                    aph.Smart,
                    aph.DeliveryTime,
                    aph.DeliveryCost,
                    aph.Popularity,

                    // ZMIANA: Dodanie nowych pól do selekcji
                    aph.IsBestPriceGuarantee,
                    aph.TopOffer
                })
                .ToListAsync();

            var totalPopularity = allOffersForProduct.Sum(o => o.Popularity ?? 0);

            return Json(new
            {
                data = allOffersForProduct,
                totalProductPopularity = totalPopularity,
                lastScrapeDate = latestScrap.Date,
                setPrice1 = priceSettings?.AllegroSetPrice1 ?? 0.01m,
                setPrice2 = priceSettings?.AllegroSetPrice2 ?? 2.00m
            });
        }

    }
}