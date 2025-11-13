using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using System.Security.Claims;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Member, PreMember")]
    public class PaymentController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        public PaymentController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
        {
            _context = context;
            _userManager = userManager;
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

        [HttpPost]
        public async Task<IActionResult> SaveOrUpdatePaymentData(UserPaymentDataViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (model.StoreId <= 0)
            {
                return BadRequest("Brak identyfikatora sklepu.");
            }

            var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == model.StoreId);
            if (!hasAccess)
            {
                return Unauthorized();
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (string.IsNullOrWhiteSpace(model.InvoiceAutoMail))
            {
                model.InvoiceAutoMailSend = false;
            }

            var existingPaymentData = await _context.UserPaymentDatas
                .FirstOrDefaultAsync(pd => pd.StoreId == model.StoreId);

            UserPaymentData paymentDataEntity;

            if (existingPaymentData != null)
            {

                paymentDataEntity = existingPaymentData;
                paymentDataEntity.CompanyName = model.CompanyName;
                paymentDataEntity.Address = model.Address;
                paymentDataEntity.PostalCode = model.PostalCode;
                paymentDataEntity.City = model.City;
                paymentDataEntity.NIP = model.NIP;
                paymentDataEntity.InvoiceAutoMail = model.InvoiceAutoMail;
                paymentDataEntity.InvoiceAutoMailSend = model.InvoiceAutoMailSend && !string.IsNullOrWhiteSpace(model.InvoiceAutoMail);

                _context.UserPaymentDatas.Update(paymentDataEntity);
            }
            else
            {

                paymentDataEntity = new UserPaymentData
                {
                    StoreId = model.StoreId,

                    CompanyName = model.CompanyName,
                    Address = model.Address,
                    PostalCode = model.PostalCode,
                    City = model.City,
                    NIP = model.NIP,
                    InvoiceAutoMail = model.InvoiceAutoMail,
                    InvoiceAutoMailSend = model.InvoiceAutoMailSend && !string.IsNullOrWhiteSpace(model.InvoiceAutoMail)
                };
                _context.UserPaymentDatas.Add(paymentDataEntity);
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                paymentData = new
                {
                    paymentDataId = paymentDataEntity.PaymentDataId,
                    companyName = paymentDataEntity.CompanyName,
                    address = paymentDataEntity.Address,
                    postalCode = paymentDataEntity.PostalCode,
                    city = paymentDataEntity.City,
                    nip = paymentDataEntity.NIP,
                    invoiceAutoMail = paymentDataEntity.InvoiceAutoMail,
                    invoiceAutoMailSend = paymentDataEntity.InvoiceAutoMailSend
                }
            });
        }

        [HttpPost]
        public async Task<IActionResult> DeletePaymentData(int paymentDataId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var paymentData = await _context.UserPaymentDatas
                .Include(pd => pd.Store)
                .ThenInclude(s => s.UserStores)
                .FirstOrDefaultAsync(pd => pd.PaymentDataId == paymentDataId);

            if (paymentData == null)
            {
                return NotFound();
            }

            var hasAccess = paymentData.Store.UserStores.Any(us => us.UserId == userId);
            if (!hasAccess)
            {
                return Unauthorized();
            }

            _context.UserPaymentDatas.Remove(paymentData);
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateInvoice(int storeId)
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

            if (store == null || store.Plan == null)
            {
                return NotFound("Sklep lub plan nie został znaleziony.");
            }

            var plan = store.Plan;

            if (plan.NetPrice == 0 || plan.IsTestPlan)
            {
                store.RemainingDays = plan.DaysPerInvoice;
                var unpaidInvoices = await _context.Invoices.Where(i => i.StoreId == store.StoreId && !i.IsPaid).ToListAsync();
                foreach (var unpaidInvoice in unpaidInvoices)
                {
                    unpaidInvoice.IsPaid = true;
                }
                await _context.SaveChangesAsync();

                TempData["Success"] = "Plan darmowy został aktywowany.";
                return RedirectToAction("StorePayments", new { storeId = storeId });
            }

            if (store.Invoices.Any(i => !i.IsPaid))
            {
                TempData["Error"] = "Istnieje już nieopłacona faktura dla tego sklepu.";
                return RedirectToAction("StorePayments", new { storeId = storeId });
            }

            var paymentData = store.PaymentData;

            if (paymentData == null)
            {
                TempData["Error"] = "Uzupełnij dane do faktury przed jej wygenerowaniem.";
                return RedirectToAction("StorePayments", new { storeId = storeId });
            }

            decimal netPrice = store.Plan.NetPrice;
            decimal appliedDiscountPercentage = 0;
            decimal appliedDiscountAmount = 0;

            if (store.DiscountPercentage.HasValue && store.DiscountPercentage.Value > 0)
            {
                appliedDiscountPercentage = store.DiscountPercentage.Value;
                appliedDiscountAmount = netPrice * (appliedDiscountPercentage / 100m);
                netPrice = netPrice - appliedDiscountAmount;
            }

            int invoiceNumber = await GetNextInvoiceNumberAsync();
            var currentYear = DateTime.Now.Year;

            var invoiceNumberFormatted = $"PS/{invoiceNumber.ToString("D6")}/sDB/{currentYear}";

            var invoice = new InvoiceClass
            {
                StoreId = storeId,
                PlanId = store.PlanId.Value,
                IssueDate = DateTime.Now,
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

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Faktura została wygenerowana. Prosimy o dokonanie płatności.";
            return RedirectToAction("StorePayments", new { storeId = storeId });
        }

        private async Task<int> GetNextInvoiceNumberAsync()
        {
            var currentYear = DateTime.Now.Year;
            var counter = await _context.InvoiceCounters.FirstOrDefaultAsync(c => c.Year == currentYear);

            if (counter == null)
            {

                counter = new InvoiceCounter { Year = currentYear, LastProformaNumber = 0, LastInvoiceNumber = 0 };
                _context.InvoiceCounters.Add(counter);
                await _context.SaveChangesAsync();
            }

            counter.LastInvoiceNumber++;
            await _context.SaveChangesAsync();

            return counter.LastInvoiceNumber;
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
    }
}