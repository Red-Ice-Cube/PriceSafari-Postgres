using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.SchedulePlan;
using PriceSafari.Services.AllegroServices;
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
        var aleBaseScalKey = Environment.GetEnvironmentVariable("ALE_BASE_SCAL");
        var urlScalAleKey = Environment.GetEnvironmentVariable("URL_SCAL_ALE");


        var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "UnknownDevice";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

                var plan = await context.SchedulePlans
                    .AsNoTracking()
                    .FirstOrDefaultAsync(stoppingToken);

                if (plan != null)
                {

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

                            var tasksToRun = dayDetail.Tasks
                                .Where(t => (t.LastRunDate == null || t.LastRunDate.Value.Date < today)
                                            && nowTime >= t.StartTime
                                            && nowTime < t.StartTime.Add(TimeSpan.FromMinutes(5)))
                                .OrderBy(t => t.StartTime)
                                .ToList();

                            foreach (var t in tasksToRun)
                            {

                                bool canRunAnything =
                                (t.UrlEnabled && urlScalKey == "49276583") ||
                                (t.CeneoEnabled && cenCrawKey == "56981467") ||
                                (t.GoogleEnabled && gooCrawKey == "03713857") ||
                                (t.BaseEnabled && baseScalKey == "34692471") ||
                                (t.AleBaseEnabled && aleBaseScalKey == "19892023") ||
                                 (t.UrlScalAleEnabled && urlScalAleKey == "20231989");

                                if (!canRunAnything)
                                {

                                    _logger.LogInformation(
                                        "Urządzenie '{DeviceName}' nie ma odpowiednich kluczy, aby wykonać zadanie '{SessionName}'. Pomijam.",
                                        deviceName, t.SessionName);

                                    continue;
                                }

                                _logger.LogInformation(
                                    "Rozpoczynam wykonywanie zadania '{SessionName}' (StartTime: {StartTime}) na urządzeniu '{DeviceName}'.",
                                    t.SessionName, t.StartTime, deviceName);

                                t.LastRunDate = DateTime.Now;
                                context.ScheduleTasks.Update(t);

                                if (t.BaseEnabled || t.AleBaseEnabled)
                                {
                                    var storeIdsInTask = t.TaskStores.Select(ts => ts.StoreId).Distinct().ToList();
                                    if (storeIdsInTask.Any())
                                    {
                                        var storesToUpdate = await context.Stores
                                            .Where(s => storeIdsInTask.Contains(s.StoreId))
                                            .ToListAsync(stoppingToken);

                                        int tokensConsumed = 0;
                                        foreach (var store in storesToUpdate)
                                        {
                                            if (store.RemainingScrapes > 0)
                                            {
                                                store.RemainingScrapes--;
                                                tokensConsumed++;
                                            }
                                        }

                                        if (tokensConsumed > 0)
                                        {
                                            _logger.LogInformation("Zadanie '{SessionName}': Zużyto {TokensConsumed} token(ów) analizy.", t.SessionName, tokensConsumed);
                                        }
                                    }
                                }

                                await context.SaveChangesAsync(stoppingToken);

                                if (t.UrlEnabled && urlScalKey == "49276583")
                                {
                                    await RunUrlScalAsync(context, deviceName, t, stoppingToken);
                                }
                                if (t.CeneoEnabled && cenCrawKey == "56981467")
                                {
                                    await RunCeneoAsync(context, deviceName, t, stoppingToken);
                                }
                                if (t.GoogleEnabled && gooCrawKey == "03713857")
                                {
                                    await RunGoogleAsync(context, deviceName, t, stoppingToken);
                                }
                                if (t.BaseEnabled && baseScalKey == "34692471")
                                {
                                    await RunBaseScalAsync(context, deviceName, t, stoppingToken);
                                }
                                if (t.AleBaseEnabled && aleBaseScalKey == "19892023")
                                {
                                    await RunAleBaseScalAsync(context, deviceName, t, stoppingToken);
                                }
                                if (t.UrlScalAleEnabled && urlScalAleKey == "20231989")
                                {
                                    await RunUrlScalAleAsync(context, deviceName, t, stoppingToken);
                                }

                                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                            }

                        }
                    }
                }

                if (DateTime.Now - _lastDeviceCheck >= _deviceCheckInterval)
                {
                    _lastDeviceCheck = DateTime.Now;
                    await UpdateDeviceStatusAsync(context, deviceName, baseScalKey, urlScalKey,
                        gooCrawKey, cenCrawKey, aleBaseScalKey, urlScalAleKey, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas wykonywania zaplanowanych zadań.");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

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
                        finishedGoogleLog.Comment += $" | Sukces. Zmielono: {resultDto.TotalScraped} produktów, odrzucono: {resultDto.TotalRejected}. Napotkano CAPCHE: xxx razy.";

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

    private async Task RunAleBaseScalAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        var log = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "ALE_BASE_SCAL",
            StartTime = DateTime.Now,
            Comment = $"Rozpoczęcie scalania Allegro | SessionName={task.SessionName}; Sklepy: {string.Join(", ", task.TaskStores.Select(ts => ts.Store.StoreName))}"
        };
        context.TaskExecutionLogs.Add(log);
        await context.SaveChangesAsync(ct);

        int logId = log.Id;
        int processedStoreCount = 0;

        try
        {
            var allegroProcessingService = context.GetService<AllegroProcessingService>();
            foreach (var stRel in task.TaskStores)
            {

                await allegroProcessingService.ProcessScrapedDataForStoreAsync(stRel.StoreId);
                processedStoreCount++;
            }

            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                finishedLog.Comment += $" | Sukces scalania Allegro. Przetworzono {processedStoreCount} sklepów.";
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                finishedLog.Comment += $" | Wystąpił błąd scalania Allegro: {ex.Message}";
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(ct);
            }
        }
    }

    private async Task RunUrlScalAleAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        var log = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "URL_SCAL_ALE",
            StartTime = DateTime.Now,
            Comment = $"Początek grupowania URL Allegro | SessionName={task.SessionName}; Sklepy: {string.Join(", ", task.TaskStores.Select(ts => ts.Store.StoreName))}"
        };
        context.TaskExecutionLogs.Add(log);
        await context.SaveChangesAsync(ct);

        int logId = log.Id;

        try
        {
            var storeIds = task.TaskStores.Select(ts => ts.StoreId).ToList();
            var allegroUrlGroupingService = context.GetService<AllegroUrlGroupingService>(); // Pobranie serwisu

            var (urlsPrepared, totalProducts, processedStoreNames) = await allegroUrlGroupingService.GroupAndSaveUrls(storeIds);

            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                finishedLog.Comment += $" | Sukces. Przygotowano {urlsPrepared} URL-i z {totalProducts} produktów. Przetworzone sklepy: {string.Join(", ", processedStoreNames)}.";
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                finishedLog.Comment += $" | Wystąpił błąd podczas grupowania URL Allegro: {ex.Message}";
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
        string aleBaseScalKey,
           string urlScalAleKey,

        CancellationToken ct)
    {
        const string BASE_SCAL_EXPECTED = "34692471";
        const string URL_SCAL_EXPECTED = "49276583";
        const string GOO_CRAW_EXPECTED = "03713857";
        const string CEN_CRAW_EXPECTED = "56981467";
        const string ALE_BASE_SCAL_EXPECTED = "19892023";
        const string URL_SCAL_ALE_EXPECTED = "20231989";

        bool hasBaseScal = (baseScalKey == BASE_SCAL_EXPECTED);
        bool hasUrlScal = (urlScalKey == URL_SCAL_EXPECTED);
        bool hasGooCraw = (gooCrawKey == GOO_CRAW_EXPECTED);
        bool hasCenCraw = (cenCrawKey == CEN_CRAW_EXPECTED);
        bool hasAleBaseScal = (aleBaseScalKey == ALE_BASE_SCAL_EXPECTED);
        bool hasUrlScalAle = (urlScalAleKey == URL_SCAL_ALE_EXPECTED);

        var newStatus = new DeviceStatus
        {
            DeviceName = deviceName,
            IsOnline = true,
            LastCheck = DateTime.Now,
            BaseScalEnabled = hasBaseScal,
            UrlScalEnabled = hasUrlScal,
            GooCrawEnabled = hasGooCraw,
            CenCrawEnabled = hasCenCraw,
            AleBaseScalEnabled = hasAleBaseScal,
            UrlScalAleEnabled = hasUrlScalAle
        };

        await context.DeviceStatuses.AddAsync(newStatus, ct);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Zaktualizowano status urządzenia '{DeviceName}'.", deviceName);
    }
}