using Microsoft.AspNetCore.SignalR;

namespace PriceSafari.Hubs
{
    public class ReportProgressHub : Hub
    {
        public async Task SendProgress(string message, int percentage)
        {
            await Clients.All.SendAsync("ReceiveProgress", message, percentage);
        }
    }
}
