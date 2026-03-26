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
                    new { contextIdentifier = new { nip = _nip } }, ct);

                if (challengeResp == null || string.IsNullOrEmpty(challengeResp.Challenge))
                {
                    _logger.LogError("Nie udało się pobrać challenge");
                    return false;
                }

                _logger.LogInformation("Challenge OK (timestamp: {ts})", challengeResp.Timestamp);

                // 2. Certyfikaty
                await EnsureCertificatesAsync(ct);
                if (_tokenEncryptionKey == null)
                {
                    _logger.LogError("Brak certyfikatu KsefTokenEncryption");
                    return false;
                }

                // 3. Szyfruj token|timestamp
                var timestamp = challengeResp.Timestamp
                    ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                var tokenPayload = $"{_ksefToken}|{timestamp}";
                var encryptedToken = _tokenEncryptionKey.Encrypt(
                    Encoding.UTF8.GetBytes(tokenPayload), RSAEncryptionPadding.OaepSHA256);

                // 4. POST /auth/ksef-token
                var authResp = await PostJsonAsync<AuthTokenResponse>(
                    $"{_baseUrl}/auth/ksef-token",
                    new
                    {
                        challenge = challengeResp.Challenge,
                        contextIdentifier = new { type = "nip", value = _nip },
                        encryptedToken = Convert.ToBase64String(encryptedToken)
                    }, ct);

                if (authResp == null)
                {
                    _logger.LogError("Brak odpowiedzi z /auth/ksef-token");
                    return false;
                }

                // KSeF 2.0 Demo zwraca JWT od razu w authenticationToken.token
                if (!string.IsNullOrEmpty(authResp.AuthenticationToken?.Token))
                {
                    _accessToken = authResp.AuthenticationToken.Token;
                    _tokenExpiresAt = DateTime.TryParse(authResp.AuthenticationToken.ValidUntil, out var exp)
                        ? exp.ToUniversalTime()
                        : DateTime.UtcNow.AddMinutes(14);
                    _logger.LogInformation("<<< [KSeF AUTH] OK! JWT z authenticationToken (ważny do {v})",
                        authResp.AuthenticationToken.ValidUntil);
                    return true;
                }

                // Alternatywa: bezpośredni accessToken
                if (!string.IsNullOrEmpty(authResp.AccessToken))
                {
                    _accessToken = authResp.AccessToken;
                    _refreshToken = authResp.RefreshToken;
                    _tokenExpiresAt = DateTime.UtcNow.AddMinutes(14);
                    _logger.LogInformation("<<< [KSeF AUTH] OK! AccessToken bezpośrednio.");
                    return true;
                }

                // ReferenceNumber → poll → redeem
                if (!string.IsNullOrEmpty(authResp.ReferenceNumber))
                {
                    _logger.LogInformation("Auth ref: {ref}, polling...", authResp.ReferenceNumber);

                    if (!await PollAuthStatusAsync(authResp.ReferenceNumber, ct))
                        return false;

                    var redeem = await PostJsonAsync<TokenRedeemResponse>(
                        $"{_baseUrl}/auth/token/redeem",
                        new { referenceNumber = authResp.ReferenceNumber }, ct);

                    if (redeem?.AccessToken != null)
                    {
                        _accessToken = redeem.AccessToken;
                        _refreshToken = redeem.RefreshToken;
                        _tokenExpiresAt = DateTime.UtcNow.AddMinutes(14);
                        _logger.LogInformation("<<< [KSeF AUTH] OK! JWT przez redeem.");
                        return true;
                    }
                }

                _logger.LogError("Auth: brak accessToken i referenceNumber");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wyjątek auth KSeF");
                return false;
            }
        }

        private async Task<bool> PollAuthStatusAsync(string refNum, CancellationToken ct)
        {
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(2000, ct);
                var r = await GetJsonAsync<AuthStatusResponse>($"{_baseUrl}/auth/{refNum}", ct);
                if (r?.ProcessingCode == 200) return true;
                if (r?.ProcessingCode >= 400)
                {
                    _logger.LogError("Auth poll error: {c} {d}", r.ProcessingCode, r.ProcessingDescription);
                    return false;
                }
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
                        formCode = new { systemCode = "FA (3)", schemaVersion = "1-0E", targetNamespace = "http://crd.gov.pl/wzor/2025/06/25/13775/" },
                        encryption = new
                        {
                            encryptionKeyBody = Convert.ToBase64String(encKey),
                            iv = Convert.ToBase64String(aes.IV)
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

                // --- KROK 3: Zamknij sesję ---
                _logger.LogInformation("[KSeF] Zamykam sesję {ref}...", sessionRef);

                await PostJsonAsync<SessionResponse>(
                    $"{_baseUrl}/sessions/online/{sessionRef}/close",
                    new { }, ct, useAuth: true);

                _logger.LogInformation("[KSeF] Sesja zamknięta.");

                // Zwracamy referenceNumber sesji (lub faktury jeśli jest)
                var resultRef = invoiceRefNumber ?? sessionRef;
                return (true, resultRef, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wyjątek wysyłki faktury (sesja)");
                return (false, "", ex.Message);
            }
        }

        public async Task<(KSeFExportStatus Status, string? KSeFNumber, string? Error)>
            CheckStatusAsync(string referenceNumber, CancellationToken ct)
        {
            try
            {
                var r = await GetJsonAsync<InvoiceStatusResponse>(
                    $"{_baseUrl}/invoices/status/{referenceNumber}", ct, useAuth: true);
                if (r == null) return (KSeFExportStatus.Pending, null, "Brak odpowiedzi");
                if (r.ProcessingCode == 200)
                    return (KSeFExportStatus.Confirmed, r.KsefReferenceNumber, null);
                if (r.ProcessingCode >= 400)
                    return (KSeFExportStatus.Error, null, $"{r.ProcessingCode}: {r.ProcessingDescription}");
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

        private async Task<T?> PostJsonAsync<T>(string url, object payload, CancellationToken ct,
            bool useAuth = false) where T : class
        {
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (useAuth && _accessToken != null)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var resp = await _httpClient.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("POST {u} → {c}: {b}", url, resp.StatusCode, Trunc(body));

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
            [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
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
            [JsonPropertyName("processingCode")] public int ProcessingCode { get; set; }
            [JsonPropertyName("processingDescription")] public string? ProcessingDescription { get; set; }
        }

        private class TokenRedeemResponse
        {
            [JsonPropertyName("accessToken")] public string? AccessToken { get; set; }
            [JsonPropertyName("refreshToken")] public string? RefreshToken { get; set; }
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
            [JsonPropertyName("processingCode")] public int ProcessingCode { get; set; }
            [JsonPropertyName("processingDescription")] public string? ProcessingDescription { get; set; }
            [JsonPropertyName("ksefReferenceNumber")] public string? KsefReferenceNumber { get; set; }
        }
    }
}