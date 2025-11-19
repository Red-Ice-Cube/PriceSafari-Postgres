using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Services.Imoje;

namespace PriceSafari.Services.SubscriptionService
{
    public class SubscriptionManagerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SubscriptionManagerService> _logger;
        private readonly IConfiguration _configuration;

        private const string REQUIRED_ENV_KEY = "99887766";

        public SubscriptionManagerService(
            IServiceScopeFactory scopeFactory,
            ILogger<SubscriptionManagerService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var machineKey = Environment.GetEnvironmentVariable("SUBSCRIPTION_KEY");

            if (machineKey != REQUIRED_ENV_KEY)
            {
                _logger.LogWarning($"Maszyna nieautoryzowana (Brak poprawnego SUBSCRIPTION_KEY). Serwis uśpiony.");
                while (!stoppingToken.IsCancellationRequested) await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                return;
            }

            _logger.LogInformation("Autoryzacja udana. SubscriptionManagerService startuje.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;

                    // --- USTAWIENIA CZASU (PRODUKCYJNE) ---
                    //var nextRun = now.Date.AddDays(1).AddMinutes(5);
                    //if (now.Hour == 0 && now.Minute < 5) nextRun = now.Date.AddMinutes(5);

                    // --- TESTOWE (ZAKOMENTOWANE) ---
                    var nextRun = now.Date.AddHours(15).AddMinutes(40);
                    if (now > nextRun) nextRun = nextRun.AddDays(1);

                    var delay = nextRun - now;
                    _logger.LogInformation($"Oczekiwanie na proces subskrypcji: {delay.TotalMinutes:F2} min. Start: {nextRun}");

                    await Task.Delay(delay, stoppingToken);
                    await ProcessSubscriptionsAsync(stoppingToken);
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd krytyczny w SubscriptionManagerService.");
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
        }

        private async Task ProcessSubscriptionsAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
            var imojeService = scope.ServiceProvider.GetRequiredService<IImojeService>();

            var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "SubscriptionManager";

            _logger.LogInformation("Rozpoczynanie przetwarzania subskrypcji...");

            var today = DateTime.Today;
            var currentYear = today.Year;

            var invoiceCounter = await context.InvoiceCounters
                .FirstOrDefaultAsync(c => c.Year == currentYear, ct);

            if (invoiceCounter == null)
            {
                invoiceCounter = new InvoiceCounter
                {
                    Year = currentYear,
                    LastProformaNumber = 0,
                    LastInvoiceNumber = 0
                };
                context.InvoiceCounters.Add(invoiceCounter);
                await context.SaveChangesAsync(ct);
            }

            var stores = await context.Stores
                .Include(s => s.Plan)
                .Include(s => s.PaymentData)
                .Where(s => s.PlanId != null)
                .ToListAsync(ct);

            foreach (var store in stores)
            {
                try
                {
                    if (store.IsPayingCustomer)
                    {
                        if (store.SubscriptionStartDate == null || store.SubscriptionStartDate.Value > today)
                        {
                            continue;
                        }

                        if (store.RemainingDays > 0)
                        {
                            store.RemainingDays--;
                        }

                        if (store.RemainingDays <= 0)
                        {
                            if (!store.Plan.IsTestPlan && store.Plan.NetPrice > 0)
                            {
                                _logger.LogInformation($"Sklep {store.StoreName}: Generowanie faktury...");
                                await GenerateRenewalInvoice(context, store, today, invoiceCounter, imojeService, deviceName);
                            }
                            else
                            {
                                _logger.LogWarning($"Sklep {store.StoreName} jest płatnikiem, ale ma plan darmowy. Pomijam.");
                            }
                        }

                        await context.SaveChangesAsync(ct);
                    }
                    else
                    {
                        if (store.RemainingDays > 0)
                        {
                            store.RemainingDays--;
                        }
                        await context.SaveChangesAsync(ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Błąd przy sklepie {store.StoreName}.");
                }
            }

            _logger.LogInformation("Zakończono przetwarzanie subskrypcji.");
        }

        private async Task GenerateRenewalInvoice(
             PriceSafariContext context,
             StoreClass store,
             DateTime issueDate,
             InvoiceCounter counter,
             IImojeService imojeService,
             string deviceName)
        {
            if (store.Plan.IsTestPlan || store.Plan.NetPrice == 0) return;

            var logEntry = new TaskExecutionLog
            {
                DeviceName = deviceName,
                OperationName = "FAKTURA_RENEWAL",
                StartTime = DateTime.Now,
                Comment = $"Start generowania faktury dla sklepu: {store.StoreName} (Plan: {store.Plan.PlanName})"
            };
            context.TaskExecutionLogs.Add(logEntry);
            await context.SaveChangesAsync();

            int logId = logEntry.Id;
            bool paymentAttempted = false;
            bool paymentSuccess = false;
            string paymentResponseMessage = ""; // Zmienna na komunikat z Imoje
            string invoiceNumberResult = "Brak";

            try
            {
                var paymentData = store.PaymentData;
                decimal netPrice = store.Plan.NetPrice;
                decimal appliedDiscountPercentage = 0;
                decimal appliedDiscountAmount = 0;

                if (store.DiscountPercentage.HasValue && store.DiscountPercentage.Value > 0)
                {
                    appliedDiscountPercentage = store.DiscountPercentage.Value;
                    appliedDiscountAmount = netPrice * (appliedDiscountPercentage / 100m);
                    netPrice = netPrice - appliedDiscountAmount;
                }

                counter.LastInvoiceNumber++;
                int currentInvoiceNumber = counter.LastInvoiceNumber;
                string invoiceNumberFormatted = $"PS/{currentInvoiceNumber:D6}/sDB/{issueDate.Year}";
                invoiceNumberResult = invoiceNumberFormatted;

                var dueDate = issueDate.AddDays(14);

                var invoice = new InvoiceClass
                {
                    StoreId = store.StoreId,
                    PlanId = store.PlanId.Value,
                    IssueDate = issueDate,
                    DueDate = dueDate,
                    IsPaidByCard = false,
                    NetAmount = netPrice,
                    DaysIncluded = store.Plan.DaysPerInvoice,
                    UrlsIncluded = store.Plan.ProductsToScrap,
                    UrlsIncludedAllegro = store.Plan.ProductsToScrapAllegro,
                    IsPaid = false,
                    CompanyName = paymentData?.CompanyName ?? "Brak Danych",
                    Address = paymentData?.Address ?? "",
                    PostalCode = paymentData?.PostalCode ?? "",
                    City = paymentData?.City ?? "",
                    NIP = paymentData?.NIP ?? "",
                    AppliedDiscountPercentage = appliedDiscountPercentage,
                    AppliedDiscountAmount = appliedDiscountAmount,
                    InvoiceNumber = invoiceNumberFormatted
                };

                store.RemainingDays += store.Plan.DaysPerInvoice;
                store.ProductsToScrap = store.Plan.ProductsToScrap;
                store.ProductsToScrapAllegro = store.Plan.ProductsToScrapAllegro;

                context.Invoices.Add(invoice);
                await context.SaveChangesAsync();

                // --- OBSŁUGA PŁATNOŚCI AUTOMATYCZNEJ ---
                if (store.IsRecurringActive && !string.IsNullOrEmpty(store.ImojePaymentProfileId))
                {
                    paymentAttempted = true;
                    string serverIp = _configuration["SERVER_PUBLIC_IP"] ?? "127.0.0.1";

                    _logger.LogInformation($"Próba automatycznego obciążenia karty dla faktury {invoice.InvoiceNumber}. IP: {serverIp}");

                    // ZMIANA: Odbieramy wynik I komunikat
                    var resultTuple = await imojeService.ChargeProfileAsync(store.ImojePaymentProfileId, invoice, serverIp);

                    paymentSuccess = resultTuple.Success;
                    paymentResponseMessage = resultTuple.Response; // Zapisujemy odpowiedź JSON

                    if (paymentSuccess)
                    {
                        invoice.IsPaid = true;
                        invoice.PaymentDate = DateTime.Now;
                        invoice.IsPaidByCard = true;

                        _logger.LogInformation($"SUKCES: Faktura {invoice.InvoiceNumber} opłacona automatycznie z karty.");
                        await context.SaveChangesAsync();
                    }
                    else
                    {
                        _logger.LogWarning($"PORAŻKA: Nie udało się obciążyć karty dla faktury {invoice.InvoiceNumber}. Info: {paymentResponseMessage}");
                    }
                }

                // --- AKTUALIZACJA LOGU (SUKCES) ---
                var logToUpdate = await context.TaskExecutionLogs.FindAsync(logId);
                if (logToUpdate != null)
                {
                    logToUpdate.EndTime = DateTime.Now;

                    string paymentStatus = "";
                    if (paymentAttempted)
                    {
                        paymentStatus = paymentSuccess ? " | Płatność Kartą: SUKCES" : " | Płatność Kartą: PORAŻKA";
                    }
                    else
                    {
                        paymentStatus = " | Płatność: Przelew (brak karty)";
                    }

                    logToUpdate.Comment += $" | Sukces. Wystawiono FV: {invoiceNumberResult}{paymentStatus}. Kwota netto: {netPrice:C}";

                    if (paymentAttempted && !paymentSuccess)
                    {
                        // Zapisujemy dokładny komunikat błędu z Imoje do bazy!
                        // Skracamy wiadomość, jeśli jest bardzo długa, żeby nie przepełnić kolumny w bazie
                        string safeMessage = paymentResponseMessage.Length > 200
                            ? paymentResponseMessage.Substring(0, 200) + "..."
                            : paymentResponseMessage;

                        logToUpdate.Comment += $" | UWAGA: Błąd obciążenia karty! Szczegóły: {safeMessage}";
                    }

                    await context.SaveChangesAsync();
                }

            }
            catch (Exception ex)
            {
                var logError = await context.TaskExecutionLogs.FindAsync(logId);
                if (logError != null)
                {
                    logError.EndTime = DateTime.Now;
                    logError.Comment += $" | Błąd krytyczny podczas wystawiania: {ex.Message}";
                    await context.SaveChangesAsync();
                }
                throw;
            }
        }
    }
}