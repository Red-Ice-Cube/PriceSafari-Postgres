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
                    paymentProfileId = profileId,
                    clientIp = ipAddress // Przekazujemy IP serwera
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

                // Interesują nas tylko zrealizowane transakcje
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

                // --- ETAP 1: LOGOWANIE ZAPISU KARTY ---
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

                            // 1. Zapisujemy zmiany w sklepie
                            await dbContext.SaveChangesAsync();

                            // 2. DODAJEMY LOG DO TaskExecutionLogs
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

                            // Log błędu w bazie (opcjonalnie)
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

                // --- ETAP 2: OBSŁUGA ZWROTU ---
                int amount = (int)(transaction["amount"] ?? 0);
                string orderId = transaction["orderId"]?.ToString();

                // Sprawdzamy czy to weryfikacja (1 PLN i prefix REG-)
                if (amount == 100 && orderId != null && orderId.StartsWith("REG-"))
                {
                    // Tworzymy osobny scope dla logowania zwrotu, bo poprzedni using się skończył
                    using (var scopeRefund = _scopeFactory.CreateScope())
                    {
                        var dbContextRefund = scopeRefund.ServiceProvider.GetRequiredService<PriceSafariContext>();

                        // Logujemy start zwrotu
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

                        // Czekamy 3 sekundy dla bezpieczeństwa
                        await Task.Delay(3000);

                        // Wykonujemy zwrot
                        bool refundSuccess = await RefundTransactionAsync(transactionId, amount, _serviceId);

                        // Aktualizujemy log
                        var logToUpdate = await dbContextRefund.TaskExecutionLogs.FindAsync(refundLogId);
                        if (logToUpdate != null)
                        {
                            logToUpdate.EndTime = DateTime.Now;
                            if (refundSuccess)
                            {
                                logToUpdate.Comment += " | Sukces. Zwrot zlecony.";
                            }
                            else
                            {
                                logToUpdate.Comment += " | BŁĄD. Imoje odrzuciło zwrot.";
                            }
                            await dbContextRefund.SaveChangesAsync();
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[IMOJE] Wyjątek krytyczny w HandleNotificationAsync");

                // Próba zalogowania krytycznego błędu do bazy
                try
                {
                    using var scopeErr = _scopeFactory.CreateScope();
                    var dbErr = scopeErr.ServiceProvider.GetRequiredService<PriceSafariContext>();
                    var logCrit = new TaskExecutionLog
                    {
                        DeviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "Server",
                        OperationName = "IMOJE_WEBHOOK_ERROR",
                        StartTime = DateTime.Now,
                        EndTime = DateTime.Now,
                        Comment = $"Krytyczny wyjątek: {ex.Message}"
                    };
                    dbErr.TaskExecutionLogs.Add(logCrit);
                    await dbErr.SaveChangesAsync();
                }
                catch { /* Jeśli baza leży, to nic nie zrobimy */ }

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

                using var client = new HttpClient();
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