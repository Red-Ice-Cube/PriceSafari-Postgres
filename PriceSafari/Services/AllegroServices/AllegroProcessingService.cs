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
            // 1. Pobierz sklep i jego aliasy
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null || string.IsNullOrEmpty(store.StoreNameAllegro))
            {
                // Ten sklep nie jest skonfigurowany do pracy z Allegro
                return (0, 0);
            }
            var storeAllegroName = store.StoreNameAllegro.ToLower();

            // 2. Pobierz wszystkie "surowe" dane z tabel pośrednich
            var allScrapedOffers = await _context.AllegroScrapedOffers
                .Include(o => o.AllegroOfferToScrape)
                .AsNoTracking()
                .ToListAsync();

            if (!allScrapedOffers.Any())
            {
                // Brak danych do przetworzenia
                return (0, 0);
            }

            // 3. Stwórz nową sesję przetwarzania (log)
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

            // 4. Pobierz wszystkie produkty, których mogą dotyczyć te oferty
            var relevantProducts = await _context.AllegroProducts
                .Where(p => allProductIds.Contains(p.AllegroProductId))
                .AsNoTracking()
                .ToDictionaryAsync(p => p.AllegroProductId);

            var newPriceHistories = new List<AllegroPriceHistory>();

            // 5. Przetwarzanie danych
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
                                // --- MAPOWANIE NOWYCH PÓL ---
                                DeliveryCost = scrapedOffer.DeliveryCost,
                                DeliveryTime = scrapedOffer.DeliveryTime,
                                Popularity = scrapedOffer.Popularity
                                // -----------------------------
                            });
                        }
                    }
                }
            }

            // 6. Zapisz "czyste" dane
            if (newPriceHistories.Any())
            {
                await _context.AllegroPriceHistories.AddRangeAsync(newPriceHistories);
            }

            // 7. Zaktualizuj statystyki w logu sesji
            scrapeHistory.ProcessedUrlsCount = allScrapedOffers.Select(o => o.AllegroOfferToScrapeId).Distinct().Count();
            scrapeHistory.SavedOffersCount = newPriceHistories.Count;

            await _context.SaveChangesAsync();

            return (scrapeHistory.ProcessedUrlsCount, scrapeHistory.SavedOffersCount);
        }

        public async Task ClearIntermediateTablesAsync()
        {
            // Opcjonalna funkcja do czyszczenia tabel pośrednich po przetworzeniu
            await _context.AllegroScrapedOffers.ExecuteDeleteAsync();
            await _context.AllegroOffersToScrape.ExecuteDeleteAsync();
        }
    }
}