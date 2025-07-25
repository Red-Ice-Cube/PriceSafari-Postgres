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
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null || string.IsNullOrEmpty(store.StoreNameAllegro))
            {
                return (0, 0);
            }
            var storeAllegroName = store.StoreNameAllegro.ToLower();

            var allScrapedOffers = await _context.AllegroScrapedOffers
                .Include(o => o.AllegroOfferToScrape)
                .AsNoTracking()
                .ToListAsync();

            if (!allScrapedOffers.Any())
            {
                return (0, 0);
            }

            var scrapeHistory = new AllegroScrapeHistory
            {
                StoreId = storeId,
                Date = DateTime.UtcNow
            };
            _context.AllegroScrapeHistories.Add(scrapeHistory);

            var allProductIds = allScrapedOffers
                .SelectMany(o => o.AllegroOfferToScrape.AllegroProductIds)
                .Distinct()
                .ToList();

            var relevantProducts = await _context.AllegroProducts
                .Where(p => allProductIds.Contains(p.AllegroProductId))
                .AsNoTracking()
                .ToDictionaryAsync(p => p.AllegroProductId);

            var newPriceHistories = new List<AllegroPriceHistory>();

            foreach (var scrapedOffer in allScrapedOffers)
            {
                var productIdsForThisUrl = scrapedOffer.AllegroOfferToScrape.AllegroProductIds;

                foreach (var productId in productIdsForThisUrl)
                {
                    if (relevantProducts.TryGetValue(productId, out var product))
                    {
                        if (product.StoreId == storeId)
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
                                // --- ZMIANA: Zmapowanie nowych pól ---
                                IsBestPriceGuarantee = scrapedOffer.IsBestPriceGuarantee,
                                TopOffer = scrapedOffer.TopOffer
                            });
                        }
                    }
                }
            }

            if (newPriceHistories.Any())
            {
                await _context.AllegroPriceHistories.AddRangeAsync(newPriceHistories);
            }

            scrapeHistory.ProcessedUrlsCount = allScrapedOffers.Select(o => o.AllegroOfferToScrapeId).Distinct().Count();
            scrapeHistory.SavedOffersCount = newPriceHistories.Count;

            await _context.SaveChangesAsync();

            return (scrapeHistory.ProcessedUrlsCount, scrapeHistory.SavedOffersCount);
        }

        // Metoda ClearIntermediateTablesAsync pozostaje bez zmian
        public async Task ClearIntermediateTablesAsync()
        {
            await _context.AllegroScrapedOffers.ExecuteDeleteAsync();
            await _context.AllegroOffersToScrape.ExecuteDeleteAsync();
        }
    }
}