


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
        // ---------------------------------
        // 1) Pobieramy sklep (plus plan)
        // ---------------------------------
        var store = await _context.Stores
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.StoreId == storeId);

        if (store == null || store.RemainingScrapes <= 0)
        {
            return;
        }

        // ---------------------------------
        // 2) Lista aliasów sklepu
        // ---------------------------------
        var storeNameVariants = new List<string>();
        if (!string.IsNullOrWhiteSpace(store.StoreName))
            storeNameVariants.Add(store.StoreName.ToLower());
        if (!string.IsNullOrWhiteSpace(store.StoreNameGoogle))
            storeNameVariants.Add(store.StoreNameGoogle.ToLower());
        if (!string.IsNullOrWhiteSpace(store.StoreNameCeneo))
            storeNameVariants.Add(store.StoreNameCeneo.ToLower());

        var canonicalStoreName = store.StoreName ?? "";

        // ---------------------------------
        // 3) Pobieramy produkty
        // ---------------------------------
        var products = await _context.Products
            .Where(p => p.StoreId == storeId)
            .ToListAsync();

        // Zerujemy ExternalPrice
        foreach (var product in products)
        {
            product.ExternalPrice = null;
        }

        // ---------------------------------
        // 4) Dane CoOfr (wszystkie w pamięci)
        // ---------------------------------
        var coOfrClasses = await _context.CoOfrs.AsNoTracking().ToListAsync();
        var coOfrPriceHistories = await _context.CoOfrPriceHistories.AsNoTracking().ToListAsync();

        // ---------------------------------
        // 5) Tworzymy ScrapHistory
        // ---------------------------------
        var scrapHistory = new ScrapHistoryClass
        {
            Date = DateTime.Now,
            StoreId = storeId,
            ProductCount = products.Count,
            PriceCount = 0,
            Store = store
        };

        // ---------------------------------
        // 6) Zbiór współbieżny na wyniki
        // ---------------------------------
        var priceHistoriesBag = new ConcurrentBag<PriceHistoryClass>();
        var updatedProductsBag = new ConcurrentBag<ProductClass>();
        var fallbackUsedList = new ConcurrentBag<(string ProductName, decimal Price, decimal? ShippingTotal)>();

        // ---------------------------------
        // 7) Równoległe przetwarzanie produktów
        // ---------------------------------
        await Task.Run(() => Parallel.ForEach(products, product =>
        {
            // Znajdź powiązany CoOfr
            var coOfr = coOfrClasses
                .FirstOrDefault(co => co.ProductIds.Contains(product.ProductId)
                                   || co.ProductIdsGoogle.Contains(product.ProductId));
            if (coOfr == null)
                return;

            var coOfrId = coOfr.Id;

            // Pobierz wszystkie powiązane ceny
            var coOfrPH = coOfrPriceHistories
                .Where(ph => ph.CoOfrClassId == coOfrId)
                .ToList();

            bool hasStorePrice = false;
            bool inCeneoList = coOfr.ProductIds.Contains(product.ProductId);
            bool inGoogleList = coOfr.ProductIdsGoogle.Contains(product.ProductId);

            // --------------------------
            // A) CENEO
            // --------------------------
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

            // --------------------------
            // B) GOOGLE
            // --------------------------
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
                        shippingCostNum = coOfrPrice.GooglePrice.Value == coOfrPrice.GooglePriceWithDelivery.Value
                            ? 0
                            : coOfrPrice.GooglePriceWithDelivery;
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
                        IsGoogle = true
                    };
                    priceHistoriesBag.Add(priceHistoryGoogle);

                    if (isOurStoreGoogle)
                    {
                        foundOurStoreGoogle = true;
                        hasStorePrice = true;
                    }
                }
            }

            // --------------------------
            // C) FALLBACK GoogleXMLPrice
            // --------------------------
            if (store.UseGoogleXMLFeedPrice && inGoogleList && !foundOurStoreGoogle)
            {
                if (product.GoogleXMLPrice.HasValue && product.GoogleXMLPrice.Value > 0)
                {
                    decimal fallbackPrice = product.GoogleXMLPrice.Value;
                    decimal? shippingTotal = null;
                    if (product.GoogleDeliveryXMLPrice.HasValue)
                    {
                        shippingTotal = fallbackPrice + product.GoogleDeliveryXMLPrice.Value;
                    }
                    else
                    {
                        shippingTotal = fallbackPrice;
                    }

                    int fallbackPosition = maxGooglePosition + 1;
                    var priceHistoryFromFeed = new PriceHistoryClass
                    {
                        ProductId = product.ProductId,
                        StoreName = canonicalStoreName,
                        Price = fallbackPrice,
                        Position = fallbackPosition,
                        IsBidding = "GoogleFeed",
                        ShippingCostNum = shippingTotal,
                        ScrapHistory = scrapHistory,
                        IsGoogle = true
                    };
                    priceHistoriesBag.Add(priceHistoryFromFeed);

                    fallbackUsedList.Add((product.ProductName ?? "(Brak nazwy)", fallbackPrice, shippingTotal));
                    hasStorePrice = true;
                }
            }

            // --------------------------
            // D) Odrzucanie / przywracanie
            // --------------------------
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

        // ---------------------------------
        // 8) Zliczamy łączną liczbę cen
        // ---------------------------------
        scrapHistory.PriceCount = priceHistoriesBag.Count;

        // Dodajemy scrapHistory
        _context.ScrapHistories.Add(scrapHistory);

        // *** ZMIANA: CHUNKOWANIE PriceHistories
        // ---------------------------------------
        var priceHistoriesAll = priceHistoriesBag.ToList();
        const int CHUNK_SIZE = 1000; // np. co 500 rekordów
        int total = priceHistoriesAll.Count;
        int totalChunks = (int)Math.Ceiling(total / (double)CHUNK_SIZE);
        Console.WriteLine(); // *** KONSOLA
        Console.WriteLine($"[INFO] Rozpoczynam zapisy PriceHistories w chunkach po {CHUNK_SIZE} szt. (łącznie: {total}, chunków: {totalChunks})."); // *** KONSOLA

        int currentIndex = 0;
        for (int i = 0; i < totalChunks; i++)
        {
            var chunk = priceHistoriesAll
                .Skip(currentIndex)
                .Take(CHUNK_SIZE)
                .ToList();

            Console.WriteLine($"[INFO]  -> Zapis chunk {i + 1} z {totalChunks}, rekordów w tym chunku: {chunk.Count}"); // *** KONSOLA

            _context.PriceHistories.AddRange(chunk);
            await _context.SaveChangesAsync();

            currentIndex += CHUNK_SIZE;
        }

        // *** ZMIANA: CHUNKOWANIE update produktów
        // ----------------------------------------
        var updatedProductsAll = updatedProductsBag.Distinct().ToList();
        total = updatedProductsAll.Count;
        totalChunks = (int)Math.Ceiling(total / (double)CHUNK_SIZE);
        Console.WriteLine(); // *** KONSOLA
        Console.WriteLine($"[INFO] Rozpoczynam zapisy zaktualizowanych produktów w chunkach po {CHUNK_SIZE} szt. (łącznie: {total}, chunków: {totalChunks})."); // *** KONSOLA

        currentIndex = 0;
        for (int i = 0; i < totalChunks; i++)
        {
            var chunk = updatedProductsAll
                .Skip(currentIndex)
                .Take(CHUNK_SIZE)
                .ToList();

            Console.WriteLine($"[INFO]  -> Zapis chunk {i + 1} z {totalChunks}, rekordów w tym chunku: {chunk.Count}"); // *** KONSOLA

            foreach (var prod in chunk)
            {
                _context.Products.Attach(prod);
                _context.Entry(prod).State = EntityState.Modified;
            }

            await _context.SaveChangesAsync();

            currentIndex += CHUNK_SIZE;
        }

        // ---------------------------------
        // 9) Zmniejszamy RemainingScrapes
        // ---------------------------------
        store.RemainingScrapes--;
        await _context.SaveChangesAsync();

        // ---------------------------------
        // 10) Log: fallback
        // ---------------------------------
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

        // ---------------------------------
        // 11) Log: produkty odrzucone
        // ---------------------------------
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



    //public async Task ProcessStoreAsync(int storeId)
    //{
    //    var store = await _context.Stores
    //        .Include(s => s.Plan)
    //        .FirstOrDefaultAsync(s => s.StoreId == storeId);

    //    // Jeżeli sklep nie istnieje lub nie ma już "zapasów" scrapowania, kończymy
    //    if (store == null || store.RemainingScrapes <= 0)
    //    {
    //        return;
    //    }

    //    // 1) Lista aliasów do porównywania nazw sklepów (Ceneo/Google)
    //    var storeNameVariants = new List<string>();
    //    if (!string.IsNullOrWhiteSpace(store.StoreName))
    //        storeNameVariants.Add(store.StoreName.ToLower());

    //    if (!string.IsNullOrWhiteSpace(store.StoreNameGoogle))
    //        storeNameVariants.Add(store.StoreNameGoogle.ToLower());

    //    if (!string.IsNullOrWhiteSpace(store.StoreNameCeneo))
    //        storeNameVariants.Add(store.StoreNameCeneo.ToLower());

    //    // Kanoniczna nazwa sklepu, np. store.StoreName
    //    var canonicalStoreName = store.StoreName ?? "";

    //    // 2) Pobieramy produkty tego sklepu
    //    var products = await _context.Products
    //        .Where(p => p.StoreId == storeId)
    //        .ToListAsync();

    //    // Zerujemy tymczasowo ExternalPrice (jeżeli używane w innym miejscu)
    //    foreach (var product in products)
    //    {
    //        product.ExternalPrice = null;
    //    }

    //    // 3) Dane CoOfr
    //    var coOfrClasses = await _context.CoOfrs.ToListAsync();
    //    var coOfrPriceHistories = await _context.CoOfrPriceHistories.ToListAsync();

    //    // Tworzymy nowy ScrapHistoryClass
    //    var scrapHistory = new ScrapHistoryClass
    //    {
    //        Date = DateTime.Now,
    //        StoreId = storeId,
    //        ProductCount = products.Count,
    //        PriceCount = 0,
    //        Store = store
    //    };

    //    // Kolekcje współbieżne na potrzeby równoległych operacji
    //    var priceHistories = new ConcurrentBag<PriceHistoryClass>();
    //    var updatedProducts = new ConcurrentBag<ProductClass>();

    //    // Pamiętamy, dla których produktów użyliśmy fallbacku:
    //    var fallbackUsedList = new ConcurrentBag<(string ProductName, decimal Price, decimal? ShippingTotal)>();

    //    // 4) Równoległa pętla - przetwarzamy każdy produkt
    //    await Task.Run(() => Parallel.ForEach(products, product =>
    //    {
    //        // Znajdź powiązany CoOfr (Ceneo + Google)
    //        var coOfr = coOfrClasses
    //            .FirstOrDefault(co => co.ProductIds.Contains(product.ProductId)
    //                               || co.ProductIdsGoogle.Contains(product.ProductId));

    //        if (coOfr == null) return; // Brak powiązanego CoOfr -> nic nie robimy

    //        var coOfrId = coOfr.Id;

    //        // Wszystkie historie (Ceneo/Google) dla danego CoOfr
    //        var coOfrPriceHistory = coOfrPriceHistories
    //            .Where(ph => ph.CoOfrClassId == coOfrId)
    //            .ToList();

    //        // Flaga -> czy nasz sklep ma już cenę w Google/Ceneo
    //        bool hasStorePrice = false;

    //        // Czy produkt występuje w Ceneo i/lub Google
    //        bool inCeneoList = coOfr.ProductIds.Contains(product.ProductId);
    //        bool inGoogleList = coOfr.ProductIdsGoogle.Contains(product.ProductId);

    //        // --------------------------
    //        // A) Sprawdzamy CENEO
    //        // --------------------------
    //        foreach (var coOfrPrice in coOfrPriceHistory)
    //        {
    //            if (inCeneoList && coOfrPrice.StoreName != null)
    //            {
    //                var priceValue = coOfrPrice.Price ?? 0;
    //                if (priceValue == 0) continue;

    //                bool isOurStoreCeneo = storeNameVariants.Contains(coOfrPrice.StoreName.ToLower());

    //                var priceHistoryCeneo = new PriceHistoryClass
    //                {
    //                    ProductId = product.ProductId,
    //                    StoreName = isOurStoreCeneo ? canonicalStoreName : coOfrPrice.StoreName,
    //                    Price = priceValue,
    //                    IsBidding = coOfrPrice.IsBidding,
    //                    Position = int.TryParse(coOfrPrice.Position, out var position)
    //                               ? position : (int?)null,
    //                    ShippingCostNum = coOfrPrice.ShippingCostNum,
    //                    AvailabilityNum = coOfrPrice.AvailabilityNum,
    //                    ScrapHistory = scrapHistory,
    //                    IsGoogle = false
    //                };
    //                priceHistories.Add(priceHistoryCeneo);

    //                // Jeśli to nasz sklep, to mamy faktyczną cenę "store"
    //                if (isOurStoreCeneo)
    //                {
    //                    hasStorePrice = true;

    //                    // Nadpisujemy ewentualnie ExportedNameCeneo
    //                    if (!string.IsNullOrEmpty(coOfrPrice.ExportedName))
    //                    {
    //                        lock (product)
    //                        {
    //                            product.ExportedNameCeneo = coOfrPrice.ExportedName;
    //                        }
    //                    }
    //                }
    //            }
    //        }

    //        // --------------------------
    //        // B) Sprawdzamy GOOGLE
    //        // --------------------------
    //        int maxGooglePosition = 0;
    //        bool foundOurStoreGoogle = false;

    //        foreach (var coOfrPrice in coOfrPriceHistory)
    //        {
    //            if (inGoogleList && coOfrPrice.GoogleStoreName != null)
    //            {
    //                var googlePrice = coOfrPrice.GooglePrice ?? 0;
    //                if (googlePrice == 0) continue;

    //                decimal? shippingCostNum = null;
    //                if (coOfrPrice.GooglePrice.HasValue && coOfrPrice.GooglePriceWithDelivery.HasValue)
    //                {
    //                    // Jeżeli GooglePrice == GooglePriceWithDelivery => shipping = 0
    //                    // W przeciwnym wypadku shipping = googlePriceWithDelivery
    //                    // (lub cokolwiek innego, zależnie od interpretacji)
    //                    shippingCostNum = coOfrPrice.GooglePrice.Value == coOfrPrice.GooglePriceWithDelivery.Value
    //                        ? 0
    //                        : coOfrPrice.GooglePriceWithDelivery;
    //                }

    //                bool isOurStoreGoogle = storeNameVariants.Contains(coOfrPrice.GoogleStoreName.ToLower());

    //                // Odczytujemy pozycję (GooglePosition)
    //                int? googlePos = int.TryParse(coOfrPrice.GooglePosition, out var gp) ? gp : (int?)null;
    //                if (googlePos.HasValue && googlePos.Value > maxGooglePosition)
    //                {
    //                    maxGooglePosition = googlePos.Value;
    //                }

    //                var priceHistoryGoogle = new PriceHistoryClass
    //                {
    //                    ProductId = product.ProductId,
    //                    StoreName = isOurStoreGoogle ? canonicalStoreName : coOfrPrice.GoogleStoreName,
    //                    Price = googlePrice,
    //                    Position = googlePos,
    //                    IsBidding = "Google",
    //                    ShippingCostNum = shippingCostNum,
    //                    ScrapHistory = scrapHistory,
    //                    IsGoogle = true  // bo to cena z GoogleScrapera
    //                };
    //                priceHistories.Add(priceHistoryGoogle);

    //                if (isOurStoreGoogle)
    //                {
    //                    foundOurStoreGoogle = true;
    //                    hasStorePrice = true;
    //                }
    //            }
    //        }

    //        // --------------------------------------------------------
    //        // C) FALLBACK do GoogleXMLPrice
    //        // --------------------------------------------------------
    //        if (store.UseGoogleXMLFeedPrice && inGoogleList && !foundOurStoreGoogle)
    //        {
    //            // Jeżeli produkt ma wypełnioną GoogleXMLPrice > 0
    //            if (product.GoogleXMLPrice.HasValue && product.GoogleXMLPrice.Value > 0)
    //            {
    //                decimal fallbackPrice = product.GoogleXMLPrice.Value;
    //                decimal? shippingTotal = null;

    //                // Tworzymy łączną cenę = cena + dostawa
    //                // np. 500 + 20 = 520
    //                if (product.GoogleDeliveryXMLPrice.HasValue)
    //                {
    //                    shippingTotal = fallbackPrice + product.GoogleDeliveryXMLPrice.Value;
    //                }
    //                else
    //                {
    //                    // Może być 0, jeśli brak dostawy
    //                    shippingTotal = fallbackPrice;
    //                }

    //                // Pozycja = maxGooglePosition + 1
    //                int fallbackPosition = maxGooglePosition + 1;

    //                // Tworzymy PriceHistoryClass z feedu XML
    //                var priceHistoryFromFeed = new PriceHistoryClass
    //                {
    //                    ProductId = product.ProductId,
    //                    StoreName = canonicalStoreName,  // Bo to nasz sklep
    //                    Price = fallbackPrice,
    //                    Position = fallbackPosition,
    //                    IsBidding = "GoogleFeed",
    //                    // ShippingCostNum => w tym podejściu to łączny koszt
    //                    ShippingCostNum = shippingTotal,
    //                    ScrapHistory = scrapHistory,
    //                    IsGoogle = true // bo to też "google" (z feedu)
    //                };
    //                priceHistories.Add(priceHistoryFromFeed);

    //                // Log do fallbackUsedList
    //                fallbackUsedList.Add((
    //                    ProductName: product.ProductName ?? "(Brak nazwy)",
    //                    Price: fallbackPrice,
    //                    ShippingTotal: shippingTotal
    //                ));

    //                hasStorePrice = true;
    //            }
    //        }

    //        // --------------------------------------------------------
    //        // D) Odrzucanie lub przywracanie produktu (IsRejected)
    //        // --------------------------------------------------------
    //        lock (product)
    //        {
    //            if (hasStorePrice)
    //            {
    //                if (product.IsRejected)
    //                {
    //                    product.IsRejected = false;
    //                    updatedProducts.Add(product);
    //                }
    //            }
    //            else
    //            {
    //                if (!product.IsRejected)
    //                {
    //                    product.IsRejected = true;
    //                    updatedProducts.Add(product);
    //                }
    //            }
    //        }
    //    }));

    //    // Po zakończeniu pętli
    //    scrapHistory.PriceCount = priceHistories.Count;
    //    _context.ScrapHistories.Add(scrapHistory);
    //    _context.PriceHistories.AddRange(priceHistories);

    //    foreach (var updatedProduct in updatedProducts)
    //    {
    //        _context.Products.Update(updatedProduct);
    //    }

    //    // Zmniejszamy RemainingScrapes i zapisujemy
    //    store.RemainingScrapes--;
    //    await _context.SaveChangesAsync();

    //    // LOG: produkty, dla których użyto fallbacku
    //    if (fallbackUsedList.Count > 0)
    //    {
    //        Console.WriteLine();
    //        Console.WriteLine($"[INFO] Wykorzystano fallback feed do {fallbackUsedList.Count} produktów:");
    //        foreach (var fallbackItem in fallbackUsedList)
    //        {
    //            Console.WriteLine($" - \"{fallbackItem.ProductName}\" " +
    //                              $"Price={fallbackItem.Price}, " +
    //                              $"ShippingTotal={(fallbackItem.ShippingTotal.HasValue ? fallbackItem.ShippingTotal.Value.ToString() : "null")}");
    //        }
    //        Console.WriteLine();
    //    }

    //    // Opcjonalnie: Log produktów odrzuconych
    //    var rejectedProducts = updatedProducts.Where(p => p.IsRejected).ToList();
    //    foreach (var rejectedProduct in rejectedProducts)
    //    {
    //        var coOfrId = coOfrClasses
    //            .FirstOrDefault(co => co.ProductIds.Contains(rejectedProduct.ProductId)
    //                               || co.ProductIdsGoogle.Contains(rejectedProduct.ProductId))
    //            ?.Id;
    //        var relatedPrices = coOfrPriceHistories
    //            .Where(ph => ph.CoOfrClassId == coOfrId)
    //            .Select(ph => new
    //            {
    //                Source = ph.StoreName != null ? "Ceneo" : "Google",
    //                StoreName = ph.StoreName ?? ph.GoogleStoreName,
    //                Price = ph.Price ?? ph.GooglePrice
    //            })
    //            .ToList();

    //        Console.WriteLine($"Produkt odrzucony: {rejectedProduct.ProductName}");
    //        Console.WriteLine($"Sklep użyty do porównania: {store.StoreName}");
    //        Console.WriteLine("Ceny z innych sklepów:");
    //        foreach (var price in relatedPrices)
    //        {
    //            Console.WriteLine($"- Źródło: {price.Source}, Sklep: {price.StoreName}, Cena: {price.Price}");
    //        }
    //    }
    //}
}
