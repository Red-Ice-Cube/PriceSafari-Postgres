using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models;
using Heat_Lead.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace Heat_Lead.Controllers
{
    [Authorize]
    public class WalletController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public WalletController(Heat_LeadContext context, UserManager<Heat_LeadUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index(WalletViewModel model = null)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            var wallet = await _context.Wallet
                .Include(w => w.Paycheck)
                .FirstOrDefaultAsync(w => w.UserId == user.Id);

            if (wallet == null) return NotFound();

            var inValidationEarnings = await _context.Order
                .Where(order => order.UserId == user.Id && order.InValidation && order.IsAccepted)
                .SumAsync(order => order.AffiliateCommision);

            var settings = await _context.Settings.FirstOrDefaultAsync();

            if (model == null)
            {
                model = new WalletViewModel
                {
                    InValidationEarnings = inValidationEarnings,
                    ReadyEarnings = wallet.ReadyEarnings,
                    PaidEarnings = wallet.PaidEarnings,
                    MinimumPayout = settings.MinimumPayout,
                    WithdrawViewModel = new WithdrawViewModel()
                };
            }
            else
            {
                model.InValidationEarnings = inValidationEarnings;
                model.ReadyEarnings = wallet.ReadyEarnings;
                model.PaidEarnings = wallet.PaidEarnings;
                model.MinimumPayout = settings.MinimumPayout;
            }

            return View("~/Views/Panel/Wallet/Index.cshtml", model);
        }

        [HttpGet]
        public async Task<IActionResult> GetPaycheckHistory()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            var paycheckHistory = await _context.Paycheck
                .Where(p => p.Wallet.UserId == user.Id)
                .OrderByDescending(p => p.CreationDate)
                .Select(p => new
                {
                    paycheckId = p.PaycheckId,
                    amount = p.Amount,
                    creationDate = p.CreationDate,
                    moneySended = p.MoneySended
                })
                .ToListAsync();

            return Json(paycheckHistory);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Withdraw(WalletViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            var wallet = await _context.Wallet
                .Include(w => w.Paycheck)
                .FirstOrDefaultAsync(w => w.UserId == user.Id);

            if (wallet == null)
            {
                TempData["ErrorMessage"] = "Portfel nie znaleziony.";
                return RedirectToAction(nameof(Index));
            }

            var settings = await _context.Settings.FirstOrDefaultAsync();
            var minimumPayout = settings.MinimumPayout;

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Wypełnij poprawnie formularz.";
                model.InValidationEarnings = await _context.Order
                    .Where(order => order.UserId == user.Id && order.InValidation && order.IsAccepted)
                    .SumAsync(order => order.AffiliateCommision);
                model.ReadyEarnings = wallet.ReadyEarnings;
                model.PaidEarnings = wallet.PaidEarnings;
                model.MinimumPayout = minimumPayout;
                return View("~/Views/Panel/Wallet/Index.cshtml", model);
            }

            if (model.WithdrawViewModel.Amount > wallet.ReadyEarnings)
            {
                TempData["ErrorMessage"] = "Niewystarczające środki do wypłaty.";
                model.InValidationEarnings = await _context.Order
                    .Where(order => order.UserId == user.Id && order.InValidation && order.IsAccepted)
                    .SumAsync(order => order.AffiliateCommision);
                model.ReadyEarnings = wallet.ReadyEarnings;
                model.PaidEarnings = wallet.PaidEarnings;
                model.MinimumPayout = minimumPayout;
                return View("~/Views/Panel/Wallet/Index.cshtml", model);
            }

            if (model.WithdrawViewModel.Amount < minimumPayout)
            {
                TempData["ErrorMessage"] = $"Minimalna kwota wypłaty to {minimumPayout} zł.";
                model.InValidationEarnings = await _context.Order
                    .Where(order => order.UserId == user.Id && order.InValidation && order.IsAccepted)
                    .SumAsync(order => order.AffiliateCommision);
                model.ReadyEarnings = wallet.ReadyEarnings;
                model.PaidEarnings = wallet.PaidEarnings;
                model.MinimumPayout = minimumPayout;
                return View("~/Views/Panel/Wallet/Index.cshtml", model);
            }

            if (!model.WithdrawViewModel.AcceptsTerms)
            {
                TempData["ErrorMessage"] = "Musisz zaakceptować regulamin, aby kontynuować.";
                model.InValidationEarnings = await _context.Order
                    .Where(order => order.UserId == user.Id && order.InValidation && order.IsAccepted)
                    .SumAsync(order => order.AffiliateCommision);
                model.ReadyEarnings = wallet.ReadyEarnings;
                model.PaidEarnings = wallet.PaidEarnings;
                model.MinimumPayout = minimumPayout;
                return View("~/Views/Panel/Wallet/Index.cshtml", model);
            }

            var paycheck = new Paycheck
            {
                PartnerName = user.PartnerName,
                PartnerSurname = user.PartnerSurname,
                PartnerEmail = user.Email,
                Address = model.WithdrawViewModel.Address,
                City = model.WithdrawViewModel.City,
                PostalCode = model.WithdrawViewModel.PostalCode,
                Pesel = model.WithdrawViewModel.Pesel,
                CompanyTaxNumber = model.WithdrawViewModel.CompanyTaxNumber,
                CompanyName = model.WithdrawViewModel.CompanyName,
                BankAccountNumber = model.WithdrawViewModel.BankAccountNumber,
                TaxOffice = model.WithdrawViewModel.TaxOffice,
                Amount = model.WithdrawViewModel.Amount,
                IsCompany = model.WithdrawViewModel.IsCompany,
                UserId = user.Id,
                WalletId = wallet.WalletId
            };

            _context.Paycheck.Add(paycheck);
            wallet.ReadyEarnings -= model.WithdrawViewModel.Amount;
            wallet.PaidEarnings += model.WithdrawViewModel.Amount;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Wypłata została pomyślnie zainicjowana.";
            return RedirectToAction(nameof(Index));
        }
    }
}