using Microsoft.AspNetCore.SignalR;

namespace PriceSafari.Hubs
{
    public class ScrapingHub : Hub
    {
        public async Task SendProgressUpdate(int totalScraped, int uniqueProducts, double elapsedSeconds, int rejectedCount)
        {
            await Clients.All.SendAsync("ReceiveProgressUpdate", totalScraped, uniqueProducts, elapsedSeconds, rejectedCount);
        }

        public async Task SendScrapingUpdate(string offerUrl, bool isScraped, bool isRejected, string scrapingMethod, int pricesCount)
        {
            await Clients.All.SendAsync("ReceiveScrapingUpdate", offerUrl, isScraped, isRejected, scrapingMethod, pricesCount);
        }
    }
}
