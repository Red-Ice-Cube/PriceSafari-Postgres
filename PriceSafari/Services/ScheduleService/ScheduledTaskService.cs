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

    // Ostatnie sprawdzenie stanu urządzenia
    private DateTime _lastDeviceCheck = DateTime.MinValue;
    // Interwał aktualizacji stanu urządzenia
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

                // Pobranie planu
                var plan = await context.SchedulePlans
                    .Include(sp => sp.Monday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Tuesday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Wednesday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Thursday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Friday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Saturday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .Include(sp => sp.Sunday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
                    .FirstOrDefaultAsync(stoppingToken);

                if (plan != null)
                {
                    var dayOfWeek = DateTime.Now.DayOfWeek;
                    var nowTime = DateTime.Now.TimeOfDay;
                    var today = DateTime.Today;

                    // Wybór odpowiedniego planu na dany dzień
                    DayDetail dayDetail = dayOfWeek switch
                    {
                        DayOfWeek.Monday => plan.Monday,
                        DayOfWeek.Tuesday => plan.Tuesday,
                        DayOfWeek.Wednesday => plan.Wednesday,
                        DayOfWeek.Thursday => plan.Thursday,
                        DayOfWeek.Friday => plan.Friday,
                        DayOfWeek.Saturday => plan.Saturday,
                        DayOfWeek.Sunday => plan.Sunday,
                        _ => null
                    };

                    if (dayDetail?.Tasks != null)
                    {
                        _logger.LogInformation("Sprawdzanie zadań dla dnia {DayOfWeek} o czasie {Time}.", dayOfWeek, DateTime.Now);

                        // Opcjonalnie: logowanie nadchodzących zadań (jeśli start jest za mniej niż 3h)
                        foreach (var t in dayDetail.Tasks)
                        {
                            if ((t.LastRunDate == null || t.LastRunDate.Value.Date < today) && nowTime < t.StartTime)
                            {
                                double minutesToStart = (t.StartTime - nowTime).TotalMinutes;
                                if (minutesToStart <= 180)
                                {
                                    _logger.LogInformation(
                                        "Zadanie '{SessionName}' wystartuje za {MinutesToStart:F1} minut.",
                                        t.SessionName, minutesToStart
                                    );
                                }
                            }
                        }

                        // Wybieramy tylko zadania, które jeszcze nie były uruchomione i których czas startu już minął
                        var tasksToRun = dayDetail.Tasks
                         .Where(t => (t.LastRunDate == null || t.LastRunDate.Value.Date < today)
                                && nowTime >= t.StartTime
                                && nowTime < t.StartTime.Add(TimeSpan.FromMinutes(5))) 
                         .OrderBy(t => t.StartTime)
                         .ToList();


                        foreach (var t in tasksToRun)
                        {
                            _logger.LogInformation("Rozpoczynam wykonywanie zadania '{SessionName}' (StartTime: {StartTime}).",
                                t.SessionName, t.StartTime);

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

                            // Oznaczamy zadanie jako uruchomione
                            t.LastRunDate = DateTime.Now;
                            context.ScheduleTasks.Update(t);
                            await context.SaveChangesAsync(stoppingToken);

                            // Opcjonalnie: krótkie opóźnienie między zadaniami
                            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                        }
                    }
                }

                // Aktualizacja statusu urządzenia co określony interwał
                if (DateTime.Now - _lastDeviceCheck >= _deviceCheckInterval)
                {
                    _lastDeviceCheck = DateTime.Now;
                    await UpdateDeviceStatusAsync(context, deviceName, baseScalKey, urlScalKey, gooCrawKey, cenCrawKey, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił błąd podczas wykonywania zaplanowanych zadań.");
            }

            // Zwiększony interwał odpytania do 60 sekund – przy zachowaniu logiki, że zadania uruchomione są, gdy czas startu już minął
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }


    // =========================== POMOCNICZE METODY ===========================

    private async Task RunUrlScalAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        // 1. Utworzenie rekordu logu przed rozpoczęciem operacji
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
            // 2. Logika zadania – pobranie listy sklepów i przetworzenie URL
            var storeIds = task.TaskStores.Select(sts => sts.StoreId).Distinct().ToList();
            var urlGroupingService = context.GetService<UrlGroupingService>();
            var (totalProducts, distinctStoreNames) = await urlGroupingService.GroupAndSaveUniqueUrls(storeIds);

            // 3. Uaktualnienie logu po zakończeniu zadania
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
            // 4. W przypadku błędu – uaktualnij log o informację o wyjątku
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






















//using Microsoft.EntityFrameworkCore;
//using Microsoft.EntityFrameworkCore.Infrastructure;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using PriceSafari.Models.SchedulePlan;
//using PriceSafari.Services.ScheduleService;

//public class ScheduledTaskService : BackgroundService
//{
//    private readonly IServiceScopeFactory _scopeFactory;
//    private readonly ILogger<ScheduledTaskService> _logger;

//    private DateTime _lastDeviceCheck = DateTime.MinValue;
//    private readonly TimeSpan _deviceCheckInterval = TimeSpan.FromMinutes(10);

//    public ScheduledTaskService(IServiceScopeFactory scopeFactory, ILogger<ScheduledTaskService> logger)
//    {
//        _scopeFactory = scopeFactory;
//        _logger = logger;
//    }

//    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//    {
//        var baseScalKey = Environment.GetEnvironmentVariable("BASE_SCAL");
//        var urlScalKey = Environment.GetEnvironmentVariable("URL_SCAL");
//        var gooCrawKey = Environment.GetEnvironmentVariable("GOO_CRAW");
//        var cenCrawKey = Environment.GetEnvironmentVariable("CEN_CRAW");

//        var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "UnknownDevice";

//        while (!stoppingToken.IsCancellationRequested)
//        {
//            try
//            {
//                using var scope = _scopeFactory.CreateScope();
//                var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();

//                // 1) Wczytanie planu
//                var plan = await context.SchedulePlans
//                    .Include(sp => sp.Monday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
//                    .Include(sp => sp.Tuesday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
//                    .Include(sp => sp.Wednesday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
//                    .Include(sp => sp.Thursday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
//                    .Include(sp => sp.Friday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
//                    .Include(sp => sp.Saturday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
//                    .Include(sp => sp.Sunday).ThenInclude(d => d.Tasks).ThenInclude(t => t.TaskStores).ThenInclude(ts => ts.Store)
//                    .FirstOrDefaultAsync(stoppingToken);

//                if (plan != null)
//                {
//                    // 2) Wybór DayDetail w zależności od bieżącego dnia
//                    var dayOfWeek = DateTime.Now.DayOfWeek;
//                    var nowTime = DateTime.Now.TimeOfDay;
//                    var today = DateTime.Today;

//                    DayDetail dayDetail = dayOfWeek switch
//                    {
//                        DayOfWeek.Monday => plan.Monday,
//                        DayOfWeek.Tuesday => plan.Tuesday,
//                        DayOfWeek.Wednesday => plan.Wednesday,
//                        DayOfWeek.Thursday => plan.Thursday,
//                        DayOfWeek.Friday => plan.Friday,
//                        DayOfWeek.Saturday => plan.Saturday,
//                        DayOfWeek.Sunday => plan.Sunday,
//                        _ => null
//                    };

//                    if (dayDetail?.Tasks != null)
//                    {

//                        var activeTasks = dayDetail.Tasks
//                            .Where(t =>
//                            {
//                                // Różnica w minutach pomiędzy nowTime a t.StartTime
//                                double diffMinutes = (nowTime - t.StartTime).TotalMinutes;
//                                // Oczekujemy np. [0..1) => tzn. jest od 0 do <1 min po "start"
//                                bool isStartMoment = (diffMinutes >= 0 && diffMinutes < 3);


//                                bool notRunToday = (t.LastRunDate == null || t.LastRunDate < today);

//                                return isStartMoment && notRunToday;
//                            })
//                            .OrderBy(t => t.StartTime)
//                            .ToList();

//                        // Wykonujemy zadania w pętli
//                        foreach (var task in activeTasks)
//                        {
//                            // URL
//                            if (urlScalKey == "49276583" && task.UrlEnabled)
//                            {
//                                await RunUrlScalAsync(context, deviceName, task, stoppingToken);
//                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
//                            }

//                            // CENEO
//                            if (cenCrawKey == "56981467" && task.CeneoEnabled)
//                            {
//                                await RunCeneoAsync(context, deviceName, task, stoppingToken);
//                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
//                            }

//                            // GOOGLE
//                            if (gooCrawKey == "03713857" && task.GoogleEnabled)
//                            {
//                                await RunGoogleAsync(context, deviceName, task, stoppingToken);
//                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
//                            }

//                            // BASE
//                            if (baseScalKey == "34692471" && task.BaseEnabled)
//                            {
//                                await RunBaseScalAsync(context, deviceName, task, stoppingToken);
//                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
//                            }

//                            // Oznaczamy LastRunDate
//                            task.LastRunDate = today;
//                            context.ScheduleTasks.Update(task);
//                            await context.SaveChangesAsync(stoppingToken);
//                        }
//                    }
//                }

//                // Okresowo update device status
//                if (DateTime.Now - _lastDeviceCheck >= _deviceCheckInterval)
//                {
//                    _lastDeviceCheck = DateTime.Now;
//                    await UpdateDeviceStatusAsync(context, deviceName, baseScalKey, urlScalKey, gooCrawKey, cenCrawKey, stoppingToken);
//                }
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "An error occurred while executing scheduled tasks.");
//            }

//            // co 30 sek
//            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
//        }
//    }

//    // =========================== POMOCNICZE METODY ===========================

//    private async Task RunUrlScalAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
//    {
//        var startLog = new TaskExecutionLog
//        {
//            DeviceName = deviceName,
//            OperationName = "URL_SCALANIE",
//            StartTime = DateTime.Now,
//            Comment = $"Początek grupowania URL | SessionName={task.SessionName}; " +
//                      $"Sklepy: {string.Join(", ", task.TaskStores.Select(ts => ts.Store.StoreName))}"
//        };
//        context.TaskExecutionLogs.Add(startLog);
//        await context.SaveChangesAsync(ct);

//        int logId = startLog.Id;

//        try
//        {
//            // 1) Pobierz listę StoreId z powiązanych z zadaniem sklepów
//            var storeIds = task.TaskStores
//                .Select(sts => sts.StoreId)
//                .Distinct()
//                .ToList();

//            // 2) Wywołaj nową metodę serwisu z listą storeIds
//            var urlGroupingService = context.GetService<UrlGroupingService>();
//            var (totalProducts, distinctStoreNames) = await urlGroupingService.GroupAndSaveUniqueUrls(storeIds);

//            // 3) Log KONIEC
//            var endLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
//            if (endLog != null)
//            {
//                endLog.EndTime = DateTime.Now;
//                endLog.Comment += $" | Sukces grupowania URL. " +
//                                  $"Sklepy: {string.Join(", ", distinctStoreNames)}. " +
//                                  $"Łącznie {totalProducts} produktów.";

//                context.TaskExecutionLogs.Update(endLog);
//                await context.SaveChangesAsync(ct);
//            }
//        }
//        catch (Exception ex)
//        {
//            var endLog = await context.TaskExecutionLogs.FindAsync(logId, ct);
//            if (endLog != null)
//            {
//                endLog.EndTime = DateTime.Now;
//                endLog.Comment += $" | Wystąpił błąd (URL_SCAL): {ex.Message}";
//                context.TaskExecutionLogs.Update(endLog);
//                await context.SaveChangesAsync(ct);
//            }
//        }
//    }


//    private async Task RunCeneoAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
//    {
//        // 1) LOG start
//        var ceneoLog = new TaskExecutionLog
//        {
//            DeviceName = deviceName,
//            OperationName = "CENEO_SCRAPER",
//            StartTime = DateTime.Now,
//            Comment = $"Start scrapowania Ceneo z captchą | SessionName={task.SessionName}; " +
//                      $"Sklepy: {string.Join(", ", task.TaskStores.Select(ts => ts.Store.StoreName))}"
//        };
//        context.TaskExecutionLogs.Add(ceneoLog);
//        await context.SaveChangesAsync(ct);

//        int ceneoLogId = ceneoLog.Id;
//        try
//        {
//            // 2) Faktyczny serwis
//            var ceneoScraperService = context.GetService<CeneoScraperService>();
//            var resultDto = await ceneoScraperService.StartScrapingWithCaptchaHandlingAsync(ct);

//            // 4) LOG koniec
//            var finishedLog = await context.TaskExecutionLogs.FindAsync(ceneoLogId, ct);
//            if (finishedLog != null)
//            {
//                finishedLog.EndTime = DateTime.Now;
//                switch (resultDto.Result)
//                {
//                    case CeneoScraperService.CeneoScrapingResult.Success:
//                        finishedLog.Comment += $" | Sukces. Zmielono {resultDto.ScrapedCount} produktów. Odrzucono: {resultDto.RejectedCount}.";
//                        break;
//                    case CeneoScraperService.CeneoScrapingResult.NoUrlsFound:
//                        finishedLog.Comment += " | Brak URL do scrapowania (NoUrlsFound).";
//                        break;
//                    case CeneoScraperService.CeneoScrapingResult.SettingsNotFound:
//                        finishedLog.Comment += " | Błąd: Brak settingsów (SettingsNotFound).";
//                        break;
//                    case CeneoScraperService.CeneoScrapingResult.Error:
//                    default:
//                        finishedLog.Comment += $" | Wystąpił błąd: {resultDto.Message}";
//                        break;
//                }

//                context.TaskExecutionLogs.Update(finishedLog);
//                await context.SaveChangesAsync(ct);
//            }

//            if (resultDto.Result == CeneoScraperService.CeneoScrapingResult.Success)
//            {
//                _logger.LogInformation("Ceneo scraping completed successfully.");
//            }
//            else if (resultDto.Result == CeneoScraperService.CeneoScrapingResult.NoUrlsFound)
//            {
//                _logger.LogInformation("No URLs to scrape for Ceneo.");
//            }
//        }
//        catch (Exception ex)
//        {
//            var finishedLog = await context.TaskExecutionLogs.FindAsync(ceneoLogId, ct);
//            if (finishedLog != null)
//            {
//                finishedLog.EndTime = DateTime.Now;
//                finishedLog.Comment += $" | Wystąpił błąd (Ceneo): {ex.Message}";
//                context.TaskExecutionLogs.Update(finishedLog);
//                await context.SaveChangesAsync(ct);
//            }
//        }
//    }

//    private async Task RunGoogleAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
//    {
//        // 1) LOG start
//        var googleLog = new TaskExecutionLog
//        {
//            DeviceName = deviceName,
//            OperationName = "GOOGLE_SCRAPER",
//            StartTime = DateTime.Now,
//            Comment = $"Początek scrapowania Google | SessionName={task.SessionName}; " +
//                      $"Sklepy: {string.Join(", ", task.TaskStores.Select(ts => ts.Store.StoreName))}"
//        };
//        context.TaskExecutionLogs.Add(googleLog);
//        await context.SaveChangesAsync(ct);

//        int googleLogId = googleLog.Id;
//        try
//        {
//            // 2) Faktyczna usługa
//            var googleScraperService = context.GetService<GoogleScraperService>();
//            var resultDto = await googleScraperService.StartScraping();

//            // 4) LOG koniec
//            var finishedGoogleLog = await context.TaskExecutionLogs.FindAsync(googleLogId, ct);
//            if (finishedGoogleLog != null)
//            {
//                finishedGoogleLog.EndTime = DateTime.Now;

//                switch (resultDto.Result)
//                {
//                    case GoogleScraperService.GoogleScrapingResult.Success:
//                        finishedGoogleLog.Comment += $" | Sukces. Zmielono: {resultDto.TotalScraped} produktów, odrzucono: {resultDto.TotalRejected}.";
//                        break;
//                    case GoogleScraperService.GoogleScrapingResult.NoProductsToScrape:
//                        finishedGoogleLog.Comment += " | Brak produktów do scrapowania.";
//                        break;
//                    case GoogleScraperService.GoogleScrapingResult.SettingsNotFound:
//                        finishedGoogleLog.Comment += " | Błąd: Brak Settings w bazie.";
//                        break;
//                    case GoogleScraperService.GoogleScrapingResult.Error:
//                    default:
//                        finishedGoogleLog.Comment += $" | Wystąpił błąd. Szczegóły: {resultDto.Message}";
//                        break;
//                }

//                context.TaskExecutionLogs.Update(finishedGoogleLog);
//                await context.SaveChangesAsync(ct);
//            }
//        }
//        catch (Exception ex)
//        {
//            var finishedLog = await context.TaskExecutionLogs.FindAsync(googleLogId, ct);
//            if (finishedLog != null)
//            {
//                finishedLog.EndTime = DateTime.Now;
//                finishedLog.Comment += $" | Błąd GoogleScraper: {ex.Message}";
//                context.TaskExecutionLogs.Update(finishedLog);
//                await context.SaveChangesAsync(ct);
//            }
//        }
//    }

//    private async Task RunBaseScalAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
//    {
//        // 1) Log - START
//        int storeCount = task.TaskStores.Count;
//        var baseScalLog = new TaskExecutionLog
//        {
//            DeviceName = deviceName,
//            OperationName = "TABLICE_SCALANIE",
//            StartTime = DateTime.Now,
//            Comment = $"Rozpoczęcie scalania bazy (StoreCount: {storeCount}) | SessionName={task.SessionName}; " +
//                      $"Sklepy: {string.Join(", ", task.TaskStores.Select(ts => ts.Store.StoreName))}"
//        };
//        context.TaskExecutionLogs.Add(baseScalLog);
//        await context.SaveChangesAsync(ct);

//        int baseScalLogId = baseScalLog.Id;

//        try
//        {
//            // 2) storeProcessingService
//            var storeProcessingService = context.GetService<StoreProcessingService>();
//            // Przykładowo: przetwarzamy sklepy w tym tasku
//            foreach (var stRel in task.TaskStores)
//            {
//                await storeProcessingService.ProcessStoreAsync(stRel.StoreId);
//            }

//            // 4) Log - KONIEC
//            var finishedBaseScalLog = await context.TaskExecutionLogs.FindAsync(baseScalLogId, ct);
//            if (finishedBaseScalLog != null)
//            {
//                finishedBaseScalLog.EndTime = DateTime.Now;
//                finishedBaseScalLog.Comment += $" | Sukces scalania. Łącznie obsłużono {storeCount} kanałów.";
//                context.TaskExecutionLogs.Update(finishedBaseScalLog);
//                await context.SaveChangesAsync(ct);
//            }
//        }
//        catch (Exception ex)
//        {
//            var finishedLog = await context.TaskExecutionLogs.FindAsync(baseScalLogId, ct);
//            if (finishedLog != null)
//            {
//                finishedLog.EndTime = DateTime.Now;
//                finishedLog.Comment += $" | Wystąpił błąd scalania: {ex.Message}";
//                context.TaskExecutionLogs.Update(finishedLog);
//                await context.SaveChangesAsync(ct);
//            }
//        }
//    }

//    // =========================
//    // UpdateDeviceStatusAsync 
//    // =========================
//    private async Task UpdateDeviceStatusAsync(
//        PriceSafariContext context,
//        string deviceName,
//        string baseScalKey,
//        string urlScalKey,
//        string gooCrawKey,
//        string cenCrawKey,
//        CancellationToken ct)
//    {
//        // 1. Odczyt twardych wartości
//        const string BASE_SCAL_EXPECTED = "34692471";
//        const string URL_SCAL_EXPECTED = "49276583";
//        const string GOO_CRAW_EXPECTED = "03713857";
//        const string CEN_CRAW_EXPECTED = "56981467";

//        // 2. Czy .env pasuje?
//        bool hasBaseScal = (baseScalKey == BASE_SCAL_EXPECTED);
//        bool hasUrlScal = (urlScalKey == URL_SCAL_EXPECTED);
//        bool hasGooCraw = (gooCrawKey == GOO_CRAW_EXPECTED);
//        bool hasCenCraw = (cenCrawKey == CEN_CRAW_EXPECTED);

//        // 3. Zapis w DeviceStatuses
//        var newStatus = new DeviceStatus
//        {
//            DeviceName = deviceName,
//            IsOnline = true,
//            LastCheck = DateTime.Now,
//            BaseScalEnabled = hasBaseScal,
//            UrlScalEnabled = hasUrlScal,
//            GooCrawEnabled = hasGooCraw,
//            CenCrawEnabled = hasCenCraw
//        };

//        await context.DeviceStatuses.AddAsync(newStatus, ct);
//        await context.SaveChangesAsync(ct);
//    }
//}





























//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using PriceSafari.Services.ScheduleService;

//public class ScheduledTaskService : BackgroundService
//{
//    private readonly IServiceScopeFactory _scopeFactory;
//    private readonly ILogger<ScheduledTaskService> _logger;

//    private DateTime _lastDeviceCheck = DateTime.MinValue;
//    private readonly TimeSpan _deviceCheckInterval = TimeSpan.FromMinutes(10);

//    public ScheduledTaskService(IServiceScopeFactory scopeFactory, ILogger<ScheduledTaskService> logger)
//    {
//        _scopeFactory = scopeFactory;
//        _logger = logger;
//    }

//    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//    {
//        // klucze z .env
//        var baseScalKey = Environment.GetEnvironmentVariable("BASE_SCAL");
//        var urlScalKey = Environment.GetEnvironmentVariable("URL_SCAL");
//        var gooCrawKey = Environment.GetEnvironmentVariable("GOO_CRAW");
//        var cenCrawKey = Environment.GetEnvironmentVariable("CEN_CRAW");

//        var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME")
//                     ?? "UnknownDevice";


//        while (!stoppingToken.IsCancellationRequested)
//        {
//            try
//            {
//                using (var scope = _scopeFactory.CreateScope())
//                {
//                    var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//                    var scheduledTask = await context.ScheduledTasks.FirstOrDefaultAsync();

//                    if (scheduledTask != null)
//                    {
//                        // 1) SPRAWDZANIE AKCJI POWIĄZANEJ Z BASE_SCAL - Scalanie Bazy

//                        if (baseScalKey == "34692471" && scheduledTask.IsEnabled)
//                        {
//                            var now = DateTime.Now.TimeOfDay;
//                            var timeDifference = now - scheduledTask.ScheduledTime;

//                            // Wywołuj, jeśli jest "pożądany" czas
//                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
//                            {
//                                // Najpierw pobierz stores
//                                var storeProcessingService = scope.ServiceProvider.GetRequiredService<StoreProcessingService>();
//                                var stores = await context.Stores.Where(s => s.AutoMatching).ToListAsync(stoppingToken);

//                                // 1) Rejestrujemy start zadania w tabeli TaskExecutionLogs
//                                var baseScalLog = new TaskExecutionLog
//                                {
//                                    DeviceName = deviceName,
//                                    OperationName = "TABLICE_SCALANIE",
//                                    StartTime = DateTime.Now,
//                                    Comment = $"Rozpoczęcie scalania bazy (StoreCount: {stores.Count})"
//                                };

//                                context.TaskExecutionLogs.Add(baseScalLog);
//                                await context.SaveChangesAsync(stoppingToken);

//                                // Zapamiętujemy ID logu, by później zaktualizować
//                                var baseScalLogId = baseScalLog.Id;

//                                // Twoja logika autoprocessingu store’ów
//                                foreach (var store in stores)
//                                {
//                                    await storeProcessingService.ProcessStoreAsync(store.StoreId);
//                                }

//                                // żeby nie wywoływać kilka razy w ciągu tej samej minuty
//                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

//                                // 2) Aktualizujemy log – koniec zadania
//                                var finishedBaseScalLog = await context.TaskExecutionLogs.FindAsync(baseScalLogId);
//                                if (finishedBaseScalLog != null)
//                                {
//                                    finishedBaseScalLog.EndTime = DateTime.Now;
//                                    finishedBaseScalLog.Comment += $" | Sukces scalania. Łącznie obsłużono {stores.Count} kanałów.";
//                                    context.TaskExecutionLogs.Update(finishedBaseScalLog);
//                                    await context.SaveChangesAsync(stoppingToken);
//                                }
//                            }
//                        }


//                        if (urlScalKey == "49276583" && scheduledTask.UrlIsEnabled)
//                        {
//                            var now = DateTime.Now.TimeOfDay;
//                            var timeDifference = now - scheduledTask.UrlScheduledTime;

//                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
//                            {
//                                // 1) Stworzenie wpisu w TaskExecutionLogs na początek operacji
//                                var startLog = new TaskExecutionLog
//                                {
//                                    DeviceName = deviceName,
//                                    OperationName = "URL_SCALANIE",
//                                    StartTime = DateTime.Now,
//                                    Comment = "Początek grupowania URL"
//                                };
//                                context.TaskExecutionLogs.Add(startLog);
//                                await context.SaveChangesAsync(stoppingToken);

//                                // Zapamiętujemy ID logu do późniejszego update
//                                var logId = startLog.Id;

//                                // 2) Wywołanie usługi grupowania URL i odebranie zwracanych statystyk
//                                var urlGroupingService = scope.ServiceProvider.GetRequiredService<UrlGroupingService>();
//                                var (totalProducts, distinctStoreNames) = await urlGroupingService.GroupAndSaveUniqueUrls();

//                                // 3) Aby nie wykonywać kilka razy w ciągu tej samej minuty
//                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

//                                // 4) Aktualizacja wpisu w logu – koniec zadania
//                                var endLog = await context.TaskExecutionLogs.FindAsync(logId);
//                                if (endLog != null)
//                                {
//                                    endLog.EndTime = DateTime.Now;
//                                    endLog.Comment += $" | Sukces grupowania URL. " +
//                                                      $"Sklepy: {string.Join(", ", distinctStoreNames)}. " +
//                                                      $"Łącznie {totalProducts} produktów.";

//                                    context.TaskExecutionLogs.Update(endLog);
//                                    await context.SaveChangesAsync(stoppingToken);
//                                }
//                            }
//                        }

//                        if (gooCrawKey == "03713857" && scheduledTask.GoogleIsEnabled)
//                        {
//                            var now = DateTime.Now.TimeOfDay;
//                            var timeDifference = now - scheduledTask.GoogleScheduledTime;

//                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
//                            {
//                                // 1) Tworzymy wpis logu (start)
//                                var googleLog = new TaskExecutionLog
//                                {
//                                    DeviceName = deviceName,
//                                    OperationName = "GOOGLE_SCRAPER",
//                                    StartTime = DateTime.Now,
//                                    Comment = "Początek scrapowania Google"
//                                };
//                                context.TaskExecutionLogs.Add(googleLog);
//                                await context.SaveChangesAsync(stoppingToken);

//                                var googleLogId = googleLog.Id;

//                                // 2) Wywołujemy scrapowanie
//                                var googleScraperService = scope.ServiceProvider.GetRequiredService<GoogleScraperService>();
//                                var resultDto = await googleScraperService.StartScraping();

//                                // 4) Aktualizujemy wpis logu (koniec)
//                                var finishedGoogleLog = await context.TaskExecutionLogs.FindAsync(googleLogId);
//                                if (finishedGoogleLog != null)
//                                {
//                                    finishedGoogleLog.EndTime = DateTime.Now;

//                                    switch (resultDto.Result)
//                                    {
//                                        case GoogleScraperService.GoogleScrapingResult.Success:
//                                            // Tutaj uwzględniasz liczbę zeskrapowanych i odrzuconych
//                                            finishedGoogleLog.Comment += $" | Sukces. Zmielono: {resultDto.TotalScraped} produktów, odrzucono: {resultDto.TotalRejected}.";
//                                            break;

//                                        case GoogleScraperService.GoogleScrapingResult.NoProductsToScrape:
//                                            finishedGoogleLog.Comment += " | Brak produktów do scrapowania.";
//                                            break;

//                                        case GoogleScraperService.GoogleScrapingResult.SettingsNotFound:
//                                            finishedGoogleLog.Comment += " | Błąd: Brak Settings w bazie.";
//                                            break;

//                                        case GoogleScraperService.GoogleScrapingResult.Error:
//                                        default:
//                                            finishedGoogleLog.Comment += $" | Wystąpił błąd. Szczegóły: {resultDto.Message}";
//                                            break;
//                                    }

//                                    context.TaskExecutionLogs.Update(finishedGoogleLog);
//                                    await context.SaveChangesAsync(stoppingToken);
//                                }

//                            }
//                        }



//                        // 4) SPRAWDZANIE AKCJI POWIĄZANEJ Z CEN_SCRAPER
//                        if (cenCrawKey == "56981467" && scheduledTask.CeneoIsEnabled)
//                        {
//                            var now = DateTime.Now.TimeOfDay;
//                            var timeDifference = now - scheduledTask.CeneoScheduledTime;

//                            if (timeDifference.TotalMinutes >= 0 && timeDifference.TotalMinutes < 1)
//                            {


//                                // 1) Log w TaskExecutionLogs (start)
//                                var ceneoLog = new TaskExecutionLog
//                                {
//                                    DeviceName = deviceName,
//                                    OperationName = "CENEO_SCRAPER",
//                                    StartTime = DateTime.Now,
//                                    Comment = "Start scrapowania Ceneo z captchą"
//                                };
//                                context.TaskExecutionLogs.Add(ceneoLog);
//                                await context.SaveChangesAsync(stoppingToken);

//                                int ceneoLogId = ceneoLog.Id;

//                                // 2) Wstrzykujemy serwis i wykonujemy scraping
//                                var ceneoScraperService = scope.ServiceProvider.GetRequiredService<CeneoScraperService>();
//                                var resultDto = await ceneoScraperService.StartScrapingWithCaptchaHandlingAsync(stoppingToken);

//                                // 3) Po zakończeniu, czekamy minutę (aby nie wywołać kilka razy)
//                                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

//                                // 4) Log w TaskExecutionLogs (koniec)
//                                var finishedLog = await context.TaskExecutionLogs.FindAsync(ceneoLogId);
//                                if (finishedLog != null)
//                                {
//                                    finishedLog.EndTime = DateTime.Now;

//                                    // W zależności od wyniku, uzupełniamy "Comment"
//                                    switch (resultDto.Result)
//                                    {
//                                        case CeneoScraperService.CeneoScrapingResult.Success:
//                                            finishedLog.Comment += $" | Sukces. Zmielono {resultDto.ScrapedCount} produktów. Odrzucono: {resultDto.RejectedCount}.";
//                                            break;
//                                        case CeneoScraperService.CeneoScrapingResult.NoUrlsFound:
//                                            finishedLog.Comment += " | Brak URL do scrapowania (NoUrlsFound).";
//                                            break;
//                                        case CeneoScraperService.CeneoScrapingResult.SettingsNotFound:
//                                            finishedLog.Comment += " | Błąd: Brak settingsów (SettingsNotFound).";
//                                            break;
//                                        case CeneoScraperService.CeneoScrapingResult.Error:
//                                        default:
//                                            finishedLog.Comment += $" | Wystąpił błąd: {resultDto.Message}";
//                                            break;
//                                    }

//                                    context.TaskExecutionLogs.Update(finishedLog);
//                                    await context.SaveChangesAsync(stoppingToken);
//                                }


//                                if (resultDto.Result == CeneoScraperService.CeneoScrapingResult.Success)
//                                {
//                                    _logger.LogInformation("Ceneo scraping completed successfully.");
//                                }
//                                else if (resultDto.Result == CeneoScraperService.CeneoScrapingResult.NoUrlsFound)
//                                {
//                                    _logger.LogInformation("No URLs to scrape for Ceneo.");
//                                }

//                            }
//                        }


//                        if (DateTime.Now - _lastDeviceCheck >= _deviceCheckInterval)
//                        {
//                            // Ustawiamy, żeby ponownie „za 10 minut” się to wykonało
//                            _lastDeviceCheck = DateTime.Now;

//                            // Wywołanie metody, która zaktualizuje DeviceStatus w bazie
//                            await UpdateDeviceStatusAsync(
//                                context,
//                                deviceName,
//                                baseScalKey,
//                                urlScalKey,
//                                gooCrawKey,
//                                cenCrawKey,
//                                stoppingToken
//                            );
//                        }

//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "An error occurred while executing scheduled tasks.");
//            }

//            // pętla co 30 sekund
//            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
//        }
//    }

//    private async Task UpdateDeviceStatusAsync(
//           PriceSafariContext context,
//           string deviceName,
//           string baseScalKey,
//           string urlScalKey,
//           string gooCrawKey,
//           string cenCrawKey,
//           CancellationToken ct
//    )
//    {
//        // 1. Odczyt "prawidłowych" wartości (twardo zakodowane lub z configa)
//        const string BASE_SCAL_EXPECTED = "34692471";
//        const string URL_SCAL_EXPECTED = "49276583";
//        const string GOO_CRAW_EXPECTED = "03713857";
//        const string CEN_CRAW_EXPECTED = "56981467";

//        // 2. Czy klucz .env zgadza się z oczekiwanym?
//        bool hasBaseScal = (baseScalKey == BASE_SCAL_EXPECTED);
//        bool hasUrlScal = (urlScalKey == URL_SCAL_EXPECTED);
//        bool hasGooCraw = (gooCrawKey == GOO_CRAW_EXPECTED);
//        bool hasCenCraw = (cenCrawKey == CEN_CRAW_EXPECTED);

//        // 3. Tworzymy ZA KAŻDYM RAZEM nowy wpis w DeviceStatuses
//        //    (czyli nie szukamy już "existingStatus", tylko zawsze Add)
//        var newStatus = new DeviceStatus
//        {
//            DeviceName = deviceName,
//            IsOnline = true,    // bo właśnie się odezwało
//            LastCheck = DateTime.Now,
//            BaseScalEnabled = hasBaseScal,
//            UrlScalEnabled = hasUrlScal,
//            GooCrawEnabled = hasGooCraw,
//            CenCrawEnabled = hasCenCraw
//        };

//        await context.DeviceStatuses.AddAsync(newStatus, ct);

//        // Zapisujemy zmiany w bazie
//        await context.SaveChangesAsync(ct);
//    }

//}
