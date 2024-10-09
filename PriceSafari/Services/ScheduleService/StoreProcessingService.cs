using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;

public class StoreProcessingService
{
    private readonly PriceSafariContext _context;

    public StoreProcessingService(PriceSafariContext context)
    {
        _context = context;
    }

    public async Task ProcessStoreAsync(int storeId)
    {
        var store = await _context.Stores.FindAsync(storeId);
        if (store == null)
        {
            
            return;
        }

   

        var products = await _context.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

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

        var priceHistories = new List<PriceHistoryClass>();
        var rejectedProducts = new List<ProductClass>();

        // Parallel processing logic
        await Task.Run(() => Parallel.ForEach(products, product =>
        {
            var coOfrId = coOfrClasses.FirstOrDefault(co => co.ProductIds.Contains(product.ProductId))?.Id;
            if (coOfrId != null)
            {
                var coOfrPriceHistory = coOfrPriceHistories.Where(ph => ph.CoOfrClassId == coOfrId).ToList();

                bool hasStorePrice = false;

                foreach (var coOfrPrice in coOfrPriceHistory)
                {
                    var priceHistory = new PriceHistoryClass
                    {
                        ProductId = product.ProductId,
                        StoreName = coOfrPrice.StoreName,
                        Price = coOfrPrice.Price,
                        IsBidding = coOfrPrice.IsBidding,
                        Position = coOfrPrice.Position,
                        ShippingCostNum = coOfrPrice.ShippingCostNum,
                        AvailabilityNum = coOfrPrice.AvailabilityNum,
                        ScrapHistory = scrapHistory
                    };

                    lock (priceHistories)
                    {
                        priceHistories.Add(priceHistory);
                    }

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

                if (!hasStorePrice)
                {
                    lock (_context)
                    {
                        product.IsRejected = true;
                        _context.SaveChanges();
                    }

                    lock (rejectedProducts)
                    {
                        rejectedProducts.Add(product);
                    }
                }
            }
        }));

        scrapHistory.PriceCount = priceHistories.Count;
        _context.ScrapHistories.Add(scrapHistory);
        _context.PriceHistories.AddRange(priceHistories);

        await _context.SaveChangesAsync();

        // Log rejected products
        foreach (var rejectedProduct in rejectedProducts)
        {
            var relatedPrices = coOfrPriceHistories
                .Where(ph => ph.CoOfrClassId == coOfrClasses.FirstOrDefault(co => co.ProductIds.Contains(rejectedProduct.ProductId))?.Id)
                .Select(ph => new { ph.StoreName, ph.Price })
                .ToList();

            Console.WriteLine($"Produkt odrzucony: {rejectedProduct.ProductName}");
            Console.WriteLine($"Sklep użyty do porównania: {store.StoreName}");
            Console.WriteLine("Ceny z innych sklepów:");

            foreach (var price in relatedPrices)
            {
                Console.WriteLine($"- Sklep: {price.StoreName}, Cena: {price.Price}");
            }
        }
    }
}
