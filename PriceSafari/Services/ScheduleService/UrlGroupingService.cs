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
            // Krok 1) Najpierw pobierz TYLKO podstawowe warunki z bazy:
            //         (IsScrapable i .RemainingScrapes > 0), ALE NIE storeIds.Contains(...)!
            var allProducts = await _context.Products
                .Include(p => p.Store)
                .Where(p => p.IsScrapable && p.Store.RemainingScrapes > 0)
                .AsNoTracking()
                .ToListAsync();

            // Krok 2) W pamięci (LINQ-to-Objects) ogranicz do tych, co pasują do `storeIds`
            var products = allProducts
                .Where(p => storeIds.Contains(p.StoreId))
                .ToList();

            // Krok 3) Dalej: distinct, groupBy, itp. w pamięci (jak w Twoim kodzie)
            var distinctStoreNames = products
                .Select(p => p.Store.StoreName)
                .Distinct()
                .ToList();

            int totalProducts = products.Count;

            var productsWithOffer = products.Where(p => !string.IsNullOrEmpty(p.OfferUrl)).ToList();
            var productsWithoutOffer = products.Where(p => string.IsNullOrEmpty(p.OfferUrl)).ToList();

            var coOfrs = new List<CoOfrClass>();

            // --- Grupowanie w pamięci ---
            // 1) Z OfferUrl
            var groupsByOfferUrl = productsWithOffer
                .GroupBy(p => p.OfferUrl ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in groupsByOfferUrl)
            {
                var offerUrl = kvp.Key;
                var productList = kvp.Value;

                var chosenGoogleUrl = productList
                    .Select(p => p.GoogleUrl)
                    .FirstOrDefault(gu => !string.IsNullOrEmpty(gu));

                var coOfr = CreateCoOfrClass(productList, offerUrl, chosenGoogleUrl);
                coOfrs.Add(coOfr);
            }

            // 2) Bez OfferUrl (po GoogleUrl)
            var groupsByGoogleUrlForNoOffer = productsWithoutOffer
                .GroupBy(p => p.GoogleUrl ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in groupsByGoogleUrlForNoOffer)
            {
                var googleUrl = kvp.Key;
                var productList = kvp.Value;

                var coOfr = CreateCoOfrClass(
                    productList,
                    null,
                    string.IsNullOrEmpty(googleUrl) ? null : googleUrl
                );
                coOfrs.Add(coOfr);
            }

            // Nadpisanie starych rekordów w CoOfrs
            _context.CoOfrs.RemoveRange(_context.CoOfrs);
            _context.CoOfrs.AddRange(coOfrs);

            await _context.SaveChangesAsync();
            return (totalProducts, distinctStoreNames);
        }


        private CoOfrClass CreateCoOfrClass(List<ProductClass> productList, string? offerUrl, string? googleUrl)
        {
            if (string.IsNullOrEmpty(offerUrl)) offerUrl = null;
            if (string.IsNullOrEmpty(googleUrl)) googleUrl = null;

            var coOfr = new CoOfrClass
            {
                OfferUrl = offerUrl,
                GoogleOfferUrl = googleUrl,
                ProductIds = new List<int>(),
                ProductIdsGoogle = new List<int>(),
                StoreNames = new List<string>(),
                StoreProfiles = new List<string>(),
                IsScraped = false,
                GoogleIsScraped = false,
                IsRejected = false,
                GoogleIsRejected = false
            };

            foreach (var product in productList)
            {
                // Każdy produkt trafia do ProductIds
                coOfr.ProductIds.Add(product.ProductId);

                // Jeśli mamy wybrany GoogleUrl i produkt go posiada – trafia również do ProductIdsGoogle
                if (!string.IsNullOrEmpty(googleUrl) && product.GoogleUrl == googleUrl)
                {
                    coOfr.ProductIdsGoogle.Add(product.ProductId);
                }

                coOfr.StoreNames.Add(product.Store.StoreName);
                coOfr.StoreProfiles.Add(product.Store.StoreProfile);
            }

            return coOfr;
        }
    }
}
