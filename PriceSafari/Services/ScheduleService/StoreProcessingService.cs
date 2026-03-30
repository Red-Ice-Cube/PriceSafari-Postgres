using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.DTOs; // <--- Dodaj to
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO; // <--- Dodaj to
using System.Linq;
using System.Text.Json; // <--- Dodaj to
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization; // <--- Dodaj to

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
                if (isOurStore)
                {
                    foundOurStoreCeneo = true;

                    if (!string.IsNullOrEmpty(coOfrPrice.ExportedName))
                    {
                        lock (product)
                        {
                            if (product.ExportedNameCeneo != coOfrPrice.ExportedName)
                            {
                                product.ExportedNameCeneo = coOfrPrice.ExportedName;
                                updatedProductsBag.Add(product);
                            }
                        }
                    }
                }

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

                // NOWE: Kalkulacja per KG (tylko gdy oba ustawienia aktywne)
                int? packUnits = null;
                decimal? unitWeightG = null;
                decimal? pricePerKg = null;

                if (store.UseCalculationEnginePerKG && store.GoogleGetTitle
                    && !string.IsNullOrEmpty(gp.GoogleOfferTitle))
                {
                    (packUnits, unitWeightG, pricePerKg) =
                        WeightParser.ParseFromTitle(gp.GoogleOfferTitle, gPrice);
                }

                priceHistoriesBag.Add(new PriceHistoryClass
                {
                    ProductId = product.ProductId,
                    StoreName = isOurStore ? canonicalStoreName : gp.GoogleStoreName,
                    Price = gPrice,
                    Position = gPos,
                    IsBidding = gp.IsBidding ?? "0",
                    ShippingCostNum = gp.GooglePriceWithDelivery.HasValue
                        ? Math.Max(0, gp.GooglePriceWithDelivery.Value - gPrice) : null,
                    GoogleInStock = gp.GoogleInStock,
                    GoogleOfferPerStoreCount = gp.GoogleOfferPerStoreCount,
                    ScrapHistory = scrapHistory,
                    IsGoogle = true,
                    GoogleOfferUrl = gp.GoogleOfferUrl,
                    // NOWE:
                    GooglePackUnits = packUnits,
                    GoogleUnitWeightG = unitWeightG,
                    GooglePricePerKg = pricePerKg
                });
            }

            // --- Czyszczenie ExportedNameCeneo gdy brak nazwy z ostatniego scrapu ---
            if (!string.IsNullOrEmpty(product.ExportedNameCeneo))
            {
                bool foundExportedName = ceneoPrices
                    .Any(cp => storeNameVariants.Contains(cp.StoreName.ToLower())
                                && !string.IsNullOrEmpty(cp.ExportedName));

                if (!foundExportedName)
                {
                    lock (product)
                    {
                        product.ExportedNameCeneo = null;
                        updatedProductsBag.Add(product);
                    }
                }
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

        // ========================================================
        // NOWOŚĆ: GENEROWANIE PLIKU PO ZAKOŃCZENIU ZAPISÓW BAZY
        // ========================================================
        if (store.IsApiExportEnabled && !string.IsNullOrEmpty(store.ApiExportToken))
        {
            await GenerateExportFilesAsync(store, products, priceHistoriesAll, canonicalStoreName);
        }
    }

    private async Task GenerateExportFilesAsync(StoreClass store, List<ProductClass> products, List<PriceHistoryClass> priceHistories, string canonicalStoreName)
    {
        var myStoreNameLower = canonicalStoreName.ToLower().Trim();

        // 1. Budujemy strukturę DTO z surowych danych
        var feed = new ExportFeedDto
        {
            StoreId = store.StoreId,
            StoreName = canonicalStoreName,
            GeneratedAt = DateTime.Now
        };

        // Grupujemy historie po ID produktu dla szybkiego dostępu
        var historiesByProduct = priceHistories.GroupBy(p => p.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var product in products)
        {
            if (!historiesByProduct.TryGetValue(product.ProductId, out var productHistories))
                continue;

            // Szukamy naszej oferty
            var myOffer = productHistories.FirstOrDefault(ph => ph.StoreName != null && ph.StoreName.ToLower().Trim() == myStoreNameLower);

            // Szukamy ofert konkurencji i mapujemy je do DTO
            var competitors = productHistories
                .Where(ph => ph.StoreName != null && ph.StoreName.ToLower().Trim() != myStoreNameLower && ph.Price > 0)
                .Select(ph =>
                {
                    // Ustalenie statusu magazynowego (Stock) zależnie od źródła
                    bool? inStock = ph.IsGoogle == true ? ph.GoogleInStock : ph.CeneoInStock;

                    // Ustalenie URLa oferty, jeśli klient ma włączoną opcję zbierania linków
                    string? finalOfferUrl = null;
                    if (store.CollectGoogleStoreLinks)
                    {
                        if (ph.IsGoogle == true)
                        {
                            // Używamy zeskrobanego linku prosto do sklepu konkurencji z Google
                            finalOfferUrl = ph.GoogleOfferUrl;
                        }
                        else
                        {
                            // Dla Ceneo używamy "sztucznego" linku do ogólnego katalogu produktu
                            finalOfferUrl = product.OfferUrl;
                        }
                    }

                    return new ExportCompetitorDto
                    {
                        StoreName = ph.StoreName,
                        Price = ph.Price,
                        ShippingCost = ph.ShippingCostNum,
                        Source = ph.IsGoogle == true ? "Google" : "Ceneo",
                        InStock = inStock,
                        OfferUrl = finalOfferUrl,
                        // NOWE:
                        PackUnits = ph.GooglePackUnits,
                        UnitWeightG = ph.GoogleUnitWeightG,
                        PricePerKg = ph.GooglePricePerKg
                    };
                }).ToList();

            feed.Products.Add(new ExportProductDto
            {
                ProductId = product.ProductId,
                ExternalId = product.ExternalId?.ToString(),
                ProducerCode = product.ProducerCode,
                Ean = product.Ean,
                ProductName = product.ProductName,
                MyPrice = myOffer?.Price,
                MyShippingCost = myOffer?.ShippingCostNum,
                Competitors = competitors
            });
        }

        var exportFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PriceSafari", "PriceExports");
        if (!Directory.Exists(exportFolder))
        {
            Directory.CreateDirectory(exportFolder);
        }

        // 3. Ścieżki do plików
        var jsonPath = Path.Combine(exportFolder, $"feed_{store.StoreId}.json");
        var xmlPath = Path.Combine(exportFolder, $"feed_{store.StoreId}.xml");

        // 4. Zapis do JSON
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        var jsonString = JsonSerializer.Serialize(feed, jsonOptions);
        await File.WriteAllTextAsync(jsonPath, jsonString);

        // 5. Zapis do XML
        var xmlSerializer = new XmlSerializer(typeof(ExportFeedDto));
        using (var stream = new FileStream(xmlPath, FileMode.Create))
        {
            xmlSerializer.Serialize(stream, feed);
        }
    }
}
public static class WeightParser
{
    /// <summary>
    /// Parsuje tytuł oferty Google i wyciąga informacje o wadze/ilości.
    /// Zwraca (ilość_w_paczce, waga_jednostki_g, cena_za_kg) lub nulle gdy nie da się sparsować.
    /// </summary>
    public static (int? units, decimal? unitWeightG, decimal? pricePerKg) ParseFromTitle(string? title, decimal price)
    {
        if (string.IsNullOrWhiteSpace(title) || price <= 0)
            return (null, null, null);

        // Odrzuć śmieci — zbyt krótkie lub brak cyfr
        if (title.Length < 5 || !Regex.IsMatch(title, @"\d"))
            return (null, null, null);

        int? units = null;
        decimal? weightPerUnitG = null;

        // ═══════════════════════════════════════════════
        // FAZA 1: Szukamy wzorca NxW (najczęstszy)
        // "6x85g", "24 x 85g", "6X50 g", "120x85g", "48x50 g"
        // ═══════════════════════════════════════════════
        var nxw = Regex.Match(title,
            @"(\d+)\s*[xX×]\s*(\d+(?:[.,]\d+)?)\s*(g|kg|ml|l)\b",
            RegexOptions.IgnoreCase);

        if (nxw.Success)
        {
            units = int.Parse(nxw.Groups[1].Value);
            weightPerUnitG = ConvertToGrams(nxw.Groups[2].Value, nxw.Groups[3].Value);
        }

        // ═══════════════════════════════════════════════
        // FAZA 2: Odwrócony wzorzec WxN — "85gx6", "85g x 6"
        // ═══════════════════════════════════════════════
        if (units == null)
        {
            var wxn = Regex.Match(title,
                @"(\d+(?:[.,]\d+)?)\s*(g|kg|ml|l)\s*[xX×]\s*(\d+)\b",
                RegexOptions.IgnoreCase);

            if (wxn.Success)
            {
                weightPerUnitG = ConvertToGrams(wxn.Groups[1].Value, wxn.Groups[2].Value);
                units = int.Parse(wxn.Groups[3].Value);
            }
        }

        // ═══════════════════════════════════════════════
        // FAZA 3: Niemiecki "Menge: N je Bestelleinheit" + waga osobno
        // "800g (Menge: 6 je Bestelleinheit)"
        // "370 g (Menge: 6 je Bestelleinheit)"
        // ═══════════════════════════════════════════════
        if (units == null)
        {
            var menge = Regex.Match(title,
                @"Menge:\s*(\d+)\s*je\s*Bestelleinheit",
                RegexOptions.IgnoreCase);

            if (menge.Success)
            {
                units = int.Parse(menge.Groups[1].Value);
                weightPerUnitG = FindFirstWeight(title);
            }
        }

        // ═══════════════════════════════════════════════
        // FAZA 4: "Ner Pack" — "6er Pack à 50g", "8er Pack"
        // ═══════════════════════════════════════════════
        if (units == null)
        {
            var erPack = Regex.Match(title, @"(\d+)er\s*Pack", RegexOptions.IgnoreCase);
            if (erPack.Success)
            {
                units = int.Parse(erPack.Groups[1].Value);
                weightPerUnitG = FindFirstWeight(title);
            }
        }

        // ═══════════════════════════════════════════════
        // FAZA 5: "N Stück" / "N szt" / "N pieces" + waga osobno
        // "8 Stück, je 6x50 g" — ale NxW złapie to wcześniej
        // "6 Stück" z wagą gdzieś indziej
        // ═══════════════════════════════════════════════
        if (units == null)
        {
            var stueck = Regex.Match(title,
                @"(\d+)\s*(?:Stück|szt\.?|pieces?|pcs)\b",
                RegexOptions.IgnoreCase);

            if (stueck.Success)
            {
                units = int.Parse(stueck.Groups[1].Value);
                weightPerUnitG = FindFirstWeight(title);
            }
        }

        // ═══════════════════════════════════════════════
        // FAZA 6: Totalny fallback — sama waga, 1 sztuka
        // "800g", "2,4 kg", "370 g"
        // ═══════════════════════════════════════════════
        if (units == null && weightPerUnitG == null)
        {
            var singleWeight = FindFirstWeight(title);
            if (singleWeight.HasValue && singleWeight > 0)
            {
                units = 1;
                weightPerUnitG = singleWeight;
            }
        }

        // ═══════════════════════════════════════════════
        // FAZA 7 (BONUS): Zewnętrzny mnożnik
        // Gdy już mamy NxW (np. "6x50g"), sprawdzamy czy jest
        // dodatkowy "8 Stück" albo "- 8 Stück" który mnoży całość.
        // "Gourmet Katzen-Nassfutter, 8 Stück, je 6x50 g" → 8×6×50g
        // ═══════════════════════════════════════════════
        if (units.HasValue && weightPerUnitG.HasValue && nxw.Success)
        {
            // Szukamy "N Stück" gdzie N != units (żeby nie łapać tego samego)
            var outerMultiplier = Regex.Match(title,
                @"(\d+)\s*(?:Stück|szt\.?|pieces?|pcs)\b",
                RegexOptions.IgnoreCase);

            if (outerMultiplier.Success)
            {
                int outerN = int.Parse(outerMultiplier.Groups[1].Value);
                if (outerN != units && outerN > 1 && outerN <= 200)
                {
                    // Mnożymy: 8 opakowań × 6 saszetek = 48 szt
                    units = units * outerN;
                }
            }
            else
            {
                // "8er Pack (8x300g)" — "Ner Pack" jako outer
                var outerErPack = Regex.Match(title, @"(\d+)er\s*Pack", RegexOptions.IgnoreCase);
                if (outerErPack.Success)
                {
                    int outerN = int.Parse(outerErPack.Groups[1].Value);
                    if (outerN != units && outerN > 1 && outerN <= 200)
                    {
                        units = units * outerN;
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════
        // WALIDACJA I KALKULACJA
        // ═══════════════════════════════════════════════
        if (!units.HasValue || !weightPerUnitG.HasValue
            || units <= 0 || weightPerUnitG <= 0
            || units > 10000 || weightPerUnitG > 100_000) // sanity: max 100kg per unit
        {
            return (null, null, null);
        }

        decimal totalWeightKg = (units.Value * weightPerUnitG.Value) / 1000m;

        if (totalWeightKg <= 0)
            return (null, null, null);

        decimal pricePerKg = Math.Round(price / totalWeightKg, 2);

        // Sanity check — cena za kg nie powinna być absurdalna
        if (pricePerKg > 100_000)
            return (null, null, null);

        return (units, weightPerUnitG, pricePerKg);
    }

    /// <summary>
    /// Znajduje pierwszą wagę w tytule (np. "800g", "2,4 kg", "370 g").
    /// Pomija wartości które wyglądają jak ceny (poprzedzone €/zł/PLN).
    /// </summary>
    private static decimal? FindFirstWeight(string title)
    {
        var matches = Regex.Matches(title,
            @"(?<!\S[€$])\b(\d+(?:[.,]\d+)?)\s*(g|kg|ml|l)\b",
            RegexOptions.IgnoreCase);

        foreach (Match m in matches)
        {
            var val = ConvertToGrams(m.Groups[1].Value, m.Groups[2].Value);
            // Pomijamy wartości < 1g (śmieciowe) i > 100kg (nieprawdopodobne)
            if (val > 0 && val <= 100_000)
                return val;
        }

        return null;
    }

    /// <summary>
    /// Konwertuje wartość z podaną jednostką na gramy.
    /// </summary>
    private static decimal ConvertToGrams(string valueStr, string unit)
    {
        valueStr = valueStr.Replace(",", ".");
        if (!decimal.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return 0;

        return unit.ToLower() switch
        {
            "kg" => value * 1000m,
            "g" => value,
            "l" => value * 1000m,   // 1l ≈ 1000g (przybliżenie dla mokrej karmy)
            "ml" => value,           // 1ml ≈ 1g
            _ => 0
        };
    }
}