using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.SchedulePlan;
using PriceSafari.ScrapersControllers;
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
        var aleCrawKey = Environment.GetEnvironmentVariable("ALE_CRAW");
        var aleApiBotKey = Environment.GetEnvironmentVariable("ALE_API_BOT");
        var subKey = Environment.GetEnvironmentVariable("SUBSCRIPTION_KEY");
        var payKey = Environment.GetEnvironmentVariable("GRAB_PAYMENT");

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
                                if ((t.LastRunDateOfTask == null || t.LastRunDateOfTask.Value.Date < today)
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
                                .Where(t => (t.LastRunDateOfTask == null || t.LastRunDateOfTask.Value.Date < today)
                                             && nowTime >= t.StartTime
                                             && nowTime < t.StartTime.Add(TimeSpan.FromMinutes(5)))
                                .OrderBy(t => t.StartTime)
                                .ToList();

                            foreach (var t in tasksToRun)
                            {
                                bool canRunAnything =
                                (t.UrlEnabled && urlScalKey == "83208716") ||
                                (t.CeneoEnabled && cenCrawKey == "84011233") ||
                                (t.GoogleEnabled && gooCrawKey == "63891743") ||
                                (t.BaseEnabled && baseScalKey == "55380981") ||
                                (t.UrlScalAleEnabled && urlScalAleKey == "74902379") ||
                                (t.AleCrawEnabled && aleCrawKey == "13894389") ||
                                (t.AleBaseEnabled && aleBaseScalKey == "64920067") ||
                                (t.AleApiBotEnabled && aleApiBotKey == "00937384");

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

                                t.LastRunDateOfTask = DateTime.Now;
                                context.ScheduleTasks.Update(t);

                                await context.SaveChangesAsync(stoppingToken);

                                if (t.UrlEnabled && urlScalKey == "83208716")
                                    await RunUrlScalAsync(context, deviceName, t, stoppingToken);

                                if (t.CeneoEnabled && cenCrawKey == "84011233")
                                    await RunCeneoAsync(context, deviceName, t, stoppingToken);

                                if (t.GoogleEnabled && gooCrawKey == "63891743")
                                    await RunGoogleAsync(context, deviceName, t, stoppingToken);

                                if (t.BaseEnabled && baseScalKey == "55380981")
                                    await RunBaseScalAsync(context, deviceName, t, stoppingToken);

                                if (t.UrlScalAleEnabled && urlScalAleKey == "74902379")
                                    await RunUrlScalAleAsync(context, deviceName, t, stoppingToken);

                                if (t.AleCrawEnabled && aleCrawKey == "13894389")
                                    await RunAleCrawAsync(context, deviceName, t, stoppingToken);

                                if (t.AleApiBotEnabled && aleApiBotKey == "00937384")
                                {
                                    await RunAleApiBotAsync(context, deviceName, t, stoppingToken);
                                }

                                if (t.AleBaseEnabled && aleBaseScalKey == "64920067")
                                {
                                    await RunAleBaseScalAsync(context, deviceName, t, stoppingToken);
                                }

                                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                            }
                        }
                    }
                }

                if (DateTime.Now - _lastDeviceCheck >= _deviceCheckInterval)
                {
                    _lastDeviceCheck = DateTime.Now;

                    await UpdateDeviceStatusAsync(
                        context,
                        deviceName,
                        baseScalKey, urlScalKey, gooCrawKey, cenCrawKey,
                        aleBaseScalKey, urlScalAleKey, aleCrawKey, aleApiBotKey,
                        subKey, payKey,
                        stoppingToken);
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

                double totalSeconds = (finishedLog.EndTime.Value - finishedLog.StartTime).TotalSeconds;
                double urlsPerSecond = (totalSeconds > 0 && resultDto.TotalUrlsToScrape > 0)
                    ? resultDto.TotalUrlsToScrape / totalSeconds
                    : 0;

                switch (resultDto.Result)
                {
                    case CeneoScraperService.CeneoScrapingResult.Success:
                        finishedLog.Comment += $" | Sukces. Zmielono {resultDto.ScrapedCount}/{resultDto.TotalUrlsToScrape} produktów. Odrzucono: {resultDto.RejectedCount}. Średnia prędkość: {urlsPerSecond:F2} URL/sek.";
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

                double totalSeconds = (finishedGoogleLog.EndTime.Value - finishedGoogleLog.StartTime).TotalSeconds;
                double urlsPerSecond = (totalSeconds > 0 && resultDto.TotalUrlsToScrape > 0)
                    ? resultDto.TotalUrlsToScrape / totalSeconds
                    : 0;

                switch (resultDto.Result)
                {
                    case GoogleScraperService.GoogleScrapingResult.Success:
                        finishedGoogleLog.Comment += $" | Sukces. Zmielono: {resultDto.TotalScraped}/{resultDto.TotalUrlsToScrape} produktów, odrzucono: {resultDto.TotalRejected}. Średnia prędkość: {urlsPerSecond:F2} URL/sek.";
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
            var allegroUrlGroupingService = context.GetService<AllegroUrlGroupingService>();

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
    private async Task RunAleCrawAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        var log = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "ALE_CRAW",
            StartTime = DateTime.Now,
            Comment = $"Inicjowanie procesu scrapowania Allegro | SessionName={task.SessionName}"
        };
        context.TaskExecutionLogs.Add(log);
        await context.SaveChangesAsync(ct);

        int logId = log.Id;

        try
        {
            var allegroScrapingService = context.GetService<AllegroScrapingService>();

            var (success, message, totalUrls) = await allegroScrapingService.StartScrapingProcessAsync();

            if (!success || totalUrls == 0)
            {
                var initialLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
                if (initialLog != null)
                {
                    initialLog.EndTime = DateTime.Now;
                    initialLog.Comment += success ? $" | {message}" : $" | Błąd startu: {message}";
                    context.TaskExecutionLogs.Update(initialLog);
                    await context.SaveChangesAsync(ct);
                }
                return;
            }

            while (AllegroScrapeManager.CurrentStatus == ScrapingProcessStatus.Running && !ct.IsCancellationRequested)
            {

                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }

            var stats = await context.AllegroOffersToScrape
                .GroupBy(o => 1)
                .Select(g => new
                {
                    Scraped = g.Count(o => o.IsScraped),
                    Rejected = g.Count(o => o.IsRejected)
                })
                .FirstOrDefaultAsync(ct);

            int finalScraped = stats?.Scraped ?? 0;
            int finalRejected = stats?.Rejected ?? 0;

            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                double totalSeconds = (finishedLog.EndTime.Value - finishedLog.StartTime).TotalSeconds;
                double urlsPerSecond = (totalSeconds > 0 && totalUrls > 0) ? totalUrls / totalSeconds : 0;

                finishedLog.Comment += $" | Sukces. Przetworzono {finalScraped}/{totalUrls} ofert. Odrzucono: {finalRejected}. Średnia prędkość: {urlsPerSecond:F2} URL/sek.";
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
                finishedLog.Comment += $" | Wystąpił krytyczny błąd podczas scrapowania Allegro: {ex.Message}";
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(ct);
            }
        }
    }

    private async Task RunAleApiBotAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        var log = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "ALE_API_BOT",
            StartTime = DateTime.Now,
            Comment = $"Rozpoczęcie pobierania danych z API Allegro | SessionName={task.SessionName}"
        };
        context.TaskExecutionLogs.Add(log);
        await context.SaveChangesAsync(ct);
        int logId = log.Id;

        try
        {
            var allegroApiBotService = context.GetService<AllegroApiBotService>();

            var result = await allegroApiBotService.ProcessOffersForActiveStoresAsync();

            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;

                if (result.Success)
                {
                    finishedLog.Comment += $" | Sukces. Przetworzono {result.TotalOffersProcessed} ofert w {result.StoresProcessedCount} sklepach.";
                }
                else
                {

                    string errorMessages = string.Join("; ", result.Messages);
                    finishedLog.Comment += $" | Błąd. Przetworzono {result.TotalOffersProcessed} ofert. Szczegóły: {errorMessages}";
                }

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
                finishedLog.Comment += $" | Wystąpił krytyczny błąd: {ex.Message}";
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
             string aleCrawKey,
             string aleApiBotKey,
             string subKey,
             string payKey,
             CancellationToken ct)
    {

        const string BASE_SCAL_EXPECTED = "55380981";
        const string URL_SCAL_EXPECTED = "83208716";
        const string GOO_CRAW_EXPECTED = "63891743";
        const string CEN_CRAW_EXPECTED = "84011233";
        const string ALE_BASE_SCAL_EXPECTED = "64920067";
        const string URL_SCAL_ALE_EXPECTED = "74902379";
        const string ALE_CRAW_EXPECTED = "13894389";
        const string ALE_API_BOT_EXPECTED = "00937384";

        const string SUB_KEY_EXPECTED = "99887766";
        const string PAY_KEY_EXPECTED = "38401048";

        bool hasBaseScal = (baseScalKey == BASE_SCAL_EXPECTED);
        bool hasUrlScal = (urlScalKey == URL_SCAL_EXPECTED);
        bool hasGooCraw = (gooCrawKey == GOO_CRAW_EXPECTED);
        bool hasCenCraw = (cenCrawKey == CEN_CRAW_EXPECTED);
        bool hasAleBaseScal = (aleBaseScalKey == ALE_BASE_SCAL_EXPECTED);
        bool hasUrlScalAle = (urlScalAleKey == URL_SCAL_ALE_EXPECTED);
        bool hasAleCraw = (aleCrawKey == ALE_CRAW_EXPECTED);
        bool hasAleApiBot = (aleApiBotKey == ALE_API_BOT_EXPECTED);

        bool hasInvoiceGen = (subKey == SUB_KEY_EXPECTED);
        bool hasPaymentProc = (payKey == PAY_KEY_EXPECTED);

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
            UrlScalAleEnabled = hasUrlScalAle,
            AleCrawEnabled = hasAleCraw,
            AleApiBotEnabled = hasAleApiBot,

            InvoiceGeneratorEnabled = hasInvoiceGen,
            PaymentProcessorEnabled = hasPaymentProc
        };

        await context.DeviceStatuses.AddAsync(newStatus, ct);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Zaktualizowano status urządzenia '{DeviceName}'. Faktury: {Inv}, Płatności: {Pay}",
            deviceName, hasInvoiceGen, hasPaymentProc);
    }
}