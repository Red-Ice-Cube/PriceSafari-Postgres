using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using PriceSafari.Models;

namespace PriceSafari.Services.Imoje
{
    public interface IImojeService
    {
        string CalculateSignature(Dictionary<string, string> data, string hashMethod = "sha256");
        Task<bool> ChargeProfileAsync(string profileId, InvoiceClass invoice, string ipAddress);
    }

    public class ImojeService : IImojeService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ImojeService> _logger;

        // Te dane powinny być w appsettings.json
        private string _merchantId => _config["Imoje:MerchantId"];
        private string _serviceId => _config["Imoje:ServiceId"];
        private string _serviceKey => _config["Imoje:ServiceKey"];
        private string _apiKey => _config["Imoje:ApiKey"]; // Bearer Token
        private string _apiUrl => _config["Imoje:ApiUrl"]; // np. https://paywall.imoje.pl lub sandbox

        public ImojeService(IConfiguration config, HttpClient httpClient, ILogger<ImojeService> logger)
        {
            _config = config;
            _httpClient = httpClient;
            _logger = logger;
        }

        public string CalculateSignature(Dictionary<string, string> data, string hashMethod = "sha256")
        {
            // 1. Sortowanie alfabetyczne kluczy
            var sortedKeys = data.Keys.OrderBy(k => k).ToList();
            var sb = new StringBuilder();

            // 2. Łączenie parametrów param1=val1&param2=val2
            foreach (var key in sortedKeys)
            {
                if (sb.Length > 0) sb.Append("&");
                sb.Append($"{key}={data[key]}");
            }

            // 3. Doklejenie klucza sklepu
            sb.Append(_serviceKey);

            // 4. Hashowanie
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

            // 5. Format końcowy
            return $"{hashString};{hashMethod}";
        }

        public async Task<bool> ChargeProfileAsync(string profileId, InvoiceClass invoice, string ipAddress)
        {
            try
            {
                var amountInGrosze = (int)(invoice.NetAmount * 1.23m * 100); // Zakładam, że NetAmount to netto, a imoje chce brutto w groszach. Dostosuj VAT.

                var payload = new
                {
                    serviceId = _serviceId,
                    amount = amountInGrosze,
                    currency = "PLN",
                    orderId = invoice.InvoiceNumber, // Unikalny ID zamówienia (numer faktury)
                    title = $"Opłata za fakturę {invoice.InvoiceNumber}",
                    paymentProfileId = profileId
                    // notificationUrl = "https://twojadomena.pl/api/imoje/notify" // Opcjonalnie
                };

                var json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiUrl}/v1/merchant/{_merchantId}/transaction/profile");
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Płatność cykliczna udana dla faktury {invoice.InvoiceNumber}. Response: {responseString}");
                    return true; // W idealnym świecie sprawdzamy status w JSON, ale 200 OK zazwyczaj oznacza przyjęcie transakcji
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
    }
}