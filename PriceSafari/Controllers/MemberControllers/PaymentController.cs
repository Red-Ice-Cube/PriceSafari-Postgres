using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using PriceSafari.Services.Imoje;
using System.Security.Claims;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Member, PreMember")]
    public class PaymentController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly IImojeService _imojeService; // Interfejs serwisu
        private readonly IConfiguration _config;      // Potrzebne do pobrania kluczy z appsettings/.env

        // Poprawiony konstruktor
        public PaymentController(
            PriceSafariContext context,
            UserManager<PriceSafariUser> userManager,
            IImojeService imojeService,
            IConfiguration config)
        {
            _context = context;
            _userManager = userManager;
            _imojeService = imojeService;
            _config = config;
        }

        [HttpGet]
        public async Task<IActionResult> StorePlans()
        {
            if (User.IsInRole("PreMember"))
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return Challenge();
                }

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

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Unauthorized();
            }

            var store = await _context.Stores
                .Include(s => s.Plan)
                .Include(s => s.Invoices)
                .Include(s => s.PaymentData)
                .FirstOrDefaultAsync(s => s.StoreId == storeId);

            if (store == null)
            {
                return NotFound("Sklep nie został znaleziony.");
            }

            var paymentDataList = store.PaymentData != null
                ? new List<UserPaymentData> { store.PaymentData }
                : new List<UserPaymentData>();

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
                Info = store.Plan?.Info ?? string.Empty
            };

            return View("~/Views/Panel/Plans/StorePayments.cshtml", viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> InvoicePdf(int invoiceId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var invoice = await _context.Invoices
                .Include(i => i.Store)
                    .ThenInclude(s => s.UserStores)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId && i.Store.UserStores.Any(us => us.UserId == userId));

            if (invoice == null)
            {
                return NotFound("Faktura nie została znaleziona.");
            }

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

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Unauthorized();
            }

            var store = await _context.Stores.FirstOrDefaultAsync(s => s.StoreId == storeId);

            if (store == null)
            {
                return NotFound("Sklep nie został znaleziony.");
            }

            store.UserWantsExit = true;

            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }





        [HttpPost] // Akcja wywoływana AJAX-em, żeby dostać dane do widgetu
        public IActionResult GetImojeWidgetData(int storeId)
        {
            var userId = _userManager.GetUserId(User);
            var store = _context.Stores.Include(s => s.Plan).FirstOrDefault(s => s.StoreId == storeId);

            // Logika: Pobieramy 1 PLN (lub kwotę planu) w celu autoryzacji.
            // Jeśli 1 PLN to potem robimy refund, jeśli pełna kwota to przedłużamy ważność.
            // Przyjmijmy autoryzację na 1.00 PLN
            string amount = "100"; // 100 groszy = 1 PLN
            string orderId = $"REG-{store.StoreId}-{DateTime.Now.Ticks}"; // Unikalne ID transakcji rejestrującej
            string customerId = store.StoreId.ToString(); // CID - ważne dla recurring

            var data = new Dictionary<string, string>
                {
                    { "merchantId", _config["Imoje:MerchantId"] },
                    { "serviceId", _config["Imoje:ServiceId"] },
                    { "amount", amount },
                    { "currency", "PLN" },
                    { "orderId", orderId },
                    { "customerId", customerId }, // To jest kluczowe!
                    { "customerFirstName", "Jan" }, // Pobierz z danych usera/sklepu
                    { "customerLastName", "Kowalski" },
                    { "customerEmail", "email@sklepu.pl" },
                    // Ważne: widgetType recurring
                    { "widgetType", "recurring" }
                };

            var signature = _imojeService.CalculateSignature(data);

            return Json(new
            {
                success = true,
                data = data,
                signature = signature,
                scriptUrl = _config["Imoje:WidgetUrl"] // https://sandbox.paywall.imoje.pl/js/widget.min.js
            });
        }
    }
}