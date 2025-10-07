//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;

//namespace PriceSafari.Services.AllegroServices
//{
//    public class AllegroProcessingService
//    {
//        private readonly PriceSafariContext _context;

//        public AllegroProcessingService(PriceSafariContext context)
//        {
//            _context = context;
//        }

//        public async Task<(int processedUrls, int savedOffers)> ProcessScrapedDataForStoreAsync(int storeId)
//        {

//            var storeProductIdsList = await _context.AllegroProducts
//                .Where(p => p.StoreId == storeId)
//                .Select(p => p.AllegroProductId)
//                .ToListAsync();

//            var storeProductIds = new HashSet<int>(storeProductIdsList); 

//            if (!storeProductIds.Any())
//            {
//                return (0, 0);
//            }

//            var allOffersToScrape = await _context.AllegroOffersToScrape.ToListAsync();

//            var relevantOffersForStore = allOffersToScrape
//                .Where(o => o.AllegroProductIds.Any(pId => storeProductIds.Contains(pId)))
//                .ToList();

//            var rejectedOffers = relevantOffersForStore.Where(o => o.IsRejected).ToList();
//            if (rejectedOffers.Any())
//            {

//                var productIdsToReject = rejectedOffers
//                    .SelectMany(o => o.AllegroProductIds)
//                    .Intersect(storeProductIds)
//                    .Distinct()
//                    .ToList();

//                await _context.AllegroProducts
//                    .Where(p => productIdsToReject.Contains(p.AllegroProductId))
//                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsRejected, true));
//            }

//            var validOfferIds = relevantOffersForStore
//                .Where(o => !o.IsRejected)
//                .Select(o => o.Id)
//                .ToList();

//            if (!validOfferIds.Any())
//            {

//                return (rejectedOffers.Count, 0);
//            }

//            var scrapedOffersData = await _context.AllegroScrapedOffers
//                .Include(o => o.AllegroOfferToScrape)
//                .Where(o => validOfferIds.Contains(o.AllegroOfferToScrapeId))
//                .AsNoTracking()
//                .ToListAsync();

//            if (!scrapedOffersData.Any())
//            {
//                return (rejectedOffers.Count, 0);
//            }

//            var scrapeHistory = new AllegroScrapeHistory
//            {
//                StoreId = storeId,
//                Date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"))
//            };
//            _context.AllegroScrapeHistories.Add(scrapeHistory);

//            var newPriceHistories = new List<AllegroPriceHistory>();

//            foreach (var scrapedOffer in scrapedOffersData)
//            {

//                var productIdsForThisStore = scrapedOffer.AllegroOfferToScrape.AllegroProductIds
//                    .Intersect(storeProductIds)
//                    .ToList();

//                foreach (var productId in productIdsForThisStore)
//                {
//                    newPriceHistories.Add(new AllegroPriceHistory
//                    {
//                        AllegroProductId = productId,
//                        AllegroScrapeHistory = scrapeHistory,
//                        SellerName = scrapedOffer.SellerName,
//                        Price = scrapedOffer.Price,
//                        DeliveryCost = scrapedOffer.DeliveryCost,
//                        DeliveryTime = scrapedOffer.DeliveryTime,
//                        Popularity = scrapedOffer.Popularity,
//                        SuperSeller = scrapedOffer.SuperSeller,
//                        Smart = scrapedOffer.Smart,
//                        IsBestPriceGuarantee = scrapedOffer.IsBestPriceGuarantee,
//                        TopOffer = scrapedOffer.TopOffer,
//                        SuperPrice = scrapedOffer.SuperPrice,
//                        Promoted = scrapedOffer.Promoted,
//                        Sponsored = scrapedOffer.Sponsored,
//                        IdAllegro = scrapedOffer.IdAllegro
//                    });
//                }
//            }

//            if (newPriceHistories.Any())
//            {
//                await _context.AllegroPriceHistories.AddRangeAsync(newPriceHistories);
//            }

//            scrapeHistory.ProcessedUrlsCount = relevantOffersForStore.Count;
//            scrapeHistory.SavedOffersCount = newPriceHistories.Count;

//            await _context.SaveChangesAsync();

//            return (scrapeHistory.ProcessedUrlsCount, scrapeHistory.SavedOffersCount);
//        }
//    }
//}



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

        public async Task<(int processedUrls, int savedOffers)> ProcessScrapedDataForStoreAsync(int storeId)
        {
            // --- NOWOŚĆ: Pobierz nazwę sklepu użytkownika z bazy danych ---
            var userStore = await _context.Stores.FindAsync(storeId);
            if (userStore == null || string.IsNullOrEmpty(userStore.StoreNameAllegro))
            {
                // Jeśli sklep nie istnieje lub nie ma zdefiniowanej nazwy na Allegro, zakończ działanie.
                // Możesz tu dodać logowanie błędu.
                return (0, 0);
            }
            var userAllegroStoreName = userStore.StoreNameAllegro;
            // -----------------------------------------------------------

            var storeProductIdsList = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId && !p.IsRejected) // Optymalizacja: nie pobieraj już odrzuconych
                .Select(p => p.AllegroProductId)
                .ToListAsync();

            var storeProductIds = new HashSet<int>(storeProductIdsList);

            if (!storeProductIds.Any())
            {
                return (0, 0);
            }

            var allOffersToScrape = await _context.AllegroOffersToScrape.ToListAsync();

            var relevantOffersForStore = allOffersToScrape
                .Where(o => o.AllegroProductIds.Any(pId => storeProductIds.Contains(pId)))
                .ToList();

            // 1. Odrzucenie na podstawie flagi IsRejected (stara logika)
            var rejectedOffers = relevantOffersForStore.Where(o => o.IsRejected).ToList();
            var productIdsToRejectFromUpstream = new List<int>();
            if (rejectedOffers.Any())
            {
                productIdsToRejectFromUpstream = rejectedOffers
                    .SelectMany(o => o.AllegroProductIds)
                    .Intersect(storeProductIds)
                    .Distinct()
                    .ToList();
            }

            var validOfferIds = relevantOffersForStore
                .Where(o => !o.IsRejected)
                .Select(o => o.Id)
                .ToList();

            if (!validOfferIds.Any())
            {
                // Jeśli są tylko odrzucone z poprzedniego kroku, zaktualizuj i zakończ
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

            // --- NOWOŚĆ: Logika odrzucania na podstawie braku nazwy sprzedawcy ---
            // 2. Znajdź URL-e (AllegroOfferToScrapeId), dla których nie znaleziono ofert naszego sklepu
            var offersGroupedByUrl = scrapedOffersData.GroupBy(o => o.AllegroOfferToScrapeId);

            var urlsWithoutUserStore = offersGroupedByUrl
                .Where(group => !group.Any(offer => offer.SellerName == userAllegroStoreName))
                .Select(group => group.Key)
                .ToHashSet();

            // 3. Zbierz ID produktów powiązanych z tymi URL-ami
            var productIdsToRejectForSellerMismatch = new List<int>();
            if (urlsWithoutUserStore.Any())
            {
                productIdsToRejectForSellerMismatch = relevantOffersForStore
                    .Where(o => urlsWithoutUserStore.Contains(o.Id))
                    .SelectMany(o => o.AllegroProductIds)
                    .Intersect(storeProductIds)
                    .Distinct()
                    .ToList();
            }
            // -------------------------------------------------------------------

            // --- NOWOŚĆ: Połącz obie listy produktów do odrzucenia ---
            var allProductIdsToReject = productIdsToRejectFromUpstream
                .Concat(productIdsToRejectForSellerMismatch)
                .Distinct()
                .ToList();

            if (allProductIdsToReject.Any())
            {
                await _context.AllegroProducts
                    .Where(p => allProductIdsToReject.Contains(p.AllegroProductId))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsRejected, true));
            }
            // -------------------------------------------------------


            var scrapeHistory = new AllegroScrapeHistory
            {
                StoreId = storeId,
                Date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"))
            };
            _context.AllegroScrapeHistories.Add(scrapeHistory);

            var newPriceHistories = new List<AllegroPriceHistory>();

            foreach (var scrapedOffer in scrapedOffersData)
            {
                var productIdsForThisStore = scrapedOffer.AllegroOfferToScrape.AllegroProductIds
                    .Intersect(storeProductIds)
                    .ToList();

                foreach (var productId in productIdsForThisStore)
                {
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
                }
            }

            if (newPriceHistories.Any())
            {
                await _context.AllegroPriceHistories.AddRangeAsync(newPriceHistories);
            }

            scrapeHistory.ProcessedUrlsCount = relevantOffersForStore.Count;
            scrapeHistory.SavedOffersCount = newPriceHistories.Count;

            await _context.SaveChangesAsync();

            return (scrapeHistory.ProcessedUrlsCount, scrapeHistory.SavedOffersCount);
        }
    }
}