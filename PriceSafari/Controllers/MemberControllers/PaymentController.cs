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

      
        // GET: Payment/StorePlans
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
                    ProductsToScrap = store.Plan?.ProductsToScrap ?? 0,               
                    LeftScrapes = store.RemainingScrapes 

                };
            }).ToList();

            return View("~/Views/Panel/Plans/StorePlans.cshtml", storeViewModels);
        }

        // GET: Payment/StorePayments/5
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
                PlanPrice = store.Plan?.NetPrice ?? 0,
                ProductsToScrap = store.Plan?.ProductsToScrap ?? 0,
                ScrapesPerInvoice = store.Plan?.ScrapesPerInvoice ?? 0,
                HasUnpaidInvoice = store.Invoices.Any(i => !i.IsPaid),
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

            if (model.PaymentDataId.HasValue && model.PaymentDataId > 0)
            {
                // Update existing data
                var existingData = await _context.UserPaymentDatas
                    .FirstOrDefaultAsync(pd => pd.UserId == userId && pd.PaymentDataId == model.PaymentDataId.Value);

                if (existingData == null)
                {
                    return NotFound();
                }

                existingData.CompanyName = model.CompanyName;
                existingData.Address = model.Address;
                existingData.PostalCode = model.PostalCode;
                existingData.City = model.City;
                existingData.NIP = model.NIP;

                _context.UserPaymentDatas.Update(existingData);
            }
            else
            {
                // Add new data
                var paymentData = new UserPaymentData
                {
                    UserId = userId,
                    CompanyName = model.CompanyName,
                    Address = model.Address,
                    PostalCode = model.PostalCode,
                    City = model.City,
                    NIP = model.NIP
                };
                _context.UserPaymentDatas.Add(paymentData);
            }

            await _context.SaveChangesAsync();

            // Return success
            return Ok(new
            {
                success = true,
                paymentData = new
                {
                    paymentDataId = model.PaymentDataId,
                    companyName = model.CompanyName,
                    address = model.Address,
                    postalCode = model.PostalCode,
                    city = model.City,
                    nip = model.NIP
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

        // POST: Payment/GenerateProforma
        [HttpPost]
        public async Task<IActionResult> GenerateProforma(int storeId, int paymentDataId)
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

            if (store == null || store.Plan == null)
            {
                return NotFound("Sklep lub plan nie został znaleziony.");
            }

            // Sprawdź, czy istnieje już nieopłacona faktura
            if (store.Invoices.Any(i => !i.IsPaid))
            {
                TempData["Error"] = "Istnieje już wygenerowana proforma dla tego sklepu.";
                return RedirectToAction("StorePayments", new { storeId = storeId });
            }

            // Pobierz wybrane dane rozliczeniowe użytkownika
            var paymentData = await _context.UserPaymentDatas
                .FirstOrDefaultAsync(pd => pd.UserId == userId && pd.PaymentDataId == paymentDataId);

            if (paymentData == null)
            {
                TempData["Error"] = "Nieprawidłowe dane rozliczeniowe.";
                return RedirectToAction("StorePayments", new { storeId = storeId });
            }

            // Generate a temporary unique InvoiceNumber
            var tempInvoiceNumber = $"TEMP-{Guid.NewGuid()}";

            // Create the invoice with the temporary InvoiceNumber
            var invoice = new InvoiceClass
            {
                StoreId = storeId,
                PlanId = store.PlanId.Value,
                IssueDate = DateTime.Now,
                NetAmount = store.Plan.NetPrice,
                ScrapesIncluded = store.Plan.ScrapesPerInvoice,
                UrlsIncluded = store.Plan.ProductsToScrap,
                IsPaid = false,
                CompanyName = paymentData.CompanyName,
                Address = paymentData.Address,
                PostalCode = paymentData.PostalCode,
                City = paymentData.City,
                NIP = paymentData.NIP,
                InvoiceNumber = tempInvoiceNumber
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync(); // Save to get the InvoiceId

            // Generate the final custom invoice number
            invoice.InvoiceNumber = $"PS{invoice.InvoiceId.ToString("D6")}";

            // Update the invoice with the new InvoiceNumber
            _context.Invoices.Update(invoice);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Proforma została wygenerowana.";
            return RedirectToAction("StorePayments", new { storeId = storeId });
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

            return File(pdfBytes, "application/pdf", $"Faktura_{invoice.InvoiceId}.pdf");
        }

        private byte[] GenerateInvoicePdf(InvoiceClass invoice)
        {
            var document = new InvoiceDocument(invoice);
            return document.GeneratePdf();
        }



    }
}
