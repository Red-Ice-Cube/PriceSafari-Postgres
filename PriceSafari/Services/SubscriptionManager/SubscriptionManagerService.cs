//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using PriceSafari.Services.Imoje;

//namespace PriceSafari.Services.SubscriptionService
//{
//    public class SubscriptionManagerService : BackgroundService
//    {
//        private readonly IServiceScopeFactory _scopeFactory;
//        private readonly ILogger<SubscriptionManagerService> _logger;
//        private readonly IConfiguration _configuration;

//        private const string REQUIRED_GENERATOR_KEY = "99887766";
//        private const string REQUIRED_PAYMENT_KEY = "38401048";

//        public SubscriptionManagerService(
//            IServiceScopeFactory scopeFactory,
//            ILogger<SubscriptionManagerService> logger,
//            IConfiguration configuration)
//        {
//            _scopeFactory = scopeFactory;
//            _logger = logger;
//            _configuration = configuration;
//        }

//        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//        {
//            var genKey = Environment.GetEnvironmentVariable("SUBSCRIPTION_KEY");
//            var payKey = Environment.GetEnvironmentVariable("GRAB_PAYMENT");

//            bool isGenerator = genKey == REQUIRED_GENERATOR_KEY;
//            bool isPayer = payKey == REQUIRED_PAYMENT_KEY;

//            if (!isGenerator && !isPayer)
//            {
//                _logger.LogWarning("Serwis uśpiony: Brak kluczy SUBSCRIPTION_KEY lub GRAB_PAYMENT.");
//                while (!stoppingToken.IsCancellationRequested) await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
//                return;
//            }

//            _logger.LogInformation($"SubscriptionManager Start. Tryb Generatora: {isGenerator}, Tryb Płatnika: {isPayer}");

//            while (!stoppingToken.IsCancellationRequested)
//            {
//                try
//                {
//                    var now = DateTime.Now;
//                    DateTime nextRun;



//                    int targetHour = 10;
//                    int targetMinute = isGenerator ? 45 : 50; 

//                    DateTime candidateTime = now.Date.AddHours(targetHour).AddMinutes(targetMinute);

//                    if (now > candidateTime)
//                    {
//                        nextRun = candidateTime.AddDays(1);
//                    }
//                    else
//                    {
//                        nextRun = candidateTime;
//                    }


//                    var delay = nextRun - now;
//                    _logger.LogInformation($"[{(isGenerator ? "GENERATOR" : "PŁATNIK")}] Czekam {delay.TotalMinutes:F2} min. Start: {nextRun}");

//                    await Task.Delay(delay, stoppingToken);

//                    if (isGenerator) await RunInvoiceGenerationLogic(stoppingToken);
//                    if (isPayer) await RunPaymentExecutionLogic(stoppingToken);
//                }
//                catch (TaskCanceledException) { }
//                catch (Exception ex)
//                {
//                    _logger.LogError(ex, "Błąd krytyczny w pętli głównej.");
//                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
//                }
//            }
//        }

//        private async Task RunInvoiceGenerationLogic(CancellationToken ct)
//        {
//            using var scope = _scopeFactory.CreateScope();
//            var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//            var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "WebioGenerator";

//            _logger.LogInformation(">>> [GENERATOR] Rozpoczynam wystawianie faktur...");

//            var currentYear = DateTime.Now.Year;
//            var invoiceCounter = await context.InvoiceCounters.FirstOrDefaultAsync(c => c.Year == currentYear, ct);
//            if (invoiceCounter == null)
//            {
//                invoiceCounter = new InvoiceCounter { Year = currentYear, LastInvoiceNumber = 0, LastProformaNumber = 0 };
//                context.InvoiceCounters.Add(invoiceCounter);
//                await context.SaveChangesAsync(ct);
//            }

//            var stores = await context.Stores
//                .Include(s => s.Plan)
//                .Include(s => s.PaymentData)
//                .Where(s => s.PlanId != null && s.IsPayingCustomer)
//                .ToListAsync(ct);

//            int count = 0;
//            foreach (var store in stores)
//            {

//                if (store.SubscriptionStartDate == null || store.SubscriptionStartDate.Value > DateTime.Today) continue;

//                if (store.RemainingDays > 0)
//                {
//                    store.RemainingDays--;
//                }

//                if (store.RemainingDays <= 0)
//                {

//                    if (!store.Plan.IsTestPlan && store.Plan.NetPrice > 0)
//                    {
//                        await GenerateInvoiceOnly(context, store, invoiceCounter, deviceName);
//                        count++;
//                    }
//                }
//            }

//            await context.SaveChangesAsync(ct);
//            _logger.LogInformation($"<<< [GENERATOR] Zakończono. Wystawiono faktur: {count}");
//        }

//        private async Task GenerateInvoiceOnly(PriceSafariContext context, StoreClass store, InvoiceCounter counter, string deviceName)
//        {

//            counter.LastInvoiceNumber++;
//            string invNum = $"PS/{counter.LastInvoiceNumber:D6}/sDB/{DateTime.Now.Year}";

//            decimal netPrice = store.Plan.NetPrice;
//            decimal appliedDiscountPercentage = 0;
//            decimal appliedDiscountAmount = 0;

//            if (store.DiscountPercentage.HasValue && store.DiscountPercentage.Value > 0)
//            {
//                appliedDiscountPercentage = store.DiscountPercentage.Value;
//                appliedDiscountAmount = netPrice * (appliedDiscountPercentage / 100m);
//                netPrice = netPrice - appliedDiscountAmount;
//            }

//            var invoice = new InvoiceClass
//            {
//                StoreId = store.StoreId,
//                PlanId = store.PlanId.Value,
//                IssueDate = DateTime.Now,
//                DueDate = DateTime.Now.AddDays(14),
//                IsPaid = false,
//                IsPaidByCard = false,
//                InvoiceNumber = invNum,
//                NetAmount = netPrice,
//                DaysIncluded = store.Plan.DaysPerInvoice,
//                UrlsIncluded = store.Plan.ProductsToScrap,
//                UrlsIncludedAllegro = store.Plan.ProductsToScrapAllegro,
//                CompanyName = store.PaymentData?.CompanyName ?? "Brak Danych",
//                Address = store.PaymentData?.Address ?? "",
//                PostalCode = store.PaymentData?.PostalCode ?? "",
//                City = store.PaymentData?.City ?? "",
//                NIP = store.PaymentData?.NIP ?? "",
//                AppliedDiscountPercentage = appliedDiscountPercentage,
//                AppliedDiscountAmount = appliedDiscountAmount
//            };

//            store.RemainingDays += store.Plan.DaysPerInvoice;
//            store.ProductsToScrap = store.Plan.ProductsToScrap;
//            store.ProductsToScrapAllegro = store.Plan.ProductsToScrapAllegro;

//            context.Invoices.Add(invoice);

//            bool hasCard = store.IsRecurringActive && !string.IsNullOrEmpty(store.ImojePaymentProfileId);
//            string paymentInfo = hasCard
//                ? "Metoda: KARTA (Czekam na Workera Płatności)"
//                : "Metoda: PRZELEW (Oczekuje na wpłatę klienta)";

//            context.TaskExecutionLogs.Add(new TaskExecutionLog
//            {
//                DeviceName = deviceName,
//                OperationName = "FAKTURA_AUTO",
//                StartTime = DateTime.Now,
//                EndTime = DateTime.Now,
//                Comment = $"Sukces. Wystawiono FV: {invNum}. Kwota netto: {netPrice:C}. {paymentInfo}"
//            });
//        }

//        private async Task RunPaymentExecutionLogic(CancellationToken ct)
//        {
//            using var scope = _scopeFactory.CreateScope();
//            var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//            var imojeService = scope.ServiceProvider.GetRequiredService<IImojeService>();
//            var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "LocalWorker";

//            string myIp = "127.0.0.1";
//            try
//            {
//                using var client = new HttpClient();
//                client.Timeout = TimeSpan.FromSeconds(2);
//                myIp = await client.GetStringAsync("https://api.ipify.org");
//            }
//            catch { }

//            _logger.LogInformation($">>> [PŁATNIK] Szukam nieopłaconych faktur... (Moje IP: {myIp})");

//            var invoicesToPay = await context.Invoices
//                .Include(i => i.Store)
//                .Where(i => !i.IsPaid
//                            && i.Store.IsRecurringActive
//                            && i.Store.ImojePaymentProfileId != null
//                            && i.IssueDate >= DateTime.Now.AddDays(-3))
//                .ToListAsync(ct);

//            foreach (var invoice in invoicesToPay)
//            {
//                _logger.LogInformation($"Próba obciążenia karty dla FV: {invoice.InvoiceNumber}");

//                var (success, response) = await imojeService.ChargeProfileAsync(
//                    invoice.Store.ImojePaymentProfileId,
//                    invoice,
//                    myIp
//                );

//                if (success)
//                {
//                    invoice.IsPaid = true;
//                    invoice.PaymentDate = DateTime.Now;
//                    invoice.IsPaidByCard = true;

//                    context.TaskExecutionLogs.Add(new TaskExecutionLog
//                    {
//                        DeviceName = deviceName,
//                        OperationName = "WORKER_PLATNOSC",
//                        StartTime = DateTime.Now,
//                        EndTime = DateTime.Now,
//                        Comment = $"SUKCES. FV: {invoice.InvoiceNumber} opłacona. IP Workera: {myIp}"
//                    });
//                }
//                else
//                {
//                    string safeMsg = response.Length > 200 ? response.Substring(0, 200) : response;

//                    context.TaskExecutionLogs.Add(new TaskExecutionLog
//                    {
//                        DeviceName = deviceName,
//                        OperationName = "WORKER_BLAD",
//                        StartTime = DateTime.Now,
//                        EndTime = DateTime.Now,
//                        Comment = $"BŁĄD. FV: {invoice.InvoiceNumber}. Info: {safeMsg}"
//                    });
//                }

//                await context.SaveChangesAsync(ct);
//            }

//            _logger.LogInformation($"<<< [PŁATNIK] Zakończono. Przetworzono: {invoicesToPay.Count}");
//        }
//    }
//}


using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Services.Imoje;
using PriceSafari.Services.EmailService;
using System.Text;
using System.Linq;

namespace PriceSafari.Services.SubscriptionService
{
    public class SubscriptionManagerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SubscriptionManagerService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _webHostEnvironment;

        // Klucze autoryzacyjne
        private const string REQUIRED_GENERATOR_KEY = "99887766";
        private const string REQUIRED_PAYMENT_KEY = "38401048";
        private const string REQUIRED_EMAIL_KEY = "55443322";

        public SubscriptionManagerService(
            IServiceScopeFactory scopeFactory,
            ILogger<SubscriptionManagerService> logger,
            IConfiguration configuration,
            IWebHostEnvironment webHostEnvironment)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 1. Pobranie kluczy
            var genKey = Environment.GetEnvironmentVariable("SUBSCRIPTION_KEY");
            var payKey = Environment.GetEnvironmentVariable("GRAB_PAYMENT");
            var emailKey = Environment.GetEnvironmentVariable("SEND_EMAILS");

            // 2. Określenie ról tego urządzenia
            bool isGenerator = genKey == REQUIRED_GENERATOR_KEY;
            bool isPayer = payKey == REQUIRED_PAYMENT_KEY;
            bool isEmailSender = emailKey == REQUIRED_EMAIL_KEY;

            if (!isGenerator && !isPayer && !isEmailSender)
            {
                _logger.LogWarning("Serwis uśpiony: Brak odpowiednich kluczy w zmiennych środowiskowych.");
                while (!stoppingToken.IsCancellationRequested) await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                return;
            }

            _logger.LogInformation($"SubscriptionManager Start. Role -> Generator: {isGenerator}, Płatnik: {isPayer}, Mailer: {isEmailSender}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    DateTime nextRun;

                    int targetHour;
                    int targetMinute;

                    if (isGenerator)
                    {
                        targetHour = 11;
                        targetMinute = 30;
                    }
                    else if (isPayer)
                    {
                        targetHour = 10;
                        targetMinute = 35;
                    }
                    else
                    {
                        //maile
                        targetHour = 11;
                        targetMinute = 40;
                    }

                    // 4. Obliczanie daty uruchomienia
                    DateTime candidateTime = now.Date.AddHours(targetHour).AddMinutes(targetMinute);

                    if (now > candidateTime)
                    {
                        nextRun = candidateTime.AddDays(1);
                    }
                    else
                    {
                        nextRun = candidateTime;
                    }

                    var delay = nextRun - now;
                    string roleName = isGenerator ? "GENERATOR" : (isPayer ? "PŁATNIK" : "MAILER");

                    _logger.LogInformation($"[{roleName}] Czekam {delay.TotalMinutes:F2} min. Start planowany na: {nextRun}");

                    // 5. Oczekiwanie
                    await Task.Delay(delay, stoppingToken);

                    // 6. Wykonanie odpowiedniej logiki
                    if (isGenerator)
                    {
                        await RunInvoiceGenerationLogic(stoppingToken);
                    }

                    if (isPayer)
                    {
                        await RunPaymentExecutionLogic(stoppingToken);
                    }

                    if (isEmailSender)
                    {
                        // Mailer uruchamia się teraz niezależnie o swojej godzinie (np. 11:30)
                        await RunEmailSendingLogic(stoppingToken);
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd krytyczny w pętli głównej.");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
        }

        // ==============================================================================
        // LOGIKA 1: GENERATOR FAKTUR
        // ==============================================================================
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
                AppliedDiscountAmount = appliedDiscountAmount,
                IsSentByEmail = false
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

        // ==============================================================================
        // LOGIKA 2: PŁATNIK (Próba obciążenia kart)
        // ==============================================================================
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

        // ==============================================================================
        // LOGIKA 3: MAILER (Wysyłka PDF)
        // ==============================================================================
        private async Task RunEmailSendingLogic(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IAppEmailSender>();
            var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "LocalWorker";

            _logger.LogInformation(">>> [MAILER] Szukam faktur do wysyłki...");

            // 1. Pobieramy faktury (Store, Plan)
            var pendingInvoices = await context.Invoices
                .Include(i => i.Store)
                .Include(i => i.Plan)
                .Where(i => !i.IsSentByEmail && i.IssueDate <= DateTime.Now)
                .ToListAsync(ct);

            int sentCount = 0;

            foreach (var invoice in pendingInvoices)
            {
                try
                {
                    // 2. POBRANIE DANYCH DO WYSYŁKI Z UserPaymentData
                    // Pobieramy PaymentData przypisane do sklepu z faktury
                    var paymentData = await context.Set<UserPaymentData>()
                        .FirstOrDefaultAsync(p => p.StoreId == invoice.StoreId, ct);

                    // Bierzemy maila z pola InvoiceAutoMail
                    string invoiceEmail = paymentData?.InvoiceAutoMail;

                    if (string.IsNullOrWhiteSpace(invoiceEmail))
                    {
                        // Możesz tu dodać logikę: jeśli brak InvoiceAutoMail, to pomiń, albo loguj warning
                        _logger.LogWarning($"Pominięto FV: {invoice.InvoiceNumber}. Brak skonfigurowanego adresu 'InvoiceAutoMail' w danych płatności sklepu.");
                        continue;
                    }

                    // 3. Generowanie PDF
                    var logoPath = Path.Combine(_webHostEnvironment.WebRootPath, "cid", "signature.png");
                    var invoiceDoc = new InvoiceDocument(invoice, logoPath);
                    byte[] pdfBytes = invoiceDoc.GeneratePdf();

                    // 4. Treść i Wysyłka
                    string subject = invoice.IsPaid
                        ? $"Faktura {invoice.InvoiceNumber} - PriceSafari (Opłacona)"
                        : $"Faktura {invoice.InvoiceNumber} - PriceSafari (Do zapłaty)";

                    string body = GenerateInvoiceEmailBody(invoice, invoice.IsPaid);

                    var mailLogoPath = Path.Combine(_webHostEnvironment.WebRootPath, "cid", "PriceSafari.png");
                    var inlineImages = new Dictionary<string, string> { { "PriceSafariLogo", mailLogoPath } };
                    string fileName = $"Faktura_{invoice.InvoiceNumber.Replace("/", "_")}.pdf";
                    var attachments = new Dictionary<string, byte[]> { { fileName, pdfBytes } };

                    bool success = await emailSender.SendEmailAsync(invoiceEmail, subject, body, inlineImages, attachments);

                    if (success)
                    {
                        invoice.IsSentByEmail = true;
                        sentCount++;
                        _logger.LogInformation($"Wysłano FV: {invoice.InvoiceNumber} na adres księgowy: {invoiceEmail}");
                    }
                    else
                    {
                        _logger.LogError($"Błąd SMTP dla FV: {invoice.InvoiceNumber} (adres: {invoiceEmail})");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Wyjątek przy wysyłce FV: {invoice.InvoiceNumber}");
                }
            }

            if (sentCount > 0)
            {
                await context.SaveChangesAsync(ct);
                context.TaskExecutionLogs.Add(new TaskExecutionLog
                {
                    DeviceName = deviceName,
                    OperationName = "EMAIL_SEND",
                    StartTime = DateTime.Now,
                    EndTime = DateTime.Now,
                    Comment = $"Wysłano {sentCount} faktur na adresy księgowe."
                });
                await context.SaveChangesAsync(ct);
            }

            _logger.LogInformation($"<<< [MAILER] Koniec. Wysłano: {sentCount}");
        }

        private string GenerateInvoiceEmailBody(InvoiceClass invoice, bool isPaid)
        {
            var color = isPaid ? "#41C7C7" : "#E74C3C";
            var statusText = isPaid ? "OPŁACONA" : "DO ZAPŁATY";
            var message = isPaid
                ? "Dziękujemy za terminową płatność. W załączniku przesyłamy fakturę VAT potwierdzającą transakcję."
                : $"Przesyłamy fakturę VAT za usługi w serwisie PriceSafari. Termin płatności mija: <strong>{invoice.DueDate?.ToString("yyyy-MM-dd") ?? "wkrótce"}</strong>.";

            var actionButton = isPaid
                ? ""
                : $@"<a href='https://price-safari.com/Payment/StorePayments?storeId={invoice.StoreId}' class='button' style='background-color: #E74C3C;'>Opłać fakturę online</a>";

            return $@"
            <!DOCTYPE html>
            <html lang=""pl"">
            <head>
                <meta charset=""UTF-8"">
                <style>
                    body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; margin: 0; padding: 0; background-color: #f5f5f7; }}
                    .container {{ max-width: 560px; margin: 40px auto; background-color: #ffffff; overflow: hidden; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.05); }}
                    .top-bar {{ height: 4px; background-color: {color}; }}
                    .header {{ padding: 30px 40px 10px 40px; text-align: center; }}
                    .content {{ padding: 20px 40px 40px 40px; line-height: 1.6; color: #1d1d1f; font-size: 16px; }}
                    .invoice-box {{ background-color: #f9f9f9; border: 1px solid #e0e0e0; padding: 15px; border-radius: 6px; margin: 20px 0; }}
                    .amount {{ font-size: 24px; font-weight: 700; color: #1d1d1f; }}
                    .status {{ font-weight: bold; color: {color}; }}
                    .button {{ display: inline-block; background-color: #007B84; color: #ffffff; padding: 12px 24px; border-radius: 6px; text-decoration: none; font-weight: 500; margin-top: 20px; }}
                    .footer {{ background-color: #f5f5f7; color: #86868b; padding: 20px 40px; text-align: center; font-size: 12px; }}
                </style>
            </head>
            <body>
                <div class=""container"">
                    <div class=""top-bar""></div>
                    <div class=""header"">
                        <img src=""cid:PriceSafariLogo"" alt=""Price Safari"" style=""height: 32px; width: auto;"">
                    </div>
                    <div class=""content"">
                        <h2 style=""margin-top: 0;"">Faktura nr {invoice.InvoiceNumber}</h2>
                        <p>Cześć {invoice.Store.StoreName},</p>
                        <p>{message}</p>
                        <div class=""invoice-box"">
                            <table width=""100%"">
                                <tr><td>Kwota brutto:</td><td align=""right"" class=""amount"">{(invoice.NetAmount * 1.23m):C}</td></tr>
                                <tr><td>Status:</td><td align=""right"" class=""status"">{statusText}</td></tr>
                            </table>
                        </div>
                        <p style=""font-size: 14px; color: #666;"">Dokument w formacie PDF znajduje się w załączniku tej wiadomości.</p>
                        {actionButton}
                    </div>
                    <div class=""footer""><p>&copy; {DateTime.Now.Year} Price Safari<br>Heated Box Sp. z o.o.</p></div>
                </div>
            </body>
            </html>";
        }
    }
}