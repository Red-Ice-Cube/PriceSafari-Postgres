using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Linq;

namespace PriceSafari.Services.AllegroServices
{
    public class AllegroUrlGroupingService
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<AllegroUrlGroupingService> _logger;

        public AllegroUrlGroupingService(PriceSafariContext context, ILogger<AllegroUrlGroupingService> logger)
        {
            _context = context;
            _logger = logger;
        }

        private long ExtractOfferIdFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return 0;
            }

            try
            {
                // 1. Najpierw sprawdzamy, czy w URL jest parametr "offerId" (DLA NOWYCH LINKÓW)
                if (url.Contains("offerId="))
                {
                    var uri = new Uri(url);
                    // Proste parsowanie query stringa bez dodatkowych bibliotek
                    var query = uri.Query.TrimStart('?');
                    var queryParams = query.Split('&');

                    foreach (var param in queryParams)
                    {
                        var parts = param.Split('=');
                        if (parts.Length == 2 && parts[0].Equals("offerId", StringComparison.OrdinalIgnoreCase))
                        {
                            if (long.TryParse(parts[1], out long idFromQuery))
                            {
                                return idFromQuery;
                            }
                        }
                    }
                }

                // 2. Jeśli nie znaleziono w parametrach, stosujemy starą metodę (DLA LINKÓW /oferta/)
                // Ale musimy najpierw usunąć wszystko od znaku '?' w prawo, żeby nie psuło parsowania
                var urlWithoutQuery = url.Split('?')[0];

                // Zabezpieczenie przed końcowym slashem
                urlWithoutQuery = urlWithoutQuery.TrimEnd('/');

                var lastPart = urlWithoutQuery.Split('-').LastOrDefault();

                if (long.TryParse(lastPart, out long idFromPath))
                {
                    return idFromPath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Błąd podczas parsowania ID z URL: {Url}", url);
            }

            _logger.LogWarning("Nie udało się wyodrębnić ID oferty z URL: {Url}", url);
            return 0;
        }

        public async Task<(int urlsPrepared, int totalProducts, List<string> processedStoreNames)> GroupAndSaveUrls(List<int> storeIds)
        {
            _logger.LogInformation("Rozpoczynam proces grupowania URL-i ofert Allegro dla wybranych sklepów...");

            if (storeIds == null || !storeIds.Any())
            {
                _logger.LogWarning("Lista ID sklepów jest pusta. Przerwanie operacji.");
                return (0, 0, new List<string>());
            }

            var validStores = await _context.Stores
                .Where(s => storeIds.Contains(s.StoreId) && s.OnAllegro && s.RemainingDays > 0)
                .Select(s => new { s.StoreId, s.StoreName })
                .ToListAsync();

            var validStoreIds = validStores.Select(s => s.StoreId).ToList();
            var validStoreNames = validStores.Select(s => s.StoreName).ToList();

            if (!validStoreIds.Any())
            {
                _logger.LogWarning("Żaden ze wskazanych sklepów nie spełnia kryteriów (OnAllegro=true, RemainingDays>0).");
                return (0, 0, new List<string>());
            }

            _logger.LogInformation("Sklepy zakwalifikowane do grupowania URL Allegro: {StoreNames}", string.Join(", ", validStoreNames));

            var allProducts = await _context.AllegroProducts
               .Where(p => validStoreIds.Contains(p.StoreId) && p.IsScrapable)
               .AsNoTracking()
               .ToListAsync();

            if (!allProducts.Any())
            {
                _logger.LogWarning("Nie znaleziono żadnych produktów Allegro do przetworzenia dla podanych sklepów.");
                return (0, 0, validStoreNames);
            }

            var groupedByUrlAndStore = allProducts
                  .Where(p => !string.IsNullOrWhiteSpace(p.AllegroOfferUrl))
                  .GroupBy(p => new { p.AllegroOfferUrl, p.StoreId });

            var offersToSave = new List<AllegroOfferToScrape>();

            foreach (var group in groupedByUrlAndStore)
            {
                var offerUrl = group.Key.AllegroOfferUrl;
                var storeIdForOffer = group.Key.StoreId;
                var offerId = ExtractOfferIdFromUrl(offerUrl);

                if (offerId == 0)
                {
                    continue;
                }

                var newOffer = new AllegroOfferToScrape
                {
                    AllegroOfferUrl = offerUrl,
                    AllegroOfferId = offerId,
                    StoreId = storeIdForOffer,
                    AllegroProductIds = group.Select(p => p.AllegroProductId).ToList(),
                    AddedDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"))
                };
                offersToSave.Add(newOffer);
            }

            _logger.LogInformation("Znaleziono {UrlCount} unikalnych URL-i z {ProductCount} produktów.", offersToSave.Count, allProducts.Count);

            _logger.LogInformation("Czyszczenie istniejących danych w tabeli pośredniej...");
            await _context.AllegroOffersToScrape.ExecuteDeleteAsync();

            _logger.LogInformation("Zapisywanie nowych, zgrupowanych danych...");
            await _context.AllegroOffersToScrape.AddRangeAsync(offersToSave);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Proces grupowania zakończony pomyślnie.");
            return (offersToSave.Count, allProducts.Count, validStoreNames);
        }
    }
}