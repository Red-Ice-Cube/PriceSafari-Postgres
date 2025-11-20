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

        private const string REQUIRED_GENERATOR_KEY = "99887766";
        private const string REQUIRED_PAYMENT_KEY = "38401048";

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
            var genKey = Environment.GetEnvironmentVariable("SUBSCRIPTION_KEY");
            var payKey = Environment.GetEnvironmentVariable("GRAB_PAYMENT");

            bool isGenerator = genKey == REQUIRED_GENERATOR_KEY;
            bool isPayer = payKey == REQUIRED_PAYMENT_KEY;

            if (!isGenerator && !isPayer)
            {
                _logger.LogWarning("Serwis uśpiony: Brak kluczy SUBSCRIPTION_KEY lub GRAB_PAYMENT.");
                while (!stoppingToken.IsCancellationRequested) await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                return;
            }

            _logger.LogInformation($"SubscriptionManager Start. Tryb Generatora: {isGenerator}, Tryb Płatnika: {isPayer}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    DateTime nextRun;

                    if (isGenerator)
                    {
                        nextRun = now.Date.AddDays(1).AddMinutes(5);
                        if (now.Hour == 0 && now.Minute < 5) nextRun = now.Date.AddMinutes(5);
                    }
                    else
                    {
                        nextRun = now.Date.AddDays(1).AddMinutes(15);
                        if (now.Hour == 0 && now.Minute < 15) nextRun = now.Date.AddMinutes(15);
                    }

                    var delay = nextRun - now;
                    _logger.LogInformation($"[{(isGenerator ? "GENERATOR" : "PŁATNIK")}] Czekam {delay.TotalMinutes:F2} min. Start: {nextRun}");

                    await Task.Delay(delay, stoppingToken);

                    if (isGenerator) await RunInvoiceGenerationLogic(stoppingToken);
                    if (isPayer) await RunPaymentExecutionLogic(stoppingToken);
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd krytyczny w pętli głównej.");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
        }

        private async Task RunInvoiceGenerationLogic(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
            var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "WebioGenerator";

            _logger.LogInformation(">>> [GENERATOR] Rozpoczynam wystawianie faktur...");

            var currentYear = DateTime.Now.Year;
            var invoiceCounter = await context.InvoiceCounters.FirstOrDefaultAsync(c => c.Year == currentYear, ct);
            if (invoiceCounter == null)
            {
                invoiceCounter = new InvoiceCounter { Year = currentYear, LastInvoiceNumber = 0, LastProformaNumber = 0 };
                context.InvoiceCounters.Add(invoiceCounter);
                await context.SaveChangesAsync(ct);
            }

            var stores = await context.Stores
                .Include(s => s.Plan)
                .Include(s => s.PaymentData)
                .Where(s => s.PlanId != null && s.IsPayingCustomer)
                .ToListAsync(ct);

            int count = 0;
            foreach (var store in stores)
            {

                if (store.SubscriptionStartDate == null || store.SubscriptionStartDate.Value > DateTime.Today) continue;

                if (store.RemainingDays > 0)
                {
                    store.RemainingDays--;
                }

                if (store.RemainingDays <= 0)
                {

                    if (!store.Plan.IsTestPlan && store.Plan.NetPrice > 0)
                    {
                        await GenerateInvoiceOnly(context, store, invoiceCounter, deviceName);
                        count++;
                    }
                }
            }

            await context.SaveChangesAsync(ct);
            _logger.LogInformation($"<<< [GENERATOR] Zakończono. Wystawiono faktur: {count}");
        }

        private async Task GenerateInvoiceOnly(PriceSafariContext context, StoreClass store, InvoiceCounter counter, string deviceName)
        {

            counter.LastInvoiceNumber++;
            string invNum = $"PS/{counter.LastInvoiceNumber:D6}/sDB/{DateTime.Now.Year}";

            decimal netPrice = store.Plan.NetPrice;
            decimal appliedDiscountPercentage = 0;
            decimal appliedDiscountAmount = 0;

            if (store.DiscountPercentage.HasValue && store.DiscountPercentage.Value > 0)
            {
                appliedDiscountPercentage = store.DiscountPercentage.Value;
                appliedDiscountAmount = netPrice * (appliedDiscountPercentage / 100m);
                netPrice = netPrice - appliedDiscountAmount;
            }

            var invoice = new InvoiceClass
            {
                StoreId = store.StoreId,
                PlanId = store.PlanId.Value,
                IssueDate = DateTime.Now,
                DueDate = DateTime.Now.AddDays(14),
                IsPaid = false,
                IsPaidByCard = false,
                InvoiceNumber = invNum,
                NetAmount = netPrice,
                DaysIncluded = store.Plan.DaysPerInvoice,
                UrlsIncluded = store.Plan.ProductsToScrap,
                UrlsIncludedAllegro = store.Plan.ProductsToScrapAllegro,
                CompanyName = store.PaymentData?.CompanyName ?? "Brak Danych",
                Address = store.PaymentData?.Address ?? "",
                PostalCode = store.PaymentData?.PostalCode ?? "",
                City = store.PaymentData?.City ?? "",
                NIP = store.PaymentData?.NIP ?? "",
                AppliedDiscountPercentage = appliedDiscountPercentage,
                AppliedDiscountAmount = appliedDiscountAmount
            };

            store.RemainingDays += store.Plan.DaysPerInvoice;
            store.ProductsToScrap = store.Plan.ProductsToScrap;
            store.ProductsToScrapAllegro = store.Plan.ProductsToScrapAllegro;

            context.Invoices.Add(invoice);

            bool hasCard = store.IsRecurringActive && !string.IsNullOrEmpty(store.ImojePaymentProfileId);
            string paymentInfo = hasCard
                ? "Metoda: KARTA (Czekam na Workera Płatności)"
                : "Metoda: PRZELEW (Oczekuje na wpłatę klienta)";

            context.TaskExecutionLogs.Add(new TaskExecutionLog
            {
                DeviceName = deviceName,
                OperationName = "FAKTURA_AUTO",
                StartTime = DateTime.Now,
                EndTime = DateTime.Now,
                Comment = $"Sukces. Wystawiono FV: {invNum}. Kwota netto: {netPrice:C}. {paymentInfo}"
            });
        }

        private async Task RunPaymentExecutionLogic(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
            var imojeService = scope.ServiceProvider.GetRequiredService<IImojeService>();
            var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "LocalWorker";

            string myIp = "127.0.0.1";
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(2);
                myIp = await client.GetStringAsync("https://api.ipify.org");
            }
            catch { }

            _logger.LogInformation($">>> [PŁATNIK] Szukam nieopłaconych faktur... (Moje IP: {myIp})");

            var invoicesToPay = await context.Invoices
                .Include(i => i.Store)
                .Where(i => !i.IsPaid
                            && i.Store.IsRecurringActive
                            && i.Store.ImojePaymentProfileId != null
                            && i.IssueDate >= DateTime.Now.AddDays(-3))
                .ToListAsync(ct);

            foreach (var invoice in invoicesToPay)
            {
                _logger.LogInformation($"Próba obciążenia karty dla FV: {invoice.InvoiceNumber}");

                var (success, response) = await imojeService.ChargeProfileAsync(
                    invoice.Store.ImojePaymentProfileId,
                    invoice,
                    myIp
                );

                if (success)
                {
                    invoice.IsPaid = true;
                    invoice.PaymentDate = DateTime.Now;
                    invoice.IsPaidByCard = true;

                    context.TaskExecutionLogs.Add(new TaskExecutionLog
                    {
                        DeviceName = deviceName,
                        OperationName = "WORKER_PLATNOSC",
                        StartTime = DateTime.Now,
                        EndTime = DateTime.Now,
                        Comment = $"SUKCES. FV: {invoice.InvoiceNumber} opłacona. IP Workera: {myIp}"
                    });
                }
                else
                {
                    string safeMsg = response.Length > 200 ? response.Substring(0, 200) : response;

                    context.TaskExecutionLogs.Add(new TaskExecutionLog
                    {
                        DeviceName = deviceName,
                        OperationName = "WORKER_BLAD",
                        StartTime = DateTime.Now,
                        EndTime = DateTime.Now,
                        Comment = $"BŁĄD. FV: {invoice.InvoiceNumber}. Info: {safeMsg}"
                    });
                }

                await context.SaveChangesAsync(ct);
            }

            _logger.LogInformation($"<<< [PŁATNIK] Zakończono. Przetworzono: {invoicesToPay.Count}");
        }
    }
}