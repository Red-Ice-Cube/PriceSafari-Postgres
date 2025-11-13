using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;

namespace PriceSafari.Services.SubscriptionService
{
    public class SubscriptionManagerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SubscriptionManagerService> _logger;

        private const string REQUIRED_ENV_KEY = "99887766";

        public SubscriptionManagerService(IServiceScopeFactory scopeFactory, ILogger<SubscriptionManagerService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var machineKey = Environment.GetEnvironmentVariable("SUBSCRIPTION_KEY");

            if (machineKey != REQUIRED_ENV_KEY)
            {
                _logger.LogWarning($"Maszyna nieautoryzowana. Serwis uśpiony.");
                while (!stoppingToken.IsCancellationRequested) await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                return;
            }

            _logger.LogInformation("Autoryzacja udana. SubscriptionManagerService startuje.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var nextRun = now.Date.AddDays(1).AddMinutes(5);
                    if (now.Hour == 0 && now.Minute < 5) nextRun = now.Date.AddMinutes(5);

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

            _logger.LogInformation("Rozpoczynanie przetwarzania subskrypcji...");

            var today = DateTime.Today;

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
                            _logger.LogInformation($"Sklep PŁATNY {store.StoreName}: Zabrano 1 dzień. Pozostało: {store.RemainingDays}.");
                        }

                        if (store.RemainingDays <= 0)
                        {

                            if (!store.Plan.IsTestPlan && store.Plan.NetPrice > 0)
                            {
                                _logger.LogInformation($"Sklep {store.StoreName}: Start/Odnowienie subskrypcji. Generowanie faktury...");
                                await GenerateRenewalInvoiceAsync(context, store, today);
                            }
                            else
                            {
                                _logger.LogWarning($"Sklep {store.StoreName} jest oznaczony jako płatnik, ale ma plan darmowy/testowy. Pomijam fakturowanie.");
                            }
                        }
                    }

                    else
                    {

                        if (store.RemainingDays > 0)
                        {
                            store.RemainingDays--;
                            _logger.LogInformation($"Sklep TESTOWY {store.StoreName}: Zabrano 1 dzień testowy. Pozostało: {store.RemainingDays}.");
                        }
                        else
                        {

                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Błąd przy sklepie {store.StoreName} (ID: {store.StoreId}).");
                }
            }

            await context.SaveChangesAsync(ct);
            _logger.LogInformation("Zakończono przetwarzanie subskrypcji.");
        }

        private async Task GenerateRenewalInvoiceAsync(PriceSafariContext context, StoreClass store, DateTime issueDate)
        {

            if (store.Plan.IsTestPlan || store.Plan.NetPrice == 0)
            {
                _logger.LogWarning($"PRÓBA WYSTAWIENIA FAKTURY DLA PLANU TESTOWEGO! Sklep: {store.StoreName}. Anulowano.");
                return;
            }

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

            int invoiceNumber = await GetNextInvoiceNumberAsync(context, issueDate.Year);
            string invoiceNumberFormatted = $"PS/{invoiceNumber:D6}/sDB/{issueDate.Year}";

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

                CompanyName = paymentData?.CompanyName ?? "Brak Danych",
                Address = paymentData?.Address ?? "",
                PostalCode = paymentData?.PostalCode ?? "",
                City = paymentData?.City ?? "",
                NIP = paymentData?.NIP ?? "",

                AppliedDiscountPercentage = appliedDiscountPercentage,
                AppliedDiscountAmount = appliedDiscountAmount,
                InvoiceNumber = invoiceNumberFormatted
            };

            context.Invoices.Add(invoice);

            store.RemainingDays += store.Plan.DaysPerInvoice;
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