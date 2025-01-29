using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;

public class ScheduledTaskService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledTaskService> _logger;

    public ScheduledTaskService(IServiceScopeFactory scopeFactory, ILogger<ScheduledTaskService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        var baseScalKey = Environment.GetEnvironmentVariable("BASE_SCAL");

        if (baseScalKey == "34692471")
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                        var scheduledTask = await context.ScheduledTasks.FirstOrDefaultAsync();

                        if (scheduledTask != null && scheduledTask.IsEnabled)
                        {
                            var now = DateTime.Now.TimeOfDay;

                            // Compare with some tolerance to avoid missing the time
                            var scheduledTime = scheduledTask.ScheduledTime;
                            var timeDifference = now - scheduledTime;
                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
                            {
                                var stores = await context.Stores
                                    .Where(s => s.AutoMatching)
                                    .ToListAsync();

                                var storeProcessingService = scope.ServiceProvider.GetRequiredService<StoreProcessingService>();

                                foreach (var store in stores)
                                {
                                    await storeProcessingService.ProcessStoreAsync(store.StoreId);
                                }

                                // Wait for a minute to avoid duplicate execution within the same minute
                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while executing scheduled tasks.");
                }

                // Check every 30 seconds
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
        else
        {
     
            _logger.LogInformation("BASE_SCAL klucz niezgodny - pomijam automatyczne zadania.");
        }

        
    }
}
