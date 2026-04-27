using AngleSharp.Dom;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PriceSafari.Data;
using PriceSafari.IntervalPriceChanger;
using PriceSafari.Models;
using PriceSafari.Models.SchedulePlan;
using PriceSafari.ScrapersControllers;
using PriceSafari.Services.AllegroServices;
using PriceSafari.Services.GoogleScraping;
using PriceSafari.Services.PriceAutomationService;
using PriceSafari.Services.ScheduleService;
using System.Text;
using static PriceSafari.Services.ScheduleService.ApiBotService;

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

        var apiBotKey = Environment.GetEnvironmentVariable("API_BOT_KEY");

        var aleBaseScalKey = Environment.GetEnvironmentVariable("ALE_BASE_SCAL");
        var urlScalAleKey = Environment.GetEnvironmentVariable("URL_SCAL_ALE");
        var aleCrawKey = Environment.GetEnvironmentVariable("ALE_CRAW");
        var aleApiBotKey = Environment.GetEnvironmentVariable("ALE_API_BOT");

        var subKey = Environment.GetEnvironmentVariable("SUBSCRIPTION_KEY");
        var payKey = Environment.GetEnvironmentVariable("GRAB_PAYMENT");
        var emailKey = Environment.GetEnvironmentVariable("SEND_EMAILS");
        var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "UnknownDevice";
        var marketAutoKey = Environment.GetEnvironmentVariable("MARKET_AUTO"); 
        var compAutoKey = Environment.GetEnvironmentVariable("COMP_AUTO");
        var aleGatherKey = Environment.GetEnvironmentVariable("ALE_GATHER");
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

                            _logger.LogInformation("Sprawdzanie zadań dla dnia {DayOfWeek} o {Time}.", dayOfWeek, DateTime.Now);

                            foreach (var t in dayDetail.Tasks)
                            {
                                if ((t.LastRunDateOfTask == null || t.LastRunDateOfTask.Value.Date < today)
                                    && nowTime < t.StartTime)
                                {
                                    double minutesToStart = (t.StartTime - nowTime).TotalMinutes;
                                    if (minutesToStart <= 180)
                                    {
                                        _logger.LogInformation("Zadanie '{SessionName}' wystartuje za {MinutesToStart:F1} minut.", t.SessionName, minutesToStart);
                                    }
                                }
                            }

                            var tasksToRun = dayDetail.Tasks
                                .Where(t => (t.LastRunDateOfTask == null || t.LastRunDateOfTask.Value.Date < today)
                                             && nowTime >= t.StartTime
                                             && nowTime < t.StartTime.Add(TimeSpan.FromMinutes(5)))
                                .OrderBy(t => t.StartTime)
                                .ToList();

                            
                            var allDayTasks = dayDetail.Tasks
                               .Where(t => (t.LastRunDateOfTask == null || t.LastRunDateOfTask.Value.Date < today)
                                            && t.StartTime > nowTime.Subtract(TimeSpan.FromMinutes(5)))
                               .OrderBy(t => t.StartTime)
                               .ToList();

                            foreach (var t in tasksToRun)
                            {
                                bool canRunAnything =
                                (t.UrlEnabled && urlScalKey == "83208716") ||
                                (t.CeneoEnabled && cenCrawKey == "84011233") ||
                                (t.GoogleEnabled && gooCrawKey == "63891743") ||
                                (t.ApiBotEnabled && apiBotKey == "11223344") ||
                                (t.BaseEnabled && baseScalKey == "55380981") ||
                                (t.UrlScalAleEnabled && urlScalAleKey == "74902379") ||
                                (t.AleCrawEnabled && aleCrawKey == "13894389") ||
                                (t.AleBaseEnabled && aleBaseScalKey == "64920067") ||
                                (t.AleApiBotEnabled && aleApiBotKey == "00937384") ||
                                (t.MarketPlaceAutomationEnabled && marketAutoKey == "99112233") ||
                                (t.PriceComparisonAutomationEnabled && compAutoKey == "88776655") ||
                                (t.AllegroGatherEnabled && aleGatherKey == "77553311");

                                if (!canRunAnything)
                                {
                                    _logger.LogInformation(
                                        "Urządzenie '{DeviceName}' nie ma odpowiednich kluczy, aby wykonać zadanie '{SessionName}'. Pomijam.",
                                        deviceName, t.SessionName);
                                    continue;
                                }

                                // === PREEMPCJA: Oblicz deadline na podstawie następnego zadania ===
                                var nextTask = allDayTasks
                                    .Where(nt => nt.StartTime > t.StartTime && nt.Id != t.Id)
                                    .OrderBy(nt => nt.StartTime)
                                    .FirstOrDefault();

                                CancellationTokenSource taskCts;
                                if (nextTask != null)
                                {
                                    var timeUntilNext = nextTask.StartTime - DateTime.Now.TimeOfDay;
                                    if (timeUntilNext.TotalSeconds < 30) timeUntilNext = TimeSpan.FromSeconds(30); // minimum 30s

                                    taskCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                                    taskCts.CancelAfter(timeUntilNext);

                                    _logger.LogInformation(
                                        "Zadanie '{SessionName}' ma {Minutes:F1} min do preempcji (następne: '{NextName}' o {NextTime}).",
                                        t.SessionName, timeUntilNext.TotalMinutes, nextTask.SessionName, nextTask.StartTime);
                                }
                                else
                                {
                                    // Ostatnie zadanie dnia — dajemy max 3h
                                    taskCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                                    taskCts.CancelAfter(TimeSpan.FromHours(3));
                                }

                                var taskToken = taskCts.Token;

                                _logger.LogInformation(
                                    "Rozpoczynam wykonywanie zadania '{SessionName}' (StartTime: {StartTime}) na urządzeniu '{DeviceName}'.",
                                    t.SessionName, t.StartTime, deviceName);

                                t.LastRunDateOfTask = DateTime.Now;
                                context.ScheduleTasks.Update(t);
                                await context.SaveChangesAsync(stoppingToken);

                                try
                                {
                                    if (t.UrlEnabled && urlScalKey == "83208716")
                                        await RunUrlScalAsync(context, deviceName, t, taskToken);

                                    if (t.CeneoEnabled && cenCrawKey == "84011233")
                                        await RunCeneoAsync(context, deviceName, t, taskToken);

                                    if (t.GoogleEnabled && gooCrawKey == "63891743")
                                        await RunGoogleAsync(context, deviceName, t, taskToken);

                                    if (t.ApiBotEnabled && apiBotKey == "11223344")
                                        await RunApiBotAsync(context, deviceName, t, taskToken);

                                    if (t.BaseEnabled && baseScalKey == "55380981")
                                        await RunBaseScalAsync(context, deviceName, t, taskToken);

                                    if (t.UrlScalAleEnabled && urlScalAleKey == "74902379")
                                        await RunUrlScalAleAsync(context, deviceName, t, taskToken);

                                    if (t.AleCrawEnabled && aleCrawKey == "13894389")
                                        await RunAleCrawAsync(context, deviceName, t, taskToken);

                                    if (t.AleApiBotEnabled && aleApiBotKey == "00937384")
                                        await RunAleApiBotAsync(context, deviceName, t, taskToken);

                                    if (t.AleBaseEnabled && aleBaseScalKey == "64920067")
                                        await RunAleBaseScalAsync(context, deviceName, t, taskToken);

                                    if (t.MarketPlaceAutomationEnabled && marketAutoKey == "99112233")
                                    {
                                        await Task.Delay(TimeSpan.FromSeconds(30), taskToken);
                                        await RunMarketPlaceAutomationAsync(context, deviceName, t, taskToken);
                                    }

                                    if (t.PriceComparisonAutomationEnabled && compAutoKey == "88776655")
                                    {
                                        await Task.Delay(TimeSpan.FromSeconds(30), taskToken);
                                        await RunPriceComparisonAutomationAsync(context, deviceName, t, taskToken);
                                    }

                                    if (t.AllegroGatherEnabled && aleGatherKey == "77553311")
                                    {
                                        await RunAllegroGatherAsync(context, deviceName, t, taskToken);
                                    }
                                }
                                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                                {
                                    _logger.LogWarning(
                                        "⚡ PREEMPCJA: Zadanie '{SessionName}' przerwane — nadchodzi kolejne zadanie.",
                                        t.SessionName);

                                
                                    if (AllegroScrapeManager.CurrentStatus == ScrapingProcessStatus.Running)
                                    {
                                        AllegroScrapeManager.FinishProcess();
                                        _logger.LogWarning("☢️ Wymuszono zakończenie AllegroScrapeManager.");
                                    }
                                    if (GoogleScrapeManager.CurrentStatus == GoogleScrapingProcessStatus.Running)
                                    {
                                        GoogleScrapeManager.FinishProcess();
                                        _logger.LogWarning("☢️ Wymuszono zakończenie GoogleScrapeManager.");
                                    }

                  
                                    try
                                    {
                                        var orphanedLogs = await context.TaskExecutionLogs
                                            .Where(l => l.DeviceName == deviceName && l.EndTime == null)
                                            .ToListAsync(stoppingToken);

                                        foreach (var orphan in orphanedLogs)
                                        {
                                            orphan.EndTime = DateTime.Now;
                                            orphan.Comment += $" | ⚡ PREEMPCJA: Zadanie przerwane — nadchodziło kolejne zadanie '{nextTask?.SessionName ?? "?"}' o {nextTask?.StartTime.ToString() ?? "?"}.";
                                        }

                                        if (orphanedLogs.Any())
                                            await context.SaveChangesAsync(stoppingToken);
                                    }
                                    catch (Exception logEx)
                                    {
                                        _logger.LogError(logEx, "Nie udało się zaktualizować logów po preempcji.");
                                    }
                                }
                                finally
                                {
                                    taskCts.Dispose();
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
                          apiBotKey,

                          aleBaseScalKey, urlScalAleKey, aleCrawKey, aleApiBotKey,
                          subKey, payKey, emailKey, marketAutoKey, compAutoKey, aleGatherKey,
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
            // 1. Pobieramy serwis
            var googleScraperService = context.GetService<GoogleScraperService>();

            // 2. Uruchamiamy proces (zwraca tylko czy się udało wystartować, a nie wynik końcowy)
            var (success, message) = await googleScraperService.StartScrapingProcessAsync();

            if (!success)
            {
                // Jeśli nie udało się wystartować (np. brak scraperów lub proces już trwa)
                var failLog = await context.TaskExecutionLogs.FindAsync(new object[] { googleLogId }, ct);
                if (failLog != null)
                {
                    failLog.EndTime = DateTime.Now;
                    failLog.Comment += $" | Błąd startu: {message}";
                    context.TaskExecutionLogs.Update(failLog);
                    await context.SaveChangesAsync(ct);
                }
                return;
            }

            // 3. Pętla oczekiwania - czekamy aż Manager zmieni status z Running na Idle
            // Monitorowaniem końca zajmuje się GoogleScrapingMonitorService, my tu tylko czekamy.
            while (GoogleScrapeManager.CurrentStatus == GoogleScrapingProcessStatus.Running && !ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }

            // 4. Proces zakończony - pobieramy statystyki z bazy
            var stats = await googleScraperService.GetDatabaseStatsAsync();

            var finishedGoogleLog = await context.TaskExecutionLogs.FindAsync(new object[] { googleLogId }, ct);
            if (finishedGoogleLog != null)
            {
                finishedGoogleLog.EndTime = DateTime.Now;

                double totalSeconds = (finishedGoogleLog.EndTime.Value - finishedGoogleLog.StartTime).TotalSeconds;

                // Obliczamy ile faktycznie przetworzono (sukces + odrzuty)
                int totalProcessed = stats.ScrapedUrls + stats.RejectedUrls;

                double urlsPerSecond = (totalSeconds > 0 && totalProcessed > 0)
                    ? totalProcessed / totalSeconds
                    : 0;

                finishedGoogleLog.Comment += $" | Sukces. Zmielono: {stats.ScrapedUrls}/{stats.TotalUrls} produktów, odrzucono: {stats.RejectedUrls}. Średnia prędkość: {urlsPerSecond:F2} URL/sek.";

                context.TaskExecutionLogs.Update(finishedGoogleLog);
                await context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            var errorLog = await context.TaskExecutionLogs.FindAsync(new object[] { googleLogId }, ct);
            if (errorLog != null)
            {
                errorLog.EndTime = DateTime.Now;
                errorLog.Comment += $" | Wystąpił krytyczny błąd w RunGoogleAsync: {ex.Message}";
                context.TaskExecutionLogs.Update(errorLog);
                await context.SaveChangesAsync(ct);
            }
        }
    }

    private async Task RunApiBotAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        var log = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "API_BOT_STORE",
            StartTime = DateTime.Now,
            Comment = $"Pobieranie danych API Sklepu | SessionName={task.SessionName}"
        };

        context.TaskExecutionLogs.Add(log);
        await context.SaveChangesAsync(ct);
        int logId = log.Id;

        // Używamy stopera do precyzyjnego pomiaru czasu
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var results = new List<ApiBotStoreResult>();
        int totalProductsFetched = 0;

        try
        {
            var apiBotService = context.GetService<ApiBotService>();

            foreach (var taskStore in task.TaskStores)
            {
                // Pobieramy szczegółowy wynik dla każdego sklepu
                var result = await apiBotService.ProcessPendingApiRequestsAsync(taskStore.StoreId);
                results.Add(result);

                if (!result.WasSkipped)
                {
                    totalProductsFetched += result.ProductsProcessed;
                }
            }

            stopwatch.Stop();
            var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;

            // Obliczamy prędkość
            double speed = (elapsedSeconds > 0 && totalProductsFetched > 0)
                ? totalProductsFetched / elapsedSeconds
                : 0;

            var endLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
            if (endLog != null)
            {
                endLog.EndTime = DateTime.Now;

                // Budowanie szczegółowego raportu
                // 1. Wybieramy tylko sklepy, które faktycznie coś robiły (nie zostały pominięte)
                var activeStores = results.Where(r => !r.WasSkipped && r.ProductsProcessed > 0).ToList();
                var skippedStores = results.Where(r => r.WasSkipped).ToList();

                var sb = new StringBuilder();

                if (activeStores.Any())
                {
                    sb.Append($" | Sukces. Pobrano: {totalProductsFetched} produktów.");
                    sb.Append($" Prędkość: {speed:F2} prod/sek.");
                    sb.Append(" Sklepy aktywne: ");

                    // Wypisujemy: "myjki.com (PrestaShop: 1200)", "inny.pl (Woo: 500)"
                    var activeDetails = activeStores.Select(s => $"{s.StoreName} ({s.SystemType}: {s.ProductsProcessed})");
                    sb.Append(string.Join(", ", activeDetails));
                }
                else
                {
                    sb.Append(" | Brak danych do pobrania (wszystkie sklepy pominięte lub brak produktów).");
                }

                // Opcjonalnie: informacja o pominiętych, jeśli chcesz mieć pełną jasność
                if (skippedStores.Any())
                {
                    sb.Append($" | Pominięto: {skippedStores.Count} sklepów (wyłączone API/brak konfigu).");
                }

                endLog.Comment += sb.ToString();

                context.TaskExecutionLogs.Update(endLog);
                await context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            var endLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
            if (endLog != null)
            {
                endLog.EndTime = DateTime.Now;
                endLog.Comment += $" | Krytyczny błąd (API_BOT): {ex.Message}";
                context.TaskExecutionLogs.Update(endLog);
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

    //private async Task RunAleApiBotAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    //{
    //    var log = new TaskExecutionLog
    //    {
    //        DeviceName = deviceName,
    //        OperationName = "ALE_API_BOT",
    //        StartTime = DateTime.Now,
    //        Comment = $"Rozpoczęcie pobierania danych z API Allegro | SessionName={task.SessionName}"
    //    };
    //    context.TaskExecutionLogs.Add(log);
    //    await context.SaveChangesAsync(ct);
    //    int logId = log.Id;

    //    try
    //    {
    //        var allegroApiBotService = context.GetService<AllegroApiBotService>();

    //        // Wywołujemy serwis, który teraz zwraca bogatsze statystyki
    //        var result = await allegroApiBotService.ProcessOffersForActiveStoresAsync();

    //        var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
    //        if (finishedLog != null)
    //        {
    //            finishedLog.EndTime = DateTime.Now;

    //            var sb = new StringBuilder();

    //            if (result.Success)
    //            {
    //                sb.Append(" | Sukces.");
    //            }
    //            else
    //            {
    //                sb.Append(" | Częściowe błędy.");
    //            }

    //            // TUTAJ FORMATUJEMY LOG WG TWOJEJ PROŚBY:
    //            sb.Append($" Sprawdzono ID: {result.TotalOffersChecked}.");
    //            sb.Append($" Pobrano prowizji/cen: {result.TotalOffersSuccess}.");

    //            if (result.TotalOffersFailed > 0)
    //            {
    //                sb.Append($" Pominięto/Błędy: {result.TotalOffersFailed}.");
    //            }

    //            sb.Append($" Przetworzono sklepów: {result.StoresProcessedCount}.");

    //            if (result.Messages.Any())
    //            {
    //                // Skracamy log jeśli jest bardzo długi, żeby nie przekroczyć limitu w bazie
    //                string details = string.Join("; ", result.Messages);
    //                if (details.Length > 500) details = details.Substring(0, 500) + "...";
    //                sb.Append($" Szczegóły: {details}");
    //            }

    //            finishedLog.Comment += sb.ToString();

    //            context.TaskExecutionLogs.Update(finishedLog);
    //            await context.SaveChangesAsync(ct);
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
    //        if (finishedLog != null)
    //        {
    //            finishedLog.EndTime = DateTime.Now;
    //            finishedLog.Comment += $" | Wystąpił krytyczny błąd (ALE_API_BOT): {ex.Message}";
    //            context.TaskExecutionLogs.Update(finishedLog);
    //            await context.SaveChangesAsync(ct);
    //        }
    //    }
    //}


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

                var sb = new StringBuilder();

                // ═══ FIX: Czytelniejszy status ═══
                if (result.TotalOffersChecked == 0 && result.StoresProcessedCount == 0)
                {
                    // Żaden sklep nie był aktywny — to może być problem z tokenem
                    sb.Append(" | ⚠️ Brak aktywnych sklepów.");
                }
                else if (result.TotalOffersSuccess == 0 && result.TotalOffersChecked > 0)
                {
                    // Były oferty do sprawdzenia, ale 0 sukces — BŁĄD
                    sb.Append(" | ❌ BŁĄD: 0 danych pobranych!");
                }
                else if (result.Success)
                {
                    sb.Append(" | Sukces.");
                }
                else
                {
                    sb.Append(" | Częściowe błędy.");
                }

                sb.Append($" Sprawdzono: {result.TotalOffersChecked}.");
                sb.Append($" Pobrano: {result.TotalOffersSuccess}.");

                if (result.TotalOffersFailed > 0)
                {
                    sb.Append($" Błędy: {result.TotalOffersFailed}.");
                }

                sb.Append($" Sklepy: {result.StoresProcessedCount}.");

                // Szczegóły (komunikaty serwisu + diagnostyka tokena + api stats)
                if (result.Messages.Any())
                {
                    string details = string.Join("; ", result.Messages);
                    if (details.Length > 500) details = details.Substring(0, 500) + "...";
                    sb.Append($" Szczegóły: {details}");
                }

                finishedLog.Comment += sb.ToString();
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
                finishedLog.Comment += $" | Krytyczny błąd (ALE_API_BOT): {ex.Message}";
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(ct);
            }
        }
    }

    //private async Task RunMarketPlaceAutomationAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    //{
    //    var log = new TaskExecutionLog
    //    {
    //        DeviceName = deviceName,
    //        OperationName = "AUTO_MARKETPLACE",
    //        StartTime = DateTime.Now,
    //        Comment = $"Start Automatyzacji Marketplace (Allegro) | SessionName={task.SessionName}"
    //    };

    //    context.TaskExecutionLogs.Add(log);
    //    await context.SaveChangesAsync(ct);
    //    int logId = log.Id;

    //    try
    //    {
    //        // 1. Pobieramy serwis
    //        var automationService = context.GetService<PriceAutomationService>();

    //        // 2. Pobieramy ID sklepów z tego zadania
    //        var storeIds = task.TaskStores.Select(x => x.StoreId).ToList();

    //        var rules = await context.AutomationRules
    //        .Include(r => r.Store)
    //        .Where(r => storeIds.Contains(r.StoreId)
    //                 && r.IsActive == true
    //                 && r.Store.RemainingDays > 0
    //                 && r.SourceType == AutomationSourceType.Marketplace)
    //        .ToListAsync(ct);

    //        var executableRules = rules.Where(r => r.CanExecute).ToList();

    //        int processedRules = 0;
    //        int totalChanges = 0;
    //        var sb = new StringBuilder();
    //        foreach (var rule in executableRules)
    //        {
    //            if (ct.IsCancellationRequested)
    //            {
    //                sb.Append($"[PREEMPCJA: przerwano przed {rule.Name}] ");
    //                break;
    //            }

    //            using var storeLock = await StoreLockManager.AcquireAsync(rule.StoreId, TimeSpan.FromMinutes(5));

    //            if (storeLock == null)
    //            {
    //                sb.Append($"[R:{rule.Name}, SKIP:StoreLock timeout] ");
    //                continue;
    //            }

    //            try
    //            {
    //                dynamic result = await automationService.ExecuteAutomationAsync(rule.Id, null);

    //                int count = 0;
    //                string ruleApiStats = "";

    //                if (result != null)
    //                {
    //                    var type = result.GetType();
    //                    var prop = type.GetProperty("count");
    //                    if (prop != null) count = (int)prop.GetValue(result, null);

    //                    var apiStatsProp = type.GetProperty("apiStats");
    //                    if (apiStatsProp != null) ruleApiStats = (string)(apiStatsProp.GetValue(result, null) ?? "");
    //                }

    //                totalChanges += count;
    //                processedRules++;
    //                sb.Append($"[R:{rule.Name}, Zmian:{count}] ");
    //                if (!string.IsNullOrEmpty(ruleApiStats))
    //                    sb.Append($"({ruleApiStats}) ");
    //            }
    //            catch (Exception ex)
    //            {
    //                sb.Append($"[R:{rule.Name}, Błąd:{ex.Message}] ");
    //            }
    //            // Lock zwalniany automatycznie przez using
    //        }

    //        // 4. Log
    //        var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, CancellationToken.None);
    //        if (finishedLog != null)
    //        {
    //            finishedLog.EndTime = DateTime.Now;
    //            if (processedRules > 0)
    //            {
    //                finishedLog.Comment += $" | Sukces. Przetworzono {processedRules} reguł. Łącznie zmian cen: {totalChanges}. Szczegóły: {sb}";
    //            }
    //            else
    //            {
    //                finishedLog.Comment += " | Brak aktywnych reguł Porównywarek dla sklepów w tym zadaniu.";
    //            }
    //            context.TaskExecutionLogs.Update(finishedLog);
    //            await context.SaveChangesAsync(CancellationToken.None);
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, CancellationToken.None);
    //        if (finishedLog != null)
    //        {
    //            finishedLog.EndTime = DateTime.Now;
    //            finishedLog.Comment += $" | Krytyczny błąd automatyzacji: {ex.Message}";
    //            context.TaskExecutionLogs.Update(finishedLog);
    //            await context.SaveChangesAsync(CancellationToken.None);
    //        }
    //    }
    //}

    // ═══════════════════════════════════════════════════════════════════════════
    // ZAMIEŃ RunMarketPlaceAutomationAsync w ScheduledTaskService.cs
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task RunMarketPlaceAutomationAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        var log = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "AUTO_MARKETPLACE",
            StartTime = DateTime.Now,
            Comment = $"Start Automatyzacji Marketplace (Allegro) | SessionName={task.SessionName}"
        };

        context.TaskExecutionLogs.Add(log);
        await context.SaveChangesAsync(ct);
        int logId = log.Id;

        try
        {
            var automationService = context.GetService<PriceAutomationService>();
            var storeIds = task.TaskStores.Select(x => x.StoreId).ToList();

            var rules = await context.AutomationRules
                .Include(r => r.Store)
                .Where(r => storeIds.Contains(r.StoreId)
                         && r.IsActive == true
                         && r.Store.RemainingDays > 0
                         && r.SourceType == AutomationSourceType.Marketplace)
                .ToListAsync(ct);

            var executableRules = rules.Where(r => r.CanExecute).ToList();

            int processedRules = 0;
            int totalChanges = 0;
            int totalApiRequests = 0;

            // Per-store tracking
            var storeStats = new Dictionary<string, (int requests, int changes)>();

            // Per-minute tracking
            var perMinuteRequests = new Dictionary<int, int>();

            var errors = new List<string>();
            var automationStopwatch = System.Diagnostics.Stopwatch.StartNew();

            foreach (var rule in executableRules)
            {
                if (ct.IsCancellationRequested)
                {
                    errors.Add($"PREEMPCJA przed regułą (store: {rule.Store?.StoreName ?? "?"})");
                    break;
                }

                using var storeLock = await StoreLockManager.AcquireAsync(rule.StoreId, TimeSpan.FromMinutes(5));

                if (storeLock == null)
                {
                    errors.Add($"{rule.Store?.StoreName ?? "?"}: StoreLock timeout");
                    continue;
                }

                string storeName = rule.Store?.StoreName ?? $"ID:{rule.StoreId}";
                int minuteKeyBefore = (int)automationStopwatch.Elapsed.TotalMinutes;

                try
                {
                    dynamic result = await automationService.ExecuteAutomationAsync(rule.Id, null);

                    int count = 0;
                    string ruleApiStats = "";

                    if (result != null)
                    {
                        var type = result.GetType();

                        var prop = type.GetProperty("count");
                        if (prop != null) count = (int)prop.GetValue(result, null);

                        var apiStatsProp = type.GetProperty("apiStats");
                        if (apiStatsProp != null) ruleApiStats = (string)(apiStatsProp.GetValue(result, null) ?? "");
                    }

                    // Wyciągnij liczbę requestów z apiStats inline (format: "API requests: 757 w ...")
                    int ruleRequests = 0;
                    if (!string.IsNullOrEmpty(ruleApiStats))
                    {
                        const string prefix = "API requests: ";
                        int startIdx = ruleApiStats.IndexOf(prefix);
                        if (startIdx >= 0)
                        {
                            startIdx += prefix.Length;
                            int endIdx = ruleApiStats.IndexOf(' ', startIdx);
                            if (endIdx < 0) endIdx = ruleApiStats.Length;
                            int.TryParse(ruleApiStats.AsSpan(startIdx, endIdx - startIdx), out ruleRequests);
                        }

                        // Wyciągnij per-minute dane z apiStats (format: "per minuta: [min0:546, min1:211]")
                        int perMinStart = ruleApiStats.IndexOf("per minuta: [");
                        if (perMinStart >= 0)
                        {
                            perMinStart += "per minuta: [".Length;
                            int perMinEnd = ruleApiStats.IndexOf(']', perMinStart);
                            if (perMinEnd > perMinStart)
                            {
                                var perMinStr = ruleApiStats.Substring(perMinStart, perMinEnd - perMinStart);
                                // "min0:546, min1:211"
                                foreach (var entry in perMinStr.Split(',', StringSplitOptions.TrimEntries))
                                {
                                    var colonIdx = entry.IndexOf(':');
                                    if (colonIdx > 0 && int.TryParse(entry.AsSpan(colonIdx + 1), out int entryCount))
                                    {
                                        // Przelicz na globalną minutę (offset + minuta lokalna reguły)
                                        var minKeyStr = entry.AsSpan(0, colonIdx).ToString().Replace("min", "");
                                        if (int.TryParse(minKeyStr, out int localMinute))
                                        {
                                            int globalMinute = minuteKeyBefore + localMinute;
                                            perMinuteRequests.TryGetValue(globalMinute, out int existing);
                                            perMinuteRequests[globalMinute] = existing + entryCount;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    totalApiRequests += ruleRequests;
                    totalChanges += count;
                    processedRules++;

                    // Per-store akumulacja
                    if (storeStats.ContainsKey(storeName))
                    {
                        var prev = storeStats[storeName];
                        storeStats[storeName] = (prev.requests + ruleRequests, prev.changes + count);
                    }
                    else
                    {
                        storeStats[storeName] = (ruleRequests, count);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{storeName}: {ex.Message}");
                }
            }

            automationStopwatch.Stop();

            // ═══ Log końcowy ═══
            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, CancellationToken.None);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;

                var sb = new StringBuilder();

                if (processedRules > 0)
                {
                    sb.Append($" | Sukces. Reguły: {processedRules}, zmian: {totalChanges}.");

                    // Per-store summary
                    if (storeStats.Any())
                    {
                        sb.Append(" Sklepy:");
                        foreach (var kvp in storeStats)
                        {
                            sb.Append($" [{kvp.Key}: {kvp.Value.requests} req, {kvp.Value.changes} zmian]");
                        }
                        sb.Append('.');
                    }

                    // API summary
                    if (totalApiRequests > 0)
                    {
                        var totalSeconds = automationStopwatch.Elapsed.TotalSeconds;
                        var avgPerMin = totalSeconds > 0 ? (int)(totalApiRequests / (totalSeconds / 60.0)) : totalApiRequests;
                        int peakPerMin = perMinuteRequests.Count > 0 ? perMinuteRequests.Values.Max() : 0;

                        sb.Append($" API łącznie: {totalApiRequests} req w {automationStopwatch.Elapsed:mm\\:ss} (śr: {avgPerMin}/min, szczyt: {peakPerMin}/min).");

                        // Per-minute breakdown
                        if (perMinuteRequests.Count > 0)
                        {
                            sb.Append(" Per minuta: [");
                            sb.Append(string.Join(", ",
                                perMinuteRequests.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}")));
                            sb.Append("].");
                        }
                    }
                }
                else
                {
                    sb.Append(" | Brak aktywnych reguł Marketplace.");
                }

                if (errors.Any())
                {
                    sb.Append($" Błędy: {string.Join("; ", errors)}");
                }

                string comment = sb.ToString();
                if (comment.Length > 900) comment = comment.Substring(0, 900) + "...";

                finishedLog.Comment += comment;
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, CancellationToken.None);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                finishedLog.Comment += $" | Krytyczny błąd automatyzacji: {ex.Message}";
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(CancellationToken.None);
            }
        }
    }

    private async Task RunPriceComparisonAutomationAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        var log = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "AUTO_COMPARISON",
            StartTime = DateTime.Now,
            Comment = $"Start Automatyzacji Porównywarek (Sklep/Ceneo/Google) | SessionName={task.SessionName}"
        };

        context.TaskExecutionLogs.Add(log);
        await context.SaveChangesAsync(ct);
        int logId = log.Id;

        try
        {
            // 1. Serwis
            var automationService = context.GetService<PriceAutomationService>();

            // 2. Sklepy
            var storeIds = task.TaskStores.Select(x => x.StoreId).ToList();

            var rules = await context.AutomationRules
                .Include(r => r.Store)
                .Where(r => storeIds.Contains(r.StoreId)
                         && r.IsActive == true
                         && r.Store.RemainingDays > 0
                         && r.SourceType == AutomationSourceType.PriceComparison)
                .ToListAsync(ct);

            var executableRules = rules.Where(r => r.CanExecute).ToList();

            int processedRules = 0;
            int totalChanges = 0;
            var sb = new StringBuilder();

            foreach (var rule in executableRules)
            {
                if (ct.IsCancellationRequested)
                {
                    sb.Append($"[PREEMPCJA: przerwano przed {rule.Name}] ");
                    break;
                }

                // Główny automat czeka na lock (max 5 min) — ma pierwszeństwo
                using var storeLock = await StoreLockManager.AcquireAsync(rule.StoreId, TimeSpan.FromMinutes(5));

                if (storeLock == null)
                {
                    sb.Append($"[R:{rule.Name}, SKIP:StoreLock timeout] ");
                    continue;
                }

                try
                {
                    dynamic result = await automationService.ExecuteAutomationAsync(rule.Id, null);

                    int count = 0;
                    if (result != null)
                    {
                        var type = result.GetType();
                        var prop = type.GetProperty("count");
                        if (prop != null) count = (int)prop.GetValue(result, null);
                    }

                    totalChanges += count;
                    processedRules++;
                    sb.Append($"[R:{rule.Name}, Zmian:{count}] ");
                }
                catch (Exception ex)
                {
                    sb.Append($"[R:{rule.Name}, Błąd:{ex.Message}] ");
                }
            }

            // 4. Log
            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, CancellationToken.None);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                if (processedRules > 0)
                {
                    finishedLog.Comment += $" | Sukces. Przetworzono {processedRules} reguł. Łącznie zmian cen: {totalChanges}. Szczegóły: {sb}";
                }
                else
                {
                    finishedLog.Comment += " | Brak aktywnych reguł Porównywarek dla sklepów w tym zadaniu.";
                }
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, CancellationToken.None);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                finishedLog.Comment += $" | Krytyczny błąd automatyzacji: {ex.Message}";
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(CancellationToken.None);
            }
        }
    }


    private async Task RunAllegroGatherAsync(PriceSafariContext context, string deviceName, ScheduleTask task, CancellationToken ct)
    {
        var storeNames = string.Join(", ", task.TaskStores.Select(ts => ts.Store.StoreName));

        var log = new TaskExecutionLog
        {
            DeviceName = deviceName,
            OperationName = "ALE_GATHER",
            StartTime = DateTime.Now,
            Comment = $"Zbieranie ofert Allegro (Gather) | SessionName={task.SessionName}; Sklepy: {storeNames}"
        };

        context.TaskExecutionLogs.Add(log);
        await context.SaveChangesAsync(ct);
        int logId = log.Id;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var gatherService = context.GetService<AllegroGatherService>();

            var storeIds = task.TaskStores.Select(ts => ts.StoreId).ToList();

            var result = await gatherService.StartAndWaitForStoresAsync(storeIds, ct);

            stopwatch.Stop();

            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;

                var sb = new System.Text.StringBuilder();

                sb.Append(result.Success ? " | Sukces." : " | Zakończono z błędami.");
                sb.Append($" {result.Message}");
                sb.Append($" Czas: {stopwatch.Elapsed.TotalSeconds:F0}s.");

                // Szczegóły per sklep
                if (result.StoreStats.Any())
                {
                    sb.Append(" Szczegóły:");
                    foreach (var stats in result.StoreStats)
                    {
                        sb.Append($" [{stats.StoreName}:");

                        if (stats.WasCancelled)
                        {
                            sb.Append(" anulowano]");
                            continue;
                        }

                        // Nowe produkty
                        if (stats.NewProductsFound > 0)
                        {
                            sb.Append($" +{stats.NewProductsFound} nowych");
                        }
                        else
                        {
                            sb.Append(" 0 nowych");
                        }

                        // Auto-aktywacja
                        if (stats.AutoActivated > 0)
                        {
                            sb.Append($", auto-aktywowano {stats.AutoActivated}");
                        }

                        // Status aktywnych vs limit
                        if (stats.Limit < int.MaxValue)
                        {
                            sb.Append($", aktywne: {stats.ActiveAfter}/{stats.Limit}");
                        }
                        else
                        {
                            sb.Append($", aktywne: {stats.ActiveAfter} (bez limitu)");
                        }

                        sb.Append(']');
                    }
                }

                // Obcinamy jeśli za długie
                string comment = sb.ToString();
                if (comment.Length > 900) comment = comment[..900] + "...";

                finishedLog.Comment += comment;
                context.TaskExecutionLogs.Update(finishedLog);
                await context.SaveChangesAsync(ct);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw; // Preempcja — niech obsłuży ją blok wyżej
        }
        catch (Exception ex)
        {
            var finishedLog = await context.TaskExecutionLogs.FindAsync(new object[] { logId }, ct);
            if (finishedLog != null)
            {
                finishedLog.EndTime = DateTime.Now;
                finishedLog.Comment += $" | Krytyczny błąd (ALE_GATHER): {ex.Message}";
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
             string apiBotKey,

             string aleBaseScalKey,
             string urlScalAleKey,
             string aleCrawKey,
             string aleApiBotKey,
             string subKey,
             string payKey,
             string emailKey,
             string marketAutoKey,
             string compAutoKey,
             string aleGatherKey,
             CancellationToken ct)
    {
        const string BASE_SCAL_EXPECTED = "55380981";
        const string URL_SCAL_EXPECTED = "83208716";
        const string GOO_CRAW_EXPECTED = "63891743";
        const string CEN_CRAW_EXPECTED = "84011233";
        const string API_BOT_EXPECTED = "11223344";

        const string ALE_BASE_SCAL_EXPECTED = "64920067";
        const string URL_SCAL_ALE_EXPECTED = "74902379";
        const string ALE_CRAW_EXPECTED = "13894389";
        const string ALE_API_BOT_EXPECTED = "00937384";

        const string SUB_KEY_EXPECTED = "99887766";
        const string PAY_KEY_EXPECTED = "38401048";
        const string EMAIL_KEY_EXPECTED = "55443322";
        const string MARKET_AUTO_EXPECTED = "99112233";
        const string COMP_AUTO_EXPECTED = "88776655";

        const string ALE_GATHER_EXPECTED = "77553311";
        const string INTERVAL_EXEC_EXPECTED = "49204718";

        bool hasBaseScal = (baseScalKey == BASE_SCAL_EXPECTED);
        bool hasUrlScal = (urlScalKey == URL_SCAL_EXPECTED);
        bool hasGooCraw = (gooCrawKey == GOO_CRAW_EXPECTED);
        bool hasCenCraw = (cenCrawKey == CEN_CRAW_EXPECTED);
        bool hasApiBot = (apiBotKey == API_BOT_EXPECTED);

        bool hasAleBaseScal = (aleBaseScalKey == ALE_BASE_SCAL_EXPECTED);
        bool hasUrlScalAle = (urlScalAleKey == URL_SCAL_ALE_EXPECTED);
        bool hasAleCraw = (aleCrawKey == ALE_CRAW_EXPECTED);
        bool hasAleApiBot = (aleApiBotKey == ALE_API_BOT_EXPECTED);

        bool hasInvoiceGen = (subKey == SUB_KEY_EXPECTED);
        bool hasPaymentProc = (payKey == PAY_KEY_EXPECTED);
        bool hasEmailSender = (emailKey == EMAIL_KEY_EXPECTED);
        bool hasMarketAuto = (marketAutoKey == MARKET_AUTO_EXPECTED);
        bool hasCompAuto = (compAutoKey == COMP_AUTO_EXPECTED);

        bool hasAleGather = (aleGatherKey == ALE_GATHER_EXPECTED);
        var intervalKey = Environment.GetEnvironmentVariable("INTERVAL_EXEC_KEY");  
        bool hasIntervalExec = (intervalKey == INTERVAL_EXEC_EXPECTED);

        var newStatus = new DeviceStatus
        {
            DeviceName = deviceName,
            IsOnline = true,
            LastCheck = DateTime.Now,

            BaseScalEnabled = hasBaseScal,
            UrlScalEnabled = hasUrlScal,
            GooCrawEnabled = hasGooCraw,
            CenCrawEnabled = hasCenCraw,
            ApiBotEnabled = hasApiBot,

            AleBaseScalEnabled = hasAleBaseScal,
            UrlScalAleEnabled = hasUrlScalAle,
            AleCrawEnabled = hasAleCraw,
            AleApiBotEnabled = hasAleApiBot,

            InvoiceGeneratorEnabled = hasInvoiceGen,
            PaymentProcessorEnabled = hasPaymentProc,
            EmailSenderEnabled = hasEmailSender,
            MarketPlaceAutomationEnabled = hasMarketAuto,
            PriceComparisonAutomationEnabled = hasCompAuto,

            AleGatherEnabled = hasAleGather,
            IntervalExecEnabled = hasIntervalExec,
        };

        await context.DeviceStatuses.AddAsync(newStatus, ct);
        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Zaktualizowano status urządzenia '{DeviceName}'. ApiBot: {ApiBot}, Faktury: {Inv}, Płatności: {Pay}, Maile: {Mail}",
            deviceName, hasApiBot, hasInvoiceGen, hasPaymentProc, hasEmailSender);
    }

 

}