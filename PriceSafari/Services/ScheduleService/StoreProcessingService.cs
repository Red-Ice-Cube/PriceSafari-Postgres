
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using System.Collections.Concurrent;

//public class StoreProcessingService
//{
//    private readonly PriceSafariContext _context;

//    public StoreProcessingService(PriceSafariContext context)
//    {
//        _context = context;
//    }


//    public async Task ProcessStoreAsync(int storeId)
//    {
//        var store = await _context.Stores
//            .Include(s => s.Plan)
//            .FirstOrDefaultAsync(s => s.StoreId == storeId);

//        if (store == null || store.RemainingScrapes <= 0)
//        {
//            return;
//        }

//        var products = await _context.Products
//            .Where(p => p.StoreId == storeId)
//            .ToListAsync();


//        foreach (var product in products)
//        {
//            product.ExternalPrice = null;
//        }

//        var coOfrClasses = await _context.CoOfrs.ToListAsync();
//        var coOfrPriceHistories = await _context.CoOfrPriceHistories.ToListAsync();

//        var scrapHistory = new ScrapHistoryClass
//        {
//            Date = DateTime.Now,
//            StoreId = storeId,
//            ProductCount = products.Count,
//            PriceCount = 0,
//            Store = store
//        };

//        var priceHistories = new ConcurrentBag<PriceHistoryClass>();
//        var updatedProducts = new ConcurrentBag<ProductClass>();

//        await Task.Run(() => Parallel.ForEach(products, product =>
//        {
//            // Znajdujemy właściwy CoOfrClass dla produktu
//            var coOfr = coOfrClasses
//                .FirstOrDefault(co => co.ProductIds.Contains(product.ProductId)
//                                   || co.ProductIdsGoogle.Contains(product.ProductId));

//            if (coOfr != null)
//            {
//                var coOfrId = coOfr.Id;
//                var coOfrPriceHistory = coOfrPriceHistories.Where(ph => ph.CoOfrClassId == coOfrId).ToList();

//                bool hasStorePrice = false;

//                // Sprawdzenie przynależności produktu do list
//                bool inCeneoList = coOfr.ProductIds.Contains(product.ProductId);
//                bool inGoogleList = coOfr.ProductIdsGoogle.Contains(product.ProductId);

//                foreach (var coOfrPrice in coOfrPriceHistory)
//                {
//                    // Procesowanie danych Ceneo tylko jeśli produkt jest w ProductIds
//                    if (inCeneoList && coOfrPrice.StoreName != null)
//                    {
//                        var priceHistoryCeneo = new PriceHistoryClass
//                        {
//                            ProductId = product.ProductId,
//                            StoreName = coOfrPrice.StoreName,
//                            Price = coOfrPrice.Price ?? 0,
//                            IsBidding = coOfrPrice.IsBidding,
//                            Position = int.TryParse(coOfrPrice.Position, out var position) ? position : (int?)null,
//                            ShippingCostNum = coOfrPrice.ShippingCostNum,
//                            AvailabilityNum = coOfrPrice.AvailabilityNum,
//                            ScrapHistory = scrapHistory,
//                            IsGoogle = false
//                        };
//                        priceHistories.Add(priceHistoryCeneo);

//                        if (string.Equals(coOfrPrice.StoreName, store.StoreName, StringComparison.OrdinalIgnoreCase))
//                        {
//                            hasStorePrice = true;

//                            if (!string.IsNullOrEmpty(coOfrPrice.ExportedName))
//                            {
//                                lock (product)
//                                {
//                                    product.ExportedNameCeneo = coOfrPrice.ExportedName;
//                                }
//                            }
//                        }
//                    }

//                    // Procesowanie danych Google tylko jeśli produkt jest w ProductIdsGoogle
//                    if (inGoogleList && coOfrPrice.GoogleStoreName != null)
//                    {
//                        var shippingCostNum = coOfrPrice.GooglePriceWithDelivery;
//                        if (coOfrPrice.GooglePrice.HasValue && coOfrPrice.GooglePriceWithDelivery.HasValue)
//                        {
//                            shippingCostNum = coOfrPrice.GooglePrice.Value == coOfrPrice.GooglePriceWithDelivery.Value
//                                ? 0
//                                : coOfrPrice.GooglePriceWithDelivery;
//                        }
//                        else
//                        {
//                            shippingCostNum = null;
//                        }

//                        var priceHistoryGoogle = new PriceHistoryClass
//                        {
//                            ProductId = product.ProductId,
//                            StoreName = coOfrPrice.GoogleStoreName,
//                            Price = coOfrPrice.GooglePrice ?? 0,
//                            Position = int.TryParse(coOfrPrice.GooglePosition, out var googlePosition) ? googlePosition : (int?)null,
//                            IsBidding = "Google",
//                            ShippingCostNum = shippingCostNum,
//                            ScrapHistory = scrapHistory,
//                            IsGoogle = true
//                        };
//                        priceHistories.Add(priceHistoryGoogle);

//                        if (string.Equals(coOfrPrice.GoogleStoreName, store.StoreName, StringComparison.OrdinalIgnoreCase))
//                        {
//                            hasStorePrice = true;
//                        }
//                    }
//                }

//                lock (product)
//                {
//                    if (hasStorePrice)
//                    {
//                        if (product.IsRejected)
//                        {
//                            product.IsRejected = false;
//                            updatedProducts.Add(product);
//                        }
//                    }
//                    else
//                    {
//                        if (!product.IsRejected)
//                        {
//                            product.IsRejected = true;
//                            updatedProducts.Add(product);
//                        }
//                    }
//                }
//            }
//        }));

//        scrapHistory.PriceCount = priceHistories.Count;
//        _context.ScrapHistories.Add(scrapHistory);
//        _context.PriceHistories.AddRange(priceHistories);

//        // Update products in the database
//        foreach (var updatedProduct in updatedProducts)
//        {
//            _context.Products.Update(updatedProduct);
//        }

//        // Save changes
//        store.RemainingScrapes--;
//        await _context.SaveChangesAsync();

//        // Log rejected products
//        foreach (var rejectedProduct in updatedProducts.Where(p => p.IsRejected))
//        {
//            var coOfrId = coOfrClasses.FirstOrDefault(co => co.ProductIds.Contains(rejectedProduct.ProductId))?.Id;
//            var relatedPrices = coOfrPriceHistories
//                .Where(ph => ph.CoOfrClassId == coOfrId)
//                .Select(ph => new
//                {
//                    Source = ph.StoreName != null ? "Ceneo" : "Google",
//                    StoreName = ph.StoreName ?? ph.GoogleStoreName,
//                    Price = ph.Price ?? ph.GooglePrice
//                })
//                .ToList();

//            Console.WriteLine($"Produkt odrzucony: {rejectedProduct.ProductName}");
//            Console.WriteLine($"Sklep użyty do porównania: {store.StoreName}");
//            Console.WriteLine("Ceny z innych sklepów:");

//            foreach (var price in relatedPrices)
//            {
//                Console.WriteLine($"- Źródło: {price.Source}, Sklep: {price.StoreName}, Cena: {price.Price}");
//            }
//        }
//    }



//}

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

        // 1) Zbuduj listę aliasów (zmień na małe litery do porównywania)
        var storeNameVariants = new List<string>();
        if (!string.IsNullOrWhiteSpace(store.StoreName))
            storeNameVariants.Add(store.StoreName.ToLower());

        if (!string.IsNullOrWhiteSpace(store.StoreNameGoogle))
            storeNameVariants.Add(store.StoreNameGoogle.ToLower());

        if (!string.IsNullOrWhiteSpace(store.StoreNameCeneo))
            storeNameVariants.Add(store.StoreNameCeneo.ToLower());

        // Wybieramy "kanoniczną" nazwę, np. store.StoreName
        var canonicalStoreName = store.StoreName ?? "";

        var products = await _context.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

        foreach (var product in products)
        {
            product.ExternalPrice = null;
        }

        var coOfrClasses = await _context.CoOfrs.ToListAsync();
        var coOfrPriceHistories = await _context.CoOfrPriceHistories.ToListAsync();

        var scrapHistory = new ScrapHistoryClass
        {
            Date = DateTime.Now,
            StoreId = storeId,
            ProductCount = products.Count,
            PriceCount = 0,
            Store = store
        };

        var priceHistories = new ConcurrentBag<PriceHistoryClass>();
        var updatedProducts = new ConcurrentBag<ProductClass>();

        await Task.Run(() => Parallel.ForEach(products, product =>
        {
            var coOfr = coOfrClasses
                .FirstOrDefault(co => co.ProductIds.Contains(product.ProductId)
                                   || co.ProductIdsGoogle.Contains(product.ProductId));

            if (coOfr == null)
                return;

            var coOfrId = coOfr.Id;
            var coOfrPriceHistory = coOfrPriceHistories
                .Where(ph => ph.CoOfrClassId == coOfrId)
                .ToList();

            bool hasStorePrice = false;

            bool inCeneoList = coOfr.ProductIds.Contains(product.ProductId);
            bool inGoogleList = coOfr.ProductIdsGoogle.Contains(product.ProductId);

            foreach (var coOfrPrice in coOfrPriceHistory)
            {
                // ------------------------
                // --- Dane z Ceneo ------
                // ------------------------
                if (inCeneoList && coOfrPrice.StoreName != null)
                {
                    var priceValue = coOfrPrice.Price ?? 0;

                    // Jeśli cena wynosi 0, pomijamy ofertę
                    if (priceValue == 0)
                        continue;

                    // Sprawdź, czy to "nasz" sklep
                    bool isOurStoreCeneo = storeNameVariants.Contains(coOfrPrice.StoreName.ToLower());

                    var priceHistoryCeneo = new PriceHistoryClass
                    {
                        ProductId = product.ProductId,
                        // Jeśli oferta należy do naszego sklepu, nadpisujemy nazwą kanoniczną
                        StoreName = isOurStoreCeneo ? canonicalStoreName : coOfrPrice.StoreName,
                        Price = priceValue,
                        IsBidding = coOfrPrice.IsBidding,
                        Position = int.TryParse(coOfrPrice.Position, out var position) ? position : (int?)null,
                        ShippingCostNum = coOfrPrice.ShippingCostNum,
                        AvailabilityNum = coOfrPrice.AvailabilityNum,
                        ScrapHistory = scrapHistory,
                        IsGoogle = false
                    };
                    priceHistories.Add(priceHistoryCeneo);

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

                // ------------------------
                // --- Dane z Google -----
                // ------------------------
                if (inGoogleList && coOfrPrice.GoogleStoreName != null)
                {
                    var googlePrice = coOfrPrice.GooglePrice ?? 0;

                    // Jeśli cena Google wynosi 0, pomijamy ofertę
                    if (googlePrice == 0)
                        continue;

                    decimal? shippingCostNum = coOfrPrice.GooglePriceWithDelivery;
                    if (coOfrPrice.GooglePrice.HasValue && coOfrPrice.GooglePriceWithDelivery.HasValue)
                    {
                        shippingCostNum = coOfrPrice.GooglePrice.Value == coOfrPrice.GooglePriceWithDelivery.Value
                            ? 0
                            : coOfrPrice.GooglePriceWithDelivery;
                    }
                    else
                    {
                        shippingCostNum = null;
                    }

                    bool isOurStoreGoogle = storeNameVariants.Contains(coOfrPrice.GoogleStoreName.ToLower());

                    var priceHistoryGoogle = new PriceHistoryClass
                    {
                        ProductId = product.ProductId,
                        // Nadpisujemy kanoniczną nazwą, jeśli oferta należy do naszego sklepu
                        StoreName = isOurStoreGoogle ? canonicalStoreName : coOfrPrice.GoogleStoreName,
                        Price = googlePrice,
                        Position = int.TryParse(coOfrPrice.GooglePosition, out var googlePosition) ? googlePosition : (int?)null,
                        IsBidding = "Google",
                        ShippingCostNum = shippingCostNum,
                        ScrapHistory = scrapHistory,
                        IsGoogle = true
                    };
                    priceHistories.Add(priceHistoryGoogle);

                    if (isOurStoreGoogle)
                    {
                        hasStorePrice = true;
                    }
                }
            }

            lock (product)
            {
                if (hasStorePrice)
                {
                    if (product.IsRejected)
                    {
                        product.IsRejected = false;
                        updatedProducts.Add(product);
                    }
                }
                else
                {
                    if (!product.IsRejected)
                    {
                        product.IsRejected = true;
                        updatedProducts.Add(product);
                    }
                }
            }
        }));

        scrapHistory.PriceCount = priceHistories.Count;
        _context.ScrapHistories.Add(scrapHistory);
        _context.PriceHistories.AddRange(priceHistories);

        foreach (var updatedProduct in updatedProducts)
        {
            _context.Products.Update(updatedProduct);
        }

        // Save changes
        store.RemainingScrapes--;
        await _context.SaveChangesAsync();

        // Log rejected products
        foreach (var rejectedProduct in updatedProducts.Where(p => p.IsRejected))
        {
            var coOfrId = coOfrClasses.FirstOrDefault(co => co.ProductIds.Contains(rejectedProduct.ProductId))?.Id;
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
