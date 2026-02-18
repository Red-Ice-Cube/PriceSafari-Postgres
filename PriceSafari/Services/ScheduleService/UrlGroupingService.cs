using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

        private string? ExtractCid(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var match = Regex.Match(url, @"/product/(\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        public async Task<(int totalProducts, List<string> distinctStoreNames)> GroupAndSaveUniqueUrls(List<int> storeIds)
        {
            // Pobieramy produkty wraz z informacją o sklepie (Store)
            var products = await _context.Products
                .Include(p => p.Store)
                .Include(p => p.GoogleCatalogs)
                .Where(p => p.IsScrapable && p.Store.RemainingDays > 0)
                .Where(p => storeIds.Contains(p.StoreId))
                .AsNoTracking()
                .ToListAsync();

            var distinctStoreNames = products.Select(p => p.Store.StoreName).Distinct().ToList();
            int totalProducts = products.Count;

            var coOfrs = new List<CoOfrClass>();

            foreach (var product in products)
            {
                // 1. Obsługa standardowego Google URL z produktu
                if (!string.IsNullOrEmpty(product.GoogleUrl))
                {
                    string? cid = ExtractCid(product.GoogleUrl);

                    var coOfr = CreateCoOfrClass(
                        new List<ProductClass> { product },
                        null,
                        product.GoogleUrl,
                        product.GoogleGid,
                        cid,
                        null,
                        false,
                        false
                    );
                    coOfr.IsGoogle = true;
                    coOfrs.Add(coOfr);
                }

                // 2. Obsługa dodatkowych katalogów Google (Additional Catalogs)
                if (product.Store.UseAdditionalCatalogsForGoogle && product.GoogleCatalogs != null)
                {
                    foreach (var extra in product.GoogleCatalogs)
                    {
                        string? gid = extra.GoogleGid;
                        string? cid = extra.GoogleCid;
                        string? hid = extra.GoogleHid;
                        bool isHidMode = extra.IsExtendedOfferByHid;

                        string? googleUrl = null;
                        bool useHidOffer = false;
                        bool isValidTask = false;

                        if (!isHidMode && !string.IsNullOrEmpty(cid))
                        {
                            googleUrl = $"https://www.google.com/shopping/product/{cid}";
                            isValidTask = true;
                        }
                        else if (!string.IsNullOrEmpty(hid))
                        {
                            useHidOffer = true;
                            isValidTask = true;
                        }

                        if (isValidTask)
                        {
                            var coOfr = CreateCoOfrClass(
                                new List<ProductClass> { product },
                                null,
                                googleUrl,
                                gid,
                                cid,
                                hid,
                                true,
                                useHidOffer
                            );
                            coOfr.IsGoogle = true;
                            coOfrs.Add(coOfr);
                        }
                    }
                }
            }

            // 3. Grupowanie po OfferUrl (zazwyczaj dla Ceneo/Innych, ale metoda CreateCoOfrClass obsłuży flagę niezależnie)
            var groupsByOfferUrl = products
                .Where(p => !string.IsNullOrEmpty(p.OfferUrl))
                .GroupBy(p => p.OfferUrl!)
                .ToList();

            foreach (var group in groupsByOfferUrl)
            {
                coOfrs.Add(CreateCoOfrClass(group.ToList(), group.Key, null, null, null, null, false, false));
            }

            // Zapis do bazy danych
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Czyszczenie tabel przed nowym zrzutem zadań
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrStoreDatas]");
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrPriceHistories]");
                    await _context.Database.ExecuteSqlRawAsync("DELETE FROM [CoOfrs]");

                    const int batchSize = 1000;
                    for (int i = 0; i < coOfrs.Count; i += batchSize)
                    {
                        var batch = coOfrs.Skip(i).Take(batchSize).ToList();
                        _context.CoOfrs.AddRange(batch);
                        await _context.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            });

            return (totalProducts, distinctStoreNames);
        }

        // --- TUTAJ JEST KLUCZOWA ZMIANA ---
        private CoOfrClass CreateCoOfrClass(List<ProductClass> productList, string? offerUrl, string? googleUrl, string? googleGid, string? googleCid, string? googleHid, bool isAdditional, bool useHidOffer)
        {
            // Sprawdzamy flagi konfiguracyjne ze sklepu
            bool useGPID = productList.Any(p => p.Store?.UseGPID == true);
            bool useWRGA = productList.Any(p => p.Store?.UseWRGA == true);

            // NOWA FLAGA: Sprawdzamy, czy którykolwiek sklep w tej grupie produktów chce zbierać linki sklepowe z Google
            bool collectGoogleStoreLinks = productList.Any(p => p.Store?.CollectGoogleStoreLinks == true);

            var coOfr = new CoOfrClass
            {
                OfferUrl = offerUrl,
                GoogleOfferUrl = googleUrl,
                GoogleGid = googleGid,
                GoogleCid = googleCid,
                GoogleHid = googleHid,
                IsAdditionalCatalog = isAdditional,
                UseGoogleHidOffer = useHidOffer,

                UseGPID = useGPID,
                UseWRGA = useWRGA,

                // Przypisanie nowej flagi do zadania scrapowania
                CollectGoogleStoreLinks = collectGoogleStoreLinks,

                ProductIds = productList.Select(p => p.ProductId).ToList(),
                ProductIdsGoogle = (googleUrl != null || useHidOffer) ? productList.Select(p => p.ProductId).ToList() : new List<int>(),

                StoreNames = productList.Select(p => p.Store?.StoreName ?? "Unknown").Distinct().ToList(),
                StoreProfiles = productList.Select(p => p.Store?.StoreProfile ?? "").Distinct().ToList(),

                IsScraped = false,
                GoogleIsScraped = false,
                IsGoogle = (googleUrl != null || useHidOffer),
                IsRejected = false,
                GoogleIsRejected = false
            };

            foreach (var p in productList)
            {
                if (p.Store != null && p.Store.FetchExtendedData && p.ExternalId.HasValue)
                {
                    coOfr.StoreData.Add(new CoOfrStoreData
                    {
                        StoreId = p.StoreId,
                        ProductExternalId = p.ExternalId.Value.ToString(),
                        IsApiProcessed = false,
                        ExtendedDataApiPrice = null
                    });
                }
            }

            return coOfr;
        }
    }
}