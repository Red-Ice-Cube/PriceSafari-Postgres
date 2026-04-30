using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceSafari.Data;
using PriceSafari.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PriceSafari.Services.AllegroServices
{
    public class AllegroAuthTokenService
    {
        private readonly PriceSafariContext _context;
        private readonly HttpClient _httpClient;
        private readonly ILogger<AllegroAuthTokenService> _logger;

        private const int MAX_REFRESH_RETRIES = 3;
        private static readonly int[] RETRY_DELAYS_MS = { 2000, 5000, 10000 };

        /// <summary>
        /// Lock per storeId — zapobiega race condition gdy dwa procesy
        /// jednocześnie próbują odświeżyć token tego samego sklepu.
        /// Pierwszy wygrywa i zapisuje nowy token, drugi odczytuje go z bazy.
        /// </summary>
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _refreshLocks = new();

        /// <summary>
        /// Diagnostyka ostatniego odświeżenia — dołączana do logów operacji.
        /// </summary>
        public string? LastTokenDiagnostics { get; private set; }

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
            if (store == null)
            {
                LastTokenDiagnostics = "Store nie istnieje.";
                return null;
            }

            if (!store.IsAllegroTokenActive)
            {
                // ═══ Nie poddawaj się od razu — jeśli jest refresh token, spróbuj odświeżyć ═══
                if (!string.IsNullOrEmpty(store.AllegroRefreshToken))
                {
                    _logger.LogWarning(
                        "🔑 Token nieaktywny dla '{StoreName}', ale refresh token istnieje. Próbuję odświeżyć...",
                        store.StoreName);
                    return await RefreshTokenWithLockAsync(store);
                }

                // Brak refresh tokena — tu naprawdę nie ma co robić
                LastTokenDiagnostics = $"Token nieaktywny i BRAK refresh tokena dla '{store.StoreName}'.";
                _logger.LogError("🔑 {Diag}", LastTokenDiagnostics);
                return null;
            }

            // Token ważny — zwróć od razu
            if (!string.IsNullOrEmpty(store.AllegroApiToken) &&
                store.AllegroTokenExpiresAt.HasValue &&
                store.AllegroTokenExpiresAt.Value > DateTime.Now.AddMinutes(5))
            {
                var minutesLeft = (store.AllegroTokenExpiresAt.Value - DateTime.Now).TotalMinutes;
                LastTokenDiagnostics = $"Użyto istniejącego tokena (wygasa za {minutesLeft:F0} min).";
                return store.AllegroApiToken;
            }

            _logger.LogInformation("🔑 Token sklepu '{StoreName}' wygasł (expiry: {Expiry}). Odświeżam...",
                store.StoreName, store.AllegroTokenExpiresAt?.ToString("dd.MM HH:mm") ?? "brak");

            return await RefreshTokenWithLockAsync(store);
        }

        public async Task<string?> ForceRefreshTokenAsync(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                LastTokenDiagnostics = "Store nie istnieje (ForceRefresh).";
                return null;
            }

            _logger.LogWarning("🔑 WYMUSZANIE odświeżenia tokena dla '{StoreName}' (401 mimo ważnej daty).", store.StoreName);
            return await RefreshTokenWithLockAsync(store);
        }

        /// <summary>
        /// Wrapper z lockiem per storeId.
        /// Gdy dwa wątki/procesy jednocześnie próbują odświeżyć token tego samego sklepu:
        /// - Pierwszy wchodzi, odświeża, zapisuje nowy token do bazy
        /// - Drugi czeka, potem sprawdza bazę — widzi świeży token, zwraca go bez ponownego refresha
        /// </summary>
        private async Task<string?> RefreshTokenWithLockAsync(StoreClass store)
        {
            var semaphore = _refreshLocks.GetOrAdd(store.StoreId, _ => new SemaphoreSlim(1, 1));

            // Czekamy max 30 sekund na lock — jeśli inny wątek odświeża, poczekamy
            bool acquired = await semaphore.WaitAsync(TimeSpan.FromSeconds(30));

            if (!acquired)
            {
                // Lock timeout — inny wątek odświeża zbyt długo, spróbujmy odczytać z bazy
                _logger.LogWarning("🔑 Lock timeout (30s) dla sklepu '{StoreName}'. Sprawdzam bazę...", store.StoreName);
                await _context.Entry(store).ReloadAsync();

                if (!string.IsNullOrEmpty(store.AllegroApiToken) &&
                    store.AllegroTokenExpiresAt.HasValue &&
                    store.AllegroTokenExpiresAt.Value > DateTime.Now.AddMinutes(2))
                {
                    LastTokenDiagnostics = $"Token odświeżony przez inny wątek (lock timeout, ale token OK).";
                    return store.AllegroApiToken;
                }

                LastTokenDiagnostics = $"Lock timeout i brak ważnego tokena w bazie.";
                return null;
            }

            try
            {
                // ═══ Double-check po uzyskaniu locka ═══
                // Inny wątek mógł odświeżyć token zanim my dostaliśmy lock
                await _context.Entry(store).ReloadAsync();

                if (!string.IsNullOrEmpty(store.AllegroApiToken) &&
                    store.AllegroTokenExpiresAt.HasValue &&
                    store.AllegroTokenExpiresAt.Value > DateTime.Now.AddMinutes(5))
                {
                    var minutesLeft = (store.AllegroTokenExpiresAt.Value - DateTime.Now).TotalMinutes;
                    LastTokenDiagnostics = $"Token odświeżony przez inny wątek (wygasa za {minutesLeft:F0} min). Pomijam refresh.";
                    _logger.LogInformation("🔑 {Diag}", LastTokenDiagnostics);
                    return store.AllegroApiToken;
                }

                // Nikt nie odświeżył — robimy to my
                return await RefreshTokenForStoreAsync(store);
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task<string?> RefreshTokenForStoreAsync(StoreClass store)
        {
            if (string.IsNullOrEmpty(store.AllegroRefreshToken))
            {
                LastTokenDiagnostics = $"Brak refresh tokena w bazie dla '{store.StoreName}'. Dezaktywuję.";
                _logger.LogError("🔑 {Diag}", LastTokenDiagnostics);
                store.IsAllegroTokenActive = false;
                await _context.SaveChangesAsync();
                return null;
            }

            // Czyszczenie tokena
            string cleanedRefreshToken = CleanToken(store.AllegroRefreshToken);
            bool wasCleaned = (cleanedRefreshToken != store.AllegroRefreshToken);

            if (wasCleaned)
            {
                _logger.LogWarning("🔑 Refresh token '{StoreName}' zawierał białe znaki (oryg: {OldLen} → {NewLen}). Wyczyszczono.",
                    store.StoreName, store.AllegroRefreshToken.Length, cleanedRefreshToken.Length);
                store.AllegroRefreshToken = cleanedRefreshToken;
                await _context.SaveChangesAsync();
            }

            var clientId = Environment.GetEnvironmentVariable("ALLEGRO_CLIENT_ID")?.Trim();
            var clientSecret = Environment.GetEnvironmentVariable("ALLEGRO_CLIENT_SECRET")?.Trim();

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                LastTokenDiagnostics = "Brak ALLEGRO_CLIENT_ID lub ALLEGRO_CLIENT_SECRET w env vars.";
                _logger.LogError("🔑 {Diag}", LastTokenDiagnostics);
                return null;
            }

            _logger.LogInformation(
                "🔑 Refresh attempt: store='{StoreName}', clientId_len={IdLen}, secret_len={SecLen}, refreshToken_len={RefLen}, cleaned={Cleaned}",
                store.StoreName, clientId.Length, clientSecret.Length, cleanedRefreshToken.Length, wasCleaned);

            var authHeaderValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

            // ═══ Retry loop ═══
            for (int attempt = 0; attempt < MAX_REFRESH_RETRIES; attempt++)
            {
                if (attempt > 0)
                {
                    int delayMs = RETRY_DELAYS_MS[Math.Min(attempt - 1, RETRY_DELAYS_MS.Length - 1)];
                    _logger.LogWarning("🔑 Retry {Attempt}/{Max} za {Delay}ms (sklep '{StoreName}')...",
                        attempt + 1, MAX_REFRESH_RETRIES, delayMs, store.StoreName);
                    await Task.Delay(delayMs);

                    // Re-read z bazy przy retry (inny proces mógł odświeżyć w międzyczasie)
                    await _context.Entry(store).ReloadAsync();

                    // Sprawdź czy ktoś inny zdążył odświeżyć
                    if (!string.IsNullOrEmpty(store.AllegroApiToken) &&
                        store.AllegroTokenExpiresAt.HasValue &&
                        store.AllegroTokenExpiresAt.Value > DateTime.Now.AddMinutes(5))
                    {
                        LastTokenDiagnostics = $"Token odświeżony przez inny proces podczas retry (próba {attempt + 1}).";
                        _logger.LogInformation("🔑 {Diag}", LastTokenDiagnostics);
                        return store.AllegroApiToken;
                    }

                    cleanedRefreshToken = CleanToken(store.AllegroRefreshToken ?? "");

                    if (string.IsNullOrEmpty(cleanedRefreshToken))
                    {
                        LastTokenDiagnostics = $"Refresh token pusty po reload (próba {attempt + 1}).";
                        _logger.LogError("🔑 {Diag}", LastTokenDiagnostics);
                        return null;
                    }
                }

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "https://allegro.pl/auth/oauth/token");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeaderValue);
                    request.Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("grant_type", "refresh_token"),
                        new KeyValuePair<string, string>("refresh_token", cleanedRefreshToken)
                    });

                    var response = await _httpClient.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        var statusCode = (int)response.StatusCode;

                        _logger.LogError("🔑 Refresh FAILED: HTTP {StatusCode} (próba {Attempt}/{Max}). Body: {ErrorBody}",
                            statusCode, attempt + 1, MAX_REFRESH_RETRIES, errorContent);

                        // 5xx → retry (Allegro ma problem)
                        if (statusCode >= 500)
                        {
                            LastTokenDiagnostics = $"Allegro zwróciło {statusCode} (próba {attempt + 1}/{MAX_REFRESH_RETRIES}). Body: {Truncate(errorContent, 200)}";
                            continue;
                        }

                        // 400/401 → token martwy
                        if (response.StatusCode == HttpStatusCode.BadRequest ||
                            response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            LastTokenDiagnostics = $"Refresh token MARTWY ({statusCode}). Body: {Truncate(errorContent, 200)}. Dezaktywuję.";
                            _logger.LogError("🔑 {Diag}", LastTokenDiagnostics);
                            store.IsAllegroTokenActive = false;
                            await _context.SaveChangesAsync();
                            return null;
                        }

                        LastTokenDiagnostics = $"Nieoczekiwany status {statusCode}. Body: {Truncate(errorContent, 200)}";
                        continue;
                    }

                    // ═══ Sukces ═══
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var tokenData = JsonSerializer.Deserialize<AllegroTokenResponse>(jsonResponse);

                    if (tokenData == null || string.IsNullOrEmpty(tokenData.access_token))
                    {
                        LastTokenDiagnostics = "Allegro zwróciło 200 ale brak access_token.";
                        _logger.LogError("🔑 {Diag}", LastTokenDiagnostics);
                        continue;
                    }

                    store.AllegroApiToken = tokenData.access_token;

                    bool gotNewRefreshToken = !string.IsNullOrEmpty(tokenData.refresh_token);
                    if (gotNewRefreshToken)
                    {
                        store.AllegroRefreshToken = CleanToken(tokenData.refresh_token);
                    }

                    store.AllegroTokenExpiresAt = DateTime.Now.AddSeconds(tokenData.expires_in);
                    store.IsAllegroTokenActive = true;
                    await _context.SaveChangesAsync();

                    LastTokenDiagnostics = $"✅ Odświeżono (próba {attempt + 1}). Nowy refresh: {(gotNewRefreshToken ? "TAK" : "NIE")}. Wygasa: {store.AllegroTokenExpiresAt?.ToString("dd.MM HH:mm")}.";
                    _logger.LogInformation("🔑 {Diag}", LastTokenDiagnostics);

                    return tokenData.access_token;
                }
                catch (TaskCanceledException)
                {
                    LastTokenDiagnostics = $"Timeout (próba {attempt + 1}/{MAX_REFRESH_RETRIES}).";
                    _logger.LogWarning("🔑 {Diag}", LastTokenDiagnostics);
                    if (attempt == MAX_REFRESH_RETRIES - 1) return null;
                }
                catch (HttpRequestException ex)
                {
                    LastTokenDiagnostics = $"Błąd sieci: {ex.Message} (próba {attempt + 1}/{MAX_REFRESH_RETRIES}).";
                    _logger.LogWarning("🔑 {Diag}", LastTokenDiagnostics);
                    if (attempt == MAX_REFRESH_RETRIES - 1) return null;
                }
                catch (Exception ex)
                {
                    LastTokenDiagnostics = $"Wyjątek: {ex.Message} (próba {attempt + 1}/{MAX_REFRESH_RETRIES}).";
                    _logger.LogError(ex, "🔑 Wyjątek podczas odświeżania tokena.");
                    if (attempt == MAX_REFRESH_RETRIES - 1) return null;
                }
            }

            LastTokenDiagnostics = $"❌ Wszystkie {MAX_REFRESH_RETRIES} prób nieudane dla '{store.StoreName}'.";
            _logger.LogError("🔑 {Diag}", LastTokenDiagnostics);
            return null;
        }

        private static string CleanToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return token;
            return token
                .Replace("\r", "").Replace("\n", "").Replace("\t", "")
                .Replace(" ", "").Replace("\u200B", "").Replace("\uFEFF", "")
                .Trim();
        }

        private static string Truncate(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLen) return text ?? "";
            return text.Substring(0, maxLen) + "...";
        }

        private class AllegroTokenResponse
        {
            public string access_token { get; set; }
            public string refresh_token { get; set; }
            public int expires_in { get; set; }
        }
    }
}