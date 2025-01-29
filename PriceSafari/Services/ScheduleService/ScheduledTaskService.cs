using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Services.ScheduleService;

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
        // klucze z .env
        var baseScalKey = Environment.GetEnvironmentVariable("BASE_SCAL");
        var urlScalKey = Environment.GetEnvironmentVariable("URL_SCAL");
        var gooCrawKey = Environment.GetEnvironmentVariable("GOO_CRAW");


        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
                    var scheduledTask = await context.ScheduledTasks.FirstOrDefaultAsync();

                    if (scheduledTask != null)
                    {
                        // ====================================================
                        // 1) SPRAWDZANIE AKCJI POWIĄZANEJ Z BASE_SCAL
                        // ====================================================
                        if (baseScalKey == "34692471" && scheduledTask.IsEnabled)
                        {
                            var now = DateTime.Now.TimeOfDay;
                            var timeDifference = now - scheduledTask.ScheduledTime;

                            // Wywołuj, jeśli jest "porządany" czas
                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
                            {
                                // Twoja dotychczasowa logika autoprocessingu store’ów
                                var storeProcessingService = scope.ServiceProvider.GetRequiredService<StoreProcessingService>();
                                var stores = await context.Stores.Where(s => s.AutoMatching).ToListAsync();

                                foreach (var store in stores)
                                {
                                    await storeProcessingService.ProcessStoreAsync(store.StoreId);
                                }

                                // żeby nie wywoływać kilka razy w ciągu tej samej minuty
                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                            }
                        }

                        // ====================================================
                        // 2) SPRAWDZANIE AKCJI POWIĄZANEJ Z URL_SCAL
                        // ====================================================
                        if (urlScalKey == "99999999" && scheduledTask.UrlIsEnabled)
                        {
                            var now = DateTime.Now.TimeOfDay;
                            var timeDifference = now - scheduledTask.UrlScheduledTime;

                            // Wywołuj, jeśli jest "porządany" czas
                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
                            {
                             
                                var urlGroupingService = scope.ServiceProvider.GetRequiredService<UrlGroupingService>();
                                await urlGroupingService.GroupAndSaveUniqueUrls();


                                // żeby nie wywoływać kilka razy w ciągu tej samej minuty
                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                            }
                        }

                        if (gooCrawKey == "03713857" && scheduledTask.GoogleIsEnabled)
                        {
                            var now = DateTime.Now.TimeOfDay;
                            var timeDifference = now - scheduledTask.GoogleScheduledTime;

                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
                            {
                                var googleScraperService = scope.ServiceProvider.GetRequiredService<GoogleScraperService>();

                                // Wywołaj i sprawdź rezultat
                                var result = await googleScraperService.StartScraping();
                                if (result == GoogleScraperService.GoogleScrapingResult.Success)
                                {
                                    // Udało się
                                }
                                else if (result == GoogleScraperService.GoogleScrapingResult.NoProductsToScrape)
                                {
                                    // Obsłuż brak produktów do scrapowania
                                }
                                // itd.

                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                            }
                        }



                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while executing scheduled tasks.");
            }

            // pętla co 30 sekund
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
