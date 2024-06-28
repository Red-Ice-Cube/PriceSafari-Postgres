//using Microsoft.AspNetCore.SignalR;

//namespace PriceTracker.Hubs
//{
//    public class ScrapingHub : Hub
//    {
//        public async Task SendProgressUpdate(int totalScraped, int uniqueProducts, int currentPage, int totalPages, int storeId, double speed)
//        {
//            await Clients.All.SendAsync("ReceiveProgressUpdate", totalScraped, uniqueProducts, currentPage, totalPages, storeId, speed);
//        }
//    }
//}

using Microsoft.AspNetCore.SignalR;

namespace PriceTracker.Hubs
{
    public class ScrapingHub : Hub
    {
        public async Task SendProgressUpdate(int totalScraped, int uniqueProducts, double elapsedSeconds, int rejectedCount)
        {
            await Clients.All.SendAsync("ReceiveProgressUpdate", totalScraped, uniqueProducts, elapsedSeconds, rejectedCount);
        }
    }
}

