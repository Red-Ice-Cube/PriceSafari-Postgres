using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models.ManagerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Heat_Lead.Models.ManagerViewModels.ManagerPayoutViewModel;

namespace Heat_Lead.Controllers.ManagerControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class PayoutController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public PayoutController(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var pendingPayouts = await _context.Paycheck
            .Where(payout => payout.MoneySended == false)
            .ToListAsync();

            var pendingPayoutsViewModel = pendingPayouts.Select(p => new PendingPayoutsViewModel
            {
                PaycheckId = p.PaycheckId,
                PartnerName = p.PartnerName,
                PartnerSurname = p.PartnerSurname,
                CreationDate = p.CreationDate,
                Amount = p.Amount,
                MoneySended = p.MoneySended,
                IsCompany = p.IsCompany,
            }).ToList();

            var model = new ManagerPayoutViewModel
            {
                PendingPayouts = pendingPayoutsViewModel
            };

            return View("~/Views/ManagerPanel/Payout/Index.cshtml", model);
        }

        public async Task<IActionResult> DonePayouts()
        {
            var donePayouts = await _context.Paycheck
            .Where(payout => payout.MoneySended == true)
            .ToListAsync();

            var donePayoutsViewModel = donePayouts.Select(p => new DonePayoutsViewModel
            {
                PaycheckId = p.PaycheckId,
                PartnerName = p.PartnerName,
                PartnerSurname = p.PartnerSurname,
                CreationDate = p.CreationDate,
                Amount = p.Amount,
                MoneySended = p.MoneySended,
                IsCompany = p.IsCompany,
            }).ToList();

            var model = new ManagerPayoutViewModel
            {
                DonePayouts = donePayoutsViewModel
            };

            return View("~/Views/ManagerPanel/Payout/DonePayouts.cshtml", model);
        }

        public async Task<IActionResult> PayoutDetails(int id)
        {
            var payout = await _context.Paycheck
                .FirstOrDefaultAsync(p => p.PaycheckId == id);

            if (payout == null)
            {
                return NotFound();
            }

            var viewModel = new PayoutDetailsViewModel
            {
                PaycheckId = payout.PaycheckId,
                PartnerName = payout.PartnerName,
                PartnerSurname = payout.PartnerSurname,
                City = payout.City,
                Address = payout.Address,
                PartnerEmail = payout.PartnerEmail,
                PostalCode = payout.PostalCode,
                CreationDate = payout.CreationDate,
                Pesel = payout.Pesel,
                TaxNumber = payout.CompanyTaxNumber,
                CompanyName = payout.CompanyName,
                IsCompany = payout.IsCompany,
                TaxOffice = payout.TaxOffice,
                MoneySended = payout.MoneySended,
                BankAccountNumber = payout.BankAccountNumber,
                Amount = payout.Amount,
            };

            return View("~/Views/ManagerPanel/Payout/PayoutDetails.cshtml", viewModel);
        }

        public async Task<IActionResult> PayoutHistory(int id)
        {
            var payout = await _context.Paycheck
                .FirstOrDefaultAsync(p => p.PaycheckId == id);

            if (payout == null)
            {
                return NotFound();
            }

            var viewModel = new PayoutHistoryViewModel
            {
                PaycheckId = payout.PaycheckId,
                PartnerName = payout.PartnerName,
                PartnerSurname = payout.PartnerSurname,
                City = payout.City,
                Address = payout.Address,
                PartnerEmail = payout.PartnerEmail,
                PostalCode = payout.PostalCode,
                CreationDate = payout.CreationDate,
                Pesel = payout.Pesel,
                TaxNumber = payout.CompanyTaxNumber,
                CompanyName = payout.CompanyName,
                IsCompany = payout.IsCompany,
                TaxOffice = payout.TaxOffice,
                MoneySended = payout.MoneySended,
                BankAccountNumber = payout.BankAccountNumber,
                Amount = payout.Amount,
            };

            return View("~/Views/ManagerPanel/Payout/PayoutHistory.cshtml", viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> MarkPayoutAsDone(int id)
        {
            var payout = await _context.Paycheck
                .FirstOrDefaultAsync(p => p.PaycheckId == id);

            if (payout == null)
            {
                return NotFound();
            }

            payout.MoneySended = true;
            _context.Update(payout);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}