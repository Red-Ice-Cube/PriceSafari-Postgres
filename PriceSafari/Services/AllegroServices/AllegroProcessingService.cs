using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;

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

            var storeProductIdsList = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId)
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

            var rejectedOffers = relevantOffersForStore.Where(o => o.IsRejected).ToList();
            if (rejectedOffers.Any())
            {

                var productIdsToReject = rejectedOffers
                    .SelectMany(o => o.AllegroProductIds)
                    .Intersect(storeProductIds)
                    .Distinct()
                    .ToList();

                await _context.AllegroProducts
                    .Where(p => productIdsToReject.Contains(p.AllegroProductId))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsRejected, true));
            }

            var validOfferIds = relevantOffersForStore
                .Where(o => !o.IsRejected)
                .Select(o => o.Id)
                .ToList();

            if (!validOfferIds.Any())
            {

                return (rejectedOffers.Count, 0);
            }

            var scrapedOffersData = await _context.AllegroScrapedOffers
                .Include(o => o.AllegroOfferToScrape)
                .Where(o => validOfferIds.Contains(o.AllegroOfferToScrapeId))
                .AsNoTracking()
                .ToListAsync();

            if (!scrapedOffersData.Any())
            {
                return (rejectedOffers.Count, 0);
            }

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