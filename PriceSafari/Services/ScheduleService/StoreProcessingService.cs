using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class StoreProcessingService
{
    private readonly PriceSafariContext _context;

    public StoreProcessingService(PriceSafariContext context)
    {
        _context = context;
    }

    public async Task ProcessStoreAsync(int storeId)
    {
        var store = await _context.Stores
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.StoreId == storeId);

        if (store == null || store.RemainingDays <= 0) return;

        var storeNameVariants = new List<string>();
        if (!string.IsNullOrWhiteSpace(store.StoreName)) storeNameVariants.Add(store.StoreName.ToLower());
        if (!string.IsNullOrWhiteSpace(store.StoreNameGoogle)) storeNameVariants.Add(store.StoreNameGoogle.ToLower());
        if (!string.IsNullOrWhiteSpace(store.StoreNameCeneo)) storeNameVariants.Add(store.StoreNameCeneo.ToLower());

        var canonicalStoreName = store.StoreName ?? "";

        var products = await _context.Products.Where(p => p.StoreId == storeId).ToListAsync();
        var coOfrClasses = await _context.CoOfrs.AsNoTracking().ToListAsync();
        var coOfrPriceHistories = await _context.CoOfrPriceHistories.AsNoTracking().ToListAsync();

        var apiStoreData = await _context.CoOfrStoreDatas
            .AsNoTracking()
            .Where(sd => sd.StoreId == storeId && sd.IsApiProcessed && sd.ExtendedDataApiPrice.HasValue)
            .ToListAsync();

        var scrapHistory = new ScrapHistoryClass
        {
            Date = DateTime.Now,
            StoreId = storeId,
            ProductCount = products.Count,
            PriceCount = 0,
            Store = store
        };

        var priceHistoriesBag = new ConcurrentBag<PriceHistoryClass>();
        var updatedProductsBag = new ConcurrentBag<ProductClass>();
        var extendedInfoBag = new ConcurrentBag<PriceHistoryExtendedInfoClass>();
        var processedProductsForExtendedInfo = new ConcurrentDictionary<int, bool>();

        await Task.Run(() => Parallel.ForEach(products, product =>
        {
            var relatedCoOfrs = coOfrClasses
                .Where(co => co.ProductIds.Contains(product.ProductId)
                             || co.ProductIdsGoogle.Contains(product.ProductId))
                .ToList();

            if (!relatedCoOfrs.Any()) return;

            var matchingApiData = apiStoreData.FirstOrDefault(sd => relatedCoOfrs.Any(co => co.Id == sd.CoOfrClassId));
            var coOfrWithSales = relatedCoOfrs.FirstOrDefault(co => co.CeneoSalesCount.HasValue);

            if ((coOfrWithSales != null || matchingApiData != null) && processedProductsForExtendedInfo.TryAdd(product.ProductId, true))
            {
                extendedInfoBag.Add(new PriceHistoryExtendedInfoClass
                {
                    ProductId = product.ProductId,
                    ScrapHistory = scrapHistory,
                    CeneoSalesCount = coOfrWithSales?.CeneoSalesCount,
                    ExtendedDataApiPrice = matchingApiData?.ExtendedDataApiPrice
                });
            }

            var allRawPrices = coOfrPriceHistories
                .Where(ph => relatedCoOfrs.Any(co => co.Id == ph.CoOfrClassId))
                .ToList();

            bool foundOurStoreCeneo = false;
            bool foundOurStoreGoogle = false;
            int maxCeneoPosition = 0;
            int maxGooglePosition = 0;

            // --- PRZETWARZANIE CENEO ---
            var ceneoPrices = allRawPrices.Where(ph => ph.StoreName != null).ToList();
            foreach (var coOfrPrice in ceneoPrices)
            {
                decimal priceValue = coOfrPrice.Price ?? 0;
                if (priceValue == 0) continue;

                bool isOurStore = storeNameVariants.Contains(coOfrPrice.StoreName.ToLower());
                int? position = int.TryParse(coOfrPrice.Position, out var pos) ? pos : null;

                if (position > maxCeneoPosition) maxCeneoPosition = position.Value;
                if (isOurStore) foundOurStoreCeneo = true;

                priceHistoriesBag.Add(new PriceHistoryClass
                {
                    ProductId = product.ProductId,
                    StoreName = isOurStore ? canonicalStoreName : coOfrPrice.StoreName,
                    Price = priceValue,
                    IsBidding = coOfrPrice.IsBidding,
                    Position = position,
                    ShippingCostNum = coOfrPrice.ShippingCostNum,
                    CeneoInStock = coOfrPrice.CeneoInStock,
                    ScrapHistory = scrapHistory,
                    IsGoogle = false
                });
            }

            // --- PRZETWARZANIE GOOGLE ---
            var googlePricesRaw = allRawPrices
                .Where(ph => ph.GoogleStoreName != null)
                .Select(ph => {
                    var coOfr = relatedCoOfrs.First(co => co.Id == ph.CoOfrClassId);
                    return new
                    {
                        Data = ph,
                        IsAdditional = coOfr.IsAdditionalCatalog
                    };
                })
                .ToList();

            var deduplicatedGoogle = googlePricesRaw
                .GroupBy(x => x.Data.GoogleStoreName.ToLower())
                .Select(g => g.OrderBy(x => x.IsAdditional).First().Data)
                .ToList();

            foreach (var gp in deduplicatedGoogle)
            {
                decimal gPrice = gp.GooglePrice ?? 0;
                if (gPrice == 0) continue;

                bool isOurStore = storeNameVariants.Contains(gp.GoogleStoreName.ToLower());
                int? gPos = int.TryParse(gp.GooglePosition, out var p) ? p : null;

                if (gPos > maxGooglePosition) maxGooglePosition = gPos.Value;

                if (isOurStore) foundOurStoreGoogle = true;

                priceHistoriesBag.Add(new PriceHistoryClass
                {
                    ProductId = product.ProductId,
                    StoreName = isOurStore ? canonicalStoreName : gp.GoogleStoreName,
                    Price = gPrice,
                    Position = gPos,
                    IsBidding = gp.IsBidding ?? "0",
                    ShippingCostNum = gp.GooglePriceWithDelivery.HasValue ? Math.Max(0, gp.GooglePriceWithDelivery.Value - gPrice) : null,
                    GoogleInStock = gp.GoogleInStock,
                    GoogleOfferPerStoreCount = gp.GoogleOfferPerStoreCount,
                    ScrapHistory = scrapHistory,
                    IsGoogle = true,

                    // --- ZMIANA TUTAJ: Przepisujemy URL z "brudnopisu" do historii ---
                    GoogleOfferUrl = gp.GoogleOfferUrl
                });
            }

            // --- Ceny z XML Feed (Ostrzykiwanie) ---
            if (store.UseCeneoXMLFeedPrice && !foundOurStoreCeneo && (product.GoogleXMLPrice ?? 0) > 0)
            {
                priceHistoriesBag.Add(new PriceHistoryClass
                {
                    ProductId = product.ProductId,
                    StoreName = canonicalStoreName,
                    Price = product.GoogleXMLPrice.Value,
                    Position = maxCeneoPosition + 1,
                    IsBidding = "0",
                    ScrapHistory = scrapHistory,
                    IsGoogle = false,
                    ShippingCostNum = product.GoogleDeliveryXMLPrice
                });
                foundOurStoreCeneo = true;
            }

            if (store.UseGoogleXMLFeedPrice && !foundOurStoreGoogle && (product.GoogleXMLPrice ?? 0) > 0)
            {
                priceHistoriesBag.Add(new PriceHistoryClass
                {
                    ProductId = product.ProductId,
                    StoreName = canonicalStoreName,
                    Price = product.GoogleXMLPrice.Value,
                    Position = maxGooglePosition + 1,
                    IsBidding = "GoogleFeed",
                    ScrapHistory = scrapHistory,
                    IsGoogle = true,
                    ShippingCostNum = product.GoogleDeliveryXMLPrice
                });
                foundOurStoreGoogle = true;
            }

          
            bool hasStorePrice = foundOurStoreCeneo || foundOurStoreGoogle;
            lock (product)
            {
                if (product.IsRejected == hasStorePrice)
                {
                    product.IsRejected = !hasStorePrice;
                    updatedProductsBag.Add(product);
                }
            }
        }));

        scrapHistory.PriceCount = priceHistoriesBag.Count;
        _context.ScrapHistories.Add(scrapHistory);
        await _context.SaveChangesAsync();

        if (extendedInfoBag.Any())
        {
            foreach (var ext in extendedInfoBag) ext.ScrapHistoryId = scrapHistory.Id;
            _context.PriceHistoryExtendedInfos.AddRange(extendedInfoBag);
        }

        var priceHistoriesAll = priceHistoriesBag.ToList();
        const int CHUNK_SIZE = 1000;
        for (int i = 0; i < priceHistoriesAll.Count; i += CHUNK_SIZE)
        {
            _context.PriceHistories.AddRange(priceHistoriesAll.Skip(i).Take(CHUNK_SIZE));
            await _context.SaveChangesAsync();
        }

        var updatedProductsAll = updatedProductsBag.DistinctBy(p => p.ProductId).ToList();
        foreach (var prod in updatedProductsAll)
        {
            _context.Products.Attach(prod);
            _context.Entry(prod).State = EntityState.Modified;
        }
        await _context.SaveChangesAsync();
    }
}