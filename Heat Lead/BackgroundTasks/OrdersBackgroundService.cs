using Heat_Lead.IRepo.Interface;
using Heat_Lead.Services;

public class OrdersBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrdersBackgroundService> _logger;

    public OrdersBackgroundService(IServiceProvider serviceProvider, ILogger<OrdersBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
                    var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();

                    await orderService.FetchAndProcessOrders();
                    await orderService.CalculateOrders();

                    var intervalInSeconds = await settingsService.GetOrderProcessIntervalAsync();
                    await Task.Delay(TimeSpan.FromSeconds(intervalInSeconds), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while executing FetchOrders");
            }
        }
    }
}
