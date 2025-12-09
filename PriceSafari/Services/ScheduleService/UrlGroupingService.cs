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

using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Collections.Generic;
using System.Linq;
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

        public async Task<(int totalProducts, List<string> distinctStoreNames)> GroupAndSaveUniqueUrls(List<int> storeIds)
        {
            // 1. POBIERANIE DANYCH (Optymalizacja: filtrowanie w bazie)
            var products = await _context.Products
                .Include(p => p.Store)
                .Where(p => p.IsScrapable && p.Store.RemainingDays > 0)
                .Where(p => storeIds.Contains(p.StoreId))
                .AsNoTracking()
                .ToListAsync();

            var distinctStoreNames = products
                .Select(p => p.Store.StoreName)
                .Distinct()
                .ToList();

            int totalProducts = products.Count;

            // --- LOGIKA GRUPOWANIA (W pamięci RAM) ---
            var productsWithOffer = products.Where(p => !string.IsNullOrEmpty(p.OfferUrl)).ToList();
            var productsWithoutOffer = products.Where(p => string.IsNullOrEmpty(p.OfferUrl)).ToList();

            var coOfrs = new List<CoOfrClass>();

            // A. Grupy z URL oferty
            var groupsByOfferUrl = productsWithOffer
                .GroupBy(p => p.OfferUrl ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in groupsByOfferUrl)
            {
                var productList = kvp.Value;
                var representativeProduct = productList.FirstOrDefault(p => !string.IsNullOrEmpty(p.GoogleUrl));
                var coOfr = CreateCoOfrClass(productList, kvp.Key, representativeProduct?.GoogleUrl, representativeProduct?.GoogleGid);
                coOfrs.Add(coOfr);
            }

            // B. Grupy bez URL oferty (tylko GoogleUrl)
            var groupsByGoogleUrlForNoOffer = productsWithoutOffer
                .Where(p => !string.IsNullOrEmpty(p.GoogleUrl))
                .GroupBy(p => p.GoogleUrl ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in groupsByGoogleUrlForNoOffer)
            {
                var productList = kvp.Value;
                var representativeGid = productList.FirstOrDefault()?.GoogleGid;
                var coOfr = CreateCoOfrClass(productList, null, kvp.Key, representativeGid);
                coOfrs.Add(coOfr);
            }

            // C. Produkty "sieroty" (bez żadnego URL)
            var productsWithNoUrl = productsWithoutOffer.Where(p => string.IsNullOrEmpty(p.GoogleUrl)).ToList();
            if (productsWithNoUrl.Any())
            {
                var coOfr = CreateCoOfrClass(productsWithNoUrl, null, null, null);
                coOfrs.Add(coOfr);
            }

            // 2. BEZPIECZNE KASOWANIE I ZAPIS (Obsługa SqlServerRetryingExecutionStrategy)

            // Pobieramy strategię wykonania z kontekstu bazy
            var strategy = _context.Database.CreateExecutionStrategy();

            // Wykonujemy wszystko wewnątrz strategii (wymagane przy EnableRetryOnFailure)
            await strategy.ExecuteAsync(async () =>
            {
                // Dopiero tutaj otwieramy transakcję
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // KROK A: Czyścimy tabele podrzędne (Dzieci)
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrStoreDatas]");
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrPriceHistories]");

                    // KROK B: Czyścimy tabelę główną (Rodzica)
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrs]");

                    // KROK C: Reset liczników ID (Identity)
                    try
                    {
                        await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[CoOfrs]', RESEED, 0)");
                        await _context.Database.ExecuteSqlRawAsync("DBCC CHECKIDENT ('[CoOfrStoreDatas]', RESEED, 0)");
                    }
                    catch
                    {
                        // Ignorujemy błędy resetowania ID
                    }

                    // KROK D: Zapis nowych danych
                    _context.CoOfrs.AddRange(coOfrs);
                    await _context.SaveChangesAsync();

                    // Zatwierdzenie transakcji
                    await transaction.CommitAsync();
                }
                catch
                {
                    // W razie błędu cofamy zmiany
                    await transaction.RollbackAsync();
                    throw; // Rzucamy wyjątek wyżej, by strategia wiedziała, że operacja się nie udała
                }
            });

            return (totalProducts, distinctStoreNames);
        }

        private CoOfrClass CreateCoOfrClass(List<ProductClass> productList, string? offerUrl, string? googleUrl, string? googleGid)
        {
            if (string.IsNullOrEmpty(offerUrl)) offerUrl = null;
            if (string.IsNullOrEmpty(googleUrl)) googleUrl = null;
            if (string.IsNullOrEmpty(googleGid)) googleGid = null;

            var coOfr = new CoOfrClass
            {
                OfferUrl = offerUrl,
                GoogleOfferUrl = googleUrl,
                GoogleGid = googleGid,
                ProductIds = new List<int>(),
                ProductIdsGoogle = new List<int>(),
                StoreNames = new List<string>(),
                StoreProfiles = new List<string>(),
                StoreData = new List<CoOfrStoreData>(),
                IsScraped = false,
                GoogleIsScraped = false,
                IsRejected = false,
                GoogleIsRejected = false
            };

            var uniqueStoreNames = new HashSet<string>();
            var uniqueStoreProfiles = new HashSet<string>();

            foreach (var product in productList)
            {
                coOfr.ProductIds.Add(product.ProductId);

                if (!string.IsNullOrEmpty(googleUrl) && product.GoogleUrl == googleUrl)
                {
                    coOfr.ProductIdsGoogle.Add(product.ProductId);
                }

                if (product.Store != null)
                {
                    uniqueStoreNames.Add(product.Store.StoreName);
                    uniqueStoreProfiles.Add(product.Store.StoreProfile);

                    if (product.Store.FetchExtendedData && product.ExternalId.HasValue)
                    {
                        var storeData = new CoOfrStoreData
                        {
                            StoreId = product.StoreId,
                            ProductExternalId = product.ExternalId.Value.ToString(),
                            IsApiProcessed = false,
                            ExtendedDataApiPrice = null
                        };
                        coOfr.StoreData.Add(storeData);
                    }
                }
            }

            coOfr.StoreNames = uniqueStoreNames.ToList();
            coOfr.StoreProfiles = uniqueStoreProfiles.ToList();

            return coOfr;
        }
    }
}