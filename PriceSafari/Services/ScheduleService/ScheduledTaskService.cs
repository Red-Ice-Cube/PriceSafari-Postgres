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

                            // Wywołuj, jeśli jest "pożądany" czas
                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
                            {
                                // Najpierw pobierz stores
                                var storeProcessingService = scope.ServiceProvider.GetRequiredService<StoreProcessingService>();
                                var stores = await context.Stores.Where(s => s.AutoMatching).ToListAsync(stoppingToken);

                                // 1) Rejestrujemy start zadania w tabeli TaskExecutionLogs
                                var baseScalLog = new TaskExecutionLog
                                {
                                    DeviceName = deviceName,
                                    OperationName = "BASE_SCAL",
                                    StartTime = DateTime.Now,
                                    Comment = $"Rozpoczęcie scalania bazy (StoreCount: {stores.Count})"
                                };

                                context.TaskExecutionLogs.Add(baseScalLog);
                                await context.SaveChangesAsync(stoppingToken);

                                // Zapamiętujemy ID logu, by później zaktualizować
                                var baseScalLogId = baseScalLog.Id;

                                // Twoja logika autoprocessingu store’ów
                                foreach (var store in stores)
                                {
                                    await storeProcessingService.ProcessStoreAsync(store.StoreId);
                                }

                                // żeby nie wywoływać kilka razy w ciągu tej samej minuty
                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                                // 2) Aktualizujemy log – koniec zadania
                                var finishedBaseScalLog = await context.TaskExecutionLogs.FindAsync(baseScalLogId);
                                if (finishedBaseScalLog != null)
                                {
                                    finishedBaseScalLog.EndTime = DateTime.Now;
                                    finishedBaseScalLog.Comment += $" | Zakończono scalanie. Łącznie obsłużono {stores.Count} store’ów.";
                                    context.TaskExecutionLogs.Update(finishedBaseScalLog);
                                    await context.SaveChangesAsync(stoppingToken);
                                }
                            }
                        }


                        if (urlScalKey == "49276583" && scheduledTask.UrlIsEnabled)
                        {
                            var now = DateTime.Now.TimeOfDay;
                            var timeDifference = now - scheduledTask.UrlScheduledTime;

                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
                            {
                                // 1) Stworzenie wpisu w TaskExecutionLogs na początek operacji
                                var startLog = new TaskExecutionLog
                                {
                                    DeviceName = deviceName,
                                    OperationName = "URL_SCAL",
                                    StartTime = DateTime.Now,
                                    Comment = "Początek grupowania URL"
                                };
                                context.TaskExecutionLogs.Add(startLog);
                                await context.SaveChangesAsync(stoppingToken);

                                // Zapamiętujemy ID logu do późniejszego update
                                var logId = startLog.Id;

                                // 2) Wywołanie usługi grupowania URL i odebranie zwracanych statystyk
                                var urlGroupingService = scope.ServiceProvider.GetRequiredService<UrlGroupingService>();
                                var (totalProducts, distinctStoreNames) = await urlGroupingService.GroupAndSaveUniqueUrls();

                                // 3) Aby nie wykonywać kilka razy w ciągu tej samej minuty
                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                                // 4) Aktualizacja wpisu w logu – koniec zadania
                                var endLog = await context.TaskExecutionLogs.FindAsync(logId);
                                if (endLog != null)
                                {
                                    endLog.EndTime = DateTime.Now;
                                    endLog.Comment += $" | Zakończono grupowanie URL. " +
                                                      $"Sklepy: {string.Join(", ", distinctStoreNames)}. " +
                                                      $"Łącznie {totalProducts} produktów.";

                                    context.TaskExecutionLogs.Update(endLog);
                                    await context.SaveChangesAsync(stoppingToken);
                                }
                            }
                        }

                        if (gooCrawKey == "03713857" && scheduledTask.GoogleIsEnabled)
                        {
                            var now = DateTime.Now.TimeOfDay;
                            var timeDifference = now - scheduledTask.GoogleScheduledTime;

                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
                            {
                                // 1) Tworzymy wpis logu (start)
                                var googleLog = new TaskExecutionLog
                                {
                                    DeviceName = deviceName,
                                    OperationName = "GOO_CRAW",
                                    StartTime = DateTime.Now,
                                    Comment = "Początek scrapowania Google"
                                };
                                context.TaskExecutionLogs.Add(googleLog);
                                await context.SaveChangesAsync(stoppingToken);

                                var googleLogId = googleLog.Id;

                                // 2) Wywołujemy scrapowanie
                                var googleScraperService = scope.ServiceProvider.GetRequiredService<GoogleScraperService>();
                                var resultDto = await googleScraperService.StartScraping();

                                // 3) Czekamy minutę, aby nie wywoływać kilka razy w ciągu tej samej minuty
                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                                // 4) Aktualizujemy wpis logu (koniec)
                                var finishedGoogleLog = await context.TaskExecutionLogs.FindAsync(googleLogId);
                                if (finishedGoogleLog != null)
                                {
                                    finishedGoogleLog.EndTime = DateTime.Now;

                                    // Możesz dobrać treść 'Comment' w zależności od wyniku
                                    switch (resultDto.Result)
                                    {
                                        case GoogleScraperService.GoogleScrapingResult.Success:
                                            finishedGoogleLog.Comment += $" | Sukces. Zeskrapowano: {resultDto.TotalScraped} ofert.";
                                            break;

                                        case GoogleScraperService.GoogleScrapingResult.NoProductsToScrape:
                                            finishedGoogleLog.Comment += " | Brak produktów do scrapowania.";
                                            break;

                                        case GoogleScraperService.GoogleScrapingResult.SettingsNotFound:
                                            finishedGoogleLog.Comment += " | Błąd: Brak Settings w bazie.";
                                            break;

                                        case GoogleScraperService.GoogleScrapingResult.Error:
                                        default:
                                            finishedGoogleLog.Comment += $" | Wystąpił błąd. Szczegóły: {resultDto.Message}";
                                            break;
                                    }

                                    context.TaskExecutionLogs.Update(finishedGoogleLog);
                                    await context.SaveChangesAsync(stoppingToken);
                                }
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
