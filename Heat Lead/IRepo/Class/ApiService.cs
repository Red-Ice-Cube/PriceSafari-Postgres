using Heat_Lead.IRepo.Interface;
using System.Net.Http.Headers;
using System.Text;

namespace Heat_Lead.IRepo.Class
{
    public class ApiService : IApiService
    {
        private readonly HttpClient _httpClient;

        public ApiService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<HttpResponseMessage> CallApiWithBasicAuth(string url, string username, string password, string xml)
        {
            var byteArray = Encoding.ASCII.GetBytes($"{username}:{password}");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            HttpContent httpContent = new StringContent(xml, Encoding.UTF8, "application/xml");

            var response = await _httpClient.PostAsync(url, httpContent);

            return response;
        }
    }
}