using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;

namespace PriceSafari.Services.SubscriptionService
{
    public class SubscriptionManagerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SubscriptionManagerService> _logger;

        // To jest Twoje hasło - musi być takie samo w pliku .env na serwerze zarządczym
        private const string REQUIRED_ENV_KEY = "99887766";

        public SubscriptionManagerService(IServiceScopeFactory scopeFactory, ILogger<SubscriptionManagerService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var machineKey = Environment.GetEnvironmentVariable("SUBSCRIPTION_KEY");

            // ZABEZPIECZENIE: Sprawdzenie czy ta maszyna ma prawo wykonywać operacje finansowe
            if (machineKey != REQUIRED_ENV_KEY)
            {
                _logger.LogWarning($"Maszyna nieautoryzowana do zarządzania subskrypcjami. Brak poprawnego klucza SUBSCRIPTION_KEY. Oczekiwano: {REQUIRED_ENV_KEY}, otrzymano: {machineKey ?? "null"}. Serwis przechodzi w stan uśpienia.");
                // Jeśli klucz się nie zgadza, serwis po prostu "wisi" i nic nie robi do końca życia procesu.
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                return;
            }

            _logger.LogInformation("Autoryzacja udana. SubscriptionManagerService startuje.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    // Ustawiamy start na 00:05 następnego dnia
                    var nextRun = now.Date.AddDays(1).AddMinutes(5);

                    // Jeśli serwis wstanie np. o 00:01, to ma odpalić się jeszcze dzisiaj o 00:05
                    if (now.Hour == 0 && now.Minute < 5)
                    {
                        nextRun = now.Date.AddMinutes(5);
                    }

                    var delay = nextRun - now;
                    _logger.LogInformation($"Oczekiwanie na uruchomienie procesu subskrypcji: {delay.TotalMinutes:F2} minut. (Planowany start: {nextRun})");

                    await Task.Delay(delay, stoppingToken);

                    await ProcessSubscriptionsAsync(stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Ignoruj przy zamykaniu
                }
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

            _logger.LogInformation("Rozpoczynanie przetwarzania subskrypcji i płatności...");

            var today = DateTime.Today;

            // Pobieramy aktywnych płatników
            var stores = await context.Stores
                .Include(s => s.Plan)
                .Include(s => s.PaymentData)
                .Where(s => s.IsPayingCustomer == true)
                .ToListAsync(ct);

            foreach (var store in stores)
            {
                try
                {
                    // 1. Walidacja daty startu
                    if (store.SubscriptionStartDate == null || store.SubscriptionStartDate.Value > today)
                    {
                        continue; // Jeszcze nie czas na tego klienta
                    }

                    // 2. LOGIKA OJMOWANIA DNI
                    // Najpierw zabieramy dzień, bo minęła doba (skoro skrypt odpala się po północy)
                    if (store.RemainingDays > 0)
                    {
                        store.RemainingDays--;
                        _logger.LogInformation($"Sklep {store.StoreName} (ID: {store.StoreId}): Zabrano 1 dzień. Stan po odjęciu: {store.RemainingDays}.");
                    }

                    // 3. LOGIKA ODNOWIENIA (NATYCHMIASTOWA)
                    // Sprawdzamy stan PO odjęciu dnia.
                    // Jeśli zeszliśmy do 0 (lub mniej), A JEDNOCZEŚNIE klient jest płatnikiem i data jest OK
                    // to OD RAZU wystawiamy fakturę i ładujemy dni.
                    // Dzięki temu rano scraper zobaczy już np. 30 dni, a nie 0.

                    if (store.RemainingDays <= 0)
                    {
                        if (store.Plan == null)
                        {
                            _logger.LogWarning($"Sklep {store.StoreName} wymaga odnowienia, ale nie ma Planu! Pomijam.");
                            continue;
                        }

                        _logger.LogInformation($"Sklep {store.StoreName}: Wykryto 0 dni. Generowanie faktury i odnawianie pakietu...");

                        await GenerateRenewalInvoiceAsync(context, store, today);

                        _logger.LogInformation($"Sklep {store.StoreName}: Sukces. Nowy stan dni: {store.RemainingDays}.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Błąd podczas przetwarzania sklepu {store.StoreName} (ID: {store.StoreId}).");
                }
            }

            await context.SaveChangesAsync(ct);
            _logger.LogInformation("Zakończono przetwarzanie subskrypcji na dzisiaj.");
        }

        private async Task GenerateRenewalInvoiceAsync(PriceSafariContext context, StoreClass store, DateTime issueDate)
        {
            // --- Logika finansowa ---
            decimal netPrice = store.Plan.NetPrice;
            decimal appliedDiscountPercentage = 0;
            decimal appliedDiscountAmount = 0;

            if (store.DiscountPercentage.HasValue && store.DiscountPercentage.Value > 0)
            {
                appliedDiscountPercentage = store.DiscountPercentage.Value;
                appliedDiscountAmount = netPrice * (appliedDiscountPercentage / 100m);
                netPrice = netPrice - appliedDiscountAmount;
            }

            // Numeracja
            int invoiceNumber = await GetNextInvoiceNumberAsync(context, issueDate.Year);
            string invoiceNumberFormatted = $"PS/{invoiceNumber:D6}/sDB/{issueDate.Year}";

            var paymentData = store.PaymentData ?? new UserPaymentData();

            // Tworzenie faktury
            var invoice = new InvoiceClass
            {
                StoreId = store.StoreId,
                PlanId = store.PlanId.Value,
                IssueDate = issueDate,
                NetAmount = netPrice,
                DaysIncluded = store.Plan.DaysPerInvoice,
                UrlsIncluded = store.Plan.ProductsToScrap,
                UrlsIncludedAllegro = store.Plan.ProductsToScrapAllegro,
                IsPaid = false,
                CompanyName = paymentData.CompanyName,
                Address = paymentData.Address,
                PostalCode = paymentData.PostalCode,
                City = paymentData.City,
                NIP = paymentData.NIP,
                AppliedDiscountPercentage = appliedDiscountPercentage,
                AppliedDiscountAmount = appliedDiscountAmount,
                InvoiceNumber = invoiceNumberFormatted
            };

            context.Invoices.Add(invoice);

            // --- KLUCZOWY MOMENT: DOŁADOWANIE DNI ---
            // Dodajemy dni z pakietu do obecnego stanu (który wynosi 0)
            store.RemainingDays += store.Plan.DaysPerInvoice;

            // Reset limitów produktów
            store.ProductsToScrap = store.Plan.ProductsToScrap;
            store.ProductsToScrapAllegro = store.Plan.ProductsToScrapAllegro;
        }

        private async Task<int> GetNextInvoiceNumberAsync(PriceSafariContext context, int year)
        {
            var counter = await context.InvoiceCounters.FirstOrDefaultAsync(c => c.Year == year);
            if (counter == null)
            {
                counter = new InvoiceCounter { Year = year, LastProformaNumber = 0, LastInvoiceNumber = 0 };
                context.InvoiceCounters.Add(counter);
            }

            counter.LastInvoiceNumber++;
            return counter.LastInvoiceNumber;
        }
    }
}