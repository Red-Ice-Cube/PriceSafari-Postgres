
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


    //public async Task ProcessStoreAsync(int storeId)
    //{
    //    var store = await _context.Stores
    //        .Include(s => s.Plan)
    //        .FirstOrDefaultAsync(s => s.StoreId == storeId);

    //    if (store == null)
    //    {
    //        return;
    //    }

    //    if (store.RemainingScrapes <= 0)
    //    {
    //        return;
    //    }

    //    var products = await _context.Products
    //        .Where(p => p.StoreId == storeId)
    //        .ToListAsync();

    //    // Ustawienie ExternalPrice na null dla każdego produktu
    //    foreach (var product in products)
    //    {
    //        product.ExternalPrice = null;
    //    }

    //    var coOfrClasses = await _context.CoOfrs.ToListAsync();
    //    var coOfrPriceHistories = await _context.CoOfrPriceHistories.ToListAsync();

    //    var scrapHistory = new ScrapHistoryClass
    //    {
    //        Date = DateTime.Now,
    //        StoreId = storeId,
    //        ProductCount = products.Count,
    //        PriceCount = 0,
    //        Store = store
    //    };

    //    var priceHistories = new ConcurrentBag<PriceHistoryClass>();
    //    var updatedProducts = new ConcurrentBag<ProductClass>();

    //    await Task.Run(() => Parallel.ForEach(products, product =>
    //    {
    //        var coOfrId = coOfrClasses.FirstOrDefault(co => co.ProductIds.Contains(product.ProductId))?.Id;
    //        if (coOfrId != null)
    //        {
    //            var coOfrPriceHistory = coOfrPriceHistories.Where(ph => ph.CoOfrClassId == coOfrId).ToList();

    //            bool hasStorePrice = false;

    //            foreach (var coOfrPrice in coOfrPriceHistory)
    //            {
    //                var priceHistory = new PriceHistoryClass
    //                {
    //                    ProductId = product.ProductId,
    //                    StoreName = coOfrPrice.StoreName,
    //                    //Price = coOfrPrice.Price,
    //                    Price = (decimal)coOfrPrice.Price,
    //                    IsBidding = coOfrPrice.IsBidding,
    //                    Position = int.TryParse(coOfrPrice.Position, out var position) ? position : (int?)null,
    //                    ShippingCostNum = coOfrPrice.ShippingCostNum,
    //                    AvailabilityNum = coOfrPrice.AvailabilityNum,
    //                    ScrapHistory = scrapHistory
    //                };

    //                priceHistories.Add(priceHistory);

    //                if (string.Equals(coOfrPrice.StoreName, store.StoreName, StringComparison.OrdinalIgnoreCase))
    //                {
    //                    hasStorePrice = true;

    //                    if (!string.IsNullOrEmpty(coOfrPrice.ExportedName))
    //                    {
    //                        lock (product)
    //                        {
    //                            product.ExportedNameCeneo = coOfrPrice.ExportedName;
    //                        }
    //                    }
    //                }
    //            }

    //            lock (product)
    //            {
    //                if (hasStorePrice)
    //                {
    //                    if (product.IsRejected)
    //                    {
    //                        product.IsRejected = false;
    //                        updatedProducts.Add(product);
    //                    }
    //                }
    //                else
    //                {
    //                    if (!product.IsRejected)
    //                    {
    //                        product.IsRejected = true;
    //                        updatedProducts.Add(product);
    //                    }
    //                }
    //            }
    //        }
    //    }));

    //    scrapHistory.PriceCount = priceHistories.Count;
    //    _context.ScrapHistories.Add(scrapHistory);
    //    _context.PriceHistories.AddRange(priceHistories);

    //    // Aktualizacja produktów w bazie danych
    //    foreach (var updatedProduct in updatedProducts)
    //    {
    //        _context.Products.Update(updatedProduct);
    //    }

    //    // Zapisanie zmian dotyczących ExternalPrice
    //    _context.Products.UpdateRange(products);

    //    store.RemainingScrapes--;

    //    await _context.SaveChangesAsync();

    //    // Logowanie odrzuconych produktów
    //    foreach (var rejectedProduct in updatedProducts.Where(p => p.IsRejected))
    //    {
    //        var coOfrId = coOfrClasses.FirstOrDefault(co => co.ProductIds.Contains(rejectedProduct.ProductId))?.Id;
    //        var relatedPrices = coOfrPriceHistories
    //            .Where(ph => ph.CoOfrClassId == coOfrId)
    //            .Select(ph => new { ph.StoreName, ph.Price })
    //            .ToList();

    //        Console.WriteLine($"Produkt odrzucony: {rejectedProduct.ProductName}");
    //        Console.WriteLine($"Sklep użyty do porównania: {store.StoreName}");
    //        Console.WriteLine("Ceny z innych sklepów:");

    //        foreach (var price in relatedPrices)
    //        {
    //            Console.WriteLine($"- Sklep: {price.StoreName}, Cena: {price.Price}");
    //        }
    //    }
    //}

    public async Task ProcessStoreAsync(int storeId)
    {
        var store = await _context.Stores
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.StoreId == storeId);

        if (store == null || store.RemainingScrapes <= 0)
        {
            return;
        }

        var products = await _context.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

        // Reset ExternalPrice for each product
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
            var coOfrId = coOfrClasses.FirstOrDefault(co => co.ProductIds.Contains(product.ProductId))?.Id;
            if (coOfrId != null)
            {
                var coOfrPriceHistory = coOfrPriceHistories.Where(ph => ph.CoOfrClassId == coOfrId).ToList();

                bool hasStorePrice = false;

                foreach (var coOfrPrice in coOfrPriceHistory)
                {
                    // Process Ceneo data
                    if (coOfrPrice.StoreName != null)
                    {
                        var priceHistoryCeneo = new PriceHistoryClass
                        {
                            ProductId = product.ProductId,
                            StoreName = coOfrPrice.StoreName,
                            Price = coOfrPrice.Price ?? 0,
                            IsBidding = coOfrPrice.IsBidding,
                            Position = int.TryParse(coOfrPrice.Position, out var position) ? position : (int?)null,
                            ShippingCostNum = coOfrPrice.ShippingCostNum,
                            AvailabilityNum = coOfrPrice.AvailabilityNum,
                            ScrapHistory = scrapHistory,
                            IsGoogle = false
                        };
                        priceHistories.Add(priceHistoryCeneo);

                        if (string.Equals(coOfrPrice.StoreName, store.StoreName, StringComparison.OrdinalIgnoreCase))
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

                    // Process Google data
                    if (coOfrPrice.GoogleStoreName != null)
                    {
                        var priceHistoryGoogle = new PriceHistoryClass
                        {
                            ProductId = product.ProductId,
                            StoreName = coOfrPrice.GoogleStoreName,
                            Price = coOfrPrice.GooglePrice ?? 0,
                            Position = int.TryParse(coOfrPrice.GooglePosition, out var googlePosition) ? googlePosition : (int?)null,
                            IsBidding = "Google",
                            ShippingCostNum = coOfrPrice.GooglePriceWithDelivery,
                            ScrapHistory = scrapHistory,
                            IsGoogle = true
                        };
                        priceHistories.Add(priceHistoryGoogle);

                        if (string.Equals(coOfrPrice.GoogleStoreName, store.StoreName, StringComparison.OrdinalIgnoreCase))
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
            }
        }));

        scrapHistory.PriceCount = priceHistories.Count;
        _context.ScrapHistories.Add(scrapHistory);
        _context.PriceHistories.AddRange(priceHistories);

        // Update products in the database
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

