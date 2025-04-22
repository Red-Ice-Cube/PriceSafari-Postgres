using Microsoft.AspNetCore.SignalR;

namespace PriceSafari.SignalIR
{
    public class DashboardProgressHub : Hub
    {
        
        public string GetConnectionId() => Context.ConnectionId;
    }
}
