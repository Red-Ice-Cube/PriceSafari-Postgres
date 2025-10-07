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
//            var store = await _context.Stores.FindAsync(storeId);
//            if (store == null || string.IsNullOrEmpty(store.StoreNameAllegro))
//            {
//                return (0, 0);
//            }
//            var storeAllegroName = store.StoreNameAllegro.ToLower();

//            var allScrapedOffers = await _context.AllegroScrapedOffers
//                .Include(o => o.AllegroOfferToScrape)
//                .AsNoTracking()
//                .ToListAsync();

//            if (!allScrapedOffers.Any())
//            {
//                return (0, 0);
//            }

//            var scrapeHistory = new AllegroScrapeHistory
//            {
//                StoreId = storeId,
//                Date = DateTime.UtcNow
//            };
//            _context.AllegroScrapeHistories.Add(scrapeHistory);

//            var allProductIds = allScrapedOffers
//                .SelectMany(o => o.AllegroOfferToScrape.AllegroProductIds)
//                .Distinct()
//                .ToList();

//            var relevantProducts = await _context.AllegroProducts
//                .Where(p => allProductIds.Contains(p.AllegroProductId))
//                .AsNoTracking()
//                .ToDictionaryAsync(p => p.AllegroProductId);

//            var newPriceHistories = new List<AllegroPriceHistory>();

//            foreach (var scrapedOffer in allScrapedOffers)
//            {
//                var productIdsForThisUrl = scrapedOffer.AllegroOfferToScrape.AllegroProductIds;

//                foreach (var productId in productIdsForThisUrl)
//                {
//                    if (relevantProducts.TryGetValue(productId, out var product))
//                    {
//                        if (product.StoreId == storeId)
//                        {
//                            newPriceHistories.Add(new AllegroPriceHistory
//                            {
//                                AllegroProductId = productId,
//                                AllegroScrapeHistory = scrapeHistory,
//                                SellerName = scrapedOffer.SellerName,
//                                Price = scrapedOffer.Price,
//                                DeliveryCost = scrapedOffer.DeliveryCost,
//                                DeliveryTime = scrapedOffer.DeliveryTime,
//                                Popularity = scrapedOffer.Popularity,
//                                SuperSeller = scrapedOffer.SuperSeller,
//                                Smart = scrapedOffer.Smart,

//                                IsBestPriceGuarantee = scrapedOffer.IsBestPriceGuarantee,
//                                TopOffer = scrapedOffer.TopOffer,
//                                SuperPrice = scrapedOffer.SuperPrice,
//                                Promoted = scrapedOffer.Promoted,
//                                Sponsored = scrapedOffer.SuperPrice,
//                                IdAllegro = scrapedOffer.IdAllegro
//                            });
//                        }
//                    }
//                }
//            }

//            if (newPriceHistories.Any())
//            {
//                await _context.AllegroPriceHistories.AddRangeAsync(newPriceHistories);
//            }

//            scrapeHistory.ProcessedUrlsCount = allScrapedOffers.Select(o => o.AllegroOfferToScrapeId).Distinct().Count();
//            scrapeHistory.SavedOffersCount = newPriceHistories.Count;

//            await _context.SaveChangesAsync();

//            return (scrapeHistory.ProcessedUrlsCount, scrapeHistory.SavedOffersCount);
//        }

//        // Metoda ClearIntermediateTablesAsync pozostaje bez zmian
//        public async Task ClearIntermediateTablesAsync()
//        {
//            await _context.AllegroScrapedOffers.ExecuteDeleteAsync();
//            await _context.AllegroOffersToScrape.ExecuteDeleteAsync();
//        }
//    }
//}


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
            // --- CZĘŚĆ LOGIKI DLA ODRZUCONYCH OFERT ---

            // 1. Pobierz ID produktów Allegro TYLKO dla przetwarzanego sklepu
            var storeProductIds = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId)
                .Select(p => p.AllegroProductId)
                .ToListAsync();

            if (!storeProductIds.Any())
            {
                return (0, 0); // Zwróć 0, jeśli sklep nie ma produktów Allegro
            }

            // 2. Znajdź odrzucone oferty powiązane z produktami TEGO sklepu
            var rejectedOffers = await _context.AllegroOffersToScrape
                .Where(o => o.IsRejected && o.AllegroProductIds.Any(pId => storeProductIds.Contains(pId)))
                .ToListAsync();

            if (rejectedOffers.Any())
            {
                // 3. Zbierz wszystkie ID produktów powiązanych z odrzuconymi ofertami TEGO sklepu
                var productIdsToReject = rejectedOffers
                    .SelectMany(o => o.AllegroProductIds)
                    .Intersect(storeProductIds) // Upewniamy się, że aktualizujemy tylko produkty tego sklepu
                    .Distinct()
                    .ToList();

                // 4. Znajdź te produkty w głównej tabeli i oznacz je jako odrzucone
                var productsToUpdate = await _context.AllegroProducts
                    .Where(p => productIdsToReject.Contains(p.AllegroProductId))
                    .ToListAsync();

                foreach (var product in productsToUpdate)
                {
                    product.IsRejected = true;
                }
            }

            // --- KONIEC LOGIKI DLA ODRZUCONYCH OFERT ---


            // --- ISTNIEJĄCA LOGIKA DLA PRZETWARZANIA PRAWIDŁOWYCH OFERT ---

            // Pobierz tylko te oferty, które są powiązane z produktami TEGO sklepu
            var relevantScrapedOffers = await _context.AllegroScrapedOffers
                .Include(o => o.AllegroOfferToScrape)
                .Where(o => o.AllegroOfferToScrape.AllegroProductIds.Any(pId => storeProductIds.Contains(pId)))
                .AsNoTracking()
                .ToListAsync();

            if (!relevantScrapedOffers.Any())
            {
                // Jeśli nie ma prawidłowych ofert, zapisz i tak zmiany (odrzucenia)
                await _context.SaveChangesAsync();
                return (rejectedOffers.Count, 0);
            }

            var scrapeHistory = new AllegroScrapeHistory
            {
                StoreId = storeId,
                Date = DateTime.UtcNow
            };
            _context.AllegroScrapeHistories.Add(scrapeHistory);

            var newPriceHistories = new List<AllegroPriceHistory>();

            foreach (var scrapedOffer in relevantScrapedOffers)
            {
                // Ta oferta jest na pewno powiązana z naszym sklepem, więc iterujemy po wszystkich jej produktach
                var productIdsForThisUrl = scrapedOffer.AllegroOfferToScrape.AllegroProductIds
                   .Intersect(storeProductIds) // Upewniamy się, że bierzemy tylko te z naszego sklepu
                   .ToList();

                foreach (var productId in productIdsForThisUrl)
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
                        Sponsored = scrapedOffer.Sponsored, // Poprawiłem błąd kopiowania
                        IdAllegro = scrapedOffer.IdAllegro
                    });
                }
            }

            if (newPriceHistories.Any())
            {
                await _context.AllegroPriceHistories.AddRangeAsync(newPriceHistories);
            }

            scrapeHistory.ProcessedUrlsCount = relevantScrapedOffers.Select(o => o.AllegroOfferToScrapeId).Distinct().Count() + rejectedOffers.Count;
            scrapeHistory.SavedOffersCount = newPriceHistories.Count;

            await _context.SaveChangesAsync();

            return (scrapeHistory.ProcessedUrlsCount, scrapeHistory.SavedOffersCount);
        }

        public async Task ClearIntermediateTablesAsync()
        {
            await _context.AllegroScrapedOffers.ExecuteDeleteAsync();
            await _context.AllegroOffersToScrape.ExecuteDeleteAsync();
        }
    }
}