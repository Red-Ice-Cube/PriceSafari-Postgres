

public class KeepAliveService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<KeepAliveService> _logger;
    private readonly string _keepAliveUrl;

    public KeepAliveService(IHttpClientFactory httpClientFactory, ILogger<KeepAliveService> logger, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _keepAliveUrl = configuration["KeepAliveUrl"];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = _httpClientFactory.CreateClient();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await client.GetAsync(_keepAliveUrl, stoppingToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Keep-alive request successful.");
                }
                else
                {
                    _logger.LogWarning("Keep-alive request failed with status code: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Keep-alive request failed.");
            }

          
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
}
