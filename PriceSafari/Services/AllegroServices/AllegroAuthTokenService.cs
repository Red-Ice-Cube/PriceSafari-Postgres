using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PriceSafari.Services.AllegroServices
{
    public class AllegroAuthTokenService
    {
        private readonly PriceSafariContext _context;
        private readonly HttpClient _httpClient;
        private readonly ILogger<AllegroAuthTokenService> _logger;

        public AllegroAuthTokenService(
            PriceSafariContext context,
            HttpClient httpClient,
            ILogger<AllegroAuthTokenService> logger)
        {
            _context = context;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string?> GetValidAccessTokenAsync(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return null;

            if (!store.IsAllegroTokenActive) return null;

            // Zwykłe sprawdzenie daty
            if (!string.IsNullOrEmpty(store.AllegroApiToken) &&
                store.AllegroTokenExpiresAt.HasValue &&
                store.AllegroTokenExpiresAt.Value > DateTime.Now.AddMinutes(5))
            {
                return store.AllegroApiToken;
            }

            _logger.LogInformation($"Token dla sklepu '{store.StoreName}' wygasł wg daty. Odświeżam...");
            return await RefreshTokenForStoreAsync(store);
        }

        // --- NOWA METODA: Wymuszenie odświeżenia (nawet jak data jest OK) ---
        public async Task<string?> ForceRefreshTokenAsync(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return null;

            _logger.LogWarning($"WYMUSZANIE odświeżenia tokena dla sklepu '{store.StoreName}' (otrzymano 401 mimo ważnej daty).");
            return await RefreshTokenForStoreAsync(store);
        }

        private async Task<string?> RefreshTokenForStoreAsync(StoreClass store)
        {
            if (string.IsNullOrEmpty(store.AllegroRefreshToken))
            {
                _logger.LogError($"Sklep '{store.StoreName}' nie posiada Refresh Tokena.");
                store.IsAllegroTokenActive = false;
                await _context.SaveChangesAsync();
                return null;
            }

            var clientId = Environment.GetEnvironmentVariable("ALLEGRO_CLIENT_ID");
            var clientSecret = Environment.GetEnvironmentVariable("ALLEGRO_CLIENT_SECRET");

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                _logger.LogError("Brak kluczy ALLEGRO w zmiennych środowiskowych.");
                return null;
            }

            var requestUrl = "https://allegro.pl/auth/oauth/token";
            var authHeaderValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeaderValue);

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", store.AllegroRefreshToken)
            });
            request.Content = content;

            try
            {
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Błąd odświeżania tokena: {response.StatusCode}. {errorContent}");

                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
                        response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        store.IsAllegroTokenActive = false;
                        await _context.SaveChangesAsync();
                    }
                    return null;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var tokenData = JsonSerializer.Deserialize<AllegroTokenResponse>(jsonResponse);

                if (tokenData != null)
                {
                    store.AllegroApiToken = tokenData.access_token;
                    if (!string.IsNullOrEmpty(tokenData.refresh_token))
                    {
                        store.AllegroRefreshToken = tokenData.refresh_token;
                    }
                    store.AllegroTokenExpiresAt = DateTime.Now.AddSeconds(tokenData.expires_in);
                    store.IsAllegroTokenActive = true;

                    await _context.SaveChangesAsync();
                    return tokenData.access_token;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wyjątek podczas odświeżania tokena.");
            }

            return null;
        }

        private class AllegroTokenResponse
        {
            public string access_token { get; set; }
            public string refresh_token { get; set; }
            public int expires_in { get; set; }
        }
    }
}