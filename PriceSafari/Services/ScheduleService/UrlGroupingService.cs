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
            var allProducts = await _context.Products
                .Include(p => p.Store)
                .Where(p => p.IsScrapable && p.Store.RemainingScrapes > 0)
                .AsNoTracking()
                .ToListAsync();

            var products = allProducts
                .Where(p => storeIds.Contains(p.StoreId))
                .ToList();

            var distinctStoreNames = products
                .Select(p => p.Store.StoreName)
                .Distinct()
                .ToList();

            int totalProducts = products.Count;

            var productsWithOffer = products.Where(p => !string.IsNullOrEmpty(p.OfferUrl)).ToList();
            var productsWithoutOffer = products.Where(p => string.IsNullOrEmpty(p.OfferUrl)).ToList();

            var coOfrs = new List<CoOfrClass>();

            var groupsByOfferUrl = productsWithOffer
                .GroupBy(p => p.OfferUrl ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in groupsByOfferUrl)
            {
                var offerUrl = kvp.Key;
                var productList = kvp.Value;

                // ZMIANA START: Znajdujemy jeden produkt-reprezentant, aby pobrać z niego spójne dane Google.
                var representativeProduct = productList.FirstOrDefault(p => !string.IsNullOrEmpty(p.GoogleUrl));
                var chosenGoogleUrl = representativeProduct?.GoogleUrl;
                var chosenGoogleGid = representativeProduct?.GoogleGid;
                // ZMIANA KONIEC

                // ZMIANA START: Przekazujemy `chosenGoogleGid` do metody tworzącej obiekt.
                var coOfr = CreateCoOfrClass(productList, offerUrl, chosenGoogleUrl, chosenGoogleGid);
                coOfrs.Add(coOfr);
                // ZMIANA KONIEC
            }

            var groupsByGoogleUrlForNoOffer = productsWithoutOffer
                .Where(p => !string.IsNullOrEmpty(p.GoogleUrl)) // Grupujemy tylko te, które mają GoogleUrl
                .GroupBy(p => p.GoogleUrl ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in groupsByGoogleUrlForNoOffer)
            {
                var googleUrl = kvp.Key;
                var productList = kvp.Value;

                // ZMIANA START: Pobieramy GID z pierwszego produktu w grupie (powinny być takie same).
                var representativeGid = productList.FirstOrDefault()?.GoogleGid;
                // ZMIANA KONIEC

                // ZMIANA START: Przekazujemy `representativeGid` do metody tworzącej obiekt.
                var coOfr = CreateCoOfrClass(productList, null, googleUrl, representativeGid);
                coOfrs.Add(coOfr);
                // ZMIANA KONIEC
            }

            // Obsługa produktów bez żadnego URL
            var productsWithNoUrl = productsWithoutOffer.Where(p => string.IsNullOrEmpty(p.GoogleUrl)).ToList();
            if (productsWithNoUrl.Any())
            {
                // Traktujemy je jako jedną "bezadresową" grupę
                var coOfr = CreateCoOfrClass(productsWithNoUrl, null, null, null);
                coOfrs.Add(coOfr);
            }

            // Usunięcie starych i dodanie nowych zgrupowanych ofert
            _context.CoOfrs.RemoveRange(_context.CoOfrs);
            await _context.SaveChangesAsync(); // Upewnijmy się, że usunięcie zostało wykonane

            _context.CoOfrs.AddRange(coOfrs);
            await _context.SaveChangesAsync();

            return (totalProducts, distinctStoreNames);
        }

        // ZMIANA START: Dodajemy parametr `googleGid` do sygnatury metody.
        private CoOfrClass CreateCoOfrClass(List<ProductClass> productList, string? offerUrl, string? googleUrl, string? googleGid)
        {
            if (string.IsNullOrEmpty(offerUrl)) offerUrl = null;
            if (string.IsNullOrEmpty(googleUrl)) googleUrl = null;
            if (string.IsNullOrEmpty(googleGid)) googleGid = null;

            var coOfr = new CoOfrClass
            {
                OfferUrl = offerUrl,
                GoogleOfferUrl = googleUrl,
                GoogleGid = googleGid, // Przypisanie GID
                ProductIds = new List<int>(),
                ProductIdsGoogle = new List<int>(),
                StoreNames = new List<string>(),
                StoreProfiles = new List<string>(),
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
                }
            }

            coOfr.StoreNames = uniqueStoreNames.ToList();
            coOfr.StoreProfiles = uniqueStoreProfiles.ToList();

            return coOfr;
        }
        // ZMIANA KONIEC
    }
}