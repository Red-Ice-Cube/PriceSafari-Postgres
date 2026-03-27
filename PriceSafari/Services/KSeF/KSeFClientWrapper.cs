using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PriceSafari.Models;

namespace PriceSafari.Services.KSeF
{
    public interface IKSeFClientWrapper
    {
        Task<bool> AuthenticateAsync(CancellationToken ct);
        Task<(bool Success, string ReferenceNumber, string? Error)>
            SendInvoiceAsync(string invoiceXml, CancellationToken ct);
        Task<(KSeFExportStatus Status, string? KSeFNumber, string? Error)>
            CheckStatusAsync(string referenceNumber, CancellationToken ct);
    }

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

        private RSA? _tokenEncryptionKey;
        private RSA? _symmetricEncryptionKey;
        private DateTime _certsCachedAt = DateTime.MinValue;

        public KSeFClientWrapper(HttpClient httpClient, ILogger<KSeFClientWrapper> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            _baseUrl = (Environment.GetEnvironmentVariable("KSEF_BASE_URL")
                ?? throw new InvalidOperationException("Brak KSEF_BASE_URL")).TrimEnd('/');
            _nip = Environment.GetEnvironmentVariable("KSEF_NIP")
                ?? throw new InvalidOperationException("Brak KSEF_NIP");
            _ksefToken = Environment.GetEnvironmentVariable("KSEF_TOKEN")
                ?? throw new InvalidOperationException("Brak KSEF_TOKEN");
        }

        public async Task<bool> AuthenticateAsync(CancellationToken ct)
        {
            try
            {
                if (_accessToken != null && _tokenExpiresAt > DateTime.UtcNow.AddMinutes(2))
                    return true;

                _logger.LogInformation(">>> [KSeF AUTH] Rozpoczynam uwierzytelnianie tokenem KSeF...");

                // 1. Challenge
                var challengeResp = await PostJsonAsync<ChallengeResponse>(
                    $"{_baseUrl}/auth/challenge",
                    new { contextIdentifier = new { type = "Nip", value = _nip } }, ct); // Zmieniono na poprawny format KSeF 2.0

                if (challengeResp == null || string.IsNullOrEmpty(challengeResp.Challenge))
                {
                    _logger.LogError("Nie udało się pobrać challenge");
                    return false;
                }

                _logger.LogInformation("Challenge OK (timestampMs: {ts})", challengeResp.TimestampMs);

                // 2. Certyfikaty
                await EnsureCertificatesAsync(ct);
                if (_tokenEncryptionKey == null)
                {
                    _logger.LogError("Brak certyfikatu KsefTokenEncryption");
                    return false;
                }

                // 3. Szyfruj token|timestampMs (KSeF 2.0 bezwzględnie wymaga TimestampMs jako liczby!)
                var tokenPayload = $"{_ksefToken}|{challengeResp.TimestampMs}";
                var encryptedToken = _tokenEncryptionKey.Encrypt(
                    Encoding.UTF8.GetBytes(tokenPayload), RSAEncryptionPadding.OaepSHA256);

                // 4. POST /auth/ksef-token
                var authResp = await PostJsonAsync<AuthTokenResponse>(
                    $"{_baseUrl}/auth/ksef-token",
                    new
                    {
                        challenge = challengeResp.Challenge,
                        contextIdentifier = new { type = "Nip", value = _nip }, // KSeF 2.0 jest czuły na wielkość liter ("Nip")
                        encryptedToken = Convert.ToBase64String(encryptedToken)
                    }, ct);

                if (authResp?.AuthenticationToken?.Token == null || string.IsNullOrEmpty(authResp.ReferenceNumber))
                {
                    _logger.LogError("Błąd logowania. Brak OperationToken lub ReferenceNumber z /auth/ksef-token.");
                    return false;
                }

                // Ustawiamy tymczasowo OperationToken jako główny token, 
                // bo jest on potrzebny w nagłówku Bearer do odpytania o status i zrobienia Redeem
                _accessToken = authResp.AuthenticationToken.Token;
                var authRef = authResp.ReferenceNumber;

                _logger.LogInformation("Otrzymano OperationToken. Polling statusu {ref}...", authRef);

                // 5. Polling statusu uwierzytelniania (teraz z użyciem OperationToken)
                if (!await PollAuthStatusAsync(authRef, ct))
                {
                    _accessToken = null; // Czyścimy token w razie błędu
                    return false;
                }

                _logger.LogInformation("Status 200 OK! Pobieram właściwy AccessToken (Redeem)...");

                // 6. POST /auth/token/redeem (Wymiana na pełnoprawny token)
                var redeem = await PostJsonAsync<TokenRedeemResponse>(
                    $"{_baseUrl}/auth/token/redeem",
                    new { }, // W KSeF 2.0 endpoint redeem nie wymaga wysyłania referenceNumber w ciele żądania
                    ct,
                    useAuth: true); // Kluczowe: wysyłamy żądanie podbite OperationTokenem

                if (!string.IsNullOrEmpty(redeem?.AccessToken?.Token))
                {
                    _accessToken = redeem.AccessToken.Token; // Nadpisujemy token docelowym AccessTokenem!
                    _refreshToken = redeem.RefreshToken?.Token;

                    _tokenExpiresAt = DateTime.TryParse(redeem.AccessToken.ValidUntil, out var exp)
                        ? exp.ToUniversalTime()
                        : DateTime.UtcNow.AddMinutes(115);

                    _logger.LogInformation("<<< [KSeF AUTH] SUKCES! Właściwy token JWT pobrany (ważny do {v}).", redeem.AccessToken.ValidUntil);
                    return true;
                }

                _logger.LogError("Redeem się nie powiódł, brak docelowego AccessToken.");
                _accessToken = null;
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wyjątek auth KSeF");
                _accessToken = null;
                return false;
            }
        }
        private async Task<bool> PollAuthStatusAsync(string refNum, CancellationToken ct)
        {
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(2000, ct);

                // Pamiętaj o useAuth: true (jest to wymagane w v2)
                var r = await GetJsonAsync<AuthStatusResponse>($"{_baseUrl}/auth/{refNum}", ct, useAuth: true);

                int code = r?.Status?.Code ?? 0;

                if (code == 200)
                {
                    _logger.LogInformation("Auth poll success (Code 200).");
                    return true;
                }

                if (code >= 400)
                {
                    _logger.LogError("Auth poll error: {c} {d}", code, r?.Status?.Description);
                    return false;
                }

                _logger.LogInformation("Auth poll in progress... (Code: {c})", code);
            }

            _logger.LogError("Auth poll timeout 30s");
            return false;
        }

        // ===========================================================
        // CERTYFIKATY X.509
        // Odpowiedź z /security/public-key-certificates to TABLICA:
        // [{"certificate":"MIIG...","validFrom":"...","validTo":"...","usage":["KsefTokenEncryption"]}]
        // ===========================================================
        private async Task EnsureCertificatesAsync(CancellationToken ct)
        {
            if (_tokenEncryptionKey != null && _certsCachedAt.AddHours(1) > DateTime.UtcNow)
                return;

            try
            {
                var raw = await GetRawStringAsync($"{_baseUrl}/security/public-key-certificates", ct);
                if (string.IsNullOrEmpty(raw)) return;

                var certs = JsonSerializer.Deserialize<List<KSeFCertificate>>(raw, _jsonOptions);
                if (certs == null || !certs.Any()) return;

                _logger.LogInformation("Pobrano {n} certyfikatów KSeF", certs.Count);

                foreach (var c in certs)
                {
                    var usages = c.Usage ?? new();
                    var x509 = new X509Certificate2(Convert.FromBase64String(c.Certificate));
                    var rsa = x509.GetRSAPublicKey();
                    if (rsa == null) continue;

                    if (usages.Contains("KsefTokenEncryption"))
                    {
                        _tokenEncryptionKey = rsa;
                        _logger.LogInformation("Cert KsefTokenEncryption OK (do {v})", c.ValidTo);
                    }
                    if (usages.Contains("SymmetricKeyEncryption"))
                    {
                        _symmetricEncryptionKey = rsa;
                        _logger.LogInformation("Cert SymmetricKeyEncryption OK (do {v})", c.ValidTo);
                    }
                }
                _certsCachedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd certyfikatów KSeF");
            }
        }

        // ===========================================================
        // WYSYŁKA FAKTURY — przez sesję online
        // Flow: POST /sessions/online → POST /sessions/online/{ref}/invoices/ → close
        // ===========================================================
        public async Task<(bool Success, string ReferenceNumber, string? Error)>
            SendInvoiceAsync(string invoiceXml, CancellationToken ct)
        {
            try
            {
                if (_accessToken == null || _tokenExpiresAt <= DateTime.UtcNow.AddMinutes(1))
                    if (!await AuthenticateAsync(ct))
                        return (false, "", "Auth failed");

                await EnsureCertificatesAsync(ct);
                if (_symmetricEncryptionKey == null)
                    return (false, "", "Brak certyfikatu SymmetricKeyEncryption");

                // --- Generuj klucz AES-256 dla sesji ---
                using var aes = Aes.Create();
                aes.KeySize = 256; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                aes.GenerateKey(); aes.GenerateIV();

                // Szyfruj klucz AES certyfikatem SymmetricKeyEncryption
                byte[] encKey = _symmetricEncryptionKey.Encrypt(aes.Key, RSAEncryptionPadding.OaepSHA256);

                // --- KROK 1: Otwórz sesję online ---
                _logger.LogInformation("[KSeF] Otwieram sesję online...");

                var sessionResp = await PostJsonAsync<SessionResponse>(
                    $"{_baseUrl}/sessions/online",
                    new
                    {
                        formCode = new
                        {
                            systemCode = "FA (3)",
                            schemaVersion = "1-0E",
                            value = "FA" // <--- Zamiast targetNamespace używamy pola "value"
                        },
                        encryption = new
                        {
                            encryptedSymmetricKey = Convert.ToBase64String(encKey), // <--- Zmieniono nazwę
                            initializationVector = Convert.ToBase64String(aes.IV)   // <--- Zmieniono nazwę
                        }
                    }, ct, useAuth: true);

                if (sessionResp == null || string.IsNullOrEmpty(sessionResp.ReferenceNumber))
                {
                    _logger.LogError("[KSeF] Nie udało się otworzyć sesji online");
                    return (false, "", "Nie udało się otworzyć sesji online");
                }

                var sessionRef = sessionResp.ReferenceNumber;
                _logger.LogInformation("[KSeF] Sesja otwarta! Ref: {ref}", sessionRef);

                // --- KROK 2: Szyfruj i wyślij fakturę ---
                byte[] plain = Encoding.UTF8.GetBytes(invoiceXml);
                byte[] encrypted;
                using (var enc = aes.CreateEncryptor())
                    encrypted = enc.TransformFinalBlock(plain, 0, plain.Length);

                var invoicePayload = new
                {
                    invoiceHash = Convert.ToBase64String(SHA256.HashData(plain)),
                    invoiceSize = plain.Length,
                    encryptedInvoiceHash = Convert.ToBase64String(SHA256.HashData(encrypted)),
                    encryptedInvoiceSize = encrypted.Length,
                    encryptedInvoiceContent = Convert.ToBase64String(encrypted),
                    offlineMode = false
                };

                _logger.LogInformation("[KSeF] Wysyłam fakturę w sesji {ref}...", sessionRef);

                var invoiceResp = await PostJsonAsync<InvoiceSendResponse>(
                    $"{_baseUrl}/sessions/online/{sessionRef}/invoices",
                    invoicePayload, ct, useAuth: true);

                string? invoiceRefNumber = invoiceResp?.ReferenceNumber;

                if (string.IsNullOrEmpty(invoiceRefNumber))
                {
                    _logger.LogWarning("[KSeF] Brak referenceNumber faktury, ale sesja aktywna");
                }
                else
                {
                    _logger.LogInformation("[KSeF] Faktura wysłana! InvoiceRef: {ir}", invoiceRefNumber);
                }

                _logger.LogInformation("[KSeF] Zamykam sesję {ref}...", sessionRef);

                await PostJsonAsync<SessionResponse>(
                    $"{_baseUrl}/sessions/online/{sessionRef}/close",
                    new { }, ct, useAuth: true);

                _logger.LogInformation("[KSeF] Sesja zamknięta.");

                // NOWE: Sklejamy obie referencje znakiem "|", by zachować je w bazie w jednym stringu!
                var resultRef = $"{sessionRef}|{invoiceRefNumber}";
                return (true, resultRef, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wyjątek wysyłki faktury (sesja)");
                return (false, "", ex.Message);
            }
        }

        public async Task<(KSeFExportStatus Status, string? KSeFNumber, string? Error)> CheckStatusAsync(string referenceNumber, CancellationToken ct)
        {
            try
            {
                // 1. Rozpakowanie obu referencji
                var parts = referenceNumber.Split('|');
                if (parts.Length != 2)
                {
                    // Opcjonalne zabezpieczenie, jeśli utknęła Ci w bazie stara faktura z samym InvoiceRef
                    return (KSeFExportStatus.Error, null, $"Zły format ReferenceNumber. Wymagane SessionRef|InvoiceRef, jest: {referenceNumber}");
                }

                string sessionRef = parts[0];
                string invoiceRef = parts[1];

                // 2. Nowy endpoint KSeF 2.0!
                var url = $"{_baseUrl}/sessions/{sessionRef}/invoices/{invoiceRef}";

                var r = await GetJsonAsync<InvoiceStatusResponse>(url, ct, useAuth: true);
                if (r == null) return (KSeFExportStatus.Pending, null, "Brak odpowiedzi (404/Pusta)");

                // 3. Wyciągnięcie kodu KSeF 2.0
                int code = r.Status?.Code ?? r.ProcessingCode;
                string? desc = r.Status?.Description ?? r.ProcessingDescription;

                if (code == 200)
                    return (KSeFExportStatus.Confirmed, r.KsefReferenceNumber, null);

                if (code >= 400)
                {
                    // Wyciągamy szczegóły błędu
                    string exactError = r.Status?.Details != null && r.Status.Details.Any()
                        ? string.Join(" | ", r.Status.Details)
                        : desc ?? "Błąd KSeF";

                    return (KSeFExportStatus.Error, null, $"{code}: {exactError}");
                }

                // Kody 100-399 oznaczają, że faktura wciąż weryfikuje się w urzędzie
                return (KSeFExportStatus.Pending, null, null);
            }
            catch (Exception ex)
            {
                return (KSeFExportStatus.Pending, null, ex.Message);
            }
        }

        // ===========================================================
        // HTTP
        // ===========================================================
        private async Task<string?> GetRawStringAsync(string url, CancellationToken ct, bool useAuth = false)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (useAuth && _accessToken != null)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            var resp = await _httpClient.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("GET {u} → {c}: {b}", url, resp.StatusCode, Trunc(body));
            return resp.IsSuccessStatusCode ? body : null;
        }

        private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct, bool useAuth = false) where T : class
        {
            var body = await GetRawStringAsync(url, ct, useAuth);
            if (body == null) return null;
            try { return JsonSerializer.Deserialize<T>(body, _jsonOptions); }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON err GET {u}: {b}", url, Trunc(body));
                return null;
            }
        }

        private async Task<T?> PostJsonAsync<T>(string url, object payload, CancellationToken ct, bool useAuth = false) where T : class
        {
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };

            if (useAuth && _accessToken != null)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var resp = await _httpClient.SendAsync(req, ct);

            // Jeśli status to 204 No Content, od razu zwracamy null, żeby nie parsować pustego stringa
            if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("POST {u} → {c}: {b}", url, resp.StatusCode, Trunc(body));

            if (string.IsNullOrWhiteSpace(body)) return null;

            try { return JsonSerializer.Deserialize<T>(body, _jsonOptions); }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON err POST {u}: {b}", url, Trunc(body));
                return null;
            }
        }

        private static string Trunc(string s, int max = 500) => s.Length <= max ? s : s[..max] + "...";

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // ===========================================================
        // DTO
        // ===========================================================
        private class KSeFCertificate
        {
            [JsonPropertyName("certificate")] public string Certificate { get; set; } = "";
            [JsonPropertyName("validFrom")] public string? ValidFrom { get; set; }
            [JsonPropertyName("validTo")] public string? ValidTo { get; set; }
            [JsonPropertyName("usage")] public List<string>? Usage { get; set; }
        }

        private class ChallengeResponse
        {
            [JsonPropertyName("challenge")] public string? Challenge { get; set; }
            [JsonPropertyName("timestampMs")] public long TimestampMs { get; set; } // <--- KSeF 2.0 używa milisekund!
        }

        private class AuthTokenResponse
        {
            [JsonPropertyName("referenceNumber")] public string? ReferenceNumber { get; set; }
            [JsonPropertyName("authenticationToken")] public AuthTokenDetail? AuthenticationToken { get; set; }
            [JsonPropertyName("accessToken")] public string? AccessToken { get; set; }
            [JsonPropertyName("refreshToken")] public string? RefreshToken { get; set; }
        }

        private class AuthTokenDetail
        {
            [JsonPropertyName("token")] public string? Token { get; set; }
            [JsonPropertyName("validUntil")] public string? ValidUntil { get; set; }
        }

        private class AuthStatusResponse
        {
            [JsonPropertyName("status")] public AuthStatusDetail? Status { get; set; }
        }

        private class AuthStatusDetail
        {
            [JsonPropertyName("code")] public int Code { get; set; }
            [JsonPropertyName("description")] public string? Description { get; set; }

            // DODAJ TĘ LINIJKĘ - tu KSeF chowa dokładne błędy XML-a!
            [JsonPropertyName("details")] public List<string>? Details { get; set; }
        }
        private class TokenRedeemResponse
        {
            // W KSeF 2.0 to jest zagnieżdżony obiekt, a nie string
            [JsonPropertyName("accessToken")] public AuthTokenDetail? AccessToken { get; set; }
            [JsonPropertyName("refreshToken")] public AuthTokenDetail? RefreshToken { get; set; }
        }

        private class InvoiceSendResponse
        {
            [JsonPropertyName("referenceNumber")] public string? ReferenceNumber { get; set; }
        }

        private class SessionResponse
        {
            [JsonPropertyName("referenceNumber")] public string? ReferenceNumber { get; set; }
            [JsonPropertyName("processingCode")] public int ProcessingCode { get; set; }
            [JsonPropertyName("processingDescription")] public string? ProcessingDescription { get; set; }
        }

        private class InvoiceStatusResponse
        {
            [JsonPropertyName("processingCode")] public int ProcessingCode { get; set; } // Zostawiamy dla wstecznej zgodności
            [JsonPropertyName("processingDescription")] public string? ProcessingDescription { get; set; }
            [JsonPropertyName("ksefReferenceNumber")] public string? KsefReferenceNumber { get; set; }

            // DODANE DLA KSEF 2.0:
            [JsonPropertyName("status")] public AuthStatusDetail? Status { get; set; }
        }
        // Uwaga: Klasę AuthStatusDetail dodałeś już wcześniej przy logowaniu, więc jest gotowa!
    }
}