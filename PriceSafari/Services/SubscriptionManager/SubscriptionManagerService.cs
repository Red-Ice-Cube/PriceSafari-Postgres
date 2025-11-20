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

//        private const string REQUIRED_ENV_KEY = "99887766";

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
//            var machineKey = Environment.GetEnvironmentVariable("SUBSCRIPTION_KEY");

//            if (machineKey != REQUIRED_ENV_KEY)
//            {
//                _logger.LogWarning($"Maszyna nieautoryzowana (Brak poprawnego SUBSCRIPTION_KEY). Serwis uśpiony.");
//                while (!stoppingToken.IsCancellationRequested) await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
//                return;
//            }

//            _logger.LogInformation("Autoryzacja udana. SubscriptionManagerService startuje.");

//            while (!stoppingToken.IsCancellationRequested)
//            {
//                try
//                {
//                    var now = DateTime.Now;

//                    // --- USTAWIENIA CZASU (PRODUKCYJNE) ---
//                    var nextRun = now.Date.AddDays(1).AddMinutes(5);
//                    if (now.Hour == 0 && now.Minute < 5) nextRun = now.Date.AddMinutes(5);

//                    // --- TESTOWE (ZAKOMENTOWANE) ---
//                    //var nextRun = now.Date.AddHours(15).AddMinutes(40);
//                    //if (now > nextRun) nextRun = nextRun.AddDays(1);

//                    var delay = nextRun - now;
//                    _logger.LogInformation($"Oczekiwanie na proces subskrypcji: {delay.TotalMinutes:F2} min. Start: {nextRun}");

//                    await Task.Delay(delay, stoppingToken);
//                    await ProcessSubscriptionsAsync(stoppingToken);
//                }
//                catch (TaskCanceledException) { }
//                catch (Exception ex)
//                {
//                    _logger.LogError(ex, "Błąd krytyczny w SubscriptionManagerService.");
//                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
//                }
//            }
//        }

//        private async Task ProcessSubscriptionsAsync(CancellationToken ct)
//        {
//            using var scope = _scopeFactory.CreateScope();
//            var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
//            var imojeService = scope.ServiceProvider.GetRequiredService<IImojeService>();

//            var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "SubscriptionManager";

//            _logger.LogInformation("Rozpoczynanie przetwarzania subskrypcji...");

//            var today = DateTime.Today;
//            var currentYear = today.Year;

//            var invoiceCounter = await context.InvoiceCounters
//                .FirstOrDefaultAsync(c => c.Year == currentYear, ct);

//            if (invoiceCounter == null)
//            {
//                invoiceCounter = new InvoiceCounter
//                {
//                    Year = currentYear,
//                    LastProformaNumber = 0,
//                    LastInvoiceNumber = 0
//                };
//                context.InvoiceCounters.Add(invoiceCounter);
//                await context.SaveChangesAsync(ct);
//            }

//            var stores = await context.Stores
//                .Include(s => s.Plan)
//                .Include(s => s.PaymentData)
//                .Where(s => s.PlanId != null)
//                .ToListAsync(ct);

//            foreach (var store in stores)
//            {
//                try
//                {
//                    if (store.IsPayingCustomer)
//                    {
//                        if (store.SubscriptionStartDate == null || store.SubscriptionStartDate.Value > today)
//                        {
//                            continue;
//                        }

//                        if (store.RemainingDays > 0)
//                        {
//                            store.RemainingDays--;
//                        }

//                        if (store.RemainingDays <= 0)
//                        {
//                            if (!store.Plan.IsTestPlan && store.Plan.NetPrice > 0)
//                            {
//                                _logger.LogInformation($"Sklep {store.StoreName}: Generowanie faktury...");
//                                await GenerateRenewalInvoice(context, store, today, invoiceCounter, imojeService, deviceName);
//                            }
//                            else
//                            {
//                                _logger.LogWarning($"Sklep {store.StoreName} jest płatnikiem, ale ma plan darmowy. Pomijam.");
//                            }
//                        }

//                        await context.SaveChangesAsync(ct);
//                    }
//                    else
//                    {
//                        if (store.RemainingDays > 0)
//                        {
//                            store.RemainingDays--;
//                        }
//                        await context.SaveChangesAsync(ct);
//                    }
//                }
//                catch (Exception ex)
//                {
//                    _logger.LogError(ex, $"Błąd przy sklepie {store.StoreName}.");
//                }
//            }

//            _logger.LogInformation("Zakończono przetwarzanie subskrypcji.");
//        }

//        private async Task GenerateRenewalInvoice(
//             PriceSafariContext context,
//             StoreClass store,
//             DateTime issueDate,
//             InvoiceCounter counter,
//             IImojeService imojeService,
//             string deviceName)
//        {
//            if (store.Plan.IsTestPlan || store.Plan.NetPrice == 0) return;

//            var logEntry = new TaskExecutionLog
//            {
//                DeviceName = deviceName,
//                OperationName = "FAKTURA_RENEWAL",
//                StartTime = DateTime.Now,
//                Comment = $"Start generowania faktury dla sklepu: {store.StoreName} (Plan: {store.Plan.PlanName})"
//            };
//            context.TaskExecutionLogs.Add(logEntry);
//            await context.SaveChangesAsync();

//            int logId = logEntry.Id;
//            bool paymentAttempted = false;
//            bool paymentSuccess = false;
//            string paymentResponseMessage = ""; // Zmienna na komunikat z Imoje
//            string invoiceNumberResult = "Brak";

//            try
//            {
//                var paymentData = store.PaymentData;
//                decimal netPrice = store.Plan.NetPrice;
//                decimal appliedDiscountPercentage = 0;
//                decimal appliedDiscountAmount = 0;

//                if (store.DiscountPercentage.HasValue && store.DiscountPercentage.Value > 0)
//                {
//                    appliedDiscountPercentage = store.DiscountPercentage.Value;
//                    appliedDiscountAmount = netPrice * (appliedDiscountPercentage / 100m);
//                    netPrice = netPrice - appliedDiscountAmount;
//                }

//                counter.LastInvoiceNumber++;
//                int currentInvoiceNumber = counter.LastInvoiceNumber;
//                string invoiceNumberFormatted = $"PS/{currentInvoiceNumber:D6}/sDB/{issueDate.Year}";
//                invoiceNumberResult = invoiceNumberFormatted;

//                var dueDate = issueDate.AddDays(14);

//                var invoice = new InvoiceClass
//                {
//                    StoreId = store.StoreId,
//                    PlanId = store.PlanId.Value,
//                    IssueDate = issueDate,
//                    DueDate = dueDate,
//                    IsPaidByCard = false,
//                    NetAmount = netPrice,
//                    DaysIncluded = store.Plan.DaysPerInvoice,
//                    UrlsIncluded = store.Plan.ProductsToScrap,
//                    UrlsIncludedAllegro = store.Plan.ProductsToScrapAllegro,
//                    IsPaid = false,
//                    CompanyName = paymentData?.CompanyName ?? "Brak Danych",
//                    Address = paymentData?.Address ?? "",
//                    PostalCode = paymentData?.PostalCode ?? "",
//                    City = paymentData?.City ?? "",
//                    NIP = paymentData?.NIP ?? "",
//                    AppliedDiscountPercentage = appliedDiscountPercentage,
//                    AppliedDiscountAmount = appliedDiscountAmount,
//                    InvoiceNumber = invoiceNumberFormatted
//                };

//                store.RemainingDays += store.Plan.DaysPerInvoice;
//                store.ProductsToScrap = store.Plan.ProductsToScrap;
//                store.ProductsToScrapAllegro = store.Plan.ProductsToScrapAllegro;

//                context.Invoices.Add(invoice);
//                await context.SaveChangesAsync();

//                // --- OBSŁUGA PŁATNOŚCI AUTOMATYCZNEJ ---
//                if (store.IsRecurringActive && !string.IsNullOrEmpty(store.ImojePaymentProfileId))
//                {
//                    paymentAttempted = true;
//                    string serverIp = _configuration["SERVER_PUBLIC_IP"] ?? "127.0.0.1";

//                    _logger.LogInformation($"Próba automatycznego obciążenia karty dla faktury {invoice.InvoiceNumber}. IP: {serverIp}");

//                    // ZMIANA: Odbieramy wynik I komunikat
//                    var resultTuple = await imojeService.ChargeProfileAsync(store.ImojePaymentProfileId, invoice, serverIp);

//                    paymentSuccess = resultTuple.Success;
//                    paymentResponseMessage = resultTuple.Response; // Zapisujemy odpowiedź JSON

//                    if (paymentSuccess)
//                    {
//                        invoice.IsPaid = true;
//                        invoice.PaymentDate = DateTime.Now;
//                        invoice.IsPaidByCard = true;

//                        _logger.LogInformation($"SUKCES: Faktura {invoice.InvoiceNumber} opłacona automatycznie z karty.");
//                        await context.SaveChangesAsync();
//                    }
//                    else
//                    {
//                        _logger.LogWarning($"PORAŻKA: Nie udało się obciążyć karty dla faktury {invoice.InvoiceNumber}. Info: {paymentResponseMessage}");
//                    }
//                }

//                // --- AKTUALIZACJA LOGU (SUKCES) ---
//                var logToUpdate = await context.TaskExecutionLogs.FindAsync(logId);
//                if (logToUpdate != null)
//                {
//                    logToUpdate.EndTime = DateTime.Now;

//                    string paymentStatus = "";
//                    if (paymentAttempted)
//                    {
//                        paymentStatus = paymentSuccess ? " | Płatność Kartą: SUKCES" : " | Płatność Kartą: PORAŻKA";
//                    }
//                    else
//                    {
//                        paymentStatus = " | Płatność: Przelew (brak karty)";
//                    }

//                    logToUpdate.Comment += $" | Sukces. Wystawiono FV: {invoiceNumberResult}{paymentStatus}. Kwota netto: {netPrice:C}";

//                    if (paymentAttempted && !paymentSuccess)
//                    {
//                        // Zapisujemy dokładny komunikat błędu z Imoje do bazy!
//                        // Skracamy wiadomość, jeśli jest bardzo długa, żeby nie przepełnić kolumny w bazie
//                        string safeMessage = paymentResponseMessage.Length > 200
//                            ? paymentResponseMessage.Substring(0, 200) + "..."
//                            : paymentResponseMessage;

//                        logToUpdate.Comment += $" | UWAGA: Błąd obciążenia karty! Szczegóły: {safeMessage}";
//                    }

//                    await context.SaveChangesAsync();
//                }

//            }
//            catch (Exception ex)
//            {
//                var logError = await context.TaskExecutionLogs.FindAsync(logId);
//                if (logError != null)
//                {
//                    logError.EndTime = DateTime.Now;
//                    logError.Comment += $" | Błąd krytyczny podczas wystawiania: {ex.Message}";
//                    await context.SaveChangesAsync();
//                }
//                throw;
//            }
//        }
//    }
//}


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

        // Klucze autoryzacyjne
        // 99887766 -> Uruchamia logikę GENEROWANIA FAKTUR (Na serwer Webio)
        private const string REQUIRED_GENERATOR_KEY = "99887766";

        // 38401048 -> Uruchamia logikę PŁATNOŚCI KARTĄ (Na maszynę lokalną)
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
            // Sprawdzamy, która to maszyna na podstawie zmiennych ENV
            var genKey = Environment.GetEnvironmentVariable("SUBSCRIPTION_KEY");
            var payKey = Environment.GetEnvironmentVariable("GRAB_PAYMENT");

            bool isGenerator = genKey == REQUIRED_GENERATOR_KEY;
            bool isPayer = payKey == REQUIRED_PAYMENT_KEY;

            if (!isGenerator && !isPayer)
            {
                _logger.LogWarning("Serwis uśpiony: Brak kluczy SUBSCRIPTION_KEY lub GRAB_PAYMENT.");
                // Usypiamy na długo, żeby nie obciążać serwera
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

                    //if (isGenerator)
                    //{
                    //    nextRun = now.Date.AddDays(1).AddMinutes(5);
                    //    if (now.Hour == 0 && now.Minute < 5) nextRun = now.Date.AddMinutes(5);
                    //}
                    //else // isPayer
                    //{
                    //    nextRun = now.Date.AddDays(1).AddMinutes(15);
                    //    if (now.Hour == 0 && now.Minute < 15) nextRun = now.Date.AddMinutes(15);
                    //}



                    if (isGenerator)
                    {
                        // Ustawiamy na godzinę 20:30
                        nextRun = now.Date.AddHours(20).AddMinutes(30);

                        // Jeśli jest już po 20:30, przenieś na jutro (żeby nie odpaliło się od razu jeśli spóźnisz się z wdrożeniem)
                        if (now > nextRun) nextRun = nextRun.AddDays(1);
                    }
                    else // isPayer
                    {
                        // Ustawiamy na godzinę 20:45
                        nextRun = now.Date.AddHours(20).AddMinutes(45);

                        if (now > nextRun) nextRun = nextRun.AddDays(1);
                    }

                    var delay = nextRun - now;
                    _logger.LogInformation($"[{(isGenerator ? "GENERATOR" : "PŁATNIK")}] Czekam {delay.TotalMinutes:F2} min. Start: {nextRun}");

                    await Task.Delay(delay, stoppingToken);

                    if (isGenerator)
                    {
                        await RunInvoiceGenerationLogic(stoppingToken);
                    }

               
                    if (isPayer)
                    {
                        await RunPaymentExecutionLogic(stoppingToken);
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

        // --- LOGIKA A: GENERATOR (Serwer Webio) ---
        // Ta metoda tylko wystawia faktury w bazie danych. NIE łączy się z Imoje.
        private async Task RunInvoiceGenerationLogic(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
            var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "WebioGenerator";

            _logger.LogInformation(">>> [GENERATOR] Rozpoczynam wystawianie faktur...");

            // 1. Inicjalizacja licznika
            var currentYear = DateTime.Now.Year;
            var invoiceCounter = await context.InvoiceCounters.FirstOrDefaultAsync(c => c.Year == currentYear, ct);
            if (invoiceCounter == null)
            {
                invoiceCounter = new InvoiceCounter { Year = currentYear, LastInvoiceNumber = 0, LastProformaNumber = 0 };
                context.InvoiceCounters.Add(invoiceCounter);
                await context.SaveChangesAsync(ct);
            }

            // 2. Pobranie sklepów do odnowienia
            var stores = await context.Stores
                .Include(s => s.Plan)
                .Include(s => s.PaymentData)
                .Where(s => s.PlanId != null && s.IsPayingCustomer)
                .ToListAsync(ct);

            int count = 0;
            foreach (var store in stores)
            {
                // Czy data subskrypcji już nadeszła?
                if (store.SubscriptionStartDate == null || store.SubscriptionStartDate.Value > DateTime.Today) continue;

                // Dekrementacja dni
                if (store.RemainingDays > 0)
                {
                    store.RemainingDays--;
                }

                // Jeśli dni się skończyły -> Wystawiamy nową fakturę
                if (store.RemainingDays <= 0)
                {
                    if (!store.Plan.IsTestPlan && store.Plan.NetPrice > 0)
                    {
                        // Wywołujemy metodę, która tylko tworzy rekord w bazie
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
            // Zwiększamy licznik
            counter.LastInvoiceNumber++;
            string invNum = $"PS/{counter.LastInvoiceNumber:D6}/sDB/{DateTime.Now.Year}";

            decimal netPrice = store.Plan.NetPrice;
            decimal appliedDiscountPercentage = 0;
            decimal appliedDiscountAmount = 0;

            // Obsługa zniżek
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
                IsPaid = false,      // WAŻNE: Faktura nieopłacona
                IsPaidByCard = false,
                InvoiceNumber = invNum,
                NetAmount = netPrice,
                DaysIncluded = store.Plan.DaysPerInvoice,
                UrlsIncluded = store.Plan.ProductsToScrap,
                UrlsIncludedAllegro = store.Plan.ProductsToScrapAllegro,

                // Dane adresowe
                CompanyName = store.PaymentData?.CompanyName ?? "Brak Danych",
                Address = store.PaymentData?.Address ?? "",
                PostalCode = store.PaymentData?.PostalCode ?? "",
                City = store.PaymentData?.City ?? "",
                NIP = store.PaymentData?.NIP ?? "",
                AppliedDiscountPercentage = appliedDiscountPercentage,
                AppliedDiscountAmount = appliedDiscountAmount
            };

            // Odnawiamy dni w sklepie
            store.RemainingDays += store.Plan.DaysPerInvoice;
            store.ProductsToScrap = store.Plan.ProductsToScrap;
            store.ProductsToScrapAllegro = store.Plan.ProductsToScrapAllegro;

            context.Invoices.Add(invoice);

            // Logujemy tylko wystawienie
            context.TaskExecutionLogs.Add(new TaskExecutionLog
            {
                DeviceName = deviceName,
                OperationName = "FAKTURA_AUTO",
                StartTime = DateTime.Now,
                Comment = $"Wystawiono FV {invNum}. Oczekuje na Workera Płatności (Localhost)."
            });
        }

        // --- LOGIKA B: PŁATNIK (Maszyna Lokalna) ---
        // Ta metoda działa tylko na lokalnym PC, który obsługuje TLS 1.3
        private async Task RunPaymentExecutionLogic(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PriceSafariContext>();
            var imojeService = scope.ServiceProvider.GetRequiredService<IImojeService>();
            var deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME") ?? "LocalWorker";

            // Próba pobrania publicznego IP (opcjonalne, dla logów)
            string myIp = "127.0.0.1";
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(2);
                myIp = await client.GetStringAsync("https://api.ipify.org");
            }
            catch { }

            _logger.LogInformation($">>> [PŁATNIK] Szukam nieopłaconych faktur... (Moje IP: {myIp})");

            // Pobieramy faktury, które:
            // 1. Są nieopłacone
            // 2. Sklep ma aktywną subskrypcję (Recurring)
            // 3. Zostały wystawione w ciągu ostatnich 3 dni (żeby nie próbować starych w nieskończoność)
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

                // Wywołujemy serwis Imoje (Teraz zadziała, bo jesteśmy na Windows 10/11 z TLS 1.3)
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

                    // Log sukcesu
                    context.TaskExecutionLogs.Add(new TaskExecutionLog
                    {
                        DeviceName = deviceName,
                        OperationName = "WORKER_PLATNOSC",
                        StartTime = DateTime.Now,
                        Comment = $"SUKCES. FV: {invoice.InvoiceNumber} opłacona. IP Workera: {myIp}"
                    });
                }
                else
                {
                    // Log błędu (ale kontynuujemy z następną fakturą)
                    string safeMsg = response.Length > 200 ? response.Substring(0, 200) : response;
                    context.TaskExecutionLogs.Add(new TaskExecutionLog
                    {
                        DeviceName = deviceName,
                        OperationName = "WORKER_BLAD",
                        StartTime = DateTime.Now,
                        Comment = $"BŁĄD. FV: {invoice.InvoiceNumber}. Info: {safeMsg}"
                    });
                }

                // Zapisujemy stan po każdej fakturze (bezpieczeństwo danych)
                await context.SaveChangesAsync(ct);
            }

            _logger.LogInformation($"<<< [PŁATNIK] Zakończono. Przetworzono: {invoicesToPay.Count}");
        }
    }
}