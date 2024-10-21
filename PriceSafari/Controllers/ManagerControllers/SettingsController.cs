
using PriceSafari.Data;
using PriceSafari.Models.ManagerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class SettingsController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        public SettingsController(UserManager<PriceSafariUser> userManager, PriceSafariContext context)
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
       
                Semophore = settings.Semophore,
                WarmUp = settings.WarmUpTime,
                Headless = settings.HeadLess,
                JS = settings.JavaScript,
                Style = settings.Styles,
                GetCeneoName = settings.GetCeneoName,   
            };

            return View("~/Views/ManagerPanel/Settings/Index.cshtml", viewModel);
        }



        // GET: Settings/EditSpeedSettings
        public async Task<IActionResult> EditSpeedSettings()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                return NotFound();
            }

            var viewModel = new EditSpeedSettingsViewModel
            {
                Semophore = settings.Semophore,
                WarmUp = settings.WarmUpTime,
                Headless = settings.HeadLess,
                JS = settings.JavaScript,
                Style = settings.Styles,
                GetCeneoName = settings.GetCeneoName
            };

            return View("~/Views/ManagerPanel/Settings/EditSpeedSettings.cshtml", viewModel);
        }

        // POST: Settings/EditSpeedSettings
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSpeedSettings(EditSpeedSettingsViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var settings = await _context.Settings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    return NotFound();
                }

                settings.Semophore = viewModel.Semophore;
                settings.WarmUpTime = viewModel.WarmUp;
                settings.HeadLess = viewModel.Headless;
                settings.Styles = viewModel.Style;
                settings.JavaScript = viewModel.JS;
                settings.GetCeneoName = viewModel.GetCeneoName;
                _context.Update(settings);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View("~/Views/ManagerPanel/Settings/EditSpeedSettings.cshtml", viewModel);
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

      
       
    }
}