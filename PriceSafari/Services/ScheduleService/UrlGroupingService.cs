//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;

//namespace PriceSafari.Services.ScheduleService
//{
//    public class UrlGroupingService
//    {
//        private readonly PriceSafariContext _context;

//        public UrlGroupingService(PriceSafariContext context)
//        {
//            _context = context;
//        }

//        public async Task<(int totalProducts, List<string> distinctStoreNames)> GroupAndSaveUniqueUrls(List<int> storeIds)
//        {
//            var allProducts = await _context.Products
//                .Include(p => p.Store)
//                .Where(p => p.IsScrapable && p.Store.RemainingDays > 0)
//                .AsNoTracking()
//                .ToListAsync();

//            var products = allProducts
//                .Where(p => storeIds.Contains(p.StoreId))
//                .ToList();

//            var distinctStoreNames = products
//                .Select(p => p.Store.StoreName)
//                .Distinct()
//                .ToList();

//            int totalProducts = products.Count;

//            var productsWithOffer = products.Where(p => !string.IsNullOrEmpty(p.OfferUrl)).ToList();
//            var productsWithoutOffer = products.Where(p => string.IsNullOrEmpty(p.OfferUrl)).ToList();

//            var coOfrs = new List<CoOfrClass>();

//            var groupsByOfferUrl = productsWithOffer
//                .GroupBy(p => p.OfferUrl ?? "")
//                .ToDictionary(g => g.Key, g => g.ToList());

//            foreach (var kvp in groupsByOfferUrl)
//            {
//                var offerUrl = kvp.Key;
//                var productList = kvp.Value;

//                var representativeProduct = productList.FirstOrDefault(p => !string.IsNullOrEmpty(p.GoogleUrl));
//                var chosenGoogleUrl = representativeProduct?.GoogleUrl;
//                var chosenGoogleGid = representativeProduct?.GoogleGid;

//                var coOfr = CreateCoOfrClass(productList, offerUrl, chosenGoogleUrl, chosenGoogleGid);
//                coOfrs.Add(coOfr);

//            }

//            var groupsByGoogleUrlForNoOffer = productsWithoutOffer
//                .Where(p => !string.IsNullOrEmpty(p.GoogleUrl))
//                .GroupBy(p => p.GoogleUrl ?? "")
//                .ToDictionary(g => g.Key, g => g.ToList());

//            foreach (var kvp in groupsByGoogleUrlForNoOffer)
//            {
//                var googleUrl = kvp.Key;
//                var productList = kvp.Value;

//                var representativeGid = productList.FirstOrDefault()?.GoogleGid;

//                var coOfr = CreateCoOfrClass(productList, null, googleUrl, representativeGid);
//                coOfrs.Add(coOfr);

//            }

//            var productsWithNoUrl = productsWithoutOffer.Where(p => string.IsNullOrEmpty(p.GoogleUrl)).ToList();
//            if (productsWithNoUrl.Any())
//            {

//                var coOfr = CreateCoOfrClass(productsWithNoUrl, null, null, null);
//                coOfrs.Add(coOfr);
//            }

//            _context.CoOfrs.RemoveRange(_context.CoOfrs);
//            await _context.SaveChangesAsync();

//            _context.CoOfrs.AddRange(coOfrs);
//            await _context.SaveChangesAsync();

//            return (totalProducts, distinctStoreNames);
//        }



//        private CoOfrClass CreateCoOfrClass(List<ProductClass> productList, string? offerUrl, string? googleUrl, string? googleGid)
//        {
//            if (string.IsNullOrEmpty(offerUrl)) offerUrl = null;
//            if (string.IsNullOrEmpty(googleUrl)) googleUrl = null;
//            if (string.IsNullOrEmpty(googleGid)) googleGid = null;

//            var coOfr = new CoOfrClass
//            {
//                OfferUrl = offerUrl,
//                GoogleOfferUrl = googleUrl,
//                GoogleGid = googleGid,
//                ProductIds = new List<int>(),
//                ProductIdsGoogle = new List<int>(),
//                StoreNames = new List<string>(),
//                StoreProfiles = new List<string>(),
//                // Inicjalizacja nowej listy
//                StoreData = new List<CoOfrStoreData>(),

//                IsScraped = false,
//                GoogleIsScraped = false,
//                IsRejected = false,
//                GoogleIsRejected = false
//            };

//            var uniqueStoreNames = new HashSet<string>();
//            var uniqueStoreProfiles = new HashSet<string>();

//            foreach (var product in productList)
//            {
//                coOfr.ProductIds.Add(product.ProductId);

//                if (!string.IsNullOrEmpty(googleUrl) && product.GoogleUrl == googleUrl)
//                {
//                    coOfr.ProductIdsGoogle.Add(product.ProductId);
//                }

//                if (product.Store != null)
//                {
//                    uniqueStoreNames.Add(product.Store.StoreName);
//                    uniqueStoreProfiles.Add(product.Store.StoreProfile);



//                    if (product.Store.FetchExtendedData && product.ExternalId.HasValue)
//                    {
//                        var storeData = new CoOfrStoreData
//                        {
//                            StoreId = product.StoreId,

//                            ProductExternalId = product.ExternalId.Value.ToString(),

//                            IsApiProcessed = false,
//                            ExtendedDataApiPrice = null
//                        };

//                        coOfr.StoreData.Add(storeData);
//                    }
//                }

//            }


//            coOfr.StoreNames = uniqueStoreNames.ToList();
//            coOfr.StoreProfiles = uniqueStoreProfiles.ToList();

//            return coOfr;
//        }

//    }
//}






//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;

//namespace PriceSafari.Services.ScheduleService
//{
//    public class UrlGroupingService
//    {
//        private readonly PriceSafariContext _context;

//        public UrlGroupingService(PriceSafariContext context)
//        {
//            _context = context;
//        }

//        public async Task<(int totalProducts, List<string> distinctStoreNames)> GroupAndSaveUniqueUrls(List<int> storeIds)
//        {
//            var products = await _context.Products
//                .Include(p => p.Store)
//                .Where(p => p.IsScrapable && p.Store.RemainingDays > 0)
//                .Where(p => storeIds.Contains(p.StoreId))
//                .AsNoTracking()
//                .ToListAsync();

//            var distinctStoreNames = products
//                .Select(p => p.Store.StoreName)
//                .Distinct()
//                .ToList();

//            int totalProducts = products.Count;

//            var productsWithOffer = products.Where(p => !string.IsNullOrEmpty(p.OfferUrl)).ToList();
//            var productsWithoutOffer = products.Where(p => string.IsNullOrEmpty(p.OfferUrl)).ToList();

//            var coOfrs = new List<CoOfrClass>();

//            var groupsByOfferUrl = productsWithOffer
//                .GroupBy(p => p.OfferUrl ?? "")
//                .ToDictionary(g => g.Key, g => g.ToList());

//            foreach (var kvp in groupsByOfferUrl)
//            {
//                var productList = kvp.Value;
//                var representativeProduct = productList.FirstOrDefault(p => !string.IsNullOrEmpty(p.GoogleUrl));
//                var coOfr = CreateCoOfrClass(productList, kvp.Key, representativeProduct?.GoogleUrl, representativeProduct?.GoogleGid);
//                coOfrs.Add(coOfr);
//            }

//            var groupsByGoogleUrlForNoOffer = productsWithoutOffer
//                .Where(p => !string.IsNullOrEmpty(p.GoogleUrl))
//                .GroupBy(p => p.GoogleUrl ?? "")
//                .ToDictionary(g => g.Key, g => g.ToList());

//            foreach (var kvp in groupsByGoogleUrlForNoOffer)
//            {
//                var productList = kvp.Value;
//                var representativeGid = productList.FirstOrDefault()?.GoogleGid;
//                var coOfr = CreateCoOfrClass(productList, null, kvp.Key, representativeGid);
//                coOfrs.Add(coOfr);
//            }

//            var productsWithNoUrl = productsWithoutOffer.Where(p => string.IsNullOrEmpty(p.GoogleUrl)).ToList();
//            if (productsWithNoUrl.Any())
//            {
//                var coOfr = CreateCoOfrClass(productsWithNoUrl, null, null, null);
//                coOfrs.Add(coOfr);
//            }

//            var strategy = _context.Database.CreateExecutionStrategy();

//            await strategy.ExecuteAsync(async () =>
//            {
//                using var transaction = await _context.Database.BeginTransactionAsync();

//                try
//                {
//                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrStoreDatas]");
//                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrPriceHistories]");
//                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrs]");

//                    try
//                    {
//                        await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[CoOfrs]', RESEED, 0)");
//                        await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[CoOfrStoreDatas]', RESEED, 0)");
//                    }
//                    catch
//                    {
//                    }

//                    _context.CoOfrs.AddRange(coOfrs);
//                    await _context.SaveChangesAsync();

//                    await transaction.CommitAsync();
//                }
//                catch
//                {
//                    await transaction.RollbackAsync();
//                    throw;
//                }
//            });

//            return (totalProducts, distinctStoreNames);
//        }

//        private CoOfrClass CreateCoOfrClass(List<ProductClass> productList, string? offerUrl, string? googleUrl, string? googleGid)
//        {
//            if (string.IsNullOrEmpty(offerUrl)) offerUrl = null;
//            if (string.IsNullOrEmpty(googleUrl)) googleUrl = null;
//            if (string.IsNullOrEmpty(googleGid)) googleGid = null;

//            var coOfr = new CoOfrClass
//            {
//                OfferUrl = offerUrl,
//                GoogleOfferUrl = googleUrl,
//                GoogleGid = googleGid,
//                ProductIds = new List<int>(),
//                ProductIdsGoogle = new List<int>(),
//                StoreNames = new List<string>(),
//                StoreProfiles = new List<string>(),
//                StoreData = new List<CoOfrStoreData>(),
//                IsScraped = false,
//                GoogleIsScraped = false,
//                IsRejected = false,
//                GoogleIsRejected = false,

//                UseGPID = false,
//                UseWRGA = false
//            };

//            var uniqueStoreNames = new HashSet<string>();
//            var uniqueStoreProfiles = new HashSet<string>();


//            bool foundUseGPID = false;
//            bool foundUseWRGA = false;

//            foreach (var product in productList)
//            {
//                coOfr.ProductIds.Add(product.ProductId);

//                if (!string.IsNullOrEmpty(googleUrl) && product.GoogleUrl == googleUrl)
//                {
//                    coOfr.ProductIdsGoogle.Add(product.ProductId);
//                }

//                if (product.Store != null)
//                {
//                    uniqueStoreNames.Add(product.Store.StoreName);
//                    uniqueStoreProfiles.Add(product.Store.StoreProfile);

//                    if (product.Store.UseGPID) foundUseGPID = true;


//                    if (product.Store.UseWRGA) foundUseWRGA = true;


//                    if (product.Store.FetchExtendedData && product.ExternalId.HasValue)
//                    {
//                        var storeData = new CoOfrStoreData
//                        {
//                            StoreId = product.StoreId,
//                            ProductExternalId = product.ExternalId.Value.ToString(),
//                            IsApiProcessed = false,
//                            ExtendedDataApiPrice = null
//                        };
//                        coOfr.StoreData.Add(storeData);
//                    }
//                }
//            }

//            coOfr.UseGPID = foundUseGPID;
//            coOfr.UseWRGA = foundUseWRGA;

//            coOfr.StoreNames = uniqueStoreNames.ToList();
//            coOfr.StoreProfiles = uniqueStoreProfiles.ToList();

//            return coOfr;
//        }
//    }
//}










//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//namespace PriceSafari.Services.ScheduleService
//{
//    public class UrlGroupingService
//    {
//        private readonly PriceSafariContext _context;

//        public UrlGroupingService(PriceSafariContext context)
//        {
//            _context = context;
//        }

//        // Pomocnicza metoda do wyciągania CID z adresu URL
//        private string? ExtractCid(string url)
//        {
//            if (string.IsNullOrEmpty(url)) return null;
//            var match = Regex.Match(url, @"/product/(\d+)");
//            return match.Success ? match.Groups[1].Value : null;
//        }

//        public async Task<(int totalProducts, List<string> distinctStoreNames)> GroupAndSaveUniqueUrls(List<int> storeIds)
//        {
//            var products = await _context.Products
//                .Include(p => p.Store)
//                .Include(p => p.GoogleCatalogs) // Pobieramy dodatkowe katalogi
//                .Where(p => p.IsScrapable && p.Store.RemainingDays > 0)
//                .Where(p => storeIds.Contains(p.StoreId))
//                .AsNoTracking()
//                .ToListAsync();

//            var distinctStoreNames = products.Select(p => p.Store.StoreName).Distinct().ToList();
//            int totalProducts = products.Count;

//            // Zmieniona mapa: Kluczem jest URL, wartością dane potrzebne do stworzenia zadania
//            var googleTaskMap = new Dictionary<string, (string? Gid, string? Cid, bool IsAdditional, List<ProductClass> Products)>();

//            foreach (var product in products)
//            {
//                // A. Katalog GŁÓWNY
//                if (!string.IsNullOrEmpty(product.GoogleUrl))
//                {
//                    string? cid = ExtractCid(product.GoogleUrl);
//                    AddToGoogleMap(googleTaskMap, product.GoogleUrl, product.GoogleGid, cid, false, product);
//                }

//                //// B. Katalogi DODATKOWE (tylko jeśli opcja w sklepie jest włączona)
//                //if (product.Store.UseAdditionalCatalogsForGoogle && product.GoogleCatalogs != null)
//                //{
//                //    foreach (var extra in product.GoogleCatalogs)
//                //    {
//                //        if (!string.IsNullOrEmpty(extra.GoogleUrl))
//                //        {
//                //            // Używamy pola GoogleCid z tabeli lub wyciągamy z URL
//                //            string? cid = extra.GoogleCid ?? ExtractCid(extra.GoogleUrl);
//                //            AddToGoogleMap(googleTaskMap, extra.GoogleUrl, extra.GoogleGid, cid, true, product);
//                //        }
//                //    }
//                //}

//             // DO POPRAWKI
//                if (product.Store.UseAdditionalCatalogsForGoogle && product.GoogleCatalogs != null)
//                {
//                    foreach (var extra in product.GoogleCatalogs)
//                    {
//                        string generatedUrl = "";

//                        // 1. Decydujemy, jaki URL zbudować na podstawie flagi lub zawartości pól
//                        if (!extra.IsExtendedOfferByHid && !string.IsNullOrEmpty(extra.GoogleCid))
//                        {
//                            // --- TRYB KATALOGU ---
//                            generatedUrl = $"https://www.google.com/shopping/product/{extra.GoogleCid}";
//                        }
//                        else if (!string.IsNullOrEmpty(extra.GoogleHid))
//                        {
//                            // --- TRYB OFERTY ROZSZERZONEJ (HID) ---
//                            // Używamy nazwy produktu do zapytania, aby Google poprawnie otworzyło panel boczny
//                            string encodedName = System.Net.WebUtility.UrlEncode(product.ProductNameInStoreForGoogle ?? "");
//                            generatedUrl = $"https://www.google.com/search?q={encodedName}&udm=28#oshopproduct=gid:{extra.GoogleGid},hid:{extra.GoogleHid},pvt:hg,pvo:3&oshop=apv";
//                        }

//                        // 2. Jeśli udało się wygenerować URL, dodajemy zadanie do mapy
//                        if (!string.IsNullOrEmpty(generatedUrl))
//                        {
//                            // Przekazujemy CID (może być null dla ofert HID, co jest poprawne)
//                            AddToGoogleMap(googleTaskMap, generatedUrl, extra.GoogleGid, extra.GoogleCid, true, product);
//                        }
//                    }
//                }
//            }

//            var coOfrs = new List<CoOfrClass>();

//            // Tworzenie zadań Google
//            foreach (var entry in googleTaskMap)
//            {
//                var url = entry.Key;
//                var info = entry.Value;
//                var coOfr = CreateCoOfrClass(info.Products, null, url, info.Gid, info.Cid, info.IsAdditional);
//                coOfr.IsGoogle = true;
//                coOfrs.Add(coOfr);
//            }

//            // Standardowe zadania Ceneo/Inne (OfferUrl)
//            var groupsByOfferUrl = products.Where(p => !string.IsNullOrEmpty(p.OfferUrl))
//                .GroupBy(p => p.OfferUrl ?? "")
//                .ToList();

//            foreach (var group in groupsByOfferUrl)
//            {
//                coOfrs.Add(CreateCoOfrClass(group.ToList(), group.Key, null, null, null, false));
//            }

//            // Zapis do bazy danych
//            var strategy = _context.Database.CreateExecutionStrategy();
//            await strategy.ExecuteAsync(async () =>
//            {
//                using var transaction = await _context.Database.BeginTransactionAsync();
//                try
//                {
//                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrStoreDatas]");
//                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrPriceHistories]");
//                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrs]");

//                    _context.CoOfrs.AddRange(coOfrs);
//                    await _context.SaveChangesAsync();
//                    await transaction.CommitAsync();
//                }
//                catch
//                {
//                    await transaction.RollbackAsync();
//                    throw;
//                }
//            });

//            return (totalProducts, distinctStoreNames);
//        }

//        private void AddToGoogleMap(Dictionary<string, (string? Gid, string? Cid, bool IsAdditional, List<ProductClass> Products)> map,
//            string url, string? gid, string? cid, bool isAdditional, ProductClass product)
//        {
//            if (!map.ContainsKey(url))
//            {
//                map[url] = (gid, cid, isAdditional, new List<ProductClass>());
//            }
//            map[url].Products.Add(product);
//        }

//        private CoOfrClass CreateCoOfrClass(List<ProductClass> productList, string? offerUrl, string? googleUrl, string? googleGid, string? googleCid, bool isAdditional)
//        {
//            // Wyciągamy ustawienia flag z pierwszego produktu w liście (wszystkie należą do tego samego sklepu w danej grupie)
//            // lub sprawdzamy czy jakikolwiek produkt w grupie ma te flagi włączone.
//            bool useGPID = productList.Any(p => p.Store?.UseGPID == true);
//            bool useWRGA = productList.Any(p => p.Store?.UseWRGA == true);

//            var coOfr = new CoOfrClass
//            {
//                OfferUrl = offerUrl,
//                GoogleOfferUrl = googleUrl,
//                GoogleGid = googleGid,
//                GoogleCid = googleCid,
//                IsAdditionalCatalog = isAdditional,

//                // PRZYPISANIE FLAG STERUJĄCYCH SCRAPEREM
//                UseGPID = useGPID,
//                UseWRGA = useWRGA,

//                ProductIds = productList.Select(p => p.ProductId).ToList(),
//                ProductIdsGoogle = googleUrl != null ? productList.Select(p => p.ProductId).ToList() : new List<int>(),
//                StoreNames = productList.Select(p => p.Store?.StoreName ?? "Unknown").Distinct().ToList(),
//                StoreProfiles = productList.Select(p => p.Store?.StoreProfile ?? "").Distinct().ToList(),
//                IsScraped = false,
//                GoogleIsScraped = false,
//                IsGoogle = googleUrl != null,
//                IsRejected = false,
//                GoogleIsRejected = false
//            };

//            foreach (var p in productList)
//            {
//                // Obsługa danych rozszerzonych API
//                if (p.Store != null && p.Store.FetchExtendedData && p.ExternalId.HasValue)
//                {
//                    coOfr.StoreData.Add(new CoOfrStoreData
//                    {
//                        StoreId = p.StoreId,
//                        ProductExternalId = p.ExternalId.Value.ToString(),
//                        IsApiProcessed = false,
//                        ExtendedDataApiPrice = null
//                    });
//                }
//            }

//            return coOfr;
//        }
//    }
//}







using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PriceSafari.Services.ScheduleService
{
    public class UrlGroupingService
    {
        private readonly PriceSafariContext _context;

        public UrlGroupingService(PriceSafariContext context)
        {
            _context = context;
        }

        // Pomocnicza metoda do wyciągania CID z adresu URL
        private string? ExtractCid(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var match = Regex.Match(url, @"/product/(\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        public async Task<(int totalProducts, List<string> distinctStoreNames)> GroupAndSaveUniqueUrls(List<int> storeIds)
        {
            var products = await _context.Products
                .Include(p => p.Store)
                .Include(p => p.GoogleCatalogs) // Pobieramy dodatkowe katalogi
                .Where(p => p.IsScrapable && p.Store.RemainingDays > 0)
                .Where(p => storeIds.Contains(p.StoreId))
                .AsNoTracking()
                .ToListAsync();

            var distinctStoreNames = products.Select(p => p.Store.StoreName).Distinct().ToList();
            int totalProducts = products.Count;

            // Lista wynikowa na wszystkie zadania (Google + Ceneo)
            var coOfrs = new List<CoOfrClass>();

            // --- 1. ETAP: Generowanie zadań GOOGLE (BEZ GRUPOWANIA) ---
            foreach (var product in products)
            {
                // A. Katalog GŁÓWNY
                if (!string.IsNullOrEmpty(product.GoogleUrl))
                {
                    string? cid = ExtractCid(product.GoogleUrl);
                    // Tworzymy osobne zadanie dla tego konkretnego produktu
                    var coOfr = CreateCoOfrClass(
                        new List<ProductClass> { product }, // Lista zawiera TYLKO ten jeden produkt
                        null,
                        product.GoogleUrl,
                        product.GoogleGid,
                        cid,
                        null,
                        false,
                        false
                    );
                    coOfr.IsGoogle = true;
                    coOfrs.Add(coOfr);
                }

                // B. Katalogi DODATKOWE (tylko jeśli opcja w sklepie jest włączona)
                if (product.Store.UseAdditionalCatalogsForGoogle && product.GoogleCatalogs != null)
                {
                    foreach (var extra in product.GoogleCatalogs)
                    {
                        string? gid = extra.GoogleGid;
                        string? cid = extra.GoogleCid;
                        string? hid = extra.GoogleHid;
                        bool isHidMode = extra.IsExtendedOfferByHid;

                        string? googleUrl = null;
                        bool useHidOffer = false;
                        bool isValidTask = false;

                        if (!isHidMode && !string.IsNullOrEmpty(cid))
                        {
                            // SCENARIUSZ 1: Dodatkowe domapowanie przez CID
                            googleUrl = $"https://www.google.com/shopping/product/{cid}";
                            isValidTask = true;
                        }
                        else if (!string.IsNullOrEmpty(hid))
                        {
                            // SCENARIUSZ 2: Dodatkowe domapowanie przez HID (UseGoogleHidOffer = true)
                            useHidOffer = true;
                            // W trybie HID url pozostaje null
                            isValidTask = true;
                        }

                        if (isValidTask)
                        {
                            // Tworzymy osobne zadanie dla tego konkretnego wpisu katalogowego
                            var coOfr = CreateCoOfrClass(
                                new List<ProductClass> { product }, // Lista zawiera TYLKO ten jeden produkt
                                null,
                                googleUrl,
                                gid,
                                cid,
                                hid,
                                true, // IsAdditional = true
                                useHidOffer
                            );
                            coOfr.IsGoogle = true;
                            coOfrs.Add(coOfr);
                        }
                    }
                }
            }

            // --- 2. ETAP: Generowanie zadań CENEO/INNE (Z GRUPOWANIEM PO URL) ---
            // Tutaj zachowujemy starą logikę: grupujemy produkty mające ten sam OfferUrl,
            // aby nie scrapować Ceneo wielokrotnie dla tego samego linku.
            var groupsByOfferUrl = products
                .Where(p => !string.IsNullOrEmpty(p.OfferUrl))
                .GroupBy(p => p.OfferUrl!) // Grupujemy po URL
                .ToList();

            foreach (var group in groupsByOfferUrl)
            {
                // Tutaj przekazujemy całą grupę produktów (group.ToList())
                coOfrs.Add(CreateCoOfrClass(group.ToList(), group.Key, null, null, null, null, false, false));
            }

            // --- 3. ZAPIS DO BAZY DANYCH ---
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrStoreDatas]");
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrPriceHistories]");
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrs]");

                    // Dzielimy na paczki przy zapisie, na wypadek dużej liczby rekordów
                    const int batchSize = 1000;
                    for (int i = 0; i < coOfrs.Count; i += batchSize)
                    {
                        var batch = coOfrs.Skip(i).Take(batchSize).ToList();
                        _context.CoOfrs.AddRange(batch);
                        await _context.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            return (totalProducts, distinctStoreNames);
        }

        private CoOfrClass CreateCoOfrClass(List<ProductClass> productList, string? offerUrl, string? googleUrl, string? googleGid, string? googleCid, string? googleHid, bool isAdditional, bool useHidOffer)
        {
            bool useGPID = productList.Any(p => p.Store?.UseGPID == true);
            bool useWRGA = productList.Any(p => p.Store?.UseWRGA == true);

            var coOfr = new CoOfrClass
            {
                OfferUrl = offerUrl,
                GoogleOfferUrl = googleUrl,
                GoogleGid = googleGid,
                GoogleCid = googleCid,
                GoogleHid = googleHid,
                IsAdditionalCatalog = isAdditional,
                UseGoogleHidOffer = useHidOffer,

                UseGPID = useGPID,
                UseWRGA = useWRGA,

                // Listy ID będą zawierać tylko 1 element w przypadku Google
                ProductIds = productList.Select(p => p.ProductId).ToList(),
                ProductIdsGoogle = (googleUrl != null || useHidOffer) ? productList.Select(p => p.ProductId).ToList() : new List<int>(),

                StoreNames = productList.Select(p => p.Store?.StoreName ?? "Unknown").Distinct().ToList(),
                StoreProfiles = productList.Select(p => p.Store?.StoreProfile ?? "").Distinct().ToList(),

                IsScraped = false,
                GoogleIsScraped = false,
                IsGoogle = (googleUrl != null || useHidOffer),
                IsRejected = false,
                GoogleIsRejected = false
            };

            foreach (var p in productList)
            {
                if (p.Store != null && p.Store.FetchExtendedData && p.ExternalId.HasValue)
                {
                    coOfr.StoreData.Add(new CoOfrStoreData
                    {
                        StoreId = p.StoreId,
                        ProductExternalId = p.ExternalId.Value.ToString(),
                        IsApiProcessed = false,
                        ExtendedDataApiPrice = null
                    });
                }
            }

            return coOfr;
        }
    }
}