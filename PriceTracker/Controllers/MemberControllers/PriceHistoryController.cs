using ChartJs.Blazor.ChartJS.Common.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using PriceTracker.Data;
using PriceTracker.Models;
using PriceTracker.ViewModels;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Xml.Linq;
using Microsoft.AspNetCore.SignalR;
using PriceTracker.Hubs;

namespace PriceTracker.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    public class PriceHistoryController : Controller
    {
        private readonly PriceTrackerContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PriceHistoryController> _logger;
        private readonly UserManager<PriceTrackerUser> _userManager;
        private readonly IHubContext<ScrapingHub> _hubContext;

        public PriceHistoryController(
            PriceTrackerContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<PriceHistoryController> logger,
            UserManager<PriceTrackerUser> userManager,
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

            var categories = await _context.Products
                .Where(p => p.StoreId == storeId)
                .Select(p => p.Category)
                .Distinct()
                .ToListAsync();


            var scrapedproducts = await _context.Products
                .Where(p => p.StoreId == storeId && p.IsScrapable)              
                .CountAsync();

            var flags = await _context.Flags
                .Where(f => f.StoreId == storeId)
                .ToListAsync();

            ViewBag.LatestScrap = latestScrap;
            ViewBag.StoreId = storeId;
            ViewBag.StoreName = storeName;
            ViewBag.Categories = categories;
            ViewBag.Flags = flags;
            ViewBag.ScrapedProducts = scrapedproducts;

            return View("~/Views/Panel/PriceHistory/Index.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetPrices(int? storeId, string competitorStore = null)
        {
            if (storeId == null)
            {
                return Json(new { productCount = 0, priceCount = 0, myStoreName = "", prices = new List<dynamic>(), missedProducts = new List<dynamic>(), setPrice1 = 2.00m, setPrice2 = 2.00m });
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
                return Json(new { productCount = 0, priceCount = 0, myStoreName = "", prices = new List<dynamic>(), missedProducts = new List<dynamic>(), setPrice1 = 2.00m, setPrice2 = 2.00m });
            }

            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == storeId)
                .Select(pv => new { pv.SetPrice1, pv.SetPrice2 })
                .FirstOrDefaultAsync();

            if (priceValues == null)
            {
                priceValues = new { SetPrice1 = 2.00m, SetPrice2 = 2.00m };
            }

            var pricesQuery = _context.PriceHistories
                .Include(ph => ph.Product)
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id && !ph.Product.IsRejected);

            var pricesForOurStore = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id && ph.StoreName.ToLower() == storeName.ToLower() && !ph.Product.IsRejected)
                .Select(ph => new
                {
                    ph.ProductId,
                    ph.Price,
                    ph.Position,
                    ph.IsBidding,
                    ph.AvailabilityNum
                })
                .ToListAsync();

            if (!string.IsNullOrEmpty(competitorStore))
            {
                pricesQuery = pricesQuery.Where(ph => ph.StoreName.ToLower() == competitorStore.ToLower());
            }

            var prices = await pricesQuery
                .Select(ph => new
                {
                    ph.ProductId,
                    ph.Product.ProductName,
                    ph.Product.Category,
                    ph.Price,
                    ph.StoreName,
                    ph.ScrapHistoryId,
                    ph.Position,
                    ph.IsBidding,
                    ph.AvailabilityNum
                })
                .ToListAsync();

            var productIds = prices.Select(p => p.ProductId).ToList();

            var storeFlags = await _context.Flags
                .Where(f => f.StoreId == storeId)
                .ToListAsync();

            var productFlagsDictionary = storeFlags
                .SelectMany(flag => _context.ProductFlags.Where(pf => pf.FlagId == flag.FlagId).Select(pf => new { pf.ProductId, pf.FlagId }))
                .GroupBy(pf => pf.ProductId)
                .ToDictionary(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            var productsWithExternalInfo = await _context.Products
                .Where(p => p.StoreId == storeId && p.ExternalId.HasValue)
                .Select(p => new { p.ProductId, p.ExternalId, p.ExternalPrice })
                .ToListAsync();

            var productExternalInfoDictionary = productsWithExternalInfo
                .ToDictionary(p => p.ProductId, p => new { p.ExternalId, p.ExternalPrice });

            var allPrices = prices
                .GroupBy(p => p.ProductId)
                .Select(g =>
                {
                    var bestPriceEntry = g.OrderBy(p => p.Price).First();
                    var myPriceEntry = pricesForOurStore.FirstOrDefault(p => p.ProductId == g.Key);
                    var productInfo = productExternalInfoDictionary.ContainsKey(g.Key) ? productExternalInfoDictionary[g.Key] : null;

                    if (!string.IsNullOrEmpty(competitorStore))
                    {
                        bestPriceEntry = g.FirstOrDefault(p => p.StoreName.ToLower() == competitorStore.ToLower()) ?? bestPriceEntry;
                    }

                    var isSharedBestPrice = g.Count(p => p.Price == bestPriceEntry.Price) > 1;
                    var isMyBestPrice = myPriceEntry != null && myPriceEntry.Price == bestPriceEntry.Price;

                    var secondBestPrice = g
                        .Where(p => p.Price > bestPriceEntry.Price && p.Price != myPriceEntry?.Price)
                        .OrderBy(p => p.Price)
                        .FirstOrDefault()?.Price ?? 0;

                    var bestPrice = bestPriceEntry.Price;
                    var myPrice = myPriceEntry != null ? myPriceEntry.Price : bestPrice;

                    productFlagsDictionary.TryGetValue(bestPriceEntry.ProductId, out var flagIds);
                    flagIds = flagIds ?? new List<int>();

                    bool isUniqueBestPrice;
                    decimal? savings;

                    if (string.IsNullOrEmpty(competitorStore))
                    {
                        isUniqueBestPrice = isMyBestPrice && !isSharedBestPrice && secondBestPrice > myPrice;
                        savings = isUniqueBestPrice ? Math.Round(secondBestPrice - bestPrice, 2) : (decimal?)null;
                    }
                    else
                    {
                        isUniqueBestPrice = myPrice < bestPrice;
                        savings = isUniqueBestPrice ? Math.Abs(Math.Round(myPrice - bestPrice, 2)) : (decimal?)null;
                    }

                    return new
                    {
                        bestPriceEntry.ProductId,
                        bestPriceEntry.ProductName,
                        bestPriceEntry.Category,
                        LowestPrice = bestPrice,
                        bestPriceEntry.StoreName,
                        MyPrice = myPrice,
                        ScrapId = bestPriceEntry.ScrapHistoryId,
                        PriceDifference = Math.Round(myPrice - bestPrice, 2),
                        PercentageDifference = Math.Round((myPrice - bestPrice) / bestPrice * 100, 2),
                        Savings = savings,
                        IsSharedBestPrice = isMyBestPrice && isSharedBestPrice,
                        IsUniqueBestPrice = isUniqueBestPrice,
                        bestPriceEntry.IsBidding,
                        bestPriceEntry.Position,
                        MyIsBidding = myPriceEntry?.IsBidding,
                        MyPosition = myPriceEntry?.Position,
                        FlagIds = flagIds,
                        Delivery = bestPriceEntry.AvailabilityNum,
                        MyDelivery = myPriceEntry?.AvailabilityNum,
                        ExternalId = productInfo?.ExternalId,
                        ExternalPrice = productInfo?.ExternalPrice,
                    };
                })
                .ToList();

            var uniqueAllPrices = allPrices.GroupBy(p => p.ProductId).Select(g => g.First()).ToList();

            var allStoreProducts = await _context.Products
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            var missedProducts = allStoreProducts
                .Where(p => !productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.ProductName, p.Category })
                .ToList();

            // Update IsRejected for missed products only if competitorStore is not specified
            if (string.IsNullOrEmpty(competitorStore))
            {
                foreach (var missedProduct in missedProducts)
                {
                    var product = allStoreProducts.FirstOrDefault(p => p.ProductId == missedProduct.ProductId);
                    if (product != null)
                    {
                        product.IsRejected = true;
                    }
                }
                await _context.SaveChangesAsync();
            }

            // Log missed products
            foreach (var missedProduct in missedProducts)
            {
                Console.WriteLine($"Missed product: {missedProduct.ProductName} (ID: {missedProduct.ProductId})");
            }

            return Json(new
            {
                productCount = uniqueAllPrices.Count,
                priceCount = prices.Count,
                myStoreName = storeName,
                prices = uniqueAllPrices,
                missedProducts = missedProducts,
                missedProductsCount = missedProducts.Count,
                setPrice1 = priceValues.SetPrice1,
                setPrice2 = priceValues.SetPrice2
            });
        }




        [HttpPost]
        public async Task<IActionResult> UpdatePricesFromExternalStore(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null || string.IsNullOrEmpty(store.StoreApiUrl) || string.IsNullOrEmpty(store.StoreApiKey))
            {
                return BadRequest("Sklep nie jest połączony z zewnętrznym API.");
            }

            var products = await _context.Products
                .Where(p => p.StoreId == storeId && p.ExternalId.HasValue)
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
                    SetPrice2 = model.SetPrice2
                };
                _context.PriceValues.Add(priceValues);
            }
            else
            {
                priceValues.SetPrice1 = model.SetPrice1;
                priceValues.SetPrice2 = model.SetPrice2;
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

            var product = await _context.Products.FindAsync(productId);
            if (product == null)
            {
                return NotFound();
            }

            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == scrapHistory.StoreId)
                .Select(pv => new { pv.SetPrice1, pv.SetPrice2 })
                .FirstOrDefaultAsync();

            if (priceValues == null)
            {
                priceValues = new { SetPrice1 = 2.00m, SetPrice2 = 2.00m };
            }

            ViewBag.ScrapHistory = scrapHistory;
            ViewBag.ProductName = product.ProductName;
            ViewBag.Url = product.OfferUrl;
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
