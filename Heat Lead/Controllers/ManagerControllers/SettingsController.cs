using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models.ManagerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Heat_Lead.Controllers.ManagerControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class SettingsController : Controller
    {
        private readonly Heat_LeadContext _context;
        private readonly UserManager<Heat_LeadUser> _userManager;

        public SettingsController(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
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
                MinimumPayout = settings.MinimumPayout,
                CookieLifeTime = settings.TTL,
                VerificationRequired = settings.VerificationRequired,
                SupervisorEmail = settings.ContactEmail,
                SupervisorNumber = settings.ContactNumber,
                ApiRequestInterval = settings.OrdersProcessIntervalInSeconds,
                CollectFingerPrint = settings.CollectFingerPrint,
                OrdersPerClick = settings.OrderPerClick,
                TrackByEan = settings.UseEanForTracking,
                
            };

            return View("~/Views/ManagerPanel/Settings/Index.cshtml", viewModel);
        }

        // GET: Settings/EditMinimumPayout
        public async Task<IActionResult> EditMinimumPayout()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null) return NotFound();

            var viewModel = new EditMinimumPayoutViewModel
            {
                MinimumPayout = settings.MinimumPayout
            };

            return View("~/Views/ManagerPanel/Settings/EditMinimumPayout.cshtml", viewModel);
        }

        // POST: Settings/EditMinimumPayout
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMinimumPayout(EditMinimumPayoutViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var settings = await _context.Settings.FirstOrDefaultAsync();
                if (settings == null) return NotFound();

                settings.MinimumPayout = viewModel.MinimumPayout;
                _context.Update(settings);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View("~/Views/ManagerPanel/Settings/Index.cshtml", viewModel);
        }

        // GET: Settings/EditCookieLifeTime
        public async Task<IActionResult> EditCookieLifeTime()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null) return NotFound();

            var viewModel = new EditCookieLifeTimeViewModel
            {
                CookieLifeTime = settings.TTL
            };

            return View("~/Views/ManagerPanel/Settings/EditCookieLifeTime.cshtml", viewModel);
        }

        // POST: Settings/EditCookieLifeTime
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCookieLifeTime(EditCookieLifeTimeViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var settings = await _context.Settings.FirstOrDefaultAsync();
                if (settings == null) return NotFound();

                settings.TTL = viewModel.CookieLifeTime;
                _context.Update(settings);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

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

        // GET: Settings/EditApiRequestInterval
        public async Task<IActionResult> EditApiRequestInterval()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                return NotFound();
            }

            var viewModel = new EditApiRequestIntervalViewModel
            {
                ApiRequestInterval = settings.OrdersProcessIntervalInSeconds
            };

            return View("~/Views/ManagerPanel/Settings/EditApiRequestInterval.cshtml", viewModel);
        }

        // POST: Settings/EditApiRequestInterval
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditApiRequestInterval(EditApiRequestIntervalViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var settings = await _context.Settings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    return NotFound();
                }

                settings.OrdersProcessIntervalInSeconds = viewModel.ApiRequestInterval;

                _context.Update(settings);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View("~/Views/ManagerPanel/Settings/EditApiRequestInterval.cshtml", viewModel);
        }

        public async Task<IActionResult> EditFingerPrintCollection()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                return NotFound();
            }

            var viewModel = new EditFingerPrintCollectionViewModel
            {
                CollectFingerPrint = settings.CollectFingerPrint
            };

            return View("~/Views/ManagerPanel/Settings/EditFingerPrintCollection.cshtml", viewModel);
        }

        // POST: Settings/EditFingerPrintCollection
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditFingerPrintCollection(EditFingerPrintCollectionViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var settings = await _context.Settings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    return NotFound();
                }

                settings.CollectFingerPrint = viewModel.CollectFingerPrint;

                _context.Update(settings);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View("~/Views/ManagerPanel/Settings/EditFingerPrintCollection.cshtml", viewModel);
        }

        public async Task<IActionResult> EditOrdersPerClick()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                return NotFound();
            }

            var viewModel = new EditOrdersPerClickViewModel
            {
                OrdersPerClick = settings.OrderPerClick
            };

            return View("~/Views/ManagerPanel/Settings/EditOrdersPerClick.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditOrdersPerClick(EditOrdersPerClickViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var settings = await _context.Settings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    return NotFound();
                }

                settings.OrderPerClick = viewModel.OrdersPerClick;

                _context.Update(settings);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View("~/Views/ManagerPanel/Settings/EditOrdersPerClick.cshtml", viewModel);
        }



        public async Task<IActionResult> EditTrackByEan()
        {
            var settings = await _context.Settings.FirstOrDefaultAsync();
            if (settings == null)
            {
                return NotFound();
            }

            var viewModel = new EditTrackByEanViewModel
            {
                TrackByEan = settings.UseEanForTracking
            };

            return View("~/Views/ManagerPanel/Settings/EditTrackByEan.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditTrackByEan(EditTrackByEanViewModel viewModel)
        {
            if (ModelState.IsValid)
            {
                var settings = await _context.Settings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    return NotFound();
                }

                settings.UseEanForTracking = viewModel.TrackByEan;

                _context.Update(settings);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View("~/Views/ManagerPanel/Settings/EditTrackByEan.cshtml", viewModel);
        }
    }
}