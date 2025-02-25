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
    [Authorize(Roles = "Member")]
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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStores = await _context.UserStores
                .Where(us => us.UserId == userId)
                .Include(us => us.StoreClass)
                    .ThenInclude(s => s.Plan)
                .Include(us => us.StoreClass)
                    .ThenInclude(s => s.ScrapHistories)
                .Include(us => us.StoreClass)
                    .ThenInclude(s => s.Invoices)
                .ToListAsync();

            var storeViewModels = userStores.Select(us =>
            {
                var store = us.StoreClass;
                var lastScrapeDate = store.ScrapHistories.OrderByDescending(sh => sh.Date).FirstOrDefault()?.Date;
                var unpaidInvoiceExists = store.Invoices.Any(i => !i.IsPaid);

                return new PaymentViewModel
                {
                    StoreId = store.StoreId,
                    StoreName = store.StoreName,
                    LogoUrl = store.StoreLogoUrl,
                    PlanName = store.Plan?.PlanName ?? "Brak Planu",
                    PlanPrice = store.Plan?.NetPrice ?? 0,
                    IsTestPlan = store.Plan?.IsTestPlan ?? false,
                    ProductsToScrap = store.ProductsToScrap ?? 0,
                    LeftScrapes = store.RemainingScrapes,
                    // Nowe właściwości
                    Ceneo = store.Plan?.Ceneo ?? false,
                    GoogleShopping = store.Plan?.GoogleShopping ?? false,
                    Info = store.Plan?.Info ?? string.Empty
                };
            }).ToList();

            return View("~/Views/Panel/Plans/StorePlans.cshtml", storeViewModels);
        }


        // GET: Payment/StorePayments/5
        [HttpGet]
        public async Task<IActionResult> StorePayments(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Sprawdzenie, czy sklep należy do użytkownika
            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Unauthorized();
            }

            var store = await _context.Stores
                .Include(s => s.Plan)
                .Include(s => s.Invoices)
                .FirstOrDefaultAsync(s => s.StoreId == storeId);

            if (store == null)
            {
                return NotFound("Sklep nie został znaleziony.");
            }

            // Pobierz zapisane dane rozliczeniowe użytkownika
            var paymentDataList = await _context.UserPaymentDatas
                .Where(p => p.UserId == userId)
                .ToListAsync();

            // Przygotowanie modelu widoku
            var viewModel = new StorePaymentsViewModel
            {
                StoreId = store.StoreId,
                StoreName = store.StoreName,
                LogoUrl = store.StoreLogoUrl,
                PlanName = store.Plan?.PlanName ?? "Brak Planu",
                IsTestPlan = store.Plan.IsTestPlan,
                PlanPrice = store.Plan?.NetPrice ?? 0,
                ProductsToScrap = store.Plan?.ProductsToScrap ?? 0,
                ScrapesPerInvoice = store.Plan?.ScrapesPerInvoice ?? 0,
                HasUnpaidInvoice = store.Invoices.Any(i => !i.IsPaid),
                DiscountValue = store.DiscountPercentage,
                Invoices = store.Invoices.OrderByDescending(i => i.IssueDate).ToList(),
                PaymentDataList = paymentDataList
            };

            return View("~/Views/Panel/Plans/StorePayments.cshtml", viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> SaveOrUpdatePaymentData(UserPaymentDataViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!ModelState.IsValid)
            {
                // Return validation errors
                return BadRequest(ModelState);
            }

            // Upewniamy się, że jeśli nie ma e-maila, to auto-wysyłka jest wyłączona
            if (string.IsNullOrWhiteSpace(model.InvoiceAutoMail))
            {
                model.InvoiceAutoMailSend = false;
            }

            UserPaymentData paymentDataEntity;

            if (model.PaymentDataId.HasValue && model.PaymentDataId > 0)
            {
                // Update existing data
                paymentDataEntity = await _context.UserPaymentDatas
                    .FirstOrDefaultAsync(pd => pd.UserId == userId && pd.PaymentDataId == model.PaymentDataId.Value);

                if (paymentDataEntity == null)
                {
                    return NotFound();
                }

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
                // Add new data
                paymentDataEntity = new UserPaymentData
                {
                    UserId = userId,
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

            // Teraz paymentDataEntity.PaymentDataId jest już ustawione przez bazę w przypadku nowego wpisu.
            // Upewniamy się, że model ma ustawione poprawne PaymentDataId na potrzeby odpowiedzi:
            model.PaymentDataId = paymentDataEntity.PaymentDataId;

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



        // POST: Payment/DeletePaymentData
        [HttpPost]
        public async Task<IActionResult> DeletePaymentData(int paymentDataId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var paymentData = await _context.UserPaymentDatas
                .FirstOrDefaultAsync(pd => pd.UserId == userId && pd.PaymentDataId == paymentDataId);

            if (paymentData == null)
            {
                return NotFound();
            }

            _context.UserPaymentDatas.Remove(paymentData);
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateProforma(int storeId, int paymentDataId)
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
                .FirstOrDefaultAsync(s => s.StoreId == storeId);

            if (store == null || store.Plan == null)
            {
                return NotFound("Sklep lub plan nie został znaleziony.");
            }

            var plan = store.Plan;

            // Jeśli plan darmowy lub testowy
            if (plan.NetPrice == 0 || plan.IsTestPlan)
            {
                store.RemainingScrapes = plan.ScrapesPerInvoice;
                var unpaidInvoices = await _context.Invoices.Where(i => i.StoreId == store.StoreId && !i.IsPaid).ToListAsync();
                foreach (var unpaidInvoice in unpaidInvoices)
                {
                    unpaidInvoice.IsPaid = true;
                }
                await _context.SaveChangesAsync();

                TempData["Success"] = "Plan darmowy został aktywowany.";
                return RedirectToAction("StorePayments", new { storeId = storeId });
            }

            // Sprawdź, czy nie ma już nieopłaconej faktury
            if (store.Invoices.Any(i => !i.IsPaid))
            {
                TempData["Error"] = "Istnieje już wygenerowana proforma dla tego sklepu.";
                return RedirectToAction("StorePayments", new { storeId = storeId });
            }

            var paymentData = await _context.UserPaymentDatas
                .FirstOrDefaultAsync(pd => pd.UserId == userId && pd.PaymentDataId == paymentDataId);

            if (paymentData == null)
            {
                TempData["Error"] = "Nieprawidłowe dane rozliczeniowe.";
                return RedirectToAction("StorePayments", new { storeId = storeId });
            }

            // Wylicz cenę netto z rabatem
            decimal netPrice = store.Plan.NetPrice;
            decimal appliedDiscountPercentage = 0;
            decimal appliedDiscountAmount = 0;

            if (store.DiscountPercentage.HasValue && store.DiscountPercentage.Value > 0)
            {
                appliedDiscountPercentage = store.DiscountPercentage.Value;
                appliedDiscountAmount = netPrice * (appliedDiscountPercentage / 100m);
                netPrice = netPrice - appliedDiscountAmount;
            }

            // Najpierw pobierzmy numer proformy
            int proformaNumber = await GetNextProformaNumberAsync();
            var currentYear = DateTime.Now.Year;
            var proformaNumberFormatted = $"FP/PS/{proformaNumber.ToString("D6")}/sDB/{currentYear}";

            // Tworzymy nową proformę z gotowym numerem
            var invoice = new InvoiceClass
            {
                StoreId = storeId,
                PlanId = store.PlanId.Value,
                IssueDate = DateTime.Now,
                NetAmount = netPrice,
                ScrapesIncluded = store.Plan.ScrapesPerInvoice,
                UrlsIncluded = store.Plan.ProductsToScrap,
                IsPaid = false,
                CompanyName = paymentData.CompanyName,
                Address = paymentData.Address,
                PostalCode = paymentData.PostalCode,
                City = paymentData.City,
                NIP = paymentData.NIP,
                AppliedDiscountPercentage = appliedDiscountPercentage,
                AppliedDiscountAmount = appliedDiscountAmount,
                InvoiceNumber = proformaNumberFormatted // ustawiamy przed zapisaniem
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync(); // teraz zapisze bez problemu, bo InvoiceNumber nie jest NULL

            TempData["Success"] = "Proforma została wygenerowana.";
            return RedirectToAction("StorePayments", new { storeId = storeId });
        }

        private async Task<int> GetNextProformaNumberAsync()
        {
            var currentYear = DateTime.Now.Year;
            var counter = await _context.InvoiceCounters.FirstOrDefaultAsync(c => c.Year == currentYear);
            if (counter == null)
            {
                counter = new InvoiceCounter { Year = currentYear, LastProformaNumber = 0, LastInvoiceNumber = 0 };
                _context.InvoiceCounters.Add(counter);
                await _context.SaveChangesAsync();
            }

            counter.LastProformaNumber++;
            await _context.SaveChangesAsync();
            return counter.LastProformaNumber;
        }



        [HttpGet]
        public async Task<IActionResult> InvoicePdf(int invoiceId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Pobierz fakturę i sprawdź, czy należy do użytkownika
            var invoice = await _context.Invoices
                .Include(i => i.Store)
                    .ThenInclude(s => s.UserStores)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId && i.Store.UserStores.Any(us => us.UserId == userId));

            if (invoice == null)
            {
                return NotFound("Faktura nie została znaleziona.");
            }

            // Wygeneruj PDF
            var pdfBytes = GenerateInvoicePdf(invoice);

            return File(pdfBytes, "application/pdf", $"{invoice.InvoiceNumber}.pdf");
        }

        private byte[] GenerateInvoicePdf(InvoiceClass invoice)
        {
            // Get the absolute path to the logo image
            var logoImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "cid", "signature.png");
            var document = new InvoiceDocument(invoice, logoImagePath);
            return document.GeneratePdf();
        }



    }
}
