using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PriceTracker.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Member, Manager")]
    public class FlagsController : Controller
    {
        private readonly PriceTrackerContext _context;
        private readonly UserManager<PriceTrackerUser> _userManager;

        public FlagsController(PriceTrackerContext context, UserManager<PriceTrackerUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        private async Task<bool> UserHasAccessToStore(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            var isAdminOrManager = await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "Manager");

            if (!isAdminOrManager)
            {
                var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
                return hasAccess;
            }

            return true;
        }

        // GET: Flags/Create
        public async Task<IActionResult> Create(int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return Content("Nie ma takiego sklepu");
            }

            ViewBag.StoreId = storeId;
            return View("~/Views/ManagerPanel/Flags/Create.cshtml");
        }

        // POST: Flags/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("FlagId,FlagName,FlagColor,StoreId")] FlagsClass flag, int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return Content("Nie ma takiego sklepu");
            }

            flag.StoreId = storeId;

            if (ModelState.IsValid)
            {
                _context.Add(flag);
                await _context.SaveChangesAsync();
                return RedirectToAction("List", new { storeId = storeId });
            }

            ViewBag.StoreId = storeId;
            return View("~/Views/ManagerPanel/Flags/Create.cshtml", flag);
        }

        // GET: Flags/List
        public async Task<IActionResult> List(int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return Content("Nie ma takiego sklepu");
            }

            var flags = await _context.Flags.Where(f => f.StoreId == storeId).ToListAsync();
            ViewBag.StoreId = storeId;

            return View("~/Views/ManagerPanel/Flags/List.cshtml", flags);
        }
    }
}
