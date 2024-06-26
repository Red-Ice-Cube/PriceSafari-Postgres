using Microsoft.AspNetCore.SignalR;

namespace Heat_Lead.Signal1R
{
    public class UserTrackingHub : Hub
    {
        private static int UserCount = 0;

        public override Task OnConnectedAsync()
        {
            Interlocked.Increment(ref UserCount);
            Clients.All.SendAsync("UpdateUserCount", UserCount);
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            Interlocked.Decrement(ref UserCount);
            Clients.All.SendAsync("UpdateUserCount", UserCount);
            return base.OnDisconnectedAsync(exception);
        }
    }
}