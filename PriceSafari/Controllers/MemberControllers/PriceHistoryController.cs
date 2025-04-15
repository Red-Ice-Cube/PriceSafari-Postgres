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
using PriceSafari.Models.ViewModels;

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

            // 1. Pobieramy najnowszą historię scrapu
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

            // 2. Nazwa naszego sklepu
            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            // 3. Pobieramy ustawienia cenowe
            var priceValues = await _context.PriceValues
                .Where(pv => pv.StoreId == storeId)
                .Select(pv => new
                {
                    pv.SetPrice1,
                    pv.SetPrice2,
                    pv.PriceStep,
                    pv.UsePriceDiff,
                    pv.UseEanForSimulation,
                    pv.UseMarginForSimulation,
                    pv.EnforceMinimalMargin,
                    pv.MinimalMarginPercent
                })
                .FirstOrDefaultAsync() ?? new
                {
                    SetPrice1 = 2.00m,
                    SetPrice2 = 2.00m,
                    PriceStep = 2.00m,
                    UsePriceDiff = true,
                    UseEanForSimulation = true,
                    UseMarginForSimulation = true,          
                    EnforceMinimalMargin = true,
                    MinimalMarginPercent = 0.00m
                };

            // 4. Podstawowe zapytanie (wszyscy scrapowalni w ostatnim scrapie)
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
                                Price = (ph != null ? ph.Price : (decimal?)null),
                                StoreName = (ph != null ? ph.StoreName : null),
                                ScrapHistoryId = (ph != null ? ph.ScrapHistoryId : (int?)null),
                                Position = (ph != null ? ph.Position : (int?)null),
                                IsBidding = ph != null ? ph.IsBidding : null,
                                IsGoogle = (ph != null ? ph.IsGoogle : (bool?)null),
                                AvailabilityNum = (ph != null ? ph.AvailabilityNum : (int?)null),
                                IsRejected = p.IsRejected
                            };

            // 5. Sprawdzamy, czy istnieje aktywny preset
            var activePreset = await _context.CompetitorPresets
                .Include(x => x.CompetitorItems)
                .FirstOrDefaultAsync(cp => cp.StoreId == storeId && cp.NowInUse);

            string activePresetName = null;

            if (activePreset != null)
            {
                // a) Nazwa preset-u
                activePresetName = activePreset.PresetName;

                // b) Filtrujemy w BAZIE: Google / Ceneo
                //    (jeśli w presecie SourceGoogle = false -> wykluczamy Google)
                if (!activePreset.SourceGoogle)
                {
                    baseQuery = baseQuery.Where(p => p.IsGoogle != true);
                }
                //    (jeśli w presecie SourceCeneo = false -> wykluczamy Ceneo)
                if (!activePreset.SourceCeneo)
                {
                    baseQuery = baseQuery.Where(p => p.IsGoogle == true);
                }
            }
            else
            {
                // Jeśli brak presetu -> bierzemy wszystko
                // (żadnego dodatkowego filtra, bo user prosił "brak preset = bierz wszystko")
            }

            // 6. Pobieramy surowe dane z bazy
            var rawPrices = await baseQuery.ToListAsync(); // List<PriceRowDto>

            // 7. Jeśli jest preset -> filtrujemy w pamięci wg UseCompetitor, UseUnmarkedStores
            if (activePreset != null)
            {
                var competitorItemsDict = activePreset.CompetitorItems
                    .GroupBy(ci => new {
                        Store = ci.StoreName.ToLower().Trim(),
                        Source = ci.IsGoogle // bool
                    })
                    .Select(g => g.First())
                    .ToDictionary(
                        x => new {
                            Store = x.StoreName.ToLower().Trim(),
                            Source = x.IsGoogle
                        },
                        x => x.UseCompetitor
                    );

                var storeNameLower = storeName.ToLower().Trim();
                var filteredPrices = new List<PriceRowDto>();

                foreach (var row in rawPrices)
                {
                    // Zawsze bierzemy nasz sklep
                    if (row.StoreName != null &&
                        row.StoreName.ToLower().Trim() == storeNameLower)
                    {
                        filteredPrices.Add(row);
                        continue;
                    }

                    // Klucz do dictionary
                    bool googleFlag = (row.IsGoogle == true);
                    var key = new
                    {
                        Store = (row.StoreName ?? "").ToLower().Trim(),
                        Source = googleFlag
                    };

                    if (competitorItemsDict.TryGetValue(key, out bool useCompetitor))
                    {
                        // Jeśli w presecie mamy zdefiniowane -> bierzemy tylko gdy useCompetitor = true
                        if (useCompetitor) filteredPrices.Add(row);
                    }
                    else
                    {
                        // Sklep nieopisany w presecie -> sprawdzamy UseUnmarkedStores
                        if (activePreset.UseUnmarkedStores)
                            filteredPrices.Add(row);
                    }
                }

                rawPrices = filteredPrices;
            }

            // 9. Pobieramy flagi
            var storeFlags = await _context.Flags
                .Where(f => f.StoreId == storeId)
                .ToListAsync();

            var productFlagsDictionary = storeFlags
                .SelectMany(flag => _context.ProductFlags
                    .Where(pf => pf.FlagId == flag.FlagId)
                    .Select(pf => new { pf.ProductId, pf.FlagId })
                )
                .GroupBy(pf => pf.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(pf => pf.FlagId).ToList()
                );

            // 10. Pobieramy info z external
            var productsWithExternalInfo = await _context.Products
                .Where(p => p.StoreId == storeId && p.ExternalId.HasValue)
                .Select(p => new
                {
                    p.ProductId,
                    p.ExternalId,
                    p.ExternalPrice,
                    p.MainUrl,
                    p.MarginPrice,
                    p.Ean
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
                        p.Ean
                    }
                );

            // 11. Liczymy finalne 'allPrices' (grupujemy i obliczamy bestPrice itd.)
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

                    // "nasza" oferta
                    var myPriceEntries = g.Where(x =>
                        x.StoreName != null &&
                        x.StoreName.ToLower() == storeName.ToLower()
                    );
                    var myPriceEntry = myPriceEntries
                        .OrderByDescending(x => x.IsGoogle == false)
                        .FirstOrDefault();

                    // oferty z ceną
                    var validPrices = g.Where(x => x.Price.HasValue).ToList();

                    bool onlyMe = validPrices.Count() > 0 &&
                                  validPrices.All(x =>
                                      x.StoreName != null &&
                                      x.StoreName.ToLower() == storeName.ToLower()
                                  );

                    // Najniższa
                    var bestPriceEntry = validPrices
                        .OrderBy(x => x.Price)
                        .ThenBy(x => x.StoreName)
                        .ThenByDescending(x => x.IsGoogle == false)
                        .FirstOrDefault();

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
                                // Remis z innym sklepem
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
                            // My mamy wyższą cenę
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

                    // SingleBestCheaperDiff
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

                    // Budujemy obiekt anonimowy
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
                        IsRejected = product.IsRejected || isRejectedDueToZeroPrice,
                        StoreCount = storeCount,
                        SourceGoogle = sourceGoogle,
                        SourceCeneo = sourceCeneo,
                        SingleBestCheaperDiff = singleBestCheaperDiff,
                        SingleBestCheaperDiffPerc = singleBestCheaperDiffPerc
                    };
                })
                .Where(p => p != null)
                .ToList();

            var missedProductsCount = allPrices.Count(p => p.IsRejected);

            // Zwracamy finalne dane
           
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
                useEanForSimulation = priceValues.UseEanForSimulation,

                presetName = activePresetName ?? "PriceSafari"

            });
               
        }

  
        public class PriceRowDto
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
            public decimal? Price { get; set; }
            public string StoreName { get; set; }
            public int? ScrapHistoryId { get; set; }
            public int? Position { get; set; }
            public string IsBidding { get; set; }

            public bool? IsGoogle { get; set; }
            public int? AvailabilityNum { get; set; }
            public bool IsRejected { get; set; }
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







        // 1) Lista dostępnych presetów (bez detali)
        [HttpGet]
        public async Task<IActionResult> GetPresets(int storeId)
        {
            // Sprawdź uprawnienia
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

        // 2) Szczegółowe dane wybranego presetu
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

        // 3) Dla załadowania danych o konkurencji w ostatnim scrapie (bez zmian):
        [HttpGet]
        public async Task<IActionResult> GetCompetitorStoresData(int storeId, string ourSource = "All")
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return Json(new { error = "Nie ma takiego sklepu" });
            }

            // Nazwa naszego sklepu
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
                // Brak danych => pusty wynik
                return Json(new { data = new List<object>() });
            }

            var myPricesQuery = _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id
                          && ph.StoreName.ToLower() == storeName.ToLower());

            // Filtr "ourSource"
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
                    // bez filtrowania
                    break;
            }

            var myProductIds = await myPricesQuery
                .Select(ph => ph.ProductId)
                .Distinct()
                .ToListAsync();

            // Konkurencja
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

            // Gdy presetId == 0 -> tworzymy nowy
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

            // Ustaw podstawowe pola
            preset.PresetName = string.IsNullOrWhiteSpace(model.PresetName)
                ? "No Name"
                : model.PresetName.Trim();

            // Jeśli nowInUse=true -> wyłącz w innych
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
                // Czyścimy istniejące
                preset.CompetitorItems.Clear();

                // Dodajemy z requesta
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
            // Sprawdź uprawnienia do sklepu
            if (!await UserHasAccessToStore(storeId))
            {
                return BadRequest("Brak dostępu do sklepu.");
            }

            // Pobierz wszystkie aktywne presety dla tego sklepu
            var activePresets = await _context.CompetitorPresets
                .Where(p => p.StoreId == storeId && p.NowInUse)
                .ToListAsync();

            // Ustaw flagę NowInUse na false dla każdego aktywnego presetu
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
            // Pobierz preset wraz z powiązanymi elementami
            var preset = await _context.CompetitorPresets
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.PresetId == presetId);

            if (preset == null)
                return NotFound("Preset nie istnieje.");

            // Sprawdź uprawnienia do sklepu, do którego należy preset
            if (!await UserHasAccessToStore(preset.StoreId))
                return BadRequest("Brak dostępu do sklepu.");

            // Usuwamy powiązania – jeśli nie korzystamy z kaskadowego usuwania
            _context.CompetitorPresetItems.RemoveRange(preset.CompetitorItems);

            // Usuwamy główny rekord presetu
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
                      isBidding = p.IsBidding,
                      isGoogle = p.IsGoogle   // <-- DODANE pole
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


        [HttpGet]
        public async Task<IActionResult> PriceTrend(int productId)
        {
            var product = await _context.Products.FindAsync(productId);
            if (product == null)
                return NotFound("Nie znaleziono produktu.");

            if (!await UserHasAccessToStore(product.StoreId))
                return Content("Nie ma takiego sklepu");

            // Zwracamy widok z obiektem 'product'
            return View("~/Views/Panel/PriceHistory/PriceTrend.cshtml", product);
        }

        [HttpGet]
        public async Task<IActionResult> GetPriceTrendData(int productId)
        {
            var product = await _context.Products.FindAsync(productId);
            if (product == null)
                return NotFound(new { Error = "Nie znaleziono produktu." });

            if (!await UserHasAccessToStore(product.StoreId))
                return Unauthorized(new { Error = "Nie ma takiego sklepu." });

            var allScraps = await _context.ScrapHistories
                .Where(sh => sh.StoreId == product.StoreId)
                .ToListAsync();

            var lastScraps = allScraps
                .OrderByDescending(sh => sh.Date)
                .Take(30)
                .OrderBy(sh => sh.Date)
                .ToList();

            var scrapIds = lastScraps.Select(sh => sh.Id).ToList();

            var allPriceHistoriesForProduct = await _context.PriceHistories
                .Where(ph => ph.ProductId == productId)
                .ToListAsync();

            var relevantPriceHistories = allPriceHistoriesForProduct
                .Where(ph => scrapIds.Contains(ph.ScrapHistoryId))
                .ToList();

            // Tutaj zmiana: ScrapDate jako string "yyyy-MM-dd"
            // DODAJEMY pole: Source = (ph.IsGoogle == true) ? "google" : "ceneo"
            var timelineData = lastScraps.Select(scrap => new
            {
                ScrapDate = scrap.Date.ToString("yyyy-MM-dd"), // tylko data, bez czasu
                PricesByStore = relevantPriceHistories
                    .Where(ph => ph.ScrapHistoryId == scrap.Id)
                    .Select(ph => new
                    {
                        ph.StoreName,
                        ph.Price,
                        // Nowe pole - skąd pochodzi oferta
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


        [HttpGet]
        public IActionResult GetPriceChangeDetails(string productIds)
        {
            if (string.IsNullOrEmpty(productIds))
            {
                return Json(new List<object>());
            }

            List<int> productIdList;
            try
            {
                productIds = productIds.Trim();
                if (productIds.StartsWith("[") && productIds.EndsWith("]"))
                {
                    productIds = productIds.Substring(1, productIds.Length - 2);
                }
                productIdList = productIds.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.Parse(s.Trim(' ', '"')))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd parsowania productIds: {productIds}", productIds);
                return Json(new List<object>());
            }

            var products = _context.Products
                .AsEnumerable()
                .Where(p => productIdList.Contains(p.ProductId))
                .Select(p => new {
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

            // 1) Sprawdzamy pierwszy produkt, by ustalić sklep i dostęp użytkownika
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

            // 2) Pobieramy najnowsze scrapowanie
            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id })
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return BadRequest("Brak scrapowania dla sklepu.");
            }
            int latestScrapId = latestScrap.Id;

            // 3) Zbieramy ID wszystkich produktów z simulationItems
            var productIds = simulationItems
                .Select(s => s.ProductId)
                .Distinct()
                .ToList();

            // 4) Pobieramy dane produktów (ProductId, Ean, MarginPrice) – modyfikacja: pobieramy także MarginPrice
            var productsData = await GetProductsInChunksAsync(productIds);
            // Upewnij się, że metoda GetProductsInChunksAsync została zmodyfikowana, by zwracać również MarginPrice.

            // 5) Pobieramy PriceHistories dla tych produktów (dla najnowszego scrapId) – również w paczkach
            var allPriceHistories = await GetPriceHistoriesInChunksAsync(productIds, latestScrapId);

            // Grupujemy PriceHistories wg ProductId
            var priceHistoriesByProduct = allPriceHistories
                .GroupBy(ph => ph.ProductId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Pomocnicza funkcja do wyliczania rankingu
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

            // 6) Przechodzimy po każdym SimulationItem i generujemy wyniki
            foreach (var sim in simulationItems)
            {
                // Szukamy danych produktu
                var product = productsData.FirstOrDefault(p => p.ProductId == sim.ProductId);
                if (product == null)
                {
                    // Brak produktu – pomijamy
                    continue;
                }

                // Szukamy PriceHistories dla danego produktu
                priceHistoriesByProduct.TryGetValue(sim.ProductId, out var allRecordsForProduct);
                if (allRecordsForProduct == null)
                {
                    simulationResults.Add(new
                    {
                        productId = product.ProductId,
                        ean = product.Ean,
                        externalId = product.ExternalId,
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
                        // Dla marży – brak danych
                        currentMargin = (decimal?)null,
                        newMargin = (decimal?)null,
                        currentMarginValue = (decimal?)null,
                        newMarginValue = (decimal?)null
                    });
                    continue;
                }

                // Jeśli MarginPrice jest dostępne (nie null i różne od zera) – wyliczamy marżę
                decimal? currentMargin = null;
                decimal? newMargin = null;
                decimal? currentMarginValue = null;
                decimal? newMarginValue = null;
                if (product.MarginPrice.HasValue && product.MarginPrice.Value != 0)
                {
                    currentMarginValue = sim.CurrentPrice - product.MarginPrice.Value;
                    newMarginValue = sim.NewPrice - product.MarginPrice.Value;
                    currentMargin = Math.Round((currentMarginValue.Value / product.MarginPrice.Value) * 100, 2);
                    newMargin = Math.Round((newMarginValue.Value / product.MarginPrice.Value) * 100, 2);

                }

                // Ranking oraz dane konkurencji – jak wcześniej...
                bool weAreInGoogle = allRecordsForProduct.Any(ph => ph.StoreName == ourStoreName && ph.IsGoogle);
                bool weAreInCeneo = allRecordsForProduct.Any(ph => ph.StoreName == ourStoreName && !ph.IsGoogle);

                var competitorPrices = allRecordsForProduct.Where(ph => ph.StoreName != ourStoreName).ToList();

                // GOOGLE
                var googleCompetitorPrices = competitorPrices.Where(x => x.IsGoogle).Select(x => x.Price).ToList();
                var currentGoogleList = new List<decimal>(googleCompetitorPrices);
                var newGoogleList = new List<decimal>(googleCompetitorPrices);
                if (weAreInGoogle)
                {
                    currentGoogleList.Add(sim.CurrentPrice);
                    newGoogleList.Add(sim.NewPrice);
                }
                int totalGoogleOffers = currentGoogleList.Count;
                string currentGoogleRanking, newGoogleRanking;
                if (totalGoogleOffers == 0)
                {
                    currentGoogleRanking = newGoogleRanking = "-";
                }
                else
                {
                    currentGoogleRanking = weAreInGoogle ? CalculateRanking(currentGoogleList, sim.CurrentPrice) : "-";
                    newGoogleRanking = weAreInGoogle ? CalculateRanking(newGoogleList, sim.NewPrice) : "-";
                }
                var googleCurrentOffers = competitorPrices.Where(x => x.IsGoogle)
                    .Select(x => new { x.Price, x.StoreName }).ToList();
                if (weAreInGoogle)
                {
                    googleCurrentOffers.Add(new { Price = sim.CurrentPrice, StoreName = ourStoreName });
                }
                var googleNewOffers = competitorPrices.Where(x => x.IsGoogle)
                    .Select(x => new { x.Price, x.StoreName }).ToList();
                if (weAreInGoogle)
                {
                    googleNewOffers.Add(new { Price = sim.NewPrice, StoreName = ourStoreName });
                }

                // CENEO
                var ceneoCompetitorPrices = competitorPrices.Where(x => !x.IsGoogle && !string.IsNullOrEmpty(x.StoreName))
                    .Select(x => x.Price).ToList();
                var currentCeneoList = new List<decimal>(ceneoCompetitorPrices);
                var newCeneoList = new List<decimal>(ceneoCompetitorPrices);
                if (weAreInCeneo)
                {
                    currentCeneoList.Add(sim.CurrentPrice);
                    newCeneoList.Add(sim.NewPrice);
                }
                int totalCeneoOffers = currentCeneoList.Count;
                string currentCeneoRanking, newCeneoRanking;
                if (totalCeneoOffers == 0)
                {
                    currentCeneoRanking = newCeneoRanking = "-";
                }
                else
                {
                    currentCeneoRanking = weAreInCeneo ? CalculateRanking(currentCeneoList, sim.CurrentPrice) : "-";
                    newCeneoRanking = weAreInCeneo ? CalculateRanking(newCeneoList, sim.NewPrice) : "-";
                }
                var ceneoCompetitorRecords = competitorPrices.Where(x => !x.IsGoogle && !string.IsNullOrEmpty(x.StoreName))
                    .Select(x => new { x.Price, x.StoreName }).ToList();
                var ceneoCurrentOffers = new List<object>(ceneoCompetitorRecords);
                if (weAreInCeneo)
                {
                    ceneoCurrentOffers.Add(new { Price = sim.CurrentPrice, StoreName = ourStoreName });
                }
                var ceneoNewOffers = new List<object>(ceneoCompetitorRecords);
                if (weAreInCeneo)
                {
                    ceneoNewOffers.Add(new { Price = sim.NewPrice, StoreName = ourStoreName });
                }

                simulationResults.Add(new
                {
                    productId = product.ProductId,
                    ean = product.Ean,
                    externalId = product.ExternalId,
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

            // 7) Zwracamy JSON z naszymi wynikami
            return Json(new
            {
                ourStoreName,
                simulationResults
            });
        }




        public class SimulationItem
        {
            public int ProductId { get; set; }
            public decimal CurrentPrice { get; set; }
            public decimal NewPrice { get; set; }
         
            public int StoreId { get; set; }
        }

        private const int CHUNK_SIZE = 200; // dostosuj w zależności od potrzeb

        private async Task<List<ProductData>> GetProductsInChunksAsync(List<int> productIds)
        {
            var result = new List<ProductData>();
            for (int i = 0; i < productIds.Count; i += CHUNK_SIZE)
            {
                var subset = productIds.Skip(i).Take(CHUNK_SIZE).ToList();
                if (subset.Count == 0) continue;

                var inClause = string.Join(",", subset);

                // Dodajemy ExternalId do selekcji
                string sql = $@"
            SELECT ProductId, Ean, MarginPrice, ExternalId
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
                        ExternalId = p.ExternalId // <-- DODANE
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
            public int? ExternalId { get; set; } // <-- DODANE
        }




        private async Task<List<(int ProductId, decimal Price, bool IsGoogle, string StoreName)>>
               GetPriceHistoriesInChunksAsync(List<int> productIds, int scrapId)
        {
            var result = new List<(int, decimal, bool, string)>();

            for (int i = 0; i < productIds.Count; i += CHUNK_SIZE)
            {
                var subset = productIds.Skip(i).Take(CHUNK_SIZE).ToList();
                if (subset.Count == 0)
                    continue;

                var inClause = string.Join(",", subset);

                string sql = $@"
            SELECT ProductId, Price, IsGoogle, StoreName
            FROM PriceHistories
            WHERE ScrapHistoryId = {scrapId}
              AND ProductId IN ({inClause})
        ";

                // Wczytanie do obiektów anonimowych, potem do ValueTuple
                var partial = await _context.PriceHistories
                    .FromSqlRaw(sql)
                    .Select(ph => new
                    {
                        ph.ProductId,
                        ph.Price,
                        ph.IsGoogle,
                        ph.StoreName
                    })
                    .ToListAsync();

                result.AddRange(
                    partial.Select(x => (x.ProductId, x.Price, x.IsGoogle, x.StoreName))
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
                    UseEanForSimulation = model.UseEanForSimulation,
                    UseMarginForSimulation = model.UseMarginForSimulation,
                    EnforceMinimalMargin = model.EnforceMinimalMargin,
                    MinimalMarginPercent = model.MinimalMarginPercent
                };
                _context.PriceValues.Add(priceValues);
            }
            else
            {
                priceValues.UseEanForSimulation = model.UseEanForSimulation;
                priceValues.UseMarginForSimulation = model.UseMarginForSimulation;
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
            public bool UseEanForSimulation { get; set; }
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