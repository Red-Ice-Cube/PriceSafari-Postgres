using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using System.Security.Claims;

namespace PriceTracker.Controllers.MemberControllers
{
    [Authorize(Roles = "Member")]
    public class ChanelController : Controller
    {
        private readonly PriceTrackerContext _context;
        private readonly UserManager<PriceTrackerUser> _userManager;

        public ChanelController(PriceTrackerContext context, UserManager<PriceTrackerUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var stores = await _context.UserStores
                .Where(us => us.UserId == userId)
                .Select(us => us.StoreClass)
                .ToListAsync();

            return View("~/Views/Panel/Chanel/Index.cshtml", stores);
        }
    }
}
