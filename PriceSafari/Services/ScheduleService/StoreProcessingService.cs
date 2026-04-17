using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.DTOs;
using PriceSafari.Services.ScheduleService;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using System.Linq;
using System.Text.Json;

using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;

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
            .Where(sd => sd.StoreId == storeId && sd.IsApiProcessed
                         && (sd.ExtendedDataApiPrice.HasValue || sd.PurchasePriceFromApi.HasValue))
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
        var googleTitleLookup = new ConcurrentDictionary<(int ProductId, string StoreNameLower), string>();
        var weightConfidenceLookup = new ConcurrentDictionary<(int ProductId, string StoreNameLower), int>();
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

            var googlePriceHistories = new List<PriceHistoryClass>();

            foreach (var gp in deduplicatedGoogle)
            {
                decimal gPrice = gp.GooglePrice ?? 0;
                if (gPrice == 0) continue;

                bool isOurStore = storeNameVariants.Contains(gp.GoogleStoreName.ToLower());
                int? gPos = int.TryParse(gp.GooglePosition, out var p) ? p : null;

                if (gPos > maxGooglePosition) maxGooglePosition = gPos.Value;
                if (isOurStore) foundOurStoreGoogle = true;

                var ph = new PriceHistoryClass
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
                };

                googlePriceHistories.Add(ph);

                if (!string.IsNullOrEmpty(gp.GoogleOfferTitle) && store.GoogleGetTitle)
                {
                    var key = (product.ProductId, gp.GoogleStoreName.ToLower());
                    googleTitleLookup.TryAdd(key, gp.GoogleOfferTitle);
                }
            }

            // --- Inteligentna kalkulacja per KG (batch) ---
            if (store.UseCalculationEnginePerKG && store.GoogleGetTitle && googlePriceHistories.Count > 0)
            {
                // Buduj słownik tytułów dla tego produktu
                var titlesForProduct = new Dictionary<string, string>();
                foreach (var ph in googlePriceHistories)
                {
                    var storeKey = (ph.StoreName ?? "").ToLower();
                    if (googleTitleLookup.TryGetValue((product.ProductId, storeKey), out var title))
                    {
                        titlesForProduct.TryAdd(storeKey, title);
                    }
                }

                var confidenceScores = WeightParser.ResolveWeightsForProduct(googlePriceHistories, titlesForProduct);

                // Zapisz confidence do eksportowego lookup (nie do bazy)
                foreach (var kvp in confidenceScores)
                {
                    weightConfidenceLookup.TryAdd((product.ProductId, kvp.Key), kvp.Value);
                }
            }

            foreach (var ph in googlePriceHistories)
            {
                priceHistoriesBag.Add(ph);
            }

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

            // UWAGA: gdy CopyXMLPrices=true, pomijamy stary mechanizm UseGoogleXMLFeedPrice
            // (nowy serwis CopyXmlPricesService zrobi to po pętli, mapując dynamicznie z XML)
            if (store.UseGoogleXMLFeedPrice && !store.CopyXMLPrices && !foundOurStoreGoogle && (product.GoogleXMLPrice ?? 0) > 0)
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

        // ─── AKTUALIZACJA CEN ZAKUPU Z API ───
        if (store.GetPurchasePriceFromApi && apiStoreData.Any(sd => sd.PurchasePriceFromApi.HasValue))
        {
            foreach (var product in products)
            {
                var relatedCoOfrs = coOfrClasses
                    .Where(co => co.ProductIds.Contains(product.ProductId)
                                 || co.ProductIdsGoogle.Contains(product.ProductId))
                    .ToList();

                if (!relatedCoOfrs.Any()) continue;

                var matchingApiData = apiStoreData
                    .FirstOrDefault(sd => sd.PurchasePriceFromApi.HasValue
                                          && relatedCoOfrs.Any(co => co.Id == sd.CoOfrClassId));

                if (matchingApiData?.PurchasePriceFromApi == null) continue;

                decimal newMarginPrice = matchingApiData.PurchasePriceFromApi.Value;

                if (product.MarginPrice != newMarginPrice)
                {
                    product.MarginPrice = newMarginPrice;
                    product.MarginPriceUpdatedDate = DateTime.Now;
                    updatedProductsBag.Add(product);
                }
            }
        }

        // ─── DOKLEJANIE CEN Z XML (CopyXMLPrices) ───
        // Jeśli włączone — dla produktów którym nie znaleziono naszej oferty w Google,
        // pobiera ceny z feedu XML używając zapisanego mapowania (EAN/ExternalId → cena, dostępność, shipping).
        if (store.CopyXMLPrices)
        {
            var copyXmlService = new CopyXmlPricesService(_context);
            var copyResult = await copyXmlService.AppendPricesForStoreAsync(
                store, products, priceHistoriesBag, scrapHistory, canonicalStoreName);

            // Produkty którym dokleiliśmy cenę → odznacz IsRejected
            foreach (var pid in copyResult.ProductIdsWithAppended)
            {
                var prod = products.FirstOrDefault(p => p.ProductId == pid);
                if (prod != null && prod.IsRejected)
                {
                    prod.IsRejected = false;
                    updatedProductsBag.Add(prod);
                }
            }
        }

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

        if (store.IsApiExportEnabled && !string.IsNullOrEmpty(store.ApiExportToken))
        {
            await GenerateExportFilesAsync(store, products, priceHistoriesAll, canonicalStoreName, googleTitleLookup, weightConfidenceLookup);
        }
    }

    private async Task GenerateExportFilesAsync(
         StoreClass store,
         List<ProductClass> products,
         List<PriceHistoryClass> priceHistories,
         string canonicalStoreName,
         ConcurrentDictionary<(int ProductId, string StoreNameLower), string>? googleTitleLookup = null,
         ConcurrentDictionary<(int ProductId, string StoreNameLower), int>? weightConfidenceLookup = null)
    {
        var myStoreNameLower = canonicalStoreName.ToLower().Trim();

        var feed = new ExportFeedDto
        {
            StoreId = store.StoreId,
            StoreName = canonicalStoreName,
            GeneratedAt = DateTime.Now
        };

        var historiesByProduct = priceHistories.GroupBy(p => p.ProductId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var product in products)
        {
            if (!historiesByProduct.TryGetValue(product.ProductId, out var productHistories))
                continue;

            var myOffer = productHistories.FirstOrDefault(ph => ph.StoreName != null && ph.StoreName.ToLower().Trim() == myStoreNameLower);

            var competitors = productHistories
                .Where(ph => ph.StoreName != null && ph.StoreName.ToLower().Trim() != myStoreNameLower && ph.Price > 0)
                .Select(ph =>
                {

                    bool? inStock = ph.IsGoogle == true ? ph.GoogleInStock : ph.CeneoInStock;

                    string? finalOfferUrl = null;
                    if (store.CollectGoogleStoreLinks)
                    {
                        if (ph.IsGoogle == true)
                        {

                            finalOfferUrl = ph.GoogleOfferUrl;
                        }
                        else
                        {

                            finalOfferUrl = product.OfferUrl;
                        }
                    }
                    string? offerTitle = null;
                    if (googleTitleLookup != null && ph.IsGoogle && ph.StoreName != null)
                    {
                        googleTitleLookup.TryGetValue(
                            (product.ProductId, ph.StoreName.ToLower()), out offerTitle);
                    }

                    int? confidence = null;
                    if (weightConfidenceLookup != null && ph.IsGoogle && ph.StoreName != null)
                    {
                        if (weightConfidenceLookup.TryGetValue(
                            (product.ProductId, ph.StoreName.ToLower()), out var conf))
                        {
                            confidence = conf;
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

                        PackUnits = ph.GooglePackUnits,
                        UnitWeightG = ph.GoogleUnitWeightG,
                        PricePerKg = ph.GooglePricePerKg,
                        OfferTitle = offerTitle,
                        WeightConfidence = confidence
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

        var jsonPath = Path.Combine(exportFolder, $"feed_{store.StoreId}.json");
        var xmlPath = Path.Combine(exportFolder, $"feed_{store.StoreId}.xml");

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        var jsonString = JsonSerializer.Serialize(feed, jsonOptions);
        await File.WriteAllTextAsync(jsonPath, jsonString);

        var xmlSerializer = new XmlSerializer(typeof(ExportFeedDto));
        using (var stream = new FileStream(xmlPath, FileMode.Create))
        {
            xmlSerializer.Serialize(stream, feed);
        }
    }
}













public static class WeightParser
{
    public static Dictionary<string, int> ResolveWeightsForProduct(
       List<PriceHistoryClass> googleOffers,
       Dictionary<string, string> titlesByStoreLower)
    {
        var confidenceResults = new Dictionary<string, int>(); // storeName.Lower → confidence 0-5

        if (googleOffers == null || googleOffers.Count == 0) return confidenceResults;

        // ─── FAZA 1: Parsowanie indywidualne (tytuł + URL + adnotacja) ───
        var parsed = new List<ParsedWeightData>();

        foreach (var offer in googleOffers)
        {
            var data = new ParsedWeightData { Offer = offer };
            var storeKey = (offer.StoreName ?? "").ToLower();

            // Źródło 1: Tytuł z lookup (NIE z bazy)
            string? rawTitle = null;
            string? perKgAnnotation = null;

            if (titlesByStoreLower.TryGetValue(storeKey, out var fullTitle) && !string.IsNullOrEmpty(fullTitle))
            {
                var bracketMatch = Regex.Match(fullTitle, @"\[([^\]]+/\s*\d+\s*(?:kg|g|l))\]\s*$", RegexOptions.IgnoreCase);
                if (bracketMatch.Success)
                {
                    perKgAnnotation = bracketMatch.Groups[1].Value;
                    rawTitle = fullTitle[..bracketMatch.Index].Trim();
                }
                else
                {
                    rawTitle = fullTitle;
                }
            }

            if (!string.IsNullOrEmpty(rawTitle))
            {
                var (u, w, _) = ParseFromTitle(rawTitle, offer.Price);
                if (u.HasValue && w.HasValue)
                {
                    data.TitleUnits = u;
                    data.TitleWeightG = w;
                }
            }

            // Źródło 2: URL oferty (z bazy)
            if (!string.IsNullOrEmpty(offer.GoogleOfferUrl))
            {
                var (urlUnits, urlWeightG) = ParseFromUrl(offer.GoogleOfferUrl);
                if (urlUnits.HasValue && urlWeightG.HasValue)
                {
                    data.UrlUnits = urlUnits;
                    data.UrlWeightG = urlWeightG;
                }
            }

            // Źródło 3: Adnotacja Google per-kg
            if (!string.IsNullOrEmpty(perKgAnnotation))
            {
                data.GooglePerKg = ParseGooglePerKgAnnotation(perKgAnnotation);
            }

            parsed.Add(data);
        }

        // ─── FAZA 2: Wybór najlepszego wyniku per oferta ───
        foreach (var data in parsed)
        {
            PickBestResult(data);
        }

        // ─── FAZA 3: Konsensus gramatury jednostkowej ───
        // Jeśli wiele ofert zgadza się co do wagi jednostki, to jest prawda
        var confirmedUnitWeights = parsed
            .Where(d => d.FinalWeightG.HasValue && d.Confidence >= WeightConfidence.High)
            .Select(d => d.FinalWeightG!.Value)
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        decimal? consensusUnitWeight = confirmedUnitWeights?.Count() >= 2
            ? confirmedUnitWeights.Key
            : null;


        // ─── FAZA 3.5: Reverse-calc z Google per-kg + konsensus ───
        // Dla ofert z adnotacją per-kg ale bez units — oblicz units z totalKg / unitWeight
        if (consensusUnitWeight.HasValue && consensusUnitWeight.Value > 0)
        {
            foreach (var data in parsed.Where(d => d.GoogleReverseTotalKg.HasValue
                                                   && (!d.FinalUnits.HasValue || d.Confidence < WeightConfidence.Medium)))
            {
                decimal totalGrams = data.GoogleReverseTotalKg!.Value * 1000m;
                decimal rawUnits = totalGrams / consensusUnitWeight.Value;
                int snapped = SnapToCommonPackSize((int)Math.Round(rawUnits));

                if (snapped >= 1 && snapped <= 10000)
                {
                    // Walidacja: obliczona per-kg vs Google per-kg
                    decimal checkPerKg = CalcPerKg(data.Offer.Price, snapped, consensusUnitWeight.Value);
                    if (data.GooglePerKg.HasValue && checkPerKg > 0)
                    {
                        decimal deviation = Math.Abs(checkPerKg - data.GooglePerKg.Value) / data.GooglePerKg.Value;
                        if (deviation < 0.05m) // <5% — bardzo dobra zgodność
                        {
                            data.FinalUnits = snapped;
                            data.FinalWeightG = consensusUnitWeight;
                            data.Confidence = WeightConfidence.High; // per-kg Google + konsensus = solidne
                        }
                    }
                }
            }
        }

        // ─── FAZA 4: Uzupełnianie luk na podstawie konsensusu + proporcji cen ───
        if (consensusUnitWeight.HasValue)
        {
            // Zbierz "cenę za sztukę" z potwierdzonych ofert
            var confirmedPricePerUnit = parsed
                .Where(d => d.FinalUnits.HasValue && d.FinalWeightG == consensusUnitWeight
                            && d.Confidence >= WeightConfidence.High && d.Offer.Price > 0)
                .Select(d => new { PricePerUnit = d.Offer.Price / d.FinalUnits!.Value, Units = d.FinalUnits!.Value })
                .ToList();

            if (confirmedPricePerUnit.Count >= 1)
            {
                // Mediana ceny za sztukę — to nasz "anchor"
                var sortedPPU = confirmedPricePerUnit.OrderBy(x => x.PricePerUnit).ToList();
                decimal medianPPU = sortedPPU[sortedPPU.Count / 2].PricePerUnit;

                foreach (var data in parsed.Where(d => !d.FinalUnits.HasValue || d.Confidence < WeightConfidence.Medium))
                {
                    if (data.Offer.Price <= 0) continue;

                    // Oblicz najbardziej prawdopodobną liczbę sztuk
                    decimal rawUnits = data.Offer.Price / medianPPU;
                    int estimatedUnits = (int)Math.Round(rawUnits);

                    // Szukaj najbliższego "popularnego" rozmiaru opakowania
                    int snappedUnits = SnapToCommonPackSize(estimatedUnits);

                    if (snappedUnits >= 1 && snappedUnits <= 10000)
                    {
                        decimal totalKg = (snappedUnits * consensusUnitWeight.Value) / 1000m;
                        decimal estimatedPerKg = totalKg > 0 ? data.Offer.Price / totalKg : 0;

                        // Walidacja przez Google per-kg
                        if (data.GooglePerKg.HasValue && estimatedPerKg > 0)
                        {
                            decimal deviation = Math.Abs(estimatedPerKg - data.GooglePerKg.Value) / data.GooglePerKg.Value;
                            if (deviation < 0.05m)
                            {
                                data.FinalUnits = snappedUnits;
                                data.FinalWeightG = consensusUnitWeight;
                                data.Confidence = WeightConfidence.Inferred;
                            }
                        }
                        else
                        {
                            // Walidacja przez odchylenie od mediany
                            decimal actualPPU = data.Offer.Price / snappedUnits;
                            decimal ppuDeviation = Math.Abs(actualPPU - medianPPU) / medianPPU;

                            if (ppuDeviation < 0.30m && estimatedPerKg > 0.5m && estimatedPerKg < 500m)
                            {
                                data.FinalUnits = snappedUnits;
                                data.FinalWeightG = consensusUnitWeight;
                                data.Confidence = WeightConfidence.Inferred;
                            }
                        }
                    }
                }
            }
        }

        foreach (var data in parsed)
        {
            var storeKey = (data.Offer.StoreName ?? "").ToLower();

            if (data.FinalUnits.HasValue && data.FinalWeightG.HasValue
                && data.FinalUnits > 0 && data.FinalWeightG > 0
                && data.Confidence >= WeightConfidence.Inferred)
            {
                decimal totalKg = (data.FinalUnits.Value * data.FinalWeightG.Value) / 1000m;
                if (totalKg > 0)
                {
                    data.Offer.GooglePackUnits = data.FinalUnits;
                    data.Offer.GoogleUnitWeightG = data.FinalWeightG;
                    data.Offer.GooglePricePerKg = Math.Round(data.Offer.Price / totalKg, 2);
                }
            }

            confidenceResults[storeKey] = (int)data.Confidence;
        }

        return confidenceResults;
    }

    // ═══════════════════════════════════════════════════════════════
    // FAZA 2 HELPER: Wybór najlepszego wyniku z wielu źródeł
    // ═══════════════════════════════════════════════════════════════

    private static void PickBestResult(ParsedWeightData data)
    {
        // Priorytet: Tytuł > URL > GooglePerKg-reverse

        // Przypadek 1: Tytuł sparsowany + walidacja przez Google per-kg
        if (data.TitleUnits.HasValue && data.TitleWeightG.HasValue)
        {
            decimal titlePerKg = CalcPerKg(data.Offer.Price, data.TitleUnits.Value, data.TitleWeightG.Value);

            if (data.GooglePerKg.HasValue && titlePerKg > 0)
            {
                decimal deviation = Math.Abs(titlePerKg - data.GooglePerKg.Value) / data.GooglePerKg.Value;
                if (deviation < 0.10m) // <10% — potwierdzone
                {
                    data.FinalUnits = data.TitleUnits;
                    data.FinalWeightG = data.TitleWeightG;
                    data.Confidence = WeightConfidence.Confirmed;
                    return;
                }
                // >10% odchylenia — tytuł może być błędny, spróbuj URL
            }
            else
            {
                // Brak Google per-kg do walidacji — ufamy tytułowi z mniejszym confidence
                data.FinalUnits = data.TitleUnits;
                data.FinalWeightG = data.TitleWeightG;
                data.Confidence = WeightConfidence.High;
                return;
            }
        }

        // Przypadek 2: URL sparsowany
        if (data.UrlUnits.HasValue && data.UrlWeightG.HasValue)
        {
            decimal urlPerKg = CalcPerKg(data.Offer.Price, data.UrlUnits.Value, data.UrlWeightG.Value);

            if (data.GooglePerKg.HasValue && urlPerKg > 0)
            {
                decimal deviation = Math.Abs(urlPerKg - data.GooglePerKg.Value) / data.GooglePerKg.Value;
                if (deviation < 0.10m)
                {
                    data.FinalUnits = data.UrlUnits;
                    data.FinalWeightG = data.UrlWeightG;
                    data.Confidence = WeightConfidence.Confirmed;
                    return;
                }
            }
            else
            {
                data.FinalUnits = data.UrlUnits;
                data.FinalWeightG = data.UrlWeightG;
                data.Confidence = WeightConfidence.Medium;
                return;
            }
        }

        // Przypadek 3: Tytuł odrzucony przez walidację, ale mamy go — użyj z niskim confidence
        if (data.TitleUnits.HasValue && data.TitleWeightG.HasValue)
        {
            data.FinalUnits = data.TitleUnits;
            data.FinalWeightG = data.TitleWeightG;
            data.Confidence = WeightConfidence.Low;
        }


        // Przypadek 4: Reverse-calculation z Google per-kg
        // Gdy tytuł/URL nie dały wyniku lub dały absurdalny, ale mamy adnotację Google
        if (data.GooglePerKg.HasValue && data.GooglePerKg.Value > 0 && data.Offer.Price > 0)
        {
            // Oblicz totalKg z proporcji cena/per-kg
            decimal totalKg = data.Offer.Price / data.GooglePerKg.Value;

            if (totalKg > 0.001m && totalKg < 1000m)
            {
                data.GoogleReverseTotalKg = totalKg;
                // Jeśli już mamy wynik ale jest absurdalny — nadpisz
                if (data.Confidence <= WeightConfidence.Low && data.FinalUnits.HasValue && data.FinalWeightG.HasValue)
                {
                    decimal existingPerKg = CalcPerKg(data.Offer.Price, data.FinalUnits.Value, data.FinalWeightG.Value);
                    if (existingPerKg > 0)
                    {
                        decimal deviation = Math.Abs(existingPerKg - data.GooglePerKg.Value) / data.GooglePerKg.Value;
                        if (deviation > 0.15m) // >15% = nasz wynik jest błędny
                        {
                            data.FinalUnits = null;
                            data.FinalWeightG = null;
                            data.Confidence = WeightConfidence.None;
                        }
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // PARSERY ŹRÓDŁOWE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Parsuje wagę z URL-a oferty. Np. "/12-x-100-g/" lub "/macs-cat-pouch-kalb-rind-100g"</summary>
    public static (int? units, decimal? weightG) ParseFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return (null, null);

        try
        {
            var path = new Uri(url).AbsolutePath.ToLower();

            // Wzorzec: "12-x-100-g", "24x85g", "6-x-50-g"
            var nxw = Regex.Match(path, @"(\d+)-?x-?(\d+(?:[.,]\d+)?)-?(g|kg|ml|l)\b", RegexOptions.IgnoreCase);
            if (nxw.Success)
            {
                int units = int.Parse(nxw.Groups[1].Value);
                decimal weight = ConvertToGrams(nxw.Groups[2].Value, nxw.Groups[3].Value);
                if (units > 0 && units <= 10000 && weight > 0)
                    return (units, weight);
            }

            // Wzorzec: "100g" solo w ścieżce (1 sztuka)
            var solo = Regex.Match(path, @"(?:^|[/-])(\d+(?:[.,]\d+)?)-?(g|kg)\b", RegexOptions.IgnoreCase);
            if (solo.Success)
            {
                decimal weight = ConvertToGrams(solo.Groups[1].Value, solo.Groups[2].Value);
                if (weight > 0 && weight <= 100_000)
                    return (1, weight);
            }
        }
        catch { }

        return (null, null);
    }

    /// <summary>Parsuje adnotację Google "10,62 € / 1 kg" → decimal per-kg</summary>
    public static decimal? ParseGooglePerKgAnnotation(string? annotation)
    {
        if (string.IsNullOrEmpty(annotation)) return null;

        // "10,62 € / 1 kg", "1,29 € / 100 g", "12,90 € / 1 kg"
        var m = Regex.Match(annotation, @"(\d+[.,]\d+)\s*€?\s*/\s*(\d+)\s*(kg|g|l)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        decimal priceVal = ParseDecimalSafe(m.Groups[1].Value);
        int qtyVal = int.Parse(m.Groups[2].Value);
        string unit = m.Groups[3].Value.ToLower();

        if (priceVal <= 0 || qtyVal <= 0) return null;

        // Normalizuj do per-kg
        decimal gramsInDenominator = unit switch
        {
            "kg" => qtyVal * 1000m,
            "g" => qtyVal,
            "l" => qtyVal * 1000m,
            _ => 0
        };

        if (gramsInDenominator <= 0) return null;

        // priceVal to cena za qtyVal jednostek
        // per-kg = priceVal * (1000 / gramsInDenominator)
        return Math.Round(priceVal * (1000m / gramsInDenominator), 2);
    }

    /// <summary>Parsuje wagę z tytułu (istniejąca logika, bez zmian).</summary>
    public static (int? units, decimal? unitWeightG, decimal? pricePerKg) ParseFromTitle(string? title, decimal price)
    {
        if (string.IsNullOrWhiteSpace(title) || price <= 0)
            return (null, null, null);

        if (title.Length < 5 || !Regex.IsMatch(title, @"\d"))
            return (null, null, null);

        int? units = null;
        decimal? weightPerUnitG = null;

        var nxw = Regex.Match(title, @"(\d+)\s*[xX×]\s*(\d+(?:[.,]\d+)?)\s*(g|kg|ml|l)\b", RegexOptions.IgnoreCase);
        if (nxw.Success)
        {
            units = int.Parse(nxw.Groups[1].Value);
            weightPerUnitG = ConvertToGrams(nxw.Groups[2].Value, nxw.Groups[3].Value);
        }

        if (units == null)
        {
            var wxn = Regex.Match(title, @"(\d+(?:[.,]\d+)?)\s*(g|kg|ml|l)\s*[xX×]\s*(\d+)\b", RegexOptions.IgnoreCase);
            if (wxn.Success)
            {
                weightPerUnitG = ConvertToGrams(wxn.Groups[1].Value, wxn.Groups[2].Value);
                units = int.Parse(wxn.Groups[3].Value);
            }
        }

        if (units == null)
        {
            var menge = Regex.Match(title, @"Menge:\s*(\d+)\s*je\s*Bestelleinheit", RegexOptions.IgnoreCase);
            if (menge.Success)
            {
                units = int.Parse(menge.Groups[1].Value);
                weightPerUnitG = FindFirstWeight(title);
            }
        }

        if (units == null)
        {
            var erPack = Regex.Match(title, @"(\d+)er\s*Pack", RegexOptions.IgnoreCase);
            if (erPack.Success)
            {
                units = int.Parse(erPack.Groups[1].Value);
                weightPerUnitG = FindFirstWeight(title);
            }
        }

        if (units == null)
        {
            var stueck = Regex.Match(title, @"(\d+)\s*(?:Stück|szt\.?|pieces?|pcs)\b", RegexOptions.IgnoreCase);
            if (stueck.Success)
            {
                units = int.Parse(stueck.Groups[1].Value);
                weightPerUnitG = FindFirstWeight(title);
            }
        }

        if (units == null && weightPerUnitG == null)
        {
            var singleWeight = FindFirstWeight(title);
            if (singleWeight.HasValue && singleWeight > 0)
            {
                units = 1;
                weightPerUnitG = singleWeight;
            }
        }

        if (units.HasValue && weightPerUnitG.HasValue && nxw.Success)
        {
            var outerMultiplier = Regex.Match(title, @"(\d+)\s*(?:Stück|szt\.?|pieces?|pcs)\b", RegexOptions.IgnoreCase);
            if (outerMultiplier.Success)
            {
                int outerN = int.Parse(outerMultiplier.Groups[1].Value);
                if (outerN != units && outerN > 1 && outerN <= 200)
                    units *= outerN;
            }
            else
            {
                var outerErPack = Regex.Match(title, @"(\d+)er\s*Pack", RegexOptions.IgnoreCase);
                if (outerErPack.Success)
                {
                    int outerN = int.Parse(outerErPack.Groups[1].Value);
                    if (outerN != units && outerN > 1 && outerN <= 200)
                        units *= outerN;
                }
            }
        }

        if (!units.HasValue || !weightPerUnitG.HasValue || units <= 0 || weightPerUnitG <= 0
            || units > 10000 || weightPerUnitG > 100_000)
            return (null, null, null);

        decimal totalWeightKg = (units.Value * weightPerUnitG.Value) / 1000m;
        if (totalWeightKg <= 0) return (null, null, null);

        decimal pricePerKg = Math.Round(price / totalWeightKg, 2);
        if (pricePerKg > 100_000) return (null, null, null);

        return (units, weightPerUnitG, pricePerKg);
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

    private static decimal CalcPerKg(decimal price, int units, decimal weightG)
    {
        if (units <= 0 || weightG <= 0 || price <= 0) return 0;
        decimal totalKg = (units * weightG) / 1000m;
        return totalKg > 0 ? Math.Round(price / totalKg, 2) : 0;
    }

    private static decimal ParseDecimalSafe(string s)
    {
        s = s.Replace(",", ".");
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static decimal? FindFirstWeight(string title)
    {
        var matches = Regex.Matches(title, @"(?<!\S[€$])\b(\d+(?:[.,]\d+)?)\s*(g|kg|ml|l)\b", RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            var val = ConvertToGrams(m.Groups[1].Value, m.Groups[2].Value);
            if (val > 0 && val <= 100_000) return val;
        }
        return null;
    }

    private static decimal ConvertToGrams(string valueStr, string unit)
    {
        valueStr = valueStr.Replace(",", ".");
        if (!decimal.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return 0;
        return unit.ToLower() switch
        {
            "kg" => value * 1000m,
            "g" => value,
            "l" => value * 1000m,
            "ml" => value,
            _ => 0
        };
    }

    private static int SnapToCommonPackSize(int estimated)
    {
        int[] commonSizes = { 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24, 30, 36, 40, 48, 50, 60, 72, 80, 96, 100, 120, 144 };

        int best = estimated;
        int bestDiff = int.MaxValue;

        foreach (var size in commonSizes)
        {
            int diff = Math.Abs(size - estimated);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = size;
            }
        }

        // Akceptuj snap tylko jeśli odchylenie < 20%
        if (estimated > 0 && Math.Abs(best - estimated) / (decimal)estimated > 0.20m)
            return estimated; // Zbyt daleko — zostaw oryginał

        return best;
    }

    // ═══════════════════════════════════════════════════════════════
    // MODELE WEWNĘTRZNE
    // ═══════════════════════════════════════════════════════════════

    private enum WeightConfidence
    {
        None = 0,
        Inferred = 1,   // Wyliczone z proporcji cenowych / konsensusu
        Low = 2,         // Sparsowane ale nie zwalidowane
        Medium = 3,      // Sparsowane z URL
        High = 4,        // Sparsowane z tytułu
        Confirmed = 5    // Sparsowane + potwierdzone przez Google per-kg
    }

    private class ParsedWeightData
    {
        public PriceHistoryClass Offer { get; set; } = null!;

        public int? TitleUnits { get; set; }
        public decimal? TitleWeightG { get; set; }

        public int? UrlUnits { get; set; }
        public decimal? UrlWeightG { get; set; }

        public decimal? GooglePerKg { get; set; }
        // NOWE:
        public decimal? GoogleReverseTotalKg { get; set; }

        public int? FinalUnits { get; set; }
        public decimal? FinalWeightG { get; set; }
        public WeightConfidence Confidence { get; set; } = WeightConfidence.None;
    }
}