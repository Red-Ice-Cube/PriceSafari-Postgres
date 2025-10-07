using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;

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

            public async Task<(int urlsPrepared, int totalProducts)> GroupAndSaveUrls()
            {
                _logger.LogInformation("Rozpoczynam proces grupowania URL-i ofert Allegro...");

                var allProducts = await _context.AllegroProducts
                 .Where(p => p.IsScrapable)
                 .AsNoTracking()
                 .ToListAsync();

            if (!allProducts.Any())
                {
                    _logger.LogWarning("Nie znaleziono żadnych produktów Allegro do przetworzenia.");
                    return (0, 0);
                }

                var groupedByUrl = allProducts
                    .Where(p => !string.IsNullOrWhiteSpace(p.AllegroOfferUrl))
                    .GroupBy(p => p.AllegroOfferUrl);

                var offersToSave = new List<AllegroOfferToScrape>();

                foreach (var group in groupedByUrl)
                {
                    var newOffer = new AllegroOfferToScrape
                    {
                        AllegroOfferUrl = group.Key,
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
                return (offersToSave.Count, allProducts.Count);
            }
        

    }
}
