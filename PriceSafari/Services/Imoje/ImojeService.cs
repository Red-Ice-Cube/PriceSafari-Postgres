using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Security.Cryptography;
using System.Text;

namespace PriceSafari.Services.Imoje
{
    public interface IImojeService
    {
        string CalculateSignature(Dictionary<string, string> data, string hashMethod = "sha256");
        Task<bool> ChargeProfileAsync(string profileId, InvoiceClass invoice, string ipAddress);

        Task<bool> HandleNotificationAsync(string headerSignature, string requestBody);
    }

    public class ImojeService : IImojeService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ImojeService> _logger;

        private readonly IServiceScopeFactory _scopeFactory;

        private string _merchantId => _config["IMOJE_MERCHANT_ID"];
        private string _serviceId => _config["IMOJE_SERVICE_ID"];
        private string _serviceKey => _config["IMOJE_SERVICE_KEY"];
        private string _apiKey => _config["IMOJE_API_KEY"];
        private string _apiUrl => _config["IMOJE_API_URL"];

        public ImojeService(IConfiguration config, HttpClient httpClient, ILogger<ImojeService> logger, IServiceScopeFactory scopeFactory)
        {
            _config = config;
            _httpClient = httpClient;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public string CalculateSignature(Dictionary<string, string> data, string hashMethod = "sha256")
        {

            var sortedKeys = data.Keys.OrderBy(k => k).ToList();
            var sb = new StringBuilder();

            foreach (var key in sortedKeys)
            {
                if (sb.Length > 0) sb.Append("&");
                sb.Append($"{key}={data[key]}");
            }

            sb.Append(_serviceKey);

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

            return $"{hashString};{hashMethod}";
        }

        public async Task<bool> ChargeProfileAsync(string profileId, InvoiceClass invoice, string ipAddress)
        {
            try
            {

                if (string.IsNullOrEmpty(_merchantId) || string.IsNullOrEmpty(_apiKey))
                {
                    _logger.LogError("Brak konfiguracji imoje w .env (MERCHANT_ID lub API_KEY są puste).");
                    return false;
                }

                var amountInGrosze = (int)(invoice.NetAmount * 1.23m * 100);

                var payload = new
                {
                    serviceId = _serviceId,
                    amount = amountInGrosze,
                    currency = "PLN",
                    orderId = invoice.InvoiceNumber,
                    title = $"Opłata za fakturę {invoice.InvoiceNumber}",
                    paymentProfileId = profileId

                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var requestUrl = $"{_apiUrl}/v1/merchant/{_merchantId}/transaction/profile";

                var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Płatność cykliczna udana dla faktury {invoice.InvoiceNumber}. Response: {responseString}");
                    return true;
                }
                else
                {
                    _logger.LogError($"Błąd płatności cyklicznej dla {invoice.InvoiceNumber}. Status: {response.StatusCode}. Body: {responseString}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Wyjątek podczas obciążania karty dla faktury {invoice.InvoiceNumber}");
                return false;
            }
        }

        public async Task<bool> HandleNotificationAsync(string headerSignature, string requestBody)
        {
            try
            {

                if (!VerifySignature(headerSignature, requestBody))
                {
                    _logger.LogError("Imoje Webhook: Nieprawidłowy podpis!");
                    return false;
                }

                var json = JObject.Parse(requestBody);
                var transaction = json["transaction"];

                if (transaction == null) return true;

                string status = transaction["status"]?.ToString();
                if (status != "settled") return true;

                string transactionId = transaction["id"]?.ToString();
                string orderId = transaction["orderId"]?.ToString();
                string customerIdStr = transaction["customer"]?["id"]?.ToString();
                int amount = (int)(transaction["amount"] ?? 0);

                string paymentProfileId = json["action"]?["paymentProfile"]?["id"]?.ToString()
                                       ?? json["paymentProfile"]?["id"]?.ToString();

                if (string.IsNullOrEmpty(paymentProfileId))
                {
                    _logger.LogWarning($"Imoje Webhook: Brak paymentProfileId dla transakcji {transactionId}");
                    return true;
                }

                if (!int.TryParse(customerIdStr, out int storeId)) return true;

                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                    var store = await dbContext.Stores.FirstOrDefaultAsync(s => s.StoreId == storeId);

                    if (store != null)
                    {

                        store.ImojePaymentProfileId = paymentProfileId;
                        store.IsRecurringActive = true;
                        store.IsPayingCustomer = true;

                        if (store.SubscriptionStartDate == null) store.SubscriptionStartDate = DateTime.Now;

                        await dbContext.SaveChangesAsync();
                        _logger.LogInformation($"Imoje: Zapisano kartę (Token: {paymentProfileId}) dla sklepu {storeId}");
                    }
                }

                if (amount == 100 && orderId != null && orderId.StartsWith("REG-"))
                {
                    await RefundTransactionPrivateAsync(transactionId, amount, _serviceId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Imoje Webhook: Błąd krytyczny");
                return false;
            }
        }

        private bool VerifySignature(string headerSignature, string body)
        {
            if (string.IsNullOrEmpty(headerSignature)) return false;

            var parts = headerSignature.Split(';')
                .Select(p => p.Split('='))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim().ToLower(), p => p[1].Trim());

            if (!parts.ContainsKey("signature")) return false;

            var incomingSig = parts["signature"];

            using var sha256 = SHA256.Create();
            var payload = body + _serviceKey;
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var calculatedSig = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

            return incomingSig.Equals(calculatedSig, StringComparison.InvariantCultureIgnoreCase);
        }

        private async Task<bool> RefundTransactionPrivateAsync(string transactionId, int amount, string serviceId)
        {
            try
            {
                var refundUrl = $"{_apiUrl}/v1/merchant/{_merchantId}/transaction/{transactionId}/refund";
                var payload = new { type = "refund", serviceId, amount, title = "Zwrot weryfikacyjny 1 PLN" };

                var request = new HttpRequestMessage(HttpMethod.Post, refundUrl);
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Imoje: Wykonano zwrot dla {transactionId}");
                    return true;
                }
                _logger.LogError($"Imoje: Błąd zwrotu {transactionId}. {await response.Content.ReadAsStringAsync()}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Imoje: Wyjątek przy zwrocie");
                return false;
            }
        }
    }
}