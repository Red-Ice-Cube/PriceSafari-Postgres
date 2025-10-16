//using System.Globalization;
//using System.Net.Http.Headers;
//using System.Text;
//using System.Text.Json;
//using System.Text.Json.Nodes;

//namespace PriceSafari.Services.AllegroServices
//{
//    public class AllegroApiBotService
//    {

//        private const string CLIENT_ID = "9163c502a1cb4c348579c5ff54c75df1";
//        private const string CLIENT_SECRET = "ULlu7uecKMM1t1LpgrDe1D7MOTn0ABVVuHeKeQfGJ0Z80v9ojN4JyVqXOBLByQMZ";
//        private const string OFFER_IDS = "14168428825";
//        private const string REFRESH_TOKEN_FILE = "refresh_token.txt";

//        private static readonly HttpClient _httpClient = new();

//        public async Task RunBotAsync()
//        {
//            try
//            {
//                string? accessToken;
//                if (File.Exists(REFRESH_TOKEN_FILE))
//                {
//                    Console.WriteLine("🔑 Znaleziono refresh_token. Próbuję odświeżyć sesję automatycznie...");
//                    var refreshToken = await File.ReadAllTextAsync(REFRESH_TOKEN_FILE);
//                    accessToken = await GetAccessTokenWithRefreshToken(refreshToken);
//                }
//                else
//                {
//                    Console.WriteLine("❗ Nie znaleziono refresh_token. Rozpoczynam jednorazową autoryzację manualną...");
//                    accessToken = await GetTokensWithDeviceCode();
//                }

//                if (string.IsNullOrEmpty(accessToken))
//                {
//                    Console.WriteLine("❌ Nie udało się uzyskać tokena dostępowego.");
//                    return;
//                }

//                Console.WriteLine("✅ Pomyślnie uzyskano token dostępowy!");

//                string[] offerIdsToProcess = OFFER_IDS.Split(',')
//                    .Select(id => id.Trim())
//                    .Where(id => !string.IsNullOrWhiteSpace(id))
//                    .ToArray();

//                Console.WriteLine($"\n🔍 Znaleziono {offerIdsToProcess.Length} ofert do przetworzenia.");

//                foreach (var offerId in offerIdsToProcess)
//                {
//                    await RunOfferAnalysisWorkflow(accessToken, offerId);
//                }
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Wystąpił nieoczekiwany błąd: {ex.Message}");
//            }
//        }

//        private async Task RunOfferAnalysisWorkflow(string accessToken, string offerId)
//        {
//            try
//            {
//                Console.WriteLine("\n--------------------------------------------------------------------------");
//                Console.WriteLine($"🚀 Analiza oferty ID: {offerId}");

//                var offerData = await GetOfferData(accessToken, offerId);
//                if (offerData == null) return;

//                DisplayCoreOfferInfo(offerData);
//                await CheckBadgeCampaigns(accessToken, offerId);
//                await CheckOfferCommission(accessToken, offerData);
//            }
//            catch (Exception ex)
//            {
//                Console.ForegroundColor = ConsoleColor.Red;
//                Console.WriteLine($"🔥 Wystąpił krytyczny błąd podczas przetwarzania oferty {offerId}: {ex.Message}");
//                Console.ResetColor();
//            }
//        }

//        private async Task<JsonNode?> GetOfferData(string accessToken, string offerId)
//        {
//            var apiUrl = $"https://api.allegro.pl/sale/product-offers/{offerId}";
//            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
//            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
//            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

//            var response = await _httpClient.SendAsync(request);

//            if (!response.IsSuccessStatusCode)
//            {
//                Console.ForegroundColor = ConsoleColor.Red;
//                Console.WriteLine($"❌ Nie udało się pobrać danych oferty {offerId}. Kod statusu: {response.StatusCode}");
//                Console.ResetColor();
//                Console.WriteLine($"Odpowiedź z serwera: {await response.Content.ReadAsStringAsync()}");
//                return null;
//            }

//            return JsonNode.Parse(await response.Content.ReadAsStringAsync());
//        }

//        private void DisplayCoreOfferInfo(JsonNode offerData)
//        {
//            Console.WriteLine("\n--- Podstawowe dane ---");
//            try
//            {
//                var currentPrice = offerData["sellingMode"]?["price"]?["amount"]?.ToString();
//                var currency = offerData["sellingMode"]?["price"]?["currency"]?.ToString();
//                Console.WriteLine($"  - Aktualna cena bazowa: {currentPrice} {currency}");

//                var parameters = offerData["productSet"]?[0]?["product"]?["parameters"]?.AsArray();
//                var ean = parameters?.FirstOrDefault(p => p?["id"]?.ToString() == "225693")?["values"]?[0]?.ToString() ?? "Brak";
//                Console.WriteLine($"  - Kod EAN/GTIN: {ean}");
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"  Nie udało się przetworzyć danych oferty: {ex.Message}");
//            }
//        }

//        private async Task CheckBadgeCampaigns(string accessToken, string offerId)
//        {
//            Console.WriteLine("\n--- Aktywne kampanie i oznaczenia ---");
//            var apiUrl = $"https://api.allegro.pl/sale/badges?offer.id={offerId}&marketplace.id=allegro-pl";
//            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
//            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
//            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

//            var response = await _httpClient.SendAsync(request);

//            if (!response.IsSuccessStatusCode)
//            {
//                Console.ForegroundColor = ConsoleColor.Red;
//                Console.WriteLine($"  ❌ Błąd podczas sprawdzania oznaczeń. Kod statusu: {response.StatusCode}");
//                Console.ResetColor();
//                return;
//            }

//            var badgesNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
//            var badgesArray = badgesNode?["badges"]?.AsArray();
//            if (badgesArray == null || badgesArray.Count == 0)
//            {
//                Console.WriteLine("  -> Oferta nie posiada żadnych oznaczeń.");
//                return;
//            }

//            bool anyActiveBadgesFound = false;
//            foreach (var badge in badgesArray)
//            {
//                var processStatus = badge?["process"]?["status"]?.ToString();
//                if (processStatus != "ACTIVE") continue;

//                anyActiveBadgesFound = true;
//                Console.ForegroundColor = ConsoleColor.Cyan;

//                var campaignName = badge?["campaign"]?["name"]?.ToString();
//                var targetPrice = badge?["prices"]?["subsidy"]?["targetPrice"]?["amount"]?.ToString();

//                Console.WriteLine($"  - Nazwa kampanii: {campaignName}");
//                if (!string.IsNullOrEmpty(targetPrice))
//                {
//                    Console.WriteLine($"    -> Cena docelowa dla klienta: {targetPrice} PLN");
//                }
//                Console.ResetColor();
//            }

//            if (!anyActiveBadgesFound)
//            {
//                Console.WriteLine("  -> Oferta nie uczestniczy w żadnej AKTYWNEJ kampanii z oznaczeniem.");
//            }
//        }

//        private async Task CheckOfferCommission(string accessToken, JsonNode offerData)
//        {
//            Console.WriteLine("\n--- Przewidywana prowizja ---");
//            var apiUrl = "https://api.allegro.pl/pricing/offer-fee-preview";
//            var payload = new { offer = offerData };
//            var httpContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.allegro.public.v1+json");

//            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = httpContent };
//            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
//            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

//            var response = await _httpClient.SendAsync(request);

//            if (!response.IsSuccessStatusCode)
//            {
//                Console.ForegroundColor = ConsoleColor.Red;
//                Console.WriteLine($"  ❌ Błąd podczas obliczania prowizji. Kod statusu: {response.StatusCode}");
//                Console.ResetColor();
//                return;
//            }

//            var feeNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
//            var commissionsArray = feeNode?["commissions"]?.AsArray();
//            if (commissionsArray == null || commissionsArray.Count == 0) return;

//            Console.ForegroundColor = ConsoleColor.Magenta;
//            var priceAmountString = offerData["sellingMode"]?["price"]?["amount"]?.ToString();

//            if (decimal.TryParse(priceAmountString, CultureInfo.InvariantCulture, out var priceDecimal) && priceDecimal > 0)
//            {
//                foreach (var fee in commissionsArray)
//                {
//                    var feeName = fee?["name"]?.ToString();
//                    var feeAmountString = fee?["fee"]?["amount"]?.ToString();
//                    var feeCurrency = fee?["fee"]?["currency"]?.ToString();
//                    if (decimal.TryParse(feeAmountString, CultureInfo.InvariantCulture, out var feeDecimal))
//                    {
//                        var rate = (feeDecimal / priceDecimal) * 100;
//                        Console.WriteLine($"  - {feeName}: {feeAmountString} {feeCurrency} (~{rate:F2}%)");
//                    }
//                    else
//                    {
//                        Console.WriteLine($"  - {feeName}: {feeAmountString} {feeCurrency}");
//                    }
//                }
//            }
//            Console.ResetColor();
//        }

//        private async Task<string?> GetAccessTokenWithRefreshToken(string refreshToken)
//        {
//            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CLIENT_ID}:{CLIENT_SECRET}"));

//            using var request = new HttpRequestMessage(HttpMethod.Post, "https://allegro.pl/auth/oauth/token");
//            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
//            request.Content = new FormUrlEncodedContent(new[]
//            {
//                new KeyValuePair<string, string>("grant_type", "refresh_token"),
//                new KeyValuePair<string, string>("refresh_token", refreshToken)
//            });

//            var response = await _httpClient.SendAsync(request);

//            if (!response.IsSuccessStatusCode)
//            {
//                if (File.Exists(REFRESH_TOKEN_FILE)) File.Delete(REFRESH_TOKEN_FILE);
//                return null;
//            }

//            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(await response.Content.ReadAsStringAsync());
//            if (tokenResponse?.refresh_token != null)
//            {
//                await File.WriteAllTextAsync(REFRESH_TOKEN_FILE, tokenResponse.refresh_token);
//            }
//            return tokenResponse?.access_token;
//        }

//        private async Task<string?> GetTokensWithDeviceCode()
//        {
//            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{CLIENT_ID}:{CLIENT_SECRET}"));

//            using var deviceAuthRequest = new HttpRequestMessage(HttpMethod.Post, "https://allegro.pl/auth/oauth/device");
//            deviceAuthRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
//            deviceAuthRequest.Content = new StringContent($"client_id={CLIENT_ID}", Encoding.UTF8, "application/x-www-form-urlencoded");

//            var deviceAuthResponse = await _httpClient.SendAsync(deviceAuthRequest);
//            if (!deviceAuthResponse.IsSuccessStatusCode) return null;

//            var deviceAuthData = JsonSerializer.Deserialize<DeviceAuthResponse>(await deviceAuthResponse.Content.ReadAsStringAsync());
//            if (deviceAuthData == null) return null;

//            Console.WriteLine("-----------------------------------------------------------------");
//            Console.WriteLine($"1. Otwórz w przeglądarce adres: {deviceAuthData.verification_uri}");
//            Console.WriteLine($"2. Wpisz poniższy kod: {deviceAuthData.user_code}");
//            Console.WriteLine("3. Po autoryzacji wróć do tego okna...");
//            Console.WriteLine("-----------------------------------------------------------------");

//            while (true)
//            {
//                await Task.Delay(TimeSpan.FromSeconds(deviceAuthData.interval));

//                using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://allegro.pl/auth/oauth/token");
//                tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
//                tokenRequest.Content = new StringContent($"grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code&device_code={deviceAuthData.device_code}", Encoding.UTF8, "application/x-www-form-urlencoded");

//                var tokenResponseMsg = await _httpClient.SendAsync(tokenRequest);

//                if (tokenResponseMsg.IsSuccessStatusCode)
//                {
//                    var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(await tokenResponseMsg.Content.ReadAsStringAsync());
//                    if (tokenResponse?.refresh_token != null)
//                    {
//                        await File.WriteAllTextAsync(REFRESH_TOKEN_FILE, tokenResponse.refresh_token);
//                    }
//                    return tokenResponse?.access_token;
//                }

//                string errorContent = await tokenResponseMsg.Content.ReadAsStringAsync();
//                if (!errorContent.Contains("authorization_pending")) return null;
//            }
//        }
//    }

//    public record DeviceAuthResponse(string device_code, string user_code, string verification_uri, int expires_in, int interval);
//    public record TokenResponse(string access_token, string? refresh_token, int expires_in);
//}







using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Models;

namespace PriceSafari.Services.AllegroServices
{
    /// <summary>
    /// Rekord pomocniczy do przechowywania wyników z API Allegro przed zapisem do bazy danych.
    /// </summary>
    public record AllegroApiData(
        decimal? BasePrice,       // Cena bazowa ustawiona przez użytkownika
        decimal? FinalPrice,      // Cena końcowa widoczna dla klienta (może być promocyjna)
        decimal? Commission,      // Przewidywana prowizja
        bool HasActivePromo       // Flaga informująca o aktywnej kampanii promocyjnej
    );

    /// <summary>
    /// Serwis odpowiedzialny za pobieranie dodatkowych danych o ofertach (ceny, prowizje, promocje)
    /// bezpośrednio z API Allegro.
    /// </summary>
    public class AllegroApiBotService
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<AllegroApiBotService> _logger;
        private static readonly HttpClient _httpClient = new();

        // UWAGA: Te dane powinny być przechowywane w bezpieczny sposób, np. w appsettings.json lub Azure Key Vault.
        private const string CLIENT_ID = "9163c502a1cb4c348579c5ff54c75df1";
        private const string CLIENT_SECRET = "ULlu7uecKMM1t1LpgrDe1D7MOTn0ABVVuHeKeQfGJ0Z80v9ojN4JyVqXOBLByQMZ";

        public AllegroApiBotService(PriceSafariContext context, ILogger<AllegroApiBotService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Główna metoda uruchamiająca proces. Wyszukuje sklepy z włączoną opcją
        /// i przetwarza dla nich oferty.
        /// </summary>
        public async Task ProcessOffersForActiveStoresAsync()
        {
            _logger.LogInformation("Rozpoczynam proces pobierania dodatkowych danych z API Allegro...");

            var activeStores = await _context.Stores
                .Where(s => s.OnAllegro && s.FetchExtendedAllegroData && s.IsAllegroTokenActive && !string.IsNullOrEmpty(s.AllegroApiToken))
                .AsNoTracking()
                .ToListAsync();

            if (!activeStores.Any())
            {
                _logger.LogInformation("Nie znaleziono aktywnych sklepów z włączoną opcją pobierania danych z API Allegro.");
                return;
            }

            _logger.LogInformation("Znaleziono {Count} sklepów do przetworzenia: {StoreNames}",
                activeStores.Count, string.Join(", ", activeStores.Select(s => s.StoreName)));

            foreach (var store in activeStores)
            {
                await ProcessOffersForSingleStore(store);
            }

            _logger.LogInformation("Zakończono proces pobierania dodatkowych danych z API Allegro.");
        }

        /// <summary>
        /// Przetwarza wszystkie oczekujące oferty dla pojedynczego sklepu.
        /// </summary>
        private async Task ProcessOffersForSingleStore(StoreClass store)
        {
            _logger.LogInformation("Przetwarzam sklep: {StoreName} (ID: {StoreId})", store.StoreName, store.StoreId);

            var offersToProcess = await _context.AllegroOffersToScrape
                .Where(o => o.StoreId == store.StoreId && o.IsScraped && o.IsApiProcessed != true)
                .ToListAsync();

            if (!offersToProcess.Any())
            {
                _logger.LogInformation("Brak nowych ofert do przetworzenia dla sklepu {StoreName}.", store.StoreName);
                return;
            }

            _logger.LogInformation("Znaleziono {Count} ofert do przetworzenia dla sklepu {StoreName}.", offersToProcess.Count, store.StoreName);

            string accessToken = store.AllegroApiToken!;

            foreach (var offer in offersToProcess)
            {
                var apiData = await FetchApiDataForOffer(accessToken, offer.AllegroOfferId.ToString());

                if (apiData != null)
                {
                    offer.ApiAllegroPriceFromUser = apiData.BasePrice;
                    offer.ApiAllegroPrice = apiData.FinalPrice;
                    offer.ApiAllegroCommission = apiData.Commission;
                    offer.AnyPromoActive = apiData.HasActivePromo;
                }

                // Oznaczamy jako przetworzone, aby uniknąć ponownego przetwarzania w przyszłości,
                // nawet jeśli pobranie danych z API się nie powiodło.
                offer.IsApiProcessed = true;
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Zakończono przetwarzanie i zapisano dane dla {Count} ofert dla sklepu {StoreName}.", offersToProcess.Count, store.StoreName);
        }

        /// <summary>
        /// Pobiera wszystkie potrzebne dane dla jednej oferty i zwraca je w postaci rekordu DTO.
        /// </summary>
        private async Task<AllegroApiData?> FetchApiDataForOffer(string accessToken, string offerId)
        {
            try
            {
                var offerDataNode = await GetOfferData(accessToken, offerId);
                if (offerDataNode == null) return null;

                // 1. Zawsze pobieraj cenę bazową (cena użytkownika)
                var basePriceString = offerDataNode["sellingMode"]?["price"]?["amount"]?.ToString();
                decimal.TryParse(basePriceString, CultureInfo.InvariantCulture, out var basePrice);
                decimal? parsedBasePrice = basePrice > 0 ? basePrice : null;

                // 2. Spróbuj znaleźć aktywną kampanię z ceną promocyjną
                var promoPrice = await GetActiveCampaignPrice(accessToken, offerId);

                // 3. Ustal cenę końcową dla klienta i status promocji
                decimal? finalPrice = promoPrice ?? parsedBasePrice;
                bool hasActivePromo = promoPrice.HasValue;

                // 4. Pobierz prowizję
                var commission = await GetOfferCommission(accessToken, offerDataNode);

                return new AllegroApiData(parsedBasePrice, finalPrice, commission, hasActivePromo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas pobierania danych API dla oferty {OfferId}", offerId);
                return null;
            }
        }

        /// <summary>
        /// Pobiera surowe dane oferty z endpointu /sale/product-offers/{offerId}.
        /// </summary>
        private async Task<JsonNode?> GetOfferData(string accessToken, string offerId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.allegro.pl/sale/product-offers/{offerId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Nie udało się pobrać danych oferty {OfferId}. Status: {StatusCode}. Odpowiedź: {Response}", offerId, response.StatusCode, await response.Content.ReadAsStringAsync());
                return null;
            }
            return JsonNode.Parse(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// Sprawdza aktywne kampanie dla oferty i zwraca cenę promocyjną, jeśli istnieje.
        /// </summary>
        private async Task<decimal?> GetActiveCampaignPrice(string accessToken, string offerId)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.allegro.pl/sale/badges?offer.id={offerId}&marketplace.id=allegro-pl");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var badgesNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var badgesArray = badgesNode?["badges"]?.AsArray();
            if (badgesArray == null) return null;

            foreach (var badge in badgesArray)
            {
                if (badge?["process"]?["status"]?.ToString() == "ACTIVE")
                {
                    var targetPriceString = badge?["prices"]?["subsidy"]?["targetPrice"]?["amount"]?.ToString();
                    if (decimal.TryParse(targetPriceString, CultureInfo.InvariantCulture, out var targetPrice) && targetPrice > 0)
                    {
                        return targetPrice; // Znaleziono aktywną kampanię z ceną - zwracamy ją
                    }
                }
            }

            return null; // Brak aktywnych kampanii z ceną docelową
        }

        /// <summary>
        /// Pobiera przewidywaną prowizję dla oferty.
        /// </summary>
        private async Task<decimal?> GetOfferCommission(string accessToken, JsonNode offerData)
        {
            var payload = new { offer = offerData };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/vnd.allegro.public.v1+json");
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.allegro.pl/pricing/offer-fee-preview") { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var feeNode = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var feeAmountString = feeNode?["commissions"]?[0]?["fee"]?["amount"]?.ToString();

            if (decimal.TryParse(feeAmountString, CultureInfo.InvariantCulture, out var feeDecimal))
            {
                return feeDecimal;
            }
            return null;
        }
    }
}