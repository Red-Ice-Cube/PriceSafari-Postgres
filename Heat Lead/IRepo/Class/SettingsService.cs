using Heat_Lead.Data;
using Heat_Lead.IRepo.Interface;
using Microsoft.EntityFrameworkCore;

namespace Heat_Lead.IRepo.Class
{
    public class SettingsService : ISettingsService
    {
        private readonly Heat_LeadContext _context;

        public SettingsService(Heat_LeadContext context)
        {
            _context = context;
        }

        public async Task<int> GetOrderProcessIntervalAsync()
        {
            var setting = await _context.Settings.FirstOrDefaultAsync();
            return setting?.OrdersProcessIntervalInSeconds ?? 15;
        }
    }
}