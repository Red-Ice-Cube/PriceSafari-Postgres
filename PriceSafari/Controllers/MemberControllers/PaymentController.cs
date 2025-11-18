using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR; // WAŻNE: Dodany using
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using PriceSafari.Services.Imoje;
using System.Security.Claims;

namespace PriceSafari.Controllers.MemberControllers
{
    // 1. Definicja Huba "na szybko" w tym samym pliku
    public class PaymentDebugHub : Hub
    {
    }

    [Authorize(Roles = "Member, PreMember")]
    public class PaymentController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly IImojeService _imojeService;
        private readonly IConfiguration _config;
        private readonly IHubContext<PaymentDebugHub> _hubContext; // 2. Pola dla Huba

        public PaymentController(
            PriceSafariContext context,
            UserManager<PriceSafariUser> userManager,
            IImojeService imojeService,
            IConfiguration config,
            IHubContext<PaymentDebugHub> hubContext) // 3. Wstrzyknięcie w konstruktorze
        {
            _context = context;
            _userManager = userManager;
            _imojeService = imojeService;
            _config = config;
            _hubContext = hubContext;
        }

        [HttpGet]
        public async Task<IActionResult> StorePlans()
        {
            if (User.IsInRole("PreMember"))
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null) return Challenge();

                var viewModel = new AwaitingConfigurationViewModel
                {
                    StoreName = user.PendingStoreNameCeneo ?? user.PendingStoreNameGoogle ?? "Twój Sklep (w konfiguracji)",
                    HasCeneoFeed = !string.IsNullOrWhiteSpace(user.PendingCeneoFeedUrl),
                    HasGoogleFeed = !string.IsNullOrWhiteSpace(user.PendingGoogleFeedUrl)
                };
                return View("~/Views/Panel/Plans/AwaitingConfiguration.cshtml", viewModel);
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userStores = await _context.UserStores
                .Where(us => us.UserId == userId)
                .Include(us => us.StoreClass).ThenInclude(s => s.Plan)
                .Include(us => us.StoreClass).ThenInclude(s => s.ScrapHistories)
                .Include(us => us.StoreClass).ThenInclude(s => s.Invoices)
                .ToListAsync();

            var storeViewModels = userStores.Select(us =>
            {
                var store = us.StoreClass;
                return new PaymentViewModel
                {
                    StoreId = store.StoreId,
                    StoreName = store.StoreName,
                    LogoUrl = store.StoreLogoUrl,
                    PlanName = store.Plan?.PlanName ?? "Brak Planu",
                    PlanPrice = store.Plan?.NetPrice ?? 0,
                    IsTestPlan = store.Plan?.IsTestPlan ?? false,
                    ProductsToScrap = store.ProductsToScrap ?? 0,
                    ProductsToScrapAllegro = store.ProductsToScrapAllegro ?? 0,
                    LeftDays = store.RemainingDays,
                    Ceneo = store.Plan?.Ceneo ?? false,
                    GoogleShopping = store.Plan?.GoogleShopping ?? false,
                    Allegro = store.Plan?.Allegro ?? false,
                    Info = store.Plan?.Info ?? string.Empty
                };
            }).ToList();

            return View("~/Views/Panel/Plans/StorePlans.cshtml", storeViewModels);
        }

        [HttpGet]
        public async Task<IActionResult> StorePayments(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null) return Unauthorized();

            var store = await _context.Stores
                .Include(s => s.Plan)
                .Include(s => s.Invoices)
                .Include(s => s.PaymentData)
                .FirstOrDefaultAsync(s => s.StoreId == storeId);

            if (store == null) return NotFound("Sklep nie został znaleziony.");

            var paymentDataList = store.PaymentData != null ? new List<UserPaymentData> { store.PaymentData } : new List<UserPaymentData>();

            var viewModel = new StorePaymentsViewModel
            {
                StoreId = store.StoreId,
                StoreName = store.StoreName,
                LogoUrl = store.StoreLogoUrl,
                PlanName = store.Plan?.PlanName ?? "Brak Planu",
                IsTestPlan = store.Plan?.IsTestPlan ?? false,
                PlanPrice = store.Plan?.NetPrice ?? 0,
                ProductsToScrap = store.ProductsToScrap ?? 0,
                ProductsToScrapAllegro = store.ProductsToScrapAllegro ?? 0,
                ScrapesPerInvoice = store.Plan?.DaysPerInvoice ?? 0,
                HasUnpaidInvoice = store.Invoices.Any(i => !i.IsPaid),
                DiscountValue = store.DiscountPercentage,
                Invoices = store.Invoices.OrderByDescending(i => i.IssueDate).ToList(),
                PaymentDataList = paymentDataList,
                Ceneo = store.Plan?.Ceneo ?? false,
                GoogleShopping = store.Plan?.GoogleShopping ?? false,
                Allegro = store.Plan?.Allegro ?? false,
                Info = store.Plan?.Info ?? string.Empty,
                IsRecurringActive = store.IsRecurringActive,
                CardMaskedNumber = store.CardMaskedNumber,
                CardBrand = store.CardBrand
            };

            return View("~/Views/Panel/Plans/StorePayments.cshtml", viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> InvoicePdf(int invoiceId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var invoice = await _context.Invoices
                .Include(i => i.Store).ThenInclude(s => s.UserStores)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId && i.Store.UserStores.Any(us => us.UserId == userId));

            if (invoice == null) return NotFound("Faktura nie została znaleziona.");
            var pdfBytes = GenerateInvoicePdf(invoice);
            return File(pdfBytes, "application/pdf", $"{invoice.InvoiceNumber}.pdf");
        }

        private byte[] GenerateInvoicePdf(InvoiceClass invoice)
        {
            var logoImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "cid", "signature.png");
            var document = new InvoiceDocument(invoice, logoImagePath);
            return document.GeneratePdf();
        }

        [HttpPost]
        public async Task<IActionResult> RequestResignation(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null) return Unauthorized();

            var store = await _context.Stores.FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null) return NotFound("Sklep nie został znaleziony.");

            store.UserWantsExit = true;
            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> GetImojeWidgetData(int storeId)
        {
            var userId = _userManager.GetUserId(User);
            var userStore = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (!userStore) return Unauthorized();

            var store = await _context.Stores.Include(s => s.Plan).FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            string customerFirstName = user.PartnerName ?? "Jan";
            string customerLastName = user.PartnerSurname ?? "Kowalski";
            string customerEmail = user.Email;

            string amount = "100";
            string orderId = $"REG-{store.StoreId}-{DateTime.Now.Ticks}";
            string customerId = store.StoreId.ToString();

            var merchantId = _config["IMOJE_MERCHANT_ID"];
            var serviceId = _config["IMOJE_SERVICE_ID"];
            var widgetUrl = _config["IMOJE_WIDGET_URL"];

            if (string.IsNullOrEmpty(merchantId) || string.IsNullOrEmpty(serviceId) || string.IsNullOrEmpty(widgetUrl))
            {
                return StatusCode(500, new { error = "Błąd konfiguracji serwera: Brak kluczy imoje w .env" });
            }

            // === POPRAWKA URL DLA PRODUKCJI I IMOJE ===
            // Imoje wymaga absolutnego URL. Wstawiam Twoją domenę na sztywno, 
            // aby mieć pewność, że nie wygeneruje "localhost" albo wewnętrznego IP.
            string baseUrl = "https://myjki.com.pricesafari.pl";
            var urlNotification = $"{baseUrl}/Payment/Notification";

            var urlSuccess = Url.Action("StorePayments", "Payment", new { storeId = storeId, status = "success" }, Request.Scheme);
            var urlFailure = Url.Action("StorePayments", "Payment", new { storeId = storeId, status = "failure" }, Request.Scheme);

            var signatureData = new Dictionary<string, string>
            {
                { "merchantId", merchantId },
                { "serviceId", serviceId },
                { "amount", amount },
                { "currency", "PLN" },
                { "orderId", orderId },
                { "customerId", customerId },
                { "customerFirstName", customerFirstName },
                { "customerLastName", customerLastName },
                { "customerEmail", customerEmail },
                { "widgetType", "recurring" },
                { "urlNotification", urlNotification },
                { "urlSuccess", urlSuccess },
                { "urlFailure", urlFailure }
            };

            var signature = _imojeService.CalculateSignature(signatureData);
            var frontendData = new Dictionary<string, string>(signatureData);

            return Json(new
            {
                success = true,
                data = frontendData,
                signature = signature,
                scriptUrl = widgetUrl
            });
        }

        [HttpPost]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [Route("Payment/Notification")]
        public async Task<IActionResult> Notification()
        {
            // 1. Najpierw czytamy body
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            // 2. WYSYŁAMY SUROWY JSON DO TWOJEJ KONSOLI PRZEGLĄDARKI
            await _hubContext.Clients.All.SendAsync("ReceiveImojeData", body);

            if (!Request.Headers.TryGetValue("X-Imoje-Signature", out var signatureHeader))
            {
                await _hubContext.Clients.All.SendAsync("ReceiveImojeLog", "BŁĄD: Brak nagłówka X-Imoje-Signature");
                return BadRequest("Missing Signature Header");
            }

            await _hubContext.Clients.All.SendAsync("ReceiveImojeLog", $"Nagłówek sygnatury: {signatureHeader}");

            // 3. Przekazujemy do logiki biznesowej
            bool success = await _imojeService.HandleNotificationAsync(signatureHeader, body);

            if (!success)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveImojeLog", "BŁĄD: Weryfikacja podpisu w ImojeService nie powiodła się.");
                return BadRequest("Invalid Signature");
            }

            await _hubContext.Clients.All.SendAsync("ReceiveImojeLog", "SUKCES: Przetworzono w ImojeService (zwrócono true). Sprawdź bazę.");

            return Ok(new { status = "ok" });
        }
    }
}