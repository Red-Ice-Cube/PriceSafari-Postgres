using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Linq;

namespace PriceSafari.Services.AllegroServices
{
    public class AllegroProcessingService
    {
        private readonly PriceSafariContext _context;

        public AllegroProcessingService(PriceSafariContext context)
        {
            _context = context;
        }

        private static long? ParseOfferIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var idString = url.Split('-').LastOrDefault();
            return long.TryParse(idString, out var parsedId) ? parsedId : null;
        }

        public async Task<(int processedUrls, int savedOffers)> ProcessScrapedDataForStoreAsync(int storeId)
        {
            var userStore = await _context.Stores.FindAsync(storeId);

            if (userStore == null || userStore.RemainingDays <= 0)
            {
                return (0, 0);
            }

            if (string.IsNullOrEmpty(userStore.StoreNameAllegro))
            {
                return (0, 0);
            }
            var userAllegroStoreName = userStore.StoreNameAllegro;

            var storeProducts = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            if (!storeProducts.Any())
            {
                return (0, 0);
            }

            var storeProductIds = new HashSet<int>(storeProducts.Select(p => p.AllegroProductId));

            var productTargetOfferIds = storeProducts
                .Where(p => !string.IsNullOrEmpty(p.AllegroOfferUrl))
                .ToDictionary(
                    p => p.AllegroProductId,
                    p => ParseOfferIdFromUrl(p.AllegroOfferUrl)
                );

            var allOffersToScrape = await _context.AllegroOffersToScrape.ToListAsync();
            var relevantOffersForStore = allOffersToScrape
                .Where(o => o.AllegroProductIds.Any(pId => storeProductIds.Contains(pId)))
                .ToList();

            var rejectedOffers = relevantOffersForStore.Where(o => o.IsRejected).ToList();

            var productIdsToRejectFromUpstream = rejectedOffers.Any()
                ? rejectedOffers.SelectMany(o => o.AllegroProductIds).Intersect(storeProductIds).Distinct().ToList()
                : new List<int>();

            var validOfferIds = relevantOffersForStore.Where(o => !o.IsRejected).Select(o => o.Id).ToList();

            if (!validOfferIds.Any())
            {

                if (productIdsToRejectFromUpstream.Any())
                {
                    await _context.AllegroProducts
                        .Where(p => productIdsToRejectFromUpstream.Contains(p.AllegroProductId))
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsRejected, true));
                }
                return (rejectedOffers.Count, 0);
            }

            var scrapedOffersData = await _context.AllegroScrapedOffers
                .Include(o => o.AllegroOfferToScrape)
                .Where(o => validOfferIds.Contains(o.AllegroOfferToScrapeId))
                .AsNoTracking()
                .ToListAsync();

            var offersGroupedByUrl = scrapedOffersData.GroupBy(o => o.AllegroOfferToScrapeId);

            var urlsWithoutUserStore = offersGroupedByUrl
                .Where(group => !group.Any(offer => offer.SellerName == userAllegroStoreName))
                .Select(group => group.Key)
                .ToHashSet();

            var tasksWithResults = scrapedOffersData.Select(x => x.AllegroOfferToScrapeId).Distinct().ToHashSet();
            var tasksWithZeroResults = validOfferIds.Where(id => !tasksWithResults.Contains(id)).ToList();

            foreach (var zeroResultId in tasksWithZeroResults)
            {
                urlsWithoutUserStore.Add(zeroResultId);
            }

            var productIdsToRejectForSellerMismatch = urlsWithoutUserStore.Any()
                ? relevantOffersForStore.Where(o => urlsWithoutUserStore.Contains(o.Id))
                    .SelectMany(o => o.AllegroProductIds).Intersect(storeProductIds).Distinct().ToList()
                : new List<int>();

            var productIdsToRejectForMissingMainOffer = new List<int>();

            var urlsToCheckForMainOffer = offersGroupedByUrl.Where(g => !urlsWithoutUserStore.Contains(g.Key));

            foreach (var group in urlsToCheckForMainOffer)
            {
                var urlId = group.Key;
                var foundOfferIdsInGroup = group.Select(o => o.IdAllegro).ToHashSet();

                var productIdsForThisUrl = relevantOffersForStore
                    .FirstOrDefault(o => o.Id == urlId)?
                    .AllegroProductIds
                    .Intersect(storeProductIds) ?? Enumerable.Empty<int>();

                foreach (var productId in productIdsForThisUrl)
                {
                    if (productTargetOfferIds.TryGetValue(productId, out long? targetId) && targetId.HasValue)
                    {
                        if (!foundOfferIdsInGroup.Contains(targetId.Value))
                        {
                            productIdsToRejectForMissingMainOffer.Add(productId);
                        }
                    }
                }
            }

            var allProductIdsToReject = productIdsToRejectFromUpstream
                .Concat(productIdsToRejectForSellerMismatch)
                .Concat(productIdsToRejectForMissingMainOffer)
                .Distinct()
                .ToList();

            if (allProductIdsToReject.Any())
            {
                await _context.AllegroProducts
                    .Where(p => allProductIdsToReject.Contains(p.AllegroProductId))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsRejected, true));
            }

            var scrapeHistory = new AllegroScrapeHistory
            {
                StoreId = storeId,
                Date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"))
            };
            _context.AllegroScrapeHistories.Add(scrapeHistory);

            var newPriceHistories = new List<AllegroPriceHistory>();
            var newExtendedInfos = new List<AllegroPriceHistoryExtendedInfoClass>();

            var sellersNeedingDisambiguation = scrapedOffersData
                .Where(o => o.StoreIdOnAllegro.HasValue)
                .GroupBy(o => o.SellerName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Select(o => o.StoreIdOnAllegro.Value).Distinct().Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var visitsPerOffer = allOffersToScrape
            .Where(o => o.IsApiProcessed == true && o.AllegroVisitsCount.HasValue)
            .ToDictionary(o => o.AllegroOfferId, o => o.AllegroVisitsCount);

            foreach (var scrapedOffer in scrapedOffersData)
            {
                var sourceOfferToScrape = allOffersToScrape.FirstOrDefault(o => o.Id == scrapedOffer.AllegroOfferToScrapeId);

                if (sourceOfferToScrape == null)
                {
                    continue;
                }

                var productIdsForThisStore = sourceOfferToScrape.AllegroProductIds
                    .Intersect(storeProductIds)
                    .ToList();

                if (sourceOfferToScrape.IsApiProcessed == true && !string.IsNullOrEmpty(sourceOfferToScrape.AllegroEan))
                {
                    var newEan = sourceOfferToScrape.AllegroEan;
                    var productsToUpdateEan = storeProducts
                        .Where(p => productIdsForThisStore.Contains(p.AllegroProductId))
                        .ToList();

                    foreach (var product in productsToUpdateEan)
                    {
                        if (product.AllegroEan != newEan)
                        {
                            product.AllegroEan = newEan;
                        }
                    }
                }

                if (sourceOfferToScrape.IsApiProcessed == true && !string.IsNullOrEmpty(sourceOfferToScrape.AllegroBrand))
                {
                    var newBrand = sourceOfferToScrape.AllegroBrand;
                    var productsToUpdateBrand = storeProducts
                        .Where(p => productIdsForThisStore.Contains(p.AllegroProductId))
                        .ToList();

                    foreach (var product in productsToUpdateBrand)
                    {
                        if (product.Producer != newBrand)
                        {
                            product.Producer = newBrand;
                        }
                    }
                }

                if (sourceOfferToScrape.IsApiProcessed == true && !string.IsNullOrEmpty(sourceOfferToScrape.AllegroProducerCode))
                {
                    var newProducerCode = sourceOfferToScrape.AllegroProducerCode;
                    var productsToUpdateSku = storeProducts
                        .Where(p => productIdsForThisStore.Contains(p.AllegroProductId))
                        .ToList();

                    foreach (var product in productsToUpdateSku)
                    {
                        if (string.IsNullOrEmpty(product.AllegroSku) || product.AllegroSku != newProducerCode)
                        {
                            product.AllegroSku = newProducerCode;
                        }
                    }
                }

                foreach (var productId in productIdsForThisStore)
                {

                    if (productIdsToRejectFromUpstream.Contains(productId))
                    {
                        continue;
                    }

                    bool shouldBeRejected = allProductIdsToReject.Contains(productId);

                    var productEntity = storeProducts.FirstOrDefault(p => p.AllegroProductId == productId);
                    if (productEntity != null)
                    {

                        if (productEntity.IsRejected != shouldBeRejected)
                        {
                            productEntity.IsRejected = shouldBeRejected;

                        }
                    }

                    newPriceHistories.Add(new AllegroPriceHistory
                    {
                        AllegroProductId = productId,
                        AllegroScrapeHistory = scrapeHistory,
                        SellerName = sellersNeedingDisambiguation.Contains(scrapedOffer.SellerName)
                            && scrapedOffer.StoreIdOnAllegro.HasValue
                            ? $"{scrapedOffer.SellerName}-{scrapedOffer.StoreIdOnAllegro.Value}"
                            : scrapedOffer.SellerName,
                        Price = scrapedOffer.Price,
                        DeliveryCost = scrapedOffer.DeliveryCost,
                        DeliveryTime = scrapedOffer.DeliveryTime,
                        Popularity = scrapedOffer.Popularity,
                        SuperSeller = scrapedOffer.SuperSeller,
                        Smart = scrapedOffer.Smart,
                        IsBestPriceGuarantee = scrapedOffer.IsBestPriceGuarantee,
                        TopOffer = scrapedOffer.TopOffer,
                        SuperPrice = scrapedOffer.SuperPrice,
                        Promoted = scrapedOffer.Promoted,
                        Sponsored = scrapedOffer.Sponsored,
                        IdAllegro = scrapedOffer.IdAllegro,
                        StoreIdOnAllegro = scrapedOffer.StoreIdOnAllegro,
                        RatingCount = scrapedOffer.RatingCount,
                        RatingPositivePercent = scrapedOffer.RatingPositivePercent,
                    });

                    if (sourceOfferToScrape.IsApiProcessed == true && scrapedOffer.SellerName.Equals(userAllegroStoreName, StringComparison.OrdinalIgnoreCase))
                    {
                        var finalApiAllegroPrice = sourceOfferToScrape.ApiAllegroPrice;
                        var finalIsSubsidyActive = sourceOfferToScrape.IsSubsidyActive;
                        var finalAnyPromoActive = sourceOfferToScrape.AnyPromoActive;

                        if (finalIsSubsidyActive == true && finalApiAllegroPrice.HasValue)
                        {
                            if (scrapedOffer.Price != finalApiAllegroPrice.Value)
                            {
                                finalIsSubsidyActive = false;
                                finalAnyPromoActive = false;
                                finalApiAllegroPrice = sourceOfferToScrape.ApiAllegroPriceFromUser;
                            }
                        }

                        newExtendedInfos.Add(new AllegroPriceHistoryExtendedInfoClass
                        {
                            AllegroProductId = productId,
                            ScrapHistory = scrapeHistory,
                            ApiAllegroPrice = finalApiAllegroPrice,
                            ApiAllegroPriceFromUser = sourceOfferToScrape.ApiAllegroPriceFromUser,
                            ApiAllegroCommission = sourceOfferToScrape.ApiAllegroCommission,
                            AnyPromoActive = finalAnyPromoActive,
                            IsSubsidyActive = finalIsSubsidyActive,
                            AllegroVisitsCount = visitsPerOffer.GetValueOrDefault(scrapedOffer.IdAllegro),
                            IdAllegro = scrapedOffer.IdAllegro,
                        });
                    }
                }
            }

            if (newPriceHistories.Any())
            {
                await _context.AllegroPriceHistories.AddRangeAsync(newPriceHistories);
            }

            if (newExtendedInfos.Any())
            {
                await _context.AllegroPriceHistoryExtendedInfos.AddRangeAsync(newExtendedInfos);
            }

            scrapeHistory.ProcessedUrlsCount = relevantOffersForStore.Count;
            scrapeHistory.SavedOffersCount = newPriceHistories.Count;

            await _context.SaveChangesAsync();

            return (scrapeHistory.ProcessedUrlsCount, scrapeHistory.SavedOffersCount);
        }
    }
}