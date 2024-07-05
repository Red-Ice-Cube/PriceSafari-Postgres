
using PriceTracker.Data;
using PriceTracker.Models.ManagerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PriceTracker.Controllers.ManagerControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class SettingsController : Controller
    {
        private readonly PriceTrackerContext _context;
        private readonly UserManager<PriceTrackerUser> _userManager;

        public SettingsController(UserManager<PriceTrackerUser> userManager, PriceTrackerContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                return NotFound();
            }

            var viewModel = new SettingsViewModel
            {

                VerificationRequired = settings.VerificationRequired,
                SupervisorEmail = settings.ContactEmail,
                SupervisorNumber = settings.ContactNumber,

                
            };

            return View("~/Views/ManagerPanel/Settings/Index.cshtml", viewModel);
        }

        

        // GET: Settings/EditVerificationRequired
        public async Task<IActionResult> EditVerificationRequired()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                return NotFound();
            }

            var viewModel = new EditVerificationRequiredViewModel
            {
                VerificationRequired = settings.VerificationRequired
            };

            return View("~/Views/ManagerPanel/Settings/EditVerificationRequired.cshtml", viewModel);
        }

        // POST: Settings/EditVerificationRequired
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditVerificationRequired(EditVerificationRequiredViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var settings = await _context.Settings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    return NotFound();
                }

                settings.VerificationRequired = viewModel.VerificationRequired;
                _context.Update(settings);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View("~/Views/ManagerPanel/Settings/EditVerificationRequired.cshtml", viewModel);
        }

        // GET: Settings/EditSupervisor
        public async Task<IActionResult> EditSupervisor()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                return NotFound();
            }

            var viewModel = new EditSupervisorViewModel
            {
                SupervisorEmail = settings.ContactEmail,
                SupervisorNumber = settings.ContactNumber
            };

            return View("~/Views/ManagerPanel/Settings/EditSupervisor.cshtml", viewModel);
        }

        // POST: Settings/EditSupervisor
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSupervisor(EditSupervisorViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var settings = await _context.Settings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    return NotFound();
                }

                settings.ContactEmail = viewModel.SupervisorEmail;
                settings.ContactNumber = viewModel.SupervisorNumber;
                _context.Update(settings);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View("~/Views/ManagerPanel/Settings/EditSupervisor.cshtml", viewModel);
        }

       
    }
}