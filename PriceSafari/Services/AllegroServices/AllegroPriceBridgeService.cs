using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Controllers.MemberControllers; // Dla DTO
using PriceSafari.Data;
using PriceSafari.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace PriceSafari.Services.AllegroServices
{
    // Wykorzystujemy DTO zdefiniowane w kontrolerze
    using static PriceSafari.Controllers.MemberControllers.AllegroPriceHistoryController;

    public class AllegroPriceBridgeService
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<AllegroPriceBridgeService> _logger;
        private static readonly HttpClient _httpClient = new();

        public AllegroPriceBridgeService(PriceSafariContext context, ILogger<AllegroPriceBridgeService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Wykonuje wsadową aktualizację cen ofert na Allegro.
        /// </summary>
        public async Task<PriceBridgeResult> ExecutePriceChangesAsync(int storeId, List<OfferPriceChangeRequest> changes)
        {
            var result = new PriceBridgeResult();
            var store = await _context.Stores.FindAsync(storeId);

            if (store == null)
            {
                result.FailedCount = changes.Count;
                result.Errors.Add(new PriceBridgeError { OfferId = "Wszystkie", Message = "Nie znaleziono sklepu." });
                return result;
            }

            if (!store.IsAllegroPriceBridgeActive)
            {
                result.FailedCount = changes.Count;
                result.Errors.Add(new PriceBridgeError { OfferId = "Wszystkie", Message = "Wgrywanie cen po API jest wyłączone dla tego sklepu." });
                return result;
            }

            if (!store.IsAllegroTokenActive || string.IsNullOrEmpty(store.AllegroApiToken))
            {
                result.FailedCount = changes.Count;
                result.Errors.Add(new PriceBridgeError { OfferId = "Wszystkie", Message = "Brak aktywnego tokenu API Allegro dla tego sklepu." });
                return result;
            }

            string accessToken = store.AllegroApiToken;

            foreach (var change in changes)
            {
                if (string.IsNullOrEmpty(change.OfferId) || string.IsNullOrEmpty(change.NewPrice))
                {
                    result.FailedCount++;
                    result.Errors.Add(new PriceBridgeError { OfferId = change.OfferId ?? "Brak ID", Message = "Brakujące ID oferty lub nowej ceny." });
                    continue;
                }

                var (success, errorMessage) = await SetNewOfferPriceAsync(accessToken, change.OfferId, change.NewPrice);

                if (success)
                {
                    result.SuccessfulCount++;
                }
                else
                {
                    result.FailedCount++;
                    result.Errors.Add(new PriceBridgeError { OfferId = change.OfferId, Message = errorMessage });

                    // Jeśli błąd to 401, token prawdopodobnie wygasł. Przerywamy dalsze próby.
                    if (errorMessage.Contains("401") || errorMessage.Contains("Unauthorized"))
                    {
                        _logger.LogWarning("Token API dla sklepu {StoreId} wygasł lub jest nieprawidłowy. Przerywam wgrywanie cen.", storeId);
                        // Oznacz token jako nieaktywny
                        store.IsAllegroTokenActive = false;
                        await _context.SaveChangesAsync();

                        // Dodaj błąd dla reszty ofert
                        int remainingChanges = changes.Count - result.SuccessfulCount - result.FailedCount;
                        if (remainingChanges > 0)
                        {
                            result.FailedCount += remainingChanges;
                            result.Errors.Add(new PriceBridgeError { OfferId = "Pozostałe", Message = "Token API wygasł. Przerwano." });
                        }
                        break; // Przerwij pętlę
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Wysyła żądanie PATCH do API Allegro w celu aktualizacji ceny jednej oferty.
        /// </summary>
        private async Task<(bool Success, string ErrorMessage)> SetNewOfferPriceAsync(string accessToken, string offerId, string newPrice)
        {
            var apiUrl = $"https://api.allegro.pl/sale/product-offers/{offerId}";

            // Upewnij się, że cena ma format z kropką jako separatorem
            var formattedPrice = decimal.Parse(newPrice, new CultureInfo("pl-PL")).ToString(CultureInfo.InvariantCulture);

            var payload = new
            {
                sellingMode = new
                {
                    price = new
                    {
                        amount = formattedPrice,
                        currency = "PLN"
                    }
                }
            };

            var httpContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.allegro.public.v1+json");

            using var request = new HttpRequestMessage(HttpMethod.Patch, apiUrl) { Content = httpContent };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            try
            {
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    return (true, string.Empty);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return (false, "Błąd autoryzacji (401). Token może być nieważny.");
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Błąd API Allegro podczas zmiany ceny oferty {OfferId}. Status: {StatusCode}. Odpowiedź: {ErrorContent}", offerId, response.StatusCode, errorContent);

                // Próba wyciągnięcia czytelniejszego błędu z JSON
                try
                {
                    var errorNode = JsonNode.Parse(errorContent);
                    var errors = errorNode?["errors"]?.AsArray();
                    if (errors != null && errors.Count > 0)
                    {
                        return (false, errors[0]?["message"]?.ToString() ?? errorContent);
                    }
                }
                catch { /* Ignoruj błąd parsowania JSON błędu */ }

                return (false, $"Błąd API ({response.StatusCode}). {errorContent}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Krytyczny błąd podczas wysyłania zmiany ceny dla oferty {OfferId}", offerId);
                return (false, $"Wyjątek aplikacji: {ex.Message}");
            }
        }
    }
}