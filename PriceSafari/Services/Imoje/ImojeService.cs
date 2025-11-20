using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PriceSafari.Services.Imoje
{
    public interface IImojeService
    {
        string CalculateSignature(Dictionary<string, string> data, string hashMethod = "sha256");

        Task<(bool Success, string Response)> ChargeProfileAsync(string profileId, InvoiceClass invoice, string ipAddress);

        Task<bool> HandleNotificationAsync(string headerSignature, string requestBody);
        Task<bool> RefundTransactionAsync(string transactionId, int amount, string serviceId);
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

        private HttpClient CreateModernHttpClient()
        {
            var handler = new SocketsHttpHandler();
            handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls13 | System.Security.Authentication.SslProtocols.Tls12,
                RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
            };

            handler.ConnectTimeout = TimeSpan.FromSeconds(30);

            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(60);
            return client;
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
            return $"{BitConverter.ToString(hashBytes).Replace("-", "").ToLower()};{hashMethod}";
        }

        public async Task<(bool Success, string Response)> ChargeProfileAsync(string profileId, InvoiceClass invoice, string ipAddress)
        {
            try
            {

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

                if (string.IsNullOrEmpty(_merchantId) || string.IsNullOrEmpty(_apiKey))
                {
                    return (false, "Brak konfiguracji imoje w .env");
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

                using (var client = CreateModernHttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "PriceSafari-Worker/2.0");
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                    var response = await client.PostAsync(requestUrl, content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"Płatność cykliczna udana. Response: {responseString}");
                        return (true, responseString);
                    }
                    else
                    {
                        string errorMsg = $"Status: {response.StatusCode}. Treść: {responseString}";
                        _logger.LogError($"Błąd płatności dla {invoice.InvoiceNumber}. {errorMsg}");
                        return (false, errorMsg);
                    }
                }
            }
            catch (PlatformNotSupportedException ex)
            {
                string msg = $"SYSTEM OS ERROR: TLS nieobsługiwany. {ex.Message}";
                _logger.LogError(ex, msg);
                return (false, msg);
            }
            catch (Exception ex)
            {
                string fullError = $"Wyjątek: {ex.Message}";
                if (ex.InnerException != null) fullError += $" | INNER: {ex.InnerException.Message}";
                _logger.LogError(ex, $"Krytyczny błąd połączenia z Imoje.");
                return (false, fullError);
            }
        }

        public async Task<bool> HandleNotificationAsync(string headerSignature, string requestBody)
        {
            try
            {
                _logger.LogInformation($"[IMOJE] Otrzymano webhook.");

                if (!VerifySignature(headerSignature, requestBody))
                {
                    _logger.LogError("[IMOJE] BŁĄD: Nieprawidłowy podpis!");
                    return false;
                }

                var json = JObject.Parse(requestBody);
                var transaction = json["transaction"];

                if (transaction == null) return true;

                string status = transaction["status"]?.ToString();
                string transactionId = transaction["id"]?.ToString();

                if (status != "settled") return true;

                string customerIdStr = transaction["customer"]?["id"]?.ToString();
                if (string.IsNullOrEmpty(customerIdStr)) customerIdStr = transaction["paymentProfile"]?["merchantCustomerId"]?.ToString();
                if (string.IsNullOrEmpty(customerIdStr)) customerIdStr = json["action"]?["paymentProfile"]?["merchantCustomerId"]?.ToString();

                if (string.IsNullOrEmpty(customerIdStr))
                {
                    _logger.LogWarning($"[IMOJE] Nie znaleziono ID klienta (StoreId) w transakcji {transactionId}. Ignoruję.");
                    return true;
                }

                if (!int.TryParse(customerIdStr, out int storeId))
                {
                    _logger.LogError($"[IMOJE] ID klienta '{customerIdStr}' nie jest liczbą.");
                    return true;
                }

                var profileObj = json["action"]?["paymentProfile"]
                                 ?? json["paymentProfile"]
                                 ?? transaction["paymentProfile"];

                string paymentProfileId = profileObj?["id"]?.ToString();
                string deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "Server";

                if (!string.IsNullOrEmpty(paymentProfileId))
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                        var store = await dbContext.Stores.FirstOrDefaultAsync(s => s.StoreId == storeId);

                        if (store != null)
                        {
                            store.ImojePaymentProfileId = paymentProfileId;
                            store.IsRecurringActive = true;
                            store.IsPayingCustomer = true;

                            if (profileObj != null)
                            {
                                store.CardMaskedNumber = profileObj["maskedNumber"]?.ToString();
                                store.CardBrand = profileObj["organization"]?.ToString();
                                store.CardExpYear = profileObj["year"]?.ToString();
                                store.CardExpMonth = profileObj["month"]?.ToString();
                            }

                            if (store.SubscriptionStartDate == null)
                                store.SubscriptionStartDate = DateTime.Now;

                            await dbContext.SaveChangesAsync();

                            var logAuth = new TaskExecutionLog
                            {
                                DeviceName = deviceName,
                                OperationName = "AUTORYZACJA_KARTY",
                                StartTime = DateTime.Now,
                                EndTime = DateTime.Now,
                                Comment = $"Sukces. Sklep: {store.StoreName}. Karta: {store.CardMaskedNumber} ({store.CardBrand}). ProfileId: {paymentProfileId}"
                            };
                            dbContext.TaskExecutionLogs.Add(logAuth);
                            await dbContext.SaveChangesAsync();

                            _logger.LogInformation($"[IMOJE] SUKCES! Zaktualizowano sklep {storeId}. Karta: {store.CardMaskedNumber}");
                        }
                        else
                        {
                            _logger.LogError($"[IMOJE] BŁĄD: Sklep o ID {storeId} nie istnieje w bazie.");

                            var logError = new TaskExecutionLog
                            {
                                DeviceName = deviceName,
                                OperationName = "AUTORYZACJA_KARTY",
                                StartTime = DateTime.Now,
                                EndTime = DateTime.Now,
                                Comment = $"BŁĄD: Otrzymano płatność dla nieistniejącego sklepu ID: {storeId}."
                            };
                            dbContext.TaskExecutionLogs.Add(logError);
                            await dbContext.SaveChangesAsync();
                        }
                    }
                }

                int amount = (int)(transaction["amount"] ?? 0);
                string orderId = transaction["orderId"]?.ToString();

                if (amount == 100 && orderId != null && orderId.StartsWith("REG-"))
                {

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(3000);
                            using (var scopeRefund = _scopeFactory.CreateScope())
                            {
                                var dbContextRefund = scopeRefund.ServiceProvider.GetRequiredService<PriceSafariContext>();
                                var serviceRefund = scopeRefund.ServiceProvider.GetRequiredService<IImojeService>();

                                var logRefund = new TaskExecutionLog
                                {
                                    DeviceName = deviceName,
                                    OperationName = "ZWROT_WERYFIKACYJNY",
                                    StartTime = DateTime.Now,
                                    Comment = $"Próba zwrotu 1.00 PLN dla zamówienia {orderId}..."
                                };
                                dbContextRefund.TaskExecutionLogs.Add(logRefund);
                                await dbContextRefund.SaveChangesAsync();
                                int refundLogId = logRefund.Id;

                                bool refundSuccess = await serviceRefund.RefundTransactionAsync(transactionId, amount, _serviceId);

                                var logToUpdate = await dbContextRefund.TaskExecutionLogs.FindAsync(refundLogId);
                                if (logToUpdate != null)
                                {
                                    logToUpdate.EndTime = DateTime.Now;
                                    logToUpdate.Comment += refundSuccess ? " | Sukces. Zwrot zlecony." : " | BŁĄD. Imoje odrzuciło zwrot.";
                                    await dbContextRefund.SaveChangesAsync();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Błąd w tle podczas zwrotu");
                        }
                    });
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[IMOJE] Wyjątek krytyczny w HandleNotificationAsync");

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

        public async Task<bool> RefundTransactionAsync(string transactionId, int amount, string serviceId)
        {
            try
            {
                var merchantId = _config["IMOJE_MERCHANT_ID"];
                var token = _config["IMOJE_API_KEY"];
                var apiUrl = _config["IMOJE_API_URL"];

                using var client = CreateModernHttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var url = $"{apiUrl}/{merchantId}/transaction/{transactionId}/refund";

                var payload = new
                {
                    type = "refund",
                    serviceId = serviceId,
                    amount = amount,
                    title = "Zwrot oplaty weryfikacyjnej",
                    sendRefundConfirmationEmail = true
                };

                var json = System.Text.Json.JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Refund failed: {errorBody}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in RefundTransactionAsync");
                return false;
            }
        }
    }
}