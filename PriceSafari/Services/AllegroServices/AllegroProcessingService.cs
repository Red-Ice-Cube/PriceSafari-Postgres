


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
                .Where(p => p.StoreId == storeId) // Usunięto: && !p.IsRejected
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

            // 1. Odrzucenia z poziomu Pythona (błędy 404, błędy sieci)
            var productIdsToRejectFromUpstream = rejectedOffers.Any()
                ? rejectedOffers.SelectMany(o => o.AllegroProductIds).Intersect(storeProductIds).Distinct().ToList()
                : new List<int>();

            var validOfferIds = relevantOffersForStore.Where(o => !o.IsRejected).Select(o => o.Id).ToList();

            if (!validOfferIds.Any())
            {
                // Jeśli nie ma żadnych poprawnych zadań, przetwarzamy tylko odrzucenia upstream
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

            // --- ZMIANA 1: USUNIĘTO BLOKADĘ (EARLY RETURN) ---
            // Wcześniej tutaj był 'if (!scrapedOffersData.Any()) return...', który chronił przed pustą listą.
            // Teraz usuwamy go, aby kod przeszedł dalej i odrzucił produkty bez ofert.

            var offersGroupedByUrl = scrapedOffersData.GroupBy(o => o.AllegroOfferToScrapeId);

            // Znajdujemy URL-e, które zwróciły oferty, ale nie ma tam nas
            var urlsWithoutUserStore = offersGroupedByUrl
                .Where(group => !group.Any(offer => offer.SellerName == userAllegroStoreName))
                .Select(group => group.Key)
                .ToHashSet();

            // --- ZMIANA 2: OBSŁUGA CAŁKOWITEGO BRAKU OFERT ---
            // Znajdujemy ID zadań, które były "valid" (scraper wszedł i nie zgłosił błędu),
            // ale nie ma ich w scrapedOffersData (czyli Allegro zwróciło 0 ofert).
            var tasksWithResults = scrapedOffersData.Select(x => x.AllegroOfferToScrapeId).Distinct().ToHashSet();
            var tasksWithZeroResults = validOfferIds.Where(id => !tasksWithResults.Contains(id)).ToList();

            // Dodajemy je do zbioru "brak naszego sklepu" - bo skoro ofert jest 0, to nas też tam nie ma.
            foreach (var zeroResultId in tasksWithZeroResults)
            {
                urlsWithoutUserStore.Add(zeroResultId);
            }
            // -----------------------------------------------------

            var productIdsToRejectForSellerMismatch = urlsWithoutUserStore.Any()
                ? relevantOffersForStore.Where(o => urlsWithoutUserStore.Contains(o.Id))
                    .SelectMany(o => o.AllegroProductIds).Intersect(storeProductIds).Distinct().ToList()
                : new List<int>();

            // 3. Logika brakującej konkretnej oferty (po ID)
            var productIdsToRejectForMissingMainOffer = new List<int>();

            // Iterujemy tylko po grupach, które MAJĄ wyniki i MAJĄ naszą ofertę (reszta i tak odpadnie wyżej)
            // Ale musimy też uważać na tasksWithZeroResults - dla nich pętla po offersGroupedByUrl się nie wykona,
            // co jest poprawne, bo one i tak wpadły już do SellerMismatch.
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

            // Wykonanie odrzucenia w bazie
            if (allProductIdsToReject.Any())
            {
                await _context.AllegroProducts
                    .Where(p => allProductIdsToReject.Contains(p.AllegroProductId))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsRejected, true));
            }

            // --- Zapis historii i sukcesów (Reszta bez zmian) ---
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
                var sourceOfferToScrape = allOffersToScrape.FirstOrDefault(o => o.Id == scrapedOffer.AllegroOfferToScrapeId);

                if (sourceOfferToScrape == null)
                {
                    continue;
                }

                var productIdsForThisStore = sourceOfferToScrape.AllegroProductIds
                    .Intersect(storeProductIds)
                    .ToList();

                // Logika aktualizacji EAN
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

                foreach (var productId in productIdsForThisStore)
                {
                    // Jeśli produkt jest na liście do odrzucenia (z aktualnego przebiegu), pomijamy go
                    if (allProductIdsToReject.Contains(productId))
                    {
                        continue;
                    }

                    // ZMIANA 2: "Przywracanie do życia" (Un-reject)
                    // Jeśli produkt przeszedł walidację (nie ma go w allProductIdsToReject),
                    // ale w bazie wciąż jest odrzucony - ustawiamy IsRejected na false.
                    var productEntity = storeProducts.FirstOrDefault(p => p.AllegroProductId == productId);
                    if (productEntity != null && productEntity.IsRejected)
                    {
                        productEntity.IsRejected = false;
                        // Entity Framework śledzi tę zmianę i zapisze ją przy SaveChangesAsync na końcu
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

            // SaveChangesAsync zapisze:
            // 1. Nową historię (scrapeHistory)
            // 2. Nowe ceny (newPriceHistories)
            // 3. Ewentualne zmiany EAN
            // 4. Ewentualne zmiany flagi IsRejected na false (ZMIANA 2)
            await _context.SaveChangesAsync();

            return (scrapeHistory.ProcessedUrlsCount, scrapeHistory.SavedOffersCount);
        }
    }
}