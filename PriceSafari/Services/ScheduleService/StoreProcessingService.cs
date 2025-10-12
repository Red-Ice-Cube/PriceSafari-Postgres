using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Collections.Concurrent;

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

        if (store == null || store.RemainingScrapes <= 0)
        {
            return;
        }

        var storeNameVariants = new List<string>();
        if (!string.IsNullOrWhiteSpace(store.StoreName))
            storeNameVariants.Add(store.StoreName.ToLower());
        if (!string.IsNullOrWhiteSpace(store.StoreNameGoogle))
            storeNameVariants.Add(store.StoreNameGoogle.ToLower());
        if (!string.IsNullOrWhiteSpace(store.StoreNameCeneo))
            storeNameVariants.Add(store.StoreNameCeneo.ToLower());

        var canonicalStoreName = store.StoreName ?? "";

        var products = await _context.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

        foreach (var product in products)
        {
            product.ExternalPrice = null;
        }

        var coOfrClasses = await _context.CoOfrs.AsNoTracking().ToListAsync();
        var coOfrPriceHistories = await _context.CoOfrPriceHistories.AsNoTracking().ToListAsync();

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
        var fallbackUsedList = new ConcurrentBag<(string ProductName, decimal Price, decimal? ShippingTotal)>();
        var extendedInfoBag = new ConcurrentBag<PriceHistoryExtendedInfoClass>();

        var processedProductsForExtendedInfo = new ConcurrentDictionary<int, bool>();

        await Task.Run(() => Parallel.ForEach(products, product =>
        {

            var coOfr = coOfrClasses
                .FirstOrDefault(co => co.ProductIds.Contains(product.ProductId)
                                   || co.ProductIdsGoogle.Contains(product.ProductId));
            if (coOfr == null)
                return;

            if (processedProductsForExtendedInfo.TryAdd(product.ProductId, true))
            {

                if (coOfr.CeneoSalesCount.HasValue)
                {
                    var extendedInfo = new PriceHistoryExtendedInfoClass
                    {
                        ProductId = product.ProductId,
                        ScrapHistory = scrapHistory,
                        CeneoSalesCount = coOfr.CeneoSalesCount.Value
                    };
                    extendedInfoBag.Add(extendedInfo);
                }
            }

            var coOfrId = coOfr.Id;

            var coOfrPH = coOfrPriceHistories
                .Where(ph => ph.CoOfrClassId == coOfrId)
                .ToList();

            bool hasStorePrice = false;
            bool inCeneoList = coOfr.ProductIds.Contains(product.ProductId);
            bool inGoogleList = coOfr.ProductIdsGoogle.Contains(product.ProductId);

            foreach (var coOfrPrice in coOfrPH)
            {
                if (inCeneoList && coOfrPrice.StoreName != null)
                {
                    decimal priceValue = coOfrPrice.Price ?? 0;
                    if (priceValue == 0)
                        continue;

                    bool isOurStoreCeneo = storeNameVariants.Contains(coOfrPrice.StoreName.ToLower());
                    var priceHistoryCeneo = new PriceHistoryClass
                    {
                        ProductId = product.ProductId,
                        StoreName = isOurStoreCeneo ? canonicalStoreName : coOfrPrice.StoreName,
                        Price = priceValue,
                        IsBidding = coOfrPrice.IsBidding,
                        Position = int.TryParse(coOfrPrice.Position, out var position) ? position : (int?)null,
                        ShippingCostNum = coOfrPrice.ShippingCostNum,
                        AvailabilityNum = coOfrPrice.AvailabilityNum,
                        ScrapHistory = scrapHistory,
                        IsGoogle = false
                    };
                    priceHistoriesBag.Add(priceHistoryCeneo);

                    if (isOurStoreCeneo)
                    {
                        hasStorePrice = true;
                        if (!string.IsNullOrEmpty(coOfrPrice.ExportedName))
                        {
                            lock (product)
                            {
                                product.ExportedNameCeneo = coOfrPrice.ExportedName;
                            }
                        }
                    }
                }
            }

            int maxGooglePosition = 0;
            bool foundOurStoreGoogle = false;

            foreach (var coOfrPrice in coOfrPH)
            {
                if (inGoogleList && coOfrPrice.GoogleStoreName != null)
                {
                    var googlePrice = coOfrPrice.GooglePrice ?? 0;
                    if (googlePrice == 0)
                        continue;
                    decimal? shippingCostNum = null;
                    if (coOfrPrice.GooglePrice.HasValue && coOfrPrice.GooglePriceWithDelivery.HasValue)
                    {
                        decimal basePrice = coOfrPrice.GooglePrice.Value;
                        decimal priceWithDelivery = coOfrPrice.GooglePriceWithDelivery.Value;

                        shippingCostNum = Math.Max(0, priceWithDelivery - basePrice);
                    }

                    bool isOurStoreGoogle = storeNameVariants.Contains(coOfrPrice.GoogleStoreName.ToLower());
                    int? googlePos = int.TryParse(coOfrPrice.GooglePosition, out var gp) ? gp : (int?)null;
                    if (googlePos.HasValue && googlePos.Value > maxGooglePosition)
                    {
                        maxGooglePosition = googlePos.Value;
                    }

                    var priceHistoryGoogle = new PriceHistoryClass
                    {
                        ProductId = product.ProductId,
                        StoreName = isOurStoreGoogle ? canonicalStoreName : coOfrPrice.GoogleStoreName,
                        Price = googlePrice,
                        Position = googlePos,
                        IsBidding = "Google",
                        ShippingCostNum = shippingCostNum,
                        ScrapHistory = scrapHistory,
                        IsGoogle = true,
                        GoogleInStock = coOfrPrice.GoogleInStock,
                        GoogleOfferPerStoreCount = coOfrPrice.GoogleOfferPerStoreCount
                    };
                    priceHistoriesBag.Add(priceHistoryGoogle);

                    if (isOurStoreGoogle)
                    {
                        foundOurStoreGoogle = true;
                        hasStorePrice = true;
                    }
                }
            }

            if (store.UseGoogleXMLFeedPrice && inGoogleList && !foundOurStoreGoogle)
            {

                if (product.GoogleXMLPrice.HasValue && product.GoogleXMLPrice.Value > 0)
                {
                    decimal fallbackPrice = product.GoogleXMLPrice.Value;

                    decimal? fallbackShippingCost = product.GoogleDeliveryXMLPrice;

                    int fallbackPosition = maxGooglePosition + 1;

                    var priceHistoryFromFeed = new PriceHistoryClass
                    {
                        ProductId = product.ProductId,
                        StoreName = canonicalStoreName,
                        Price = fallbackPrice,
                        Position = fallbackPosition,
                        IsBidding = "GoogleFeed",
                        ShippingCostNum = fallbackShippingCost,
                        ScrapHistory = scrapHistory,
                        IsGoogle = true,

                    };
                    priceHistoriesBag.Add(priceHistoryFromFeed);

                    fallbackUsedList.Add((product.ProductName ?? "(Brak nazwy)", fallbackPrice, fallbackShippingCost));
                    hasStorePrice = true;
                }
            }

            lock (product)
            {
                if (hasStorePrice)
                {
                    if (product.IsRejected)
                    {
                        product.IsRejected = false;
                        updatedProductsBag.Add(product);
                    }
                }
                else
                {
                    if (!product.IsRejected)
                    {
                        product.IsRejected = true;
                        updatedProductsBag.Add(product);
                    }
                }
            }
        }));

        scrapHistory.PriceCount = priceHistoriesBag.Count;

        _context.ScrapHistories.Add(scrapHistory);
        await _context.SaveChangesAsync();

        foreach (var extendedInfo in extendedInfoBag)
        {
            extendedInfo.ScrapHistoryId = scrapHistory.Id;
        }

        if (extendedInfoBag.Any())
        {
            _context.PriceHistoryExtendedInfos.AddRange(extendedInfoBag);
        }

        var priceHistoriesAll = priceHistoriesBag.ToList();
        const int CHUNK_SIZE = 1000;
        int total = priceHistoriesAll.Count;
        int totalChunks = (int)Math.Ceiling(total / (double)CHUNK_SIZE);
        Console.WriteLine();
        Console.WriteLine($"[INFO] Rozpoczynam zapisy PriceHistories w chunkach po {CHUNK_SIZE} szt. (łącznie: {total}, chunków: {totalChunks}).");

        int currentIndex = 0;
        for (int i = 0; i < totalChunks; i++)
        {
            var chunk = priceHistoriesAll
                .Skip(currentIndex)
                .Take(CHUNK_SIZE)
                .ToList();

            Console.WriteLine($"[INFO]  -> Zapis chunk {i + 1} z {totalChunks}, rekordów w tym chunku: {chunk.Count}");

            _context.PriceHistories.AddRange(chunk);
            await _context.SaveChangesAsync();

            currentIndex += CHUNK_SIZE;
        }

        var updatedProductsAll = updatedProductsBag.Distinct().ToList();
        total = updatedProductsAll.Count;
        totalChunks = (int)Math.Ceiling(total / (double)CHUNK_SIZE);
        Console.WriteLine();
        Console.WriteLine($"[INFO] Rozpoczynam zapisy zaktualizowanych produktów w chunkach po {CHUNK_SIZE} szt. (łącznie: {total}, chunków: {totalChunks}).");

        currentIndex = 0;
        for (int i = 0; i < totalChunks; i++)
        {
            var chunk = updatedProductsAll
                .Skip(currentIndex)
                .Take(CHUNK_SIZE)
                .ToList();

            Console.WriteLine($"[INFO]  -> Zapis chunk {i + 1} z {totalChunks}, rekordów w tym chunku: {chunk.Count}");

            foreach (var prod in chunk)
            {
                _context.Products.Attach(prod);
                _context.Entry(prod).State = EntityState.Modified;
            }

            await _context.SaveChangesAsync();

            currentIndex += CHUNK_SIZE;
        }

        if (fallbackUsedList.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"[INFO] Wykorzystano fallback feed do {fallbackUsedList.Count} produktów:");
            foreach (var fallbackItem in fallbackUsedList)
            {
                Console.WriteLine($" - \"{fallbackItem.ProductName}\" " +
                                  $"Price={fallbackItem.Price}, " +
                                  $"ShippingTotal={fallbackItem.ShippingTotal?.ToString() ?? "null"}");
            }
            Console.WriteLine();
        }

        var rejectedProducts = updatedProductsAll.Where(p => p.IsRejected).ToList();
        foreach (var rejectedProduct in rejectedProducts)
        {
            var coOfrId = coOfrClasses
                .FirstOrDefault(co => co.ProductIds.Contains(rejectedProduct.ProductId)
                                   || co.ProductIdsGoogle.Contains(rejectedProduct.ProductId))
                ?.Id;

            var relatedPrices = coOfrPriceHistories
                .Where(ph => ph.CoOfrClassId == coOfrId)
                .Select(ph => new
                {
                    Source = ph.StoreName != null ? "Ceneo" : "Google",
                    StoreName = ph.StoreName ?? ph.GoogleStoreName,
                    Price = ph.Price ?? ph.GooglePrice
                })
                .ToList();

            Console.WriteLine($"Produkt odrzucony: {rejectedProduct.ProductName}");
            Console.WriteLine($"Sklep użyty do porównania: {store.StoreName}");
            Console.WriteLine("Ceny z innych sklepów:");
            foreach (var price in relatedPrices)
            {
                Console.WriteLine($"- Źródło: {price.Source}, Sklep: {price.StoreName}, Cena: {price.Price}");
            }
        }
    }

}