using ChartJs.Blazor.ChartJS.Common.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.ViewModels;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Xml.Linq;
using Microsoft.AspNetCore.SignalR;
using PriceSafari.Hubs;

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
                .Where(f => f.StoreId == storeId)
                .ToListAsync();

            ViewBag.LatestScrap = latestScrap;
            ViewBag.StoreId = storeId;
            ViewBag.StoreName = storeName;
            ViewBag.StoreLogo = storeLogo;          
            ViewBag.Flags = flags;
            ViewBag.ScrapedProducts = scrapedproducts;

            return View("~/Views/Panel/PriceHistory/Index.cshtml");
        }








        [HttpGet]
        public async Task<IActionResult> GetPrices(int? storeId, string competitorStore = null, string source = "All")
        {
            if (storeId == null)
            {
                return Json(new
                {
                    productCount = 0,
                    priceCount = 0,
                    myStoreName = "",
                    prices = new List<object>(),
                    missedProducts = new List<object>(),
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
                    missedProducts = new List<object>(),
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
                .Select(pv => new { pv.SetPrice1, pv.SetPrice2, pv.PriceStep, pv.UsePriceDiff })
                .FirstOrDefaultAsync() ?? new { SetPrice1 = 2.00m, SetPrice2 = 2.00m, PriceStep = 2.00m, UsePriceDiff = true };

            var pricesQuery = from p in _context.Products
                              where p.StoreId == storeId && p.IsScrapable
                              join ph in _context.PriceHistories
                                  .Where(ph => ph.ScrapHistoryId == latestScrap.Id)
                                  on p.ProductId equals ph.ProductId into phGroup
                              from ph in phGroup.DefaultIfEmpty()
                              select new
                              {
                                  p.ProductId,
                                  p.ProductName,
                                  Price = ph != null ? ph.Price : (decimal?)null,
                                  StoreName = ph != null ? ph.StoreName : null,
                                  ScrapHistoryId = ph != null ? ph.ScrapHistoryId : (int?)null,
                                  Position = ph != null ? ph.Position : (int?)null,
                                  IsBidding = ph != null ? ph.IsBidding : null,
                                  IsGoogle = ph != null ? ph.IsGoogle : (bool?)null,
                                  AvailabilityNum = ph != null ? ph.AvailabilityNum : (int?)null,
                                  IsRejected = p.IsRejected
                              };

            if (!string.IsNullOrEmpty(source))
            {
                switch (source.ToLower())
                {
                    case "ceneo":
                        pricesQuery = pricesQuery.Where(p => p.IsGoogle == false || p.IsGoogle == null);
                        break;
                    case "google":
                        pricesQuery = pricesQuery.Where(p => p.IsGoogle == true);
                        break;
                    case "all":
                        // No filtering
                        break;
                    default:
                        return Json(new { error = "Invalid source parameter" });
                }
            }

            if (!string.IsNullOrEmpty(competitorStore))
            {
                pricesQuery = pricesQuery.Where(p =>
                    (p.StoreName != null && p.StoreName.ToLower() == storeName.ToLower()) ||
                    (p.StoreName != null && p.StoreName.ToLower() == competitorStore.ToLower()));
            }

            var prices = await pricesQuery.ToListAsync();

            var storeFlags = await _context.Flags
                .Where(f => f.StoreId == storeId)
                .ToListAsync();

            var productFlagsDictionary = storeFlags
                .SelectMany(flag => _context.ProductFlags.Where(pf => pf.FlagId == flag.FlagId).Select(pf => new { pf.ProductId, pf.FlagId }))
                .GroupBy(pf => pf.ProductId)
                .ToDictionary(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            var productsWithExternalInfo = await _context.Products
                .Where(p => p.StoreId == storeId && p.ExternalId.HasValue)
                .Select(p => new { p.ProductId, p.ExternalId, p.ExternalPrice, p.MainUrl, p.MarginPrice })
                .ToListAsync();

            var productExternalInfoDictionary = productsWithExternalInfo
                .ToDictionary(p => p.ProductId, p => new { p.ExternalId, p.ExternalPrice, p.MainUrl, p.MarginPrice });

            var allPrices = prices
             .GroupBy(p => p.ProductId)
             .Select(g =>
             {
                 var product = g.First();
                 var storeCount = g.Select(p => p.StoreName).Where(s => s != null).Distinct().Count();

                 bool sourceGoogle = g.Any(p => p.IsGoogle == true);
                 bool sourceCeneo = g.Any(p => p.IsGoogle == false);

                 var myPriceEntries = g.Where(p => p.StoreName != null && p.StoreName.ToLower() == storeName.ToLower());
                 var myPriceEntry = myPriceEntries.OrderByDescending(p => p.IsGoogle == false).FirstOrDefault();

                 if (string.IsNullOrEmpty(competitorStore) && myPriceEntry == null)
                 {
                     if (source.ToLower() != "all")
                     {
                         return null;
                     }
                     else
                     {
                         myPriceEntry = null;
                     }
                 }

                 var validPrices = g.Where(p => p.Price.HasValue).ToList();

                 bool onlyMe = validPrices.Count > 0 && validPrices.All(p => p.StoreName != null && p.StoreName.ToLower() == storeName.ToLower());

                 var bestPriceEntry = validPrices
                     .OrderBy(p => p.Price)
                     .ThenBy(p => p.StoreName)
                     .ThenByDescending(p => p.IsGoogle == false)
                     .FirstOrDefault();

                 var competitorPriceEntry = validPrices.FirstOrDefault(p =>
                     !string.IsNullOrEmpty(competitorStore) &&
                     p.StoreName != null &&
                     p.StoreName.ToLower() == competitorStore.ToLower());

                 if (!string.IsNullOrEmpty(competitorStore) && (myPriceEntry == null || competitorPriceEntry == null))
                 {
                     return null;
                 }

                 if (!string.IsNullOrEmpty(competitorStore))
                 {
                     bestPriceEntry = competitorPriceEntry;
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
                         .Where(p => p.Price > myPrice)
                         .OrderBy(p => p.Price)
                         .FirstOrDefault()?.Price ?? myPrice;

                     var bestPriceEntries = validPrices.Where(p => p.Price == bestPrice).ToList();
                     bool allBestFromMyStore = bestPriceEntries.All(p =>
                         p.StoreName != null && p.StoreName.ToLower() == storeName.ToLower()
                     );

                     if (string.IsNullOrEmpty(competitorStore))
                     {
                         if (myPrice == bestPrice)
                         {
                             // Jeśli wszystkie najlepsze oferty są nasze lub jest tylko jedna
                             if (allBestFromMyStore || bestPriceEntries.Count == 1)
                             {
                                 isUniqueBestPrice = true;

                                 if (secondBestPrice > myPrice)
                                 {
                                     var secondBestPriceEntry = validPrices.FirstOrDefault(p => p.Price == secondBestPrice);

                                     if (secondBestPriceEntry != null)
                                     {
                                         bestPrice = secondBestPrice;
                                         bestPriceEntry = secondBestPriceEntry;
                                         savings = Math.Round(secondBestPrice.Value - myPrice.Value, 2);
                                         percentageDifference = Math.Round((secondBestPrice.Value - myPrice.Value) / myPrice.Value * 100, 2);
                                         priceDifference = Math.Round(myPrice.Value - secondBestPrice.Value, 2);
                                     }
                                     else
                                     {
                                         savings = Math.Round(secondBestPrice.Value - bestPrice.Value, 2);
                                         percentageDifference = Math.Round((secondBestPrice.Value - myPrice.Value) / myPrice.Value * 100, 2);
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
                                 // Więcej niż jedna oferta z najlepszą ceną i nie wszystkie są nasze
                                 isUniqueBestPrice = false;
                                 savings = null;
                                 priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
                                 percentageDifference = Math.Round((myPrice.Value - bestPrice.Value) / bestPrice.Value * 100, 2);
                             }
                         }
                         else
                         {
                             isUniqueBestPrice = false;
                             priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
                             percentageDifference = Math.Round((myPrice.Value - bestPrice.Value) / bestPrice.Value * 100, 2);
                             savings = null;
                         }
                     }
                     else
                     {
                         // Porównanie z konkretnym sklepem
                         isUniqueBestPrice = myPrice < bestPrice;
                         savings = isUniqueBestPrice ? Math.Abs(Math.Round(myPrice.Value - bestPrice.Value, 2)) : (decimal?)null;
                         percentageDifference = Math.Round((myPrice.Value - bestPrice.Value) / bestPrice.Value * 100, 2);
                         priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
                     }

                     // Nowa logika: jeśli jest remis, preferujemy sklep inny niż nasz jako bestPriceEntry
                     var tiedBestPriceEntries = validPrices.Where(p => p.Price == bestPrice).ToList();
                     if (tiedBestPriceEntries.Count > 1 && tiedBestPriceEntries.Any(e => e.StoreName.ToLower() == storeName.ToLower()))
                     {
                         var nonMyBest = tiedBestPriceEntries.FirstOrDefault(x => x.StoreName != null && x.StoreName.ToLower() != storeName.ToLower());
                         if (nonMyBest != null)
                         {
                             bestPriceEntry = nonMyBest;
                         }
                     }
                 }

                 // Jeśli onlyMe = true, to nie ustawiamy unique best price na true
                 if (onlyMe)
                 {
                     isUniqueBestPrice = false;
                 }

                 productFlagsDictionary.TryGetValue(g.Key, out var flagIds);
                 flagIds = flagIds ?? new List<int>();

                 var finalBestPriceEntries = validPrices.Where(p => p.Price == bestPrice).ToList();
                 int externalBestPriceCount = finalBestPriceEntries.Count;

                 // Nowa logika: jeśli nasze sklepy mają niższą cenę niż bestPrice, to externalBestPriceCount = 0
                 // Czyli jeżeli myPrice < bestPrice
                 if (myPrice < bestPrice)
                 {
                     externalBestPriceCount = 0;
                 }


                 return new
                 {
                     ProductId = product.ProductId,
                     ProductName = product.ProductName,
                     LowestPrice = bestPrice,
                     StoreName = bestPriceEntry?.StoreName,
                     MyPrice = myPriceEntry?.Price,
                     ScrapId = bestPriceEntry?.ScrapHistoryId,
                     PriceDifference = priceDifference,
                     PercentageDifference = percentageDifference,
                     Savings = savings,
                     IsSharedBestPrice = string.IsNullOrEmpty(competitorStore) &&
                                         myPrice == bestPrice &&
                                         validPrices.Count(p => p.Price == bestPrice) > 1 &&
                                         !(validPrices.Where(p => p.Price == bestPrice)
                                             .All(x => x.StoreName != null && x.StoreName.ToLower() == storeName.ToLower())),
                     IsUniqueBestPrice = isUniqueBestPrice,
                     OnlyMe = onlyMe,
                     // Dodajemy nowe pole ExternalBestPriceCount
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
                     ExternalId = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].ExternalId : null,
                     ExternalPrice = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].ExternalPrice : null,
                     MarginPrice = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].MarginPrice : null,
                     ImgUrl = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].MainUrl : null,
                     IsRejected = product.IsRejected || isRejectedDueToZeroPrice,
                     StoreCount = storeCount,
                     SourceGoogle = sourceGoogle,
                     SourceCeneo = sourceCeneo
                 };
             })
             .Where(p => p != null)
             .ToList();

                    var missedProductsCount = allPrices.Count(p => p.IsRejected == true);

                    return Json(new
                    {
                        productCount = allPrices.Count,
                        priceCount = prices.Count,
                        myStoreName = storeName,
                        prices = allPrices,
                        missedProductsCount = missedProductsCount,
                        setPrice1 = priceValues.SetPrice1,
                        setPrice2 = priceValues.SetPrice2,
                        stepPrice = priceValues.PriceStep,
                        usePriceDiff = priceValues.UsePriceDiff
                    });


        }






        //NOWA METODA

        //[HttpGet]
        //public async Task<IActionResult> GetPrices(int? storeId, string competitorStore = null, string source = "All")
        //{
        //    if (storeId == null)
        //    {
        //        return Json(new
        //        {
        //            productCount = 0,
        //            priceCount = 0,
        //            myStoreName = "",
        //            prices = new List<object>(),
        //            missedProducts = new List<object>(),
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
        //            missedProducts = new List<object>(),
        //            setPrice1 = 2.00m,
        //            setPrice2 = 2.00m
        //        });
        //    }

        //    var storeName = await _context.Stores
        //        .Where(s => s.StoreId == storeId)
        //        .Select(s => s.StoreName)
        //        .FirstOrDefaultAsync();

        //    var priceValues = await _context.PriceValues
        //        .Where(pv => pv.StoreId == storeId)
        //        .Select(pv => new { pv.SetPrice1, pv.SetPrice2, pv.PriceStep, pv.UsePriceDiff })
        //        .FirstOrDefaultAsync() ?? new { SetPrice1 = 2.00m, SetPrice2 = 2.00m, PriceStep = 2.00m, UsePriceDiff = true };

        //    var pricesQuery = from p in _context.Products
        //                      where p.StoreId == storeId && p.IsScrapable
        //                      join ph in _context.PriceHistories
        //                          .Where(ph => ph.ScrapHistoryId == latestScrap.Id)
        //                          on p.ProductId equals ph.ProductId into phGroup
        //                      from ph in phGroup.DefaultIfEmpty()
        //                      select new
        //                      {
        //                          p.ProductId,
        //                          p.ProductName,
        //                          Price = ph != null ? ph.Price : (decimal?)null,
        //                          StoreName = ph != null ? ph.StoreName : null,
        //                          ScrapHistoryId = ph != null ? ph.ScrapHistoryId : (int?)null,
        //                          Position = ph != null ? ph.Position : (int?)null,
        //                          IsBidding = ph != null ? ph.IsBidding : null,
        //                          IsGoogle = ph != null ? ph.IsGoogle : (bool?)null,
        //                          AvailabilityNum = ph != null ? ph.AvailabilityNum : (int?)null,
        //                          IsRejected = p.IsRejected
        //                      };

        //    if (!string.IsNullOrEmpty(source))
        //    {
        //        switch (source.ToLower())
        //        {
        //            case "ceneo":
        //                pricesQuery = pricesQuery.Where(p => p.IsGoogle == false || p.IsGoogle == null);
        //                break;
        //            case "google":
        //                pricesQuery = pricesQuery.Where(p => p.IsGoogle == true);
        //                break;
        //            case "all":
        //                // No filtering
        //                break;
        //            default:
        //                return Json(new { error = "Invalid source parameter" });
        //        }
        //    }

        //    if (!string.IsNullOrEmpty(competitorStore))
        //    {
        //        pricesQuery = pricesQuery.Where(p =>
        //            (p.StoreName != null && p.StoreName.ToLower() == storeName.ToLower()) ||
        //            (p.StoreName != null && p.StoreName.ToLower() == competitorStore.ToLower()));
        //    }

        //    var prices = await pricesQuery.ToListAsync();

        //    var storeFlags = await _context.Flags
        //        .Where(f => f.StoreId == storeId)
        //        .ToListAsync();

        //    var productFlagsDictionary = storeFlags
        //        .SelectMany(flag => _context.ProductFlags.Where(pf => pf.FlagId == flag.FlagId).Select(pf => new { pf.ProductId, pf.FlagId }))
        //        .GroupBy(pf => pf.ProductId)
        //        .ToDictionary(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

        //    var productsWithExternalInfo = await _context.Products
        //        .Where(p => p.StoreId == storeId && p.ExternalId.HasValue)
        //        .Select(p => new { p.ProductId, p.ExternalId, p.ExternalPrice, p.MainUrl, p.MarginPrice })
        //        .ToListAsync();

        //    var productExternalInfoDictionary = productsWithExternalInfo
        //        .ToDictionary(p => p.ProductId, p => new { p.ExternalId, p.ExternalPrice, p.MainUrl, p.MarginPrice });

        //    var allPrices = prices
        //        .GroupBy(p => p.ProductId)
        //        .Select(g =>
        //        {
        //            var product = g.First();

        //            var storeCount = g.Select(p => p.StoreName).Where(s => s != null).Distinct().Count();

        //            bool sourceGoogle = g.Any(p => p.IsGoogle == true);
        //            bool sourceCeneo = g.Any(p => p.IsGoogle == false);

        //            var myPriceEntries = g.Where(p => p.StoreName != null && p.StoreName.ToLower() == storeName.ToLower());
        //            var myPriceEntry = myPriceEntries.OrderByDescending(p => p.IsGoogle == false).FirstOrDefault();

        //            if (string.IsNullOrEmpty(competitorStore) && myPriceEntry == null)
        //            {
        //                if (source.ToLower() != "all")
        //                {
        //                    return null;
        //                }
        //                else
        //                {
        //                    myPriceEntry = null;
        //                }
        //            }

        //            var validPrices = g.Where(p => p.Price.HasValue).ToList();
        //            var bestPriceEntry = validPrices
        //                .OrderBy(p => p.Price)
        //                .ThenBy(p => p.StoreName)
        //                .ThenByDescending(p => p.IsGoogle == false)
        //                .FirstOrDefault();

        //            var competitorPriceEntry = validPrices.FirstOrDefault(p =>
        //                !string.IsNullOrEmpty(competitorStore) &&
        //                p.StoreName != null &&
        //                p.StoreName.ToLower() == competitorStore.ToLower());

        //            if (!string.IsNullOrEmpty(competitorStore) && (myPriceEntry == null || competitorPriceEntry == null))
        //            {
        //                return null;
        //            }

        //            if (!string.IsNullOrEmpty(competitorStore))
        //            {
        //                bestPriceEntry = competitorPriceEntry;
        //            }

        //            decimal? bestPrice = bestPriceEntry?.Price;
        //            decimal? myPrice = myPriceEntry?.Price ?? bestPrice;
        //            decimal? priceDifference = null;
        //            decimal? percentageDifference = null;
        //            decimal? savings = null;
        //            bool isUniqueBestPrice = false;
        //            int? myPosition = myPriceEntry?.Position;
        //            int? myDelivery = myPriceEntry?.AvailabilityNum;
        //            bool isRejectedDueToZeroPrice = false;

        //            if (product.IsRejected || bestPrice == 0 || myPrice == 0)
        //            {
        //                // Rezygnujemy z obliczeń dla odrzuconych lub zerowych cen
        //                percentageDifference = null;
        //                priceDifference = null;
        //                savings = null;
        //                isUniqueBestPrice = false;
        //                isRejectedDueToZeroPrice = true;
        //            }
        //            else if (bestPrice.HasValue && myPrice.HasValue)
        //            {
        //                var secondBestPrice = validPrices
        //                    .Where(p => p.Price > myPrice)
        //                    .OrderBy(p => p.Price)
        //                    .FirstOrDefault()?.Price ?? myPrice;

        //                var bestPriceEntries = validPrices.Where(p => p.Price == bestPrice).ToList();
        //                bool allBestFromMyStore = bestPriceEntries.All(p =>
        //                    p.StoreName != null && p.StoreName.ToLower() == storeName.ToLower()
        //                );

        //                if (string.IsNullOrEmpty(competitorStore))
        //                {
        //                    if (myPrice == bestPrice)
        //                    {
        //                        // Jeśli wszystkie najlepsze oferty są nasze lub jest tylko jedna
        //                        if (allBestFromMyStore || bestPriceEntries.Count == 1)
        //                        {
        //                            isUniqueBestPrice = true;

        //                            // Jeżeli mamy drugą najlepszą cenę większą niż nasza
        //                            if (secondBestPrice > myPrice)
        //                            {
        //                                // Pobieramy wpis drugiej najlepszej ceny
        //                                var secondBestPriceEntry = validPrices.FirstOrDefault(p => p.Price == secondBestPrice);

        //                                if (secondBestPriceEntry != null)
        //                                {
        //                                    // Teraz ustawiamy bestPrice oraz bestPriceEntry na druga najlepszą cenę
        //                                    bestPrice = secondBestPrice;
        //                                    bestPriceEntry = secondBestPriceEntry;

        //                                    // Obliczamy różnice w stosunku do nowej "bestPrice" (która jest tak naprawdę drugą najlepszą ceną, droższą od naszej)
        //                                    savings = Math.Round(secondBestPrice.Value - myPrice.Value, 2);
        //                                    percentageDifference = Math.Round((secondBestPrice.Value - myPrice.Value) / myPrice.Value * 100, 2);
        //                                    priceDifference = Math.Round(myPrice.Value - secondBestPrice.Value, 2);
        //                                }
        //                                else
        //                                {
        //                                    // Jeśli nie znaleziono wpisu odpowiadającego secondBestPrice (teoretycznie nie powinno się zdarzyć)
        //                                    // zachowujemy domyślne obliczenia tak jak były
        //                                    savings = Math.Round(secondBestPrice.Value - bestPrice.Value, 2);
        //                                    percentageDifference = Math.Round((secondBestPrice.Value - myPrice.Value) / myPrice.Value * 100, 2);
        //                                    priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
        //                                }
        //                            }
        //                            else
        //                            {
        //                                // Brak drugiej najlepszej ceny wyższej niż nasza - pozostawiamy pierwotną logikę
        //                                savings = null;
        //                                percentageDifference = 0;
        //                                priceDifference = 0;
        //                            }
        //                        }
        //                        else
        //                        {
        //                            // Więcej niż jedna oferta z najlepszą ceną i nie wszystkie są nasze
        //                            isUniqueBestPrice = false;
        //                            savings = null;
        //                            priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
        //                            percentageDifference = Math.Round((myPrice.Value - bestPrice.Value) / bestPrice.Value * 100, 2);
        //                        }
        //                    }
        //                    else
        //                    {
        //                        isUniqueBestPrice = false;
        //                        priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
        //                        percentageDifference = Math.Round((myPrice.Value - bestPrice.Value) / bestPrice.Value * 100, 2);
        //                        savings = null;
        //                    }
        //                }
        //                else
        //                {
        //                    // Porównanie z konkretnym sklepem (bez zmian)
        //                    isUniqueBestPrice = myPrice < bestPrice;
        //                    savings = isUniqueBestPrice ? Math.Abs(Math.Round(myPrice.Value - bestPrice.Value, 2)) : (decimal?)null;
        //                    percentageDifference = Math.Round((myPrice.Value - bestPrice.Value) / bestPrice.Value * 100, 2);
        //                    priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
        //                }
        //            }


        //            productFlagsDictionary.TryGetValue(g.Key, out var flagIds);
        //            flagIds = flagIds ?? new List<int>();

        //            return new
        //            {
        //                ProductId = product.ProductId,
        //                ProductName = product.ProductName,
        //                LowestPrice = bestPrice,
        //                StoreName = bestPriceEntry?.StoreName,
        //                MyPrice = myPriceEntry?.Price,
        //                ScrapId = bestPriceEntry?.ScrapHistoryId,
        //                PriceDifference = priceDifference,
        //                PercentageDifference = percentageDifference,
        //                Savings = savings,
        //                IsSharedBestPrice = string.IsNullOrEmpty(competitorStore) &&
        //                                    myPrice == bestPrice &&
        //                                    validPrices.Count(p => p.Price == bestPrice) > 1 &&
        //                                    // Zmienione warunki - IsShared tylko jeśli nie wszystkie są nasze
        //                                    !(validPrices.Where(p => p.Price == bestPrice).All(x => x.StoreName != null && x.StoreName.ToLower() == storeName.ToLower())),
        //                IsUniqueBestPrice = isUniqueBestPrice,
        //                IsBidding = bestPriceEntry?.IsBidding,
        //                IsGoogle = bestPriceEntry?.IsGoogle,
        //                Position = bestPriceEntry?.Position,
        //                MyIsBidding = myPriceEntry?.IsBidding,
        //                MyIsGoogle = myPriceEntry?.IsGoogle,
        //                MyPosition = myPosition,
        //                FlagIds = flagIds,
        //                Delivery = bestPriceEntry?.AvailabilityNum,
        //                MyDelivery = myDelivery,
        //                ExternalId = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].ExternalId : null,
        //                ExternalPrice = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].ExternalPrice : null,
        //                MarginPrice = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].MarginPrice : null,
        //                ImgUrl = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].MainUrl : null,
        //                IsRejected = product.IsRejected || isRejectedDueToZeroPrice,
        //                StoreCount = storeCount,
        //                SourceGoogle = sourceGoogle,
        //                SourceCeneo = sourceCeneo
        //            };
        //        })
        //        .Where(p => p != null)
        //        .ToList();

        //    var missedProductsCount = allPrices.Count(p => p.IsRejected == true);

        //    return Json(new
        //    {
        //        productCount = allPrices.Count,
        //        priceCount = prices.Count,
        //        myStoreName = storeName,
        //        prices = allPrices,
        //        missedProductsCount = missedProductsCount,
        //        setPrice1 = priceValues.SetPrice1,
        //        setPrice2 = priceValues.SetPrice2,
        //        stepPrice = priceValues.PriceStep,
        //        usePriceDiff = priceValues.UsePriceDiff
        //    });
        //}


        //STARA METODA

        //[HttpGet]
        //public async Task<IActionResult> GetPrices(int? storeId, string competitorStore = null, string source = "All")
        //{
        //    if (storeId == null)
        //    {
        //        return Json(new
        //        {
        //            productCount = 0,
        //            priceCount = 0,
        //            myStoreName = "",
        //            prices = new List<dynamic>(),
        //            missedProducts = new List<dynamic>(),
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
        //            prices = new List<dynamic>(),
        //            missedProducts = new List<dynamic>(),
        //            setPrice1 = 2.00m,
        //            setPrice2 = 2.00m
        //        });
        //    }

        //    var storeName = await _context.Stores
        //        .Where(s => s.StoreId == storeId)
        //        .Select(s => s.StoreName)
        //        .FirstOrDefaultAsync();

        //    var priceValues = await _context.PriceValues
        //        .Where(pv => pv.StoreId == storeId)
        //        .Select(pv => new { pv.SetPrice1, pv.SetPrice2, pv.PriceStep, pv.UsePriceDiff })
        //        .FirstOrDefaultAsync() ?? new { SetPrice1 = 2.00m, SetPrice2 = 2.00m, PriceStep = 2.00m, UsePriceDiff = true };

        //    // Modify pricesQuery to include only products where IsScrapable is true
        //    var pricesQuery = from p in _context.Products
        //                      where p.StoreId == storeId && p.IsScrapable
        //                      join ph in _context.PriceHistories
        //                          .Where(ph => ph.ScrapHistoryId == latestScrap.Id)
        //                          on p.ProductId equals ph.ProductId into phGroup
        //                      from ph in phGroup.DefaultIfEmpty()
        //                      select new
        //                      {
        //                          p.ProductId,
        //                          p.ProductName,
        //                          Price = ph != null ? ph.Price : (decimal?)null,
        //                          StoreName = ph != null ? ph.StoreName : null,
        //                          ScrapHistoryId = ph != null ? ph.ScrapHistoryId : (int?)null,
        //                          Position = ph != null ? ph.Position : (int?)null,
        //                          IsBidding = ph != null ? ph.IsBidding : null,
        //                          IsGoogle = ph != null ? ph.IsGoogle : (bool?)null,
        //                          AvailabilityNum = ph != null ? ph.AvailabilityNum : (int?)null,
        //                          IsRejected = p.IsRejected
        //                          // Removed p.IsScrapable as it's always true now
        //                      };

        //    // Adjust the filtering based on the source
        //    if (!string.IsNullOrEmpty(source))
        //    {
        //        switch (source.ToLower())
        //        {
        //            case "ceneo":
        //                pricesQuery = pricesQuery.Where(p => p.IsGoogle == false || p.IsGoogle == null);
        //                break;
        //            case "google":
        //                pricesQuery = pricesQuery.Where(p => p.IsGoogle == true);
        //                break;
        //            case "all":
        //                // No filtering
        //                break;
        //            default:
        //                return Json(new { error = "Invalid source parameter" });
        //        }
        //    }

        //    if (!string.IsNullOrEmpty(competitorStore))
        //    {
        //        pricesQuery = pricesQuery.Where(p =>
        //            (p.StoreName != null && p.StoreName.ToLower() == storeName.ToLower()) ||
        //            (p.StoreName != null && p.StoreName.ToLower() == competitorStore.ToLower()));
        //    }

        //    var prices = await pricesQuery.ToListAsync();

        //    var storeFlags = await _context.Flags
        //        .Where(f => f.StoreId == storeId)
        //        .ToListAsync();

        //    var productFlagsDictionary = storeFlags
        //        .SelectMany(flag => _context.ProductFlags.Where(pf => pf.FlagId == flag.FlagId).Select(pf => new { pf.ProductId, pf.FlagId }))
        //        .GroupBy(pf => pf.ProductId)
        //        .ToDictionary(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

        //    var productsWithExternalInfo = await _context.Products
        //        .Where(p => p.StoreId == storeId && p.ExternalId.HasValue)
        //        .Select(p => new { p.ProductId, p.ExternalId, p.ExternalPrice, p.MainUrl, p.MarginPrice })
        //        .ToListAsync();

        //    var productExternalInfoDictionary = productsWithExternalInfo
        //        .ToDictionary(p => p.ProductId, p => new { p.ExternalId, p.ExternalPrice, p.MainUrl, p.MarginPrice });

        //    var allPrices = prices
        //        .GroupBy(p => p.ProductId)
        //        .Select(g =>
        //        {
        //            var product = g.First();

        //            // Compute the number of unique stores
        //            var storeCount = g.Select(p => p.StoreName).Where(s => s != null).Distinct().Count();

        //            // Determine present data sources
        //            bool sourceGoogle = g.Any(p => p.IsGoogle == true);
        //            bool sourceCeneo = g.Any(p => p.IsGoogle == false);

        //            // Filter out products when a specific source is selected and our price doesn't exist
        //            var myPriceEntries = g.Where(p => p.StoreName != null && p.StoreName.ToLower() == storeName.ToLower());
        //            var myPriceEntry = myPriceEntries.OrderByDescending(p => p.IsGoogle == false).FirstOrDefault();

        //            if (string.IsNullOrEmpty(competitorStore) && myPriceEntry == null)
        //            {
        //                if (source.ToLower() != "all")
        //                {
        //                    // Exclude the product if our price doesn't exist for the selected source
        //                    return null;
        //                }
        //                else
        //                {
        //                    // Include the product but without price and position info
        //                    myPriceEntry = null;
        //                }
        //            }

        //            // Find the best price entry
        //            var validPrices = g.Where(p => p.Price.HasValue).ToList();
        //            var bestPriceEntry = validPrices
        //                .OrderBy(p => p.Price)
        //                .ThenBy(p => p.StoreName)
        //                .ThenByDescending(p => p.IsGoogle == false)
        //                .FirstOrDefault();

        //            var competitorPriceEntry = validPrices.FirstOrDefault(p =>
        //                !string.IsNullOrEmpty(competitorStore) &&
        //                p.StoreName != null &&
        //                p.StoreName.ToLower() == competitorStore.ToLower());

        //            if (!string.IsNullOrEmpty(competitorStore) && (myPriceEntry == null || competitorPriceEntry == null))
        //            {
        //                return null;
        //            }

        //            if (!string.IsNullOrEmpty(competitorStore))
        //            {
        //                bestPriceEntry = competitorPriceEntry;
        //            }

        //            decimal? bestPrice = bestPriceEntry?.Price;
        //            decimal? myPrice = myPriceEntry?.Price ?? bestPrice;
        //            decimal? priceDifference = null;
        //            decimal? percentageDifference = null;
        //            decimal? savings = null;
        //            bool isUniqueBestPrice = false;
        //            int? myPosition = myPriceEntry?.Position;
        //            int? myDelivery = myPriceEntry?.AvailabilityNum;
        //            bool isRejectedDueToZeroPrice = false;

        //            if (product.IsRejected || bestPrice == 0 || myPrice == 0)
        //            {
        //                // Omit calculations for rejected products or zero prices
        //                percentageDifference = null;
        //                priceDifference = null;
        //                savings = null;
        //                isUniqueBestPrice = false;
        //                isRejectedDueToZeroPrice = true;
        //            }
        //            else if (bestPrice.HasValue && myPrice.HasValue)
        //            {
        //                if (string.IsNullOrEmpty(competitorStore))
        //                {
        //                    var secondBestPrice = validPrices
        //                        .Where(p => p.Price > myPrice)
        //                        .OrderBy(p => p.Price)
        //                        .FirstOrDefault()?.Price ?? myPrice;

        //                    isUniqueBestPrice = myPrice == bestPrice && validPrices.Count(p => p.Price == bestPrice) == 1 && secondBestPrice > myPrice;
        //                    savings = isUniqueBestPrice ? Math.Round(secondBestPrice.Value - bestPrice.Value, 2) : (decimal?)null;
        //                    percentageDifference = isUniqueBestPrice
        //                        ? Math.Round((secondBestPrice.Value - myPrice.Value) / myPrice.Value * 100, 2)
        //                        : Math.Round((myPrice.Value - bestPrice.Value) / bestPrice.Value * 100, 2);
        //                    priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
        //                }
        //                else
        //                {
        //                    isUniqueBestPrice = myPrice < bestPrice;
        //                    savings = isUniqueBestPrice ? Math.Abs(Math.Round(myPrice.Value - bestPrice.Value, 2)) : (decimal?)null;
        //                    percentageDifference = Math.Round((myPrice.Value - bestPrice.Value) / bestPrice.Value * 100, 2);
        //                    priceDifference = Math.Round(myPrice.Value - bestPrice.Value, 2);
        //                }
        //            }

        //            productFlagsDictionary.TryGetValue(g.Key, out var flagIds);
        //            flagIds = flagIds ?? new List<int>();

        //            return new
        //            {
        //                ProductId = product.ProductId,
        //                ProductName = product.ProductName,
        //                LowestPrice = bestPrice,
        //                StoreName = bestPriceEntry?.StoreName,
        //                MyPrice = myPriceEntry?.Price,
        //                ScrapId = bestPriceEntry?.ScrapHistoryId,
        //                PriceDifference = priceDifference,
        //                PercentageDifference = percentageDifference,
        //                Savings = savings,
        //                IsSharedBestPrice = string.IsNullOrEmpty(competitorStore) && myPrice == bestPrice && validPrices.Count(p => p.Price == bestPrice) > 1,
        //                IsUniqueBestPrice = isUniqueBestPrice,
        //                IsBidding = bestPriceEntry?.IsBidding,
        //                IsGoogle = bestPriceEntry?.IsGoogle,
        //                Position = bestPriceEntry?.Position,
        //                MyIsBidding = myPriceEntry?.IsBidding,
        //                MyIsGoogle = myPriceEntry?.IsGoogle,
        //                MyPosition = myPosition,
        //                FlagIds = flagIds,
        //                Delivery = bestPriceEntry?.AvailabilityNum,
        //                MyDelivery = myDelivery,
        //                ExternalId = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].ExternalId : null,
        //                ExternalPrice = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].ExternalPrice : null,
        //                MarginPrice = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].MarginPrice : null,
        //                ImgUrl = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key].MainUrl : null,
        //                IsRejected = product.IsRejected || isRejectedDueToZeroPrice,
        //                StoreCount = storeCount,
        //                SourceGoogle = sourceGoogle,
        //                SourceCeneo = sourceCeneo
        //            };
        //        })
        //        .Where(p => p != null)
        //        .ToList();


        //    // Po przetworzeniu `allPrices` dodaj obliczenie liczby produktów odrzuconych
        //    var missedProductsCount = allPrices.Count(p => p.IsRejected == true);

        //    return Json(new
        //    {
        //        productCount = allPrices.Count,
        //        priceCount = prices.Count,
        //        myStoreName = storeName,
        //        prices = allPrices,
        //        missedProductsCount = missedProductsCount, // Dodaj liczbę odrzuconych produktów
        //        setPrice1 = priceValues.SetPrice1,
        //        setPrice2 = priceValues.SetPrice2,
        //        stepPrice = priceValues.PriceStep,
        //        usePriceDiff = priceValues.UsePriceDiff
        //    });

        //}




        [HttpPost]
        public async Task<IActionResult> UpdatePricesFromExternalStore(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null || string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
            {
                return BadRequest("Sklep nie jest połączony z zewnętrznym API.");
            }

            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.ExternalId.HasValue  && p.IsRejected == false && p.IsScrapable == true)
                .ToListAsync();

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return BadRequest("Brak ostatniego scrapowania dla sklepu.");
            }

            int totalProducts = products.Count;
            int updatedCount = 0;
            int skippedCount = 0;
            int processedCount = 0;

            foreach (var product in products)
            {
                try
                {
                    var latestPriceInfo = await _context.PriceHistories
                        .Where(ph => ph.ProductId == product.ProductId && ph.ScrapHistoryId == latestScrap.Id && ph.StoreName == store.StoreName)
                        .Select(ph => new { ph.Price, ph.Id })
                        .FirstOrDefaultAsync();

                    if (latestPriceInfo == null)
                    {
                        _logger.LogWarning("Brak ceny z ostatniego scrapowania dla produktu ID: {ProductId}, ExternalId: {ExternalId}", product.ProductId, product.ExternalId);
                        continue;
                    }

                    var latestPrice = latestPriceInfo.Price;
                    var priceHistoryId = latestPriceInfo.Id;

                    var priceResult = await GetExternalStorePrice(store.StoreApiUrl, store.StoreApiKey, product.ExternalId.Value);

                    if (priceResult.Price != latestPrice)
                    {
                        product.ExternalPrice = priceResult.Price;
                        updatedCount++;
                    }
                    else
                    {
                        product.ExternalPrice = null;
                        skippedCount++;
                    }

                    processedCount++;
                    await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", processedCount, totalProducts, updatedCount, skippedCount);

                    _logger.LogInformation("Zaktualizowano cenę dla produktu ID: {ProductId}, ExternalId: {ExternalId}", product.ProductId, product.ExternalId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd podczas aktualizacji ceny dla produktu ID: {ProductId}, ExternalId: {ExternalId}", product.ProductId, product.ExternalId);
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new { totalProducts, updatedCount, skippedCount });
        }

        private async Task<ExternalStorePriceResult> GetExternalStorePrice(string apiUrl, string apiKey, int externalId)
        {
            var client = _httpClientFactory.CreateClient();
            var byteArray = System.Text.Encoding.ASCII.GetBytes($"{apiKey}:");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            try
            {
                var response = await client.GetStringAsync($"{apiUrl}{externalId}");
                var doc = XDocument.Parse(response);

                var priceElement = doc.Descendants("price").FirstOrDefault();
                if (priceElement != null)
                {
                    if (decimal.TryParse(priceElement.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
                    {
                        return new ExternalStorePriceResult
                        {
                            Price = price
                        };
                    }
                    else
                    {
                        throw new Exception("Failed to parse price value");
                    }
                }
                else
                {
                    throw new Exception("Price element not found in XML response");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request error fetching price for external product ID: {ExternalId}", externalId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching price for external product ID: {ExternalId}", externalId);
                throw;
            }
        }

        public class ExternalStorePriceResult
        {
            public decimal Price { get; set; }
        }



        [HttpGet]
        public async Task<IActionResult> GetStores(int? storeId)
        {
            if (storeId == null)
            {
                return Json(new List<string>());
            }

            if (!await UserHasAccessToStore(storeId.Value))
            {
                return Json(new List<string>());
            }

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return Json(new List<string>());
            }

            var stores = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id)
                .Select(ph => ph.StoreName)
                .Distinct()
                .ToListAsync();

            return Json(stores);
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
                    UsePriceDiff = model.usePriceDiff // Używamy 'UsePriceDiff'
                };
                _context.PriceValues.Add(priceValues);
            }
            else
            {
                priceValues.SetPrice1 = model.SetPrice1;
                priceValues.SetPrice2 = model.SetPrice2;
                priceValues.PriceStep = model.PriceStep;
                priceValues.UsePriceDiff = model.usePriceDiff; // Używamy 'UsePriceDiff'
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
                return NotFound();
            }

            if (!await UserHasAccessToStore(scrapHistory.StoreId))
            {
                return Content("Nie ma takiego sklepu");
            }
            var prices = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == scrapId && ph.ProductId == productId)
                .Include(ph => ph.Product)
                .ToListAsync();

            // Najpierw sortujemy po cenie, a w przypadku remisu po nazwie sklepu
            prices = prices.OrderBy(p => p.Price)
                           .ThenBy(p => p.StoreName)
                            .ThenByDescending(p => p.IsGoogle == false)
                           .ToList();


           

            var product = await _context.Products.FindAsync(productId);
            if (product == null)
            {
                return NotFound();
            }

            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == scrapHistory.StoreId)
                .Select(pv => new { pv.SetPrice1, pv.SetPrice2 })
                .FirstOrDefaultAsync();

            var pricesDataJson = JsonConvert.SerializeObject(
                prices.Select(p => new {
                    store = p.StoreName,
                    price = p.Price,
                    isBidding = p.IsBidding
                })
            );

            if (priceValues == null)
            {
                priceValues = new { SetPrice1 = 2.00m, SetPrice2 = 2.00m };
            }

            ViewBag.ScrapHistory = scrapHistory;
            ViewBag.ProductName = product.ProductName;
            ViewBag.Url = product.OfferUrl;
            ViewBag.GoogleUrl = product.GoogleUrl;
            ViewBag.StoreName = (await _context.Stores.FindAsync(scrapHistory.StoreId))?.StoreName;
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

            return View("~/Views/Panel/PriceHistory/Details.cshtml", prices);
        }


        [HttpPost]
        public async Task<IActionResult> UpdateExternalId(int productId, int? externalId)
        {
            var product = await _context.Products.FindAsync(productId);
            if (product == null)
            {
                return NotFound();
            }

            product.ExternalId = externalId;


            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> DeleteExternalId(int productId)
        {
            var product = await _context.Products.FindAsync(productId);
            if (product == null)
            {
                return NotFound();
            }

            product.ExternalId = null;


            await _context.SaveChangesAsync();

            return Ok();
        }




    }
}