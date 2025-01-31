using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Services.ScheduleService;

public class ScheduledTaskService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledTaskService> _logger;

    private DateTime _lastDeviceCheck = DateTime.MinValue;
    private readonly TimeSpan _deviceCheckInterval = TimeSpan.FromMinutes(10);

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
        var cenCrawKey = Environment.GetEnvironmentVariable("CEN_CRAW");

        var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME")
                     ?? "UnknownDevice";


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
                        // 1) SPRAWDZANIE AKCJI POWIĄZANEJ Z BASE_SCAL - Scalanie Bazy
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

                        // 2) SPRAWDZANIE AKCJI POWIĄZANEJ Z URL_SCAL - Grupowanie URL
                        if (urlScalKey == "49276583" && scheduledTask.UrlIsEnabled)
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
                        // 3) SPRAWDZANIE AKCJI POWIĄZANEJ Z GOO_SCRAPER - Google
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


                        // 4) SPRAWDZANIE AKCJI POWIĄZANEJ Z CEN_SCRAPER
                        if (cenCrawKey == "56981467" && scheduledTask.CeneoIsEnabled)
                        {
                            var now = DateTime.Now.TimeOfDay;
                            var timeDifference = now - scheduledTask.CeneoScheduledTime;

                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
                            {
                                // Wstrzykujemy serwis
                                var ceneoScraperService = scope.ServiceProvider.GetRequiredService<CeneoScraperService>();

                                // Wywołanie scrapowania
                                var result = await ceneoScraperService.StartScrapingWithCaptchaHandlingAsync(stoppingToken);

                                if (result.Result == CeneoScraperService.CeneoScrapingResult.Success)
                                {
                                    _logger.LogInformation("Ceneo scraping completed successfully.");
                                }
                                else if (result.Result == CeneoScraperService.CeneoScrapingResult.NoUrlsFound)
                                {
                                    _logger.LogInformation("No URLs to scrape for Ceneo.");
                                }
                                // etc.

                                // aby unikać wielokrotnego wywołania w tej samej minucie
                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                            }
                        }

                        if (DateTime.Now - _lastDeviceCheck >= _deviceCheckInterval)
                        {
                            // Ustawiamy, żeby ponownie „za 10 minut” się to wykonało
                            _lastDeviceCheck = DateTime.Now;

                            // Wywołanie metody, która zaktualizuje DeviceStatus w bazie
                            await UpdateDeviceStatusAsync(
                                context,
                                deviceName,
                                baseScalKey,
                                urlScalKey,
                                gooCrawKey,
                                cenCrawKey,
                                stoppingToken
                            );
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

    private async Task UpdateDeviceStatusAsync(
       PriceSafariContext context,
       string deviceName,
       string baseScalKey,
       string urlScalKey,
       string gooCrawKey,
       string cenCrawKey,
       CancellationToken ct
   )
    {
        // 1. Odczyt "prawidłowych" wartości (twardo zakodowane lub z configa)
        const string BASE_SCAL_EXPECTED = "34692471";
        const string URL_SCAL_EXPECTED = "49276583";
        const string GOO_CRAW_EXPECTED = "03713857";
        const string CEN_CRAW_EXPECTED = "56981467";

        // 2. Czy klucz .env zgadza się z oczekiwanym?
        bool hasBaseScal = (baseScalKey == BASE_SCAL_EXPECTED);
        bool hasUrlScal = (urlScalKey == URL_SCAL_EXPECTED);
        bool hasGooCraw = (gooCrawKey == GOO_CRAW_EXPECTED);
        bool hasCenCraw = (cenCrawKey == CEN_CRAW_EXPECTED);

        // 3. Znajdź w bazie DeviceStatus dla danego deviceName
        var existingStatus = await context.DeviceStatuses
            .FirstOrDefaultAsync(d => d.DeviceName == deviceName, ct);

        if (existingStatus == null)
        {
            // Nie ma wpisu w bazie => tworzymy nowy
            var newStatus = new DeviceStatus
            {
                DeviceName = deviceName,
                IsOnline = true,             // bo właśnie się odezwało
                LastCheck = DateTime.Now,
                BaseScalEnabled = hasBaseScal,
                UrlScalEnabled = hasUrlScal,
                GooCrawEnabled = hasGooCraw,
                CenCrawEnabled = hasCenCraw
            };

            await context.DeviceStatuses.AddAsync(newStatus, ct);
        }
        else
        {
            // Już istnieje => uaktualniamy
            existingStatus.IsOnline = true;  // bo właśnie się odezwało
            existingStatus.LastCheck = DateTime.Now;
            existingStatus.BaseScalEnabled = hasBaseScal;
            existingStatus.UrlScalEnabled = hasUrlScal;
            existingStatus.GooCrawEnabled = hasGooCraw;
            existingStatus.CenCrawEnabled = hasCenCraw;
        }

        await context.SaveChangesAsync(ct);
    }
}
