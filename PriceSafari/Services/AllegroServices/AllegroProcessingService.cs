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

            if (userStore == null || userStore.RemainingScrapes <= 0)
            {
                return (0, 0);
            }

            userStore.RemainingScrapes--;

            await _context.SaveChangesAsync();

            if (string.IsNullOrEmpty(userStore.StoreNameAllegro))
            {
                return (0, 0);
            }
            var userAllegroStoreName = userStore.StoreNameAllegro;

            var storeProducts = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId && !p.IsRejected)
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

            if (!scrapedOffersData.Any())
            {
                if (productIdsToRejectFromUpstream.Any())
                {
                    await _context.AllegroProducts
                        .Where(p => productIdsToRejectFromUpstream.Contains(p.AllegroProductId))
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsRejected, true));
                }
                return (rejectedOffers.Count, 0);
            }

            var offersGroupedByUrl = scrapedOffersData.GroupBy(o => o.AllegroOfferToScrapeId);

            var urlsWithoutUserStore = offersGroupedByUrl
                .Where(group => !group.Any(offer => offer.SellerName == userAllegroStoreName))
                .Select(group => group.Key)
                .ToHashSet();

            var productIdsToRejectForSellerMismatch = urlsWithoutUserStore.Any()
                ? relevantOffersForStore.Where(o => urlsWithoutUserStore.Contains(o.Id))
                    .SelectMany(o => o.AllegroProductIds).Intersect(storeProductIds).Distinct().ToList()
                : new List<int>();

            var productIdsToRejectForMissingMainOffer = new List<int>();
            var urlsWithUserStoreButPotentiallyMissingMain = offersGroupedByUrl.Where(g => !urlsWithoutUserStore.Contains(g.Key));

            foreach (var group in urlsWithUserStoreButPotentiallyMissingMain)
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

            foreach (var scrapedOffer in scrapedOffersData)
            {
                // ---------- ZMIANA START ----------
                // 1. Znajdź ŚLEDZONY obiekt 'OfferToScrape' z listy załadowanej na początku metody.
                var sourceOfferToScrape = allOffersToScrape.FirstOrDefault(o => o.Id == scrapedOffer.AllegroOfferToScrapeId);

                // 2. Zabezpieczenie (choć nie powinno się zdarzyć)
                if (sourceOfferToScrape == null)
                {
                    continue; // Pomiń, jeśli z jakiegoś powodu nie znaleziono obiektu nadrzędnego
                }

                // 3. Użyj śledzonego obiektu 'sourceOfferToScrape' do pobrania listy ID produktów
                var productIdsForThisStore = sourceOfferToScrape.AllegroProductIds
                    .Intersect(storeProductIds)
                    .ToList();
                // ---------- ZMIANA KONIEC ----------


                if (sourceOfferToScrape.IsApiProcessed == true && !string.IsNullOrEmpty(sourceOfferToScrape.AllegroEan))
                {
                    var newEan = sourceOfferToScrape.AllegroEan;

                    // Ta logika była już poprawna - modyfikuje śledzoną listę 'storeProducts'
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

                // Reszta pętli (dodawanie do historii cen) bez zmian...
                foreach (var productId in productIdsForThisStore)
                {

                    if (allProductIdsToReject.Contains(productId))
                    {
                        continue;
                    }

                    newPriceHistories.Add(new AllegroPriceHistory
                    {
                        AllegroProductId = productId,
                        AllegroScrapeHistory = scrapeHistory,
                        SellerName = scrapedOffer.SellerName,
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
                        IdAllegro = scrapedOffer.IdAllegro
                    });

                    if (sourceOfferToScrape.IsApiProcessed == true)
                    {

                        newExtendedInfos.Add(new AllegroPriceHistoryExtendedInfoClass
                        {
                            AllegroProductId = productId,
                            ScrapHistory = scrapeHistory,
                            ApiAllegroPrice = sourceOfferToScrape.ApiAllegroPrice,
                            ApiAllegroPriceFromUser = sourceOfferToScrape.ApiAllegroPriceFromUser,
                            ApiAllegroCommission = sourceOfferToScrape.ApiAllegroCommission,
                            AnyPromoActive = sourceOfferToScrape.AnyPromoActive
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