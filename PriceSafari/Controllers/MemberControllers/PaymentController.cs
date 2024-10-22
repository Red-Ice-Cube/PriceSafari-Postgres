using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

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
        [HttpGet]
        public async Task<IActionResult> StorePayments(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Verify that the store belongs to the user
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

            // Prepare the view model
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
                Invoices = store.Invoices.OrderByDescending(i => i.IssueDate).ToList()
            };

            return View("~/Views/Panel/Plans/StorePayments.cshtml", viewModel);
        }

        // POST: Payment/GenerateProforma
        [HttpPost]
        public async Task<IActionResult> GenerateProforma(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Verify that the store belongs to the user
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

            // Check if there's an existing unpaid invoice
            if (store.Invoices.Any(i => !i.IsPaid))
            {
                TempData["Error"] = "Istnieje już wygenerowana proforma dla tego sklepu.";
                return RedirectToAction("StorePayments", new { storeId = storeId });
            }

            // Create the invoice
            var invoice = new InvoiceClass
            {
                StoreId = storeId,
                PlanId = store.PlanId.Value,
                IssueDate = DateTime.Now,
                NetAmount = store.Plan.NetPrice,
                ScrapesIncluded = store.Plan.ScrapesPerInvoice,
                IsPaid = false
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Proforma została wygenerowana.";
            return RedirectToAction("StorePayments", new { storeId = storeId });
        }
    }
}
