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

        // GET: Flags/List
        public async Task<IActionResult> List(int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return Content("Nie ma takiego sklepu");
            }

            var flags = await _context.Flags.Where(f => f.StoreId == storeId).ToListAsync();
            var store = await _context.Stores.Where(f => f.StoreId == storeId).FirstOrDefaultAsync();

            ViewBag.StoreId = storeId;
            ViewBag.StoreName = store?.StoreName;

            return View("~/Views/Panel/Flags/List.cshtml", flags);
        }

        [HttpPost]
        public async Task<IActionResult> Create([Bind("FlagId,FlagName,FlagColor,StoreId")] FlagsClass flag)
        {
            if (!await UserHasAccessToStore(flag.StoreId))
            {
                return Content("Nie ma takiego sklepu");
            }

            if (ModelState.IsValid)
            {
                _context.Add(flag);
                await _context.SaveChangesAsync();
                return Ok();
            }

            return BadRequest(ModelState);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateFlagName(int id, string flagName)
        {
            var flag = await _context.Flags.FindAsync(id);
            if (flag == null)
            {
                return NotFound();
            }

            flag.FlagName = flagName;
            _context.Update(flag);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> UpdateFlagColor(int id, string flagColor)
        {
            var flag = await _context.Flags.FindAsync(id);
            if (flag == null)
            {
                return NotFound();
            }

            flag.FlagColor = flagColor;
            _context.Update(flag);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            var flag = await _context.Flags.FindAsync(id);
            if (flag == null)
            {
                return NotFound();
            }

            var productFlags = await _context.ProductFlags.Where(pf => pf.FlagId == id).ToListAsync();
            _context.ProductFlags.RemoveRange(productFlags);

            _context.Flags.Remove(flag);
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
