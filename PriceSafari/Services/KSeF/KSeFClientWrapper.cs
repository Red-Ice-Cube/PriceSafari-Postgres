using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PriceSafari.Models;

namespace PriceSafari.Services.KSeF
{
    public interface IKSeFClientWrapper
    {
        /// <summary>
        /// Uwierzytelnia się tokenem KSeF → zwraca true jeśli otrzymano accessToken JWT.
        /// </summary>
        Task<bool> AuthenticateAsync(CancellationToken ct);

        /// <summary>
        /// Wysyła zaszyfrowaną fakturę XML do KSeF.
        /// Zwraca (success, referenceNumber, error).
        /// </summary>
        Task<(bool Success, string ReferenceNumber, string? Error)>
            SendInvoiceAsync(string invoiceXml, CancellationToken ct);

        /// <summary>
        /// Sprawdza status przetwarzania faktury po referenceNumber.
        /// Zwraca (status, ksefNumber, error).
        /// </summary>
        Task<(KSeFExportStatus Status, string? KSeFNumber, string? Error)>
            CheckStatusAsync(string referenceNumber, CancellationToken ct);
    }

    /// <summary>
    /// Wrapper na KSeF 2.0 REST API.
    /// Obsługuje pełen flow: auth (token KSeF) → szyfrowanie AES-256-CBC → wysyłka → status.
    /// 
    /// Napisany jako bezpośredni HttpClient zamiast przez bibliotekę KSeF.Client,
    /// żeby mieć pełną kontrolę nad callami HTTP i łatwe debugowanie na środowisku Demo.
    /// Modele z biblioteki KSeF.Client.Core możesz wykorzystać do walidacji/typowania.
    /// </summary>
    public class KSeFClientWrapper : IKSeFClientWrapper
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<KSeFClientWrapper> _logger;

        private readonly string _baseUrl;
        private readonly string _nip;
        private readonly string _ksefToken;

        private string? _accessToken;
        private string? _refreshToken;
        private DateTime _tokenExpiresAt = DateTime.MinValue;

        // Klucz publiczny KSeF (cache na 1h)
        private byte[]? _ksefPublicKey;
        private DateTime _publicKeyCachedAt = DateTime.MinValue;

        public KSeFClientWrapper(HttpClient httpClient, ILogger<KSeFClientWrapper> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            _baseUrl = Environment.GetEnvironmentVariable("KSEF_BASE_URL")
                ?? throw new InvalidOperationException("Brak KSEF_BASE_URL");
            _nip = Environment.GetEnvironmentVariable("KSEF_NIP")
                ?? throw new InvalidOperationException("Brak KSEF_NIP");
            _ksefToken = Environment.GetEnvironmentVariable("KSEF_TOKEN")
                ?? throw new InvalidOperationException("Brak KSEF_TOKEN");

            // Upewnij się, że baseUrl nie kończy się na /
            _baseUrl = _baseUrl.TrimEnd('/');
        }

        // ===========================================================
        // UWIERZYTELNIANIE — Token KSeF
        // ===========================================================

        public async Task<bool> AuthenticateAsync(CancellationToken ct)
        {
            try
            {
                // Jeśli mamy jeszcze ważny token, nie logujemy się ponownie
                if (_accessToken != null && _tokenExpiresAt > DateTime.UtcNow.AddMinutes(2))
                {
                    _logger.LogDebug("Używam istniejącego accessToken (ważny do {exp})", _tokenExpiresAt);
                    return true;
                }

                _logger.LogInformation(">>> [KSeF AUTH] Rozpoczynam uwierzytelnianie tokenem KSeF...");

                // KROK 1: Pobierz challenge
                var challenge = await GetChallengeAsync(ct);
                if (string.IsNullOrEmpty(challenge))
                {
                    _logger.LogError("Nie udało się pobrać challenge z KSeF");
                    return false;
                }

                // KROK 2: Pobierz klucz publiczny KSeF (do szyfrowania tokena)
                await EnsurePublicKeyAsync(ct);
                if (_ksefPublicKey == null)
                {
                    _logger.LogError("Nie udało się pobrać klucza publicznego KSeF");
                    return false;
                }

                // KROK 3: Zaszyfruj "token|timestamp" kluczem publicznym
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var tokenPayload = $"{_ksefToken}|{timestamp}";
                var encryptedToken = EncryptWithRSA(tokenPayload, _ksefPublicKey);

                // KROK 4: Wyślij do /auth/ksef-token
                var authRequest = new
                {
                    contextIdentifier = new
                    {
                        type = "onip",
                        identifier = _nip
                    },
                    token = Convert.ToBase64String(encryptedToken)
                };

                var authResponse = await PostJsonAsync<AuthTokenResponse>(
                    $"{_baseUrl}/auth/ksef-token", authRequest, ct);

                if (authResponse == null || string.IsNullOrEmpty(authResponse.ReferenceNumber))
                {
                    _logger.LogError("Błąd auth/ksef-token — brak referenceNumber");
                    return false;
                }

                _logger.LogInformation($"Auth challenge OK. RefNum: {authResponse.ReferenceNumber}");

                // KROK 5: Sprawdź status uwierzytelniania (poll)
                var authStatus = await PollAuthStatusAsync(authResponse.ReferenceNumber, ct);
                if (!authStatus)
                {
                    _logger.LogError("Uwierzytelnianie nie powiodło się (status poll)");
                    return false;
                }

                // KROK 6: Wymień na accessToken JWT
                var tokenRedeemResponse = await PostJsonAsync<TokenRedeemResponse>(
                    $"{_baseUrl}/auth/token/redeem",
                    new { referenceNumber = authResponse.ReferenceNumber },
                    ct,
                    useAuth: false // jeszcze nie mamy JWT
                );

                if (tokenRedeemResponse == null || string.IsNullOrEmpty(tokenRedeemResponse.AccessToken))
                {
                    _logger.LogError("Nie udało się wymienić tokena na JWT (token/redeem)");
                    return false;
                }

                _accessToken = tokenRedeemResponse.AccessToken;
                _refreshToken = tokenRedeemResponse.RefreshToken;
                // JWT z KSeF ważny ~15 min
                _tokenExpiresAt = DateTime.UtcNow.AddMinutes(14);

                _logger.LogInformation("<<< [KSeF AUTH] Sukces! AccessToken JWT otrzymany.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wyjątek podczas uwierzytelniania w KSeF");
                return false;
            }
        }

        private async Task<string?> GetChallengeAsync(CancellationToken ct)
        {
            var challengeRequest = new
            {
                contextIdentifier = new
                {
                    type = "onip",
                    identifier = _nip
                }
            };

            var response = await PostJsonAsync<ChallengeResponse>(
                $"{_baseUrl}/auth/challenge", challengeRequest, ct);

            return response?.Challenge;
        }

        private async Task<bool> PollAuthStatusAsync(string referenceNumber, CancellationToken ct)
        {
            // Poll max 30 sekund (auth z tokenem KSeF jest szybki)
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(2000, ct);

                var response = await GetJsonAsync<AuthStatusResponse>(
                    $"{_baseUrl}/auth/{referenceNumber}", ct);

                if (response?.ProcessingCode == 200)
                {
                    _logger.LogDebug("Auth status: OK (200)");
                    return true;
                }

                if (response?.ProcessingCode >= 400)
                {
                    _logger.LogError($"Auth status error: {response.ProcessingCode} - {response.ProcessingDescription}");
                    return false;
                }

                _logger.LogDebug($"Auth status: {response?.ProcessingCode} — czekam...");
            }

            _logger.LogError("Auth status timeout po 30s");
            return false;
        }

        // ===========================================================
        // WYSYŁKA FAKTURY
        // ===========================================================

        public async Task<(bool Success, string ReferenceNumber, string? Error)>
            SendInvoiceAsync(string invoiceXml, CancellationToken ct)
        {
            try
            {
                // Upewnij się, że mamy ważny token
                if (_accessToken == null || _tokenExpiresAt <= DateTime.UtcNow.AddMinutes(1))
                {
                    var reAuth = await AuthenticateAsync(ct);
                    if (!reAuth)
                        return (false, "", "Nie udało się uwierzytelnić w KSeF");
                }

                // Szyfrowanie faktury jest OBOWIĄZKOWE w KSeF 2.0
                await EnsurePublicKeyAsync(ct);
                if (_ksefPublicKey == null)
                    return (false, "", "Brak klucza publicznego KSeF");

                // Generuj klucz AES-256 i IV
                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.GenerateKey();
                aes.GenerateIV();

                // Szyfruj XML faktury kluczem AES
                byte[] invoiceBytes = Encoding.UTF8.GetBytes(invoiceXml);
                byte[] encryptedInvoice;
                using (var encryptor = aes.CreateEncryptor())
                {
                    encryptedInvoice = encryptor.TransformFinalBlock(
                        invoiceBytes, 0, invoiceBytes.Length);
                }

                // Szyfruj klucz AES kluczem publicznym RSA KSeF (OAEP SHA-256)
                byte[] encryptedKey = EncryptWithRSA(aes.Key, _ksefPublicKey);

                // Buduj payload
                var sendRequest = new
                {
                    invoicePayload = new
                    {
                        type = "encrypted",
                        encryptedInvoiceHash = new
                        {
                            // SHA-256 oryginalnego (niezaszyfrowanego) XML-a
                            hashSHA = new
                            {
                                algorithm = "SHA-256",
                                encoding = "Base64",
                                value = Convert.ToBase64String(
                                    SHA256.HashData(invoiceBytes))
                            },
                            fileSize = invoiceBytes.Length
                        },
                        encryptedInvoiceBody = Convert.ToBase64String(encryptedInvoice),
                        encryptionKey = new
                        {
                            encryptionKeyBody = Convert.ToBase64String(encryptedKey),
                            encryptionInitializationVector = Convert.ToBase64String(aes.IV)
                        }
                    }
                };

                var response = await PostJsonAsync<InvoiceSendResponse>(
                    $"{_baseUrl}/invoices/send", sendRequest, ct, useAuth: true);

                if (response != null && !string.IsNullOrEmpty(response.ReferenceNumber))
                {
                    _logger.LogInformation($"Faktura wysłana do KSeF. RefNum: {response.ReferenceNumber}");
                    return (true, response.ReferenceNumber, null);
                }

                return (false, "", "Brak referenceNumber w odpowiedzi z /invoices/send");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Błąd HTTP przy wysyłce faktury do KSeF");
                return (false, "", $"HTTP Error: {ex.StatusCode} - {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wyjątek przy wysyłce faktury do KSeF");
                return (false, "", ex.Message);
            }
        }

        // ===========================================================
        // SPRAWDZANIE STATUSU
        // ===========================================================

        public async Task<(KSeFExportStatus Status, string? KSeFNumber, string? Error)>
            CheckStatusAsync(string referenceNumber, CancellationToken ct)
        {
            try
            {
                var response = await GetJsonAsync<InvoiceStatusResponse>(
                    $"{_baseUrl}/invoices/status/{referenceNumber}", ct, useAuth: true);

                if (response == null)
                    return (KSeFExportStatus.Pending, null, "Brak odpowiedzi ze statusu");

                // ProcessingCode: 200 = gotowe, 1xx = w trakcie, 4xx/5xx = błąd
                if (response.ProcessingCode == 200)
                {
                    return (KSeFExportStatus.Confirmed,
                        response.KsefReferenceNumber, null);
                }
                else if (response.ProcessingCode >= 400)
                {
                    return (KSeFExportStatus.Error, null,
                        $"KSeF error {response.ProcessingCode}: {response.ProcessingDescription}");
                }

                // Jeszcze w trakcie
                return (KSeFExportStatus.Pending, null, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Błąd sprawdzania statusu KSeF ref: {referenceNumber}");
                return (KSeFExportStatus.Pending, null, ex.Message);
            }
        }

        // ===========================================================
        // KLUCZ PUBLICZNY KSeF (do szyfrowania)
        // ===========================================================

        private async Task EnsurePublicKeyAsync(CancellationToken ct)
        {
            // Cache na 1 godzinę
            if (_ksefPublicKey != null && _publicKeyCachedAt.AddHours(1) > DateTime.UtcNow)
                return;

            try
            {
                var response = await GetJsonAsync<PublicKeyCertificatesResponse>(
                    $"{_baseUrl}/security/public-key-certificates", ct);

                if (response?.Items?.Any() == true)
                {
                    // Bierzemy pierwszy aktywny certyfikat
                    var cert = response.Items.First();
                    _ksefPublicKey = Convert.FromBase64String(cert.PublicKey);
                    _publicKeyCachedAt = DateTime.UtcNow;
                    _logger.LogDebug("Pobrano klucz publiczny KSeF (serial: {serial})", cert.SerialNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd pobierania klucza publicznego KSeF");
            }
        }

        // ===========================================================
        // SZYFROWANIE RSA (RSAES-OAEP SHA-256)
        // ===========================================================

        private byte[] EncryptWithRSA(string plainText, byte[] publicKeyDer)
        {
            return EncryptWithRSA(Encoding.UTF8.GetBytes(plainText), publicKeyDer);
        }

        private byte[] EncryptWithRSA(byte[] data, byte[] publicKeyDer)
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(publicKeyDer, out _);
            return rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
        }

        // ===========================================================
        // HELPER: HTTP GET/POST z JSON
        // ===========================================================

        private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct, bool useAuth = false)
            where T : class
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (useAuth && _accessToken != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning($"GET {url} → {response.StatusCode}: {Truncate(errorBody, 300)}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }

        private async Task<T?> PostJsonAsync<T>(string url, object body, CancellationToken ct,
            bool useAuth = false) where T : class
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(
                JsonSerializer.Serialize(body, _jsonOptions),
                Encoding.UTF8, "application/json");

            if (useAuth && _accessToken != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _httpClient.SendAsync(request, ct);

            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"POST {url} → {response.StatusCode}: {Truncate(json, 300)}");
                return null;
            }

            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s[..max] + "...";

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // ===========================================================
        // MODELE ODPOWIEDZI (DTO)
        // Dostosuj do rzeczywistych odpowiedzi API KSeF 2.0
        // Po pierwszych testach na Demo zobaczysz dokładny kształt JSON-ów
        // ===========================================================

        private class ChallengeResponse
        {
            [JsonPropertyName("challenge")]
            public string? Challenge { get; set; }

            [JsonPropertyName("timestamp")]
            public string? Timestamp { get; set; }
        }

        private class AuthTokenResponse
        {
            [JsonPropertyName("referenceNumber")]
            public string? ReferenceNumber { get; set; }

            [JsonPropertyName("authenticationToken")]
            public string? AuthenticationToken { get; set; }
        }

        private class AuthStatusResponse
        {
            [JsonPropertyName("processingCode")]
            public int ProcessingCode { get; set; }

            [JsonPropertyName("processingDescription")]
            public string? ProcessingDescription { get; set; }
        }

        private class TokenRedeemResponse
        {
            [JsonPropertyName("accessToken")]
            public string? AccessToken { get; set; }

            [JsonPropertyName("refreshToken")]
            public string? RefreshToken { get; set; }
        }

        private class InvoiceSendResponse
        {
            [JsonPropertyName("referenceNumber")]
            public string? ReferenceNumber { get; set; }
        }

        private class InvoiceStatusResponse
        {
            [JsonPropertyName("processingCode")]
            public int ProcessingCode { get; set; }

            [JsonPropertyName("processingDescription")]
            public string? ProcessingDescription { get; set; }

            [JsonPropertyName("ksefReferenceNumber")]
            public string? KsefReferenceNumber { get; set; }
        }

        private class PublicKeyCertificatesResponse
        {
            [JsonPropertyName("items")]
            public List<CertItem>? Items { get; set; }

            public class CertItem
            {
                [JsonPropertyName("publicKey")]
                public string PublicKey { get; set; } = "";

                [JsonPropertyName("serialNumber")]
                public string? SerialNumber { get; set; }
            }
        }
    }
}