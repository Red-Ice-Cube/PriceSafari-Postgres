
// NOWA METODA USTAWIAJACA WAS EXECUTED NA POCZATEK, 


using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.SchedulePlan;
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
        var baseScalKey = Environment.GetEnvironmentVariable("BASE_SCAL");
        var urlScalKey = Environment.GetEnvironmentVariable("URL_SCAL");
        var gooCrawKey = Environment.GetEnvironmentVariable("GOO_CRAW");
        var cenCrawKey = Environment.GetEnvironmentVariable("CEN_CRAW");

        var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "UnknownDevice";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                // 1) Wczytujemy tylko SchedulePlan (bez Include) – chcemy poznać ID DayDetail
                var plan = await context.SchedulePlans
                    .AsNoTracking()
                    .FirstOrDefaultAsync(stoppingToken);

                if (plan != null)
                {
                    // 2) Określamy, który DayDetailId (int?) nas interesuje
                    var dayOfWeek = DateTime.Now.DayOfWeek;
                    int? neededDayDetailId = dayOfWeek switch
                    {
                        DayOfWeek.Monday => plan.MondayId,
                        DayOfWeek.Tuesday => plan.TuesdayId,
                        DayOfWeek.Wednesday => plan.WednesdayId,
                        DayOfWeek.Thursday => plan.ThursdayId,
                        DayOfWeek.Friday => plan.FridayId,
                        DayOfWeek.Saturday => plan.SaturdayId,
                        DayOfWeek.Sunday => plan.SundayId,
                        _ => null
                    };

                    if (neededDayDetailId.HasValue)
                    {
                        // 3) Wczytujemy TYLKO ten pojedynczy DayDetail
                        var dayDetail = await context.DayDetails
                          
                            .Where(d => d.Id == neededDayDetailId.Value)
                            .Include(d => d.Tasks)
                                .ThenInclude(t => t.TaskStores)
                                    .ThenInclude(ts => ts.Store)
                            .FirstOrDefaultAsync(stoppingToken);

                        if (dayDetail?.Tasks != null)
                        {
                            var nowTime = DateTime.Now.TimeOfDay;
                            var today = DateTime.Today;

                            _logger.LogInformation("Sprawdzanie zadań dla dnia {DayOfWeek} o {Time}.",
                                dayOfWeek, DateTime.Now);

                            // Logowanie nadchodzących zadań (start <= 3h)
                            foreach (var t in dayDetail.Tasks)
                            {
                                if ((t.LastRunDate == null || t.LastRunDate.Value.Date < today)
                                    && nowTime < t.StartTime)
                                {
                                    double minutesToStart = (t.StartTime - nowTime).TotalMinutes;
                                    if (minutesToStart <= 180)
                                    {
                                        _logger.LogInformation(
                                            "Zadanie '{SessionName}' wystartuje za {MinutesToStart:F1} minut.",
                                            t.SessionName, minutesToStart);
                                    }
                                }
                            }

                            // Zadania do uruchomienia
                            var tasksToRun = dayDetail.Tasks
                                .Where(t => (t.LastRunDate == null || t.LastRunDate.Value.Date < today)
                                            && nowTime >= t.StartTime
                                            && nowTime < t.StartTime.Add(TimeSpan.FromMinutes(5)))
                                .OrderBy(t => t.StartTime)
                                .ToList();

                            // 4) Uruchamianie zadań
                            foreach (var t in tasksToRun)
                            {
                                _logger.LogInformation(
                                    "Rozpoczynam wykonywanie zadania '{SessionName}' (StartTime: {StartTime}).",
                                    t.SessionName, t.StartTime);

                                // USTAWIAMY LastRunDate = teraz, od razu
                                // (tak aby kolejne sprawdzenie nie uruchomiło zadania ponownie).
                                t.LastRunDate = DateTime.Now;
                                context.ScheduleTasks.Update(t);
                                await context.SaveChangesAsync(stoppingToken);

                                // Teraz wykonujemy poszczególne operacje (tylko jeśli dany klucz środowiskowy i flaga są włączone)
                                if (urlScalKey == "49276583" && t.UrlEnabled)
                                {
                                    await RunUrlScalAsync(context, deviceName, t, stoppingToken);
                                }
                                if (cenCrawKey == "56981467" && t.CeneoEnabled)
                                {
                                    await RunCeneoAsync(context, deviceName, t, stoppingToken);
                                }
                                if (gooCrawKey == "03713857" && t.GoogleEnabled)
                                {
                                    await RunGoogleAsync(context, deviceName, t, stoppingToken);
                                }
                                if (baseScalKey == "34692471" && t.BaseEnabled)
                                {
                                    await RunBaseScalAsync(context, deviceName, t, stoppingToken);
                                }

                                // Opcjonalne opóźnienie między zadaniami
                                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                            }
                        }
                    }
                }

                // 5) Aktualizujemy status urządzenia co jakiś interwał
                if (DateTime.Now - _lastDeviceCheck >= _deviceCheckInterval)
                {
                    _lastDeviceCheck = DateTime.Now;
                    await UpdateDeviceStatusAsync(context, deviceName, baseScalKey, urlScalKey,
                        gooCrawKey, cenCrawKey, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas wykonywania zaplanowanych zadań.");
            }

            // Co minutę ponawiamy
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    // =========================== POMOCNICZE METODY ===========================

    private async Task RunUrlScalAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        var startLog = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "URL_SCALANIE",
            StartTime = DateTime.Now,
            Comment = $"Początek grupowania URL | SessionName={task.SessionName}; Sklepy: {string.Join(", ", task.TaskStores.Select(ts => ts.Store.StoreName))}"
        };

        context.TaskExecutionLogs.Add(startLog);
        await context.SaveChangesAsync(ct);

        int logId = startLog.Id;

        try
        {
            var storeIds = task.TaskStores.Select(sts => sts.StoreId).Distinct().ToList();
            var urlGroupingService = context.GetService<UrlGroupingService>();
            var (totalProducts, distinctStoreNames) = await urlGroupingService.GroupAndSaveUniqueUrls(storeIds);

            var endLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
            if (endLog != null)
            {
                endLog.EndTime = DateTime.Now;
                endLog.Comment += $" | Sukces grupowania URL. Sklepy: {string.Join(", ", distinctStoreNames)}. Łącznie {totalProducts} produktów.";
                context.TaskExecutionLogs.Update(endLog);
                await context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            var endLog = await context.TaskExecutionLogs.FindAsync(logId, ct);
            if (endLog != null)
            {
                endLog.EndTime = DateTime.Now;
                endLog.Comment += $" | Wystąpił błąd (URL_SCAL): {ex.Message}";
                context.TaskExecutionLogs.Update(endLog);
                await context.SaveChangesAsync(ct);
            }
        }
    }

    private async Task RunCeneoAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        var ceneoLog = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "CENEO_SCRAPER",
            StartTime = DateTime.Now,
            Comment = $"Start scrapowania Ceneo | SessionName={task.SessionName}; Sklepy: {string.Join(", ", task.TaskStores.Select(ts => ts.Store.StoreName))}"
        };

        context.TaskExecutionLogs.Add(ceneoLog);
        await context.SaveChangesAsync(ct);

        int ceneoLogId = ceneoLog.Id;

        try
        {
            var ceneoScraperService = context.GetService<CeneoScraperService>();
            var resultDto = await ceneoScraperService.StartScrapingWithCaptchaHandlingAsync(ct);

            var finishedLog = await context.TaskExecutionLogs.FindAsync(ceneoLogId, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                switch (resultDto.Result)
                {
                    case CeneoScraperService.CeneoScrapingResult.Success:
                        finishedLog.Comment += $" | Sukces. Zmielono {resultDto.ScrapedCount} produktów. Odrzucono: {resultDto.RejectedCount}.";
                        break;
                    case CeneoScraperService.CeneoScrapingResult.NoUrlsFound:
                        finishedLog.Comment += " | Brak URL do scrapowania (NoUrlsFound).";
                        break;
                    case CeneoScraperService.CeneoScrapingResult.SettingsNotFound:
                        finishedLog.Comment += " | Błąd: Brak settingsów (SettingsNotFound).";
                        break;
                    default:
                        finishedLog.Comment += $" | Wystąpił błąd: {resultDto.Message}";
                        break;
                }
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            var finishedLog = await context.TaskExecutionLogs.FindAsync(ceneoLogId, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                finishedLog.Comment += $" | Wystąpił błąd (Ceneo): {ex.Message}";
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(ct);
            }
        }
    }

    private async Task RunGoogleAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        var googleLog = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "GOOGLE_SCRAPER",
            StartTime = DateTime.Now,
            Comment = $"Początek scrapowania Google | SessionName={task.SessionName}; Sklepy: {string.Join(", ", task.TaskStores.Select(ts => ts.Store.StoreName))}"
        };

        context.TaskExecutionLogs.Add(googleLog);
        await context.SaveChangesAsync(ct);

        int googleLogId = googleLog.Id;

        try
        {
            var googleScraperService = context.GetService<GoogleScraperService>();
            var resultDto = await googleScraperService.StartScraping();

            var finishedGoogleLog = await context.TaskExecutionLogs.FindAsync(googleLogId, ct);
            if (finishedGoogleLog != null)
            {
                finishedGoogleLog.EndTime = DateTime.Now;
                switch (resultDto.Result)
                {
                    case GoogleScraperService.GoogleScrapingResult.Success:
                        finishedGoogleLog.Comment += $" | Sukces. Zmielono: {resultDto.TotalScraped} produktów, odrzucono: {resultDto.TotalRejected}.";
                        break;
                    case GoogleScraperService.GoogleScrapingResult.NoProductsToScrape:
                        finishedGoogleLog.Comment += " | Brak produktów do scrapowania.";
                        break;
                    case GoogleScraperService.GoogleScrapingResult.SettingsNotFound:
                        finishedGoogleLog.Comment += " | Błąd: Brak Settings w bazie.";
                        break;
                    default:
                        finishedGoogleLog.Comment += $" | Wystąpił błąd. Szczegóły: {resultDto.Message}";
                        break;
                }
                context.TaskExecutionLogs.Update(finishedGoogleLog);
                await context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            var finishedLog = await context.TaskExecutionLogs.FindAsync(googleLogId, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                finishedLog.Comment += $" | Wystąpił błąd w GoogleScraper: {ex.Message}";
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(ct);
            }
        }
    }

    private async Task RunBaseScalAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        int storeCount = task.TaskStores.Count;
        var baseScalLog = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "TABLICE_SCALANIE",
            StartTime = DateTime.Now,
            Comment = $"Rozpoczęcie scalania bazy (StoreCount: {storeCount}) | SessionName={task.SessionName}; Sklepy: {string.Join(", ", task.TaskStores.Select(ts => ts.Store.StoreName))}"
        };

        context.TaskExecutionLogs.Add(baseScalLog);
        await context.SaveChangesAsync(ct);

        int baseScalLogId = baseScalLog.Id;

        try
        {
            var storeProcessingService = context.GetService<StoreProcessingService>();
            foreach (var stRel in task.TaskStores)
            {
                await storeProcessingService.ProcessStoreAsync(stRel.StoreId);
            }

            var finishedBaseScalLog = await context.TaskExecutionLogs.FindAsync(baseScalLogId, ct);
            if (finishedBaseScalLog != null)
            {
                finishedBaseScalLog.EndTime = DateTime.Now;
                finishedBaseScalLog.Comment += $" | Sukces scalania. Łącznie obsłużono {storeCount} sklepów.";
                context.TaskExecutionLogs.Update(finishedBaseScalLog);
                await context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            var finishedLog = await context.TaskExecutionLogs.FindAsync(baseScalLogId, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                finishedLog.Comment += $" | Wystąpił błąd scalania: {ex.Message}";
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(ct);
            }
        }
    }

    private async Task UpdateDeviceStatusAsync(
        PriceSafariContext context,
        string deviceName,
        string baseScalKey,
        string urlScalKey,
        string gooCrawKey,
        string cenCrawKey,
        CancellationToken ct)
    {
        const string BASE_SCAL_EXPECTED = "34692471";
        const string URL_SCAL_EXPECTED = "49276583";
        const string GOO_CRAW_EXPECTED = "03713857";
        const string CEN_CRAW_EXPECTED = "56981467";

        bool hasBaseScal = (baseScalKey == BASE_SCAL_EXPECTED);
        bool hasUrlScal = (urlScalKey == URL_SCAL_EXPECTED);
        bool hasGooCraw = (gooCrawKey == GOO_CRAW_EXPECTED);
        bool hasCenCraw = (cenCrawKey == CEN_CRAW_EXPECTED);

        var newStatus = new DeviceStatus
        {
            DeviceName = deviceName,
            IsOnline = true,
            LastCheck = DateTime.Now,
            BaseScalEnabled = hasBaseScal,
            UrlScalEnabled = hasUrlScal,
            GooCrawEnabled = hasGooCraw,
            CenCrawEnabled = hasCenCraw
        };

        await context.DeviceStatuses.AddAsync(newStatus, ct);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Zaktualizowano status urządzenia '{DeviceName}'.", deviceName);
    }
}
