using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models.ViewModels;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;

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

            var userStores = await _context.UserStores
                .Where(us => us.UserId == userId)
                .Include(us => us.StoreClass)
                .ThenInclude(s => s.ScrapHistories)
                
          
              
                .ToListAsync();

            var stores = userStores.Select(us => us.StoreClass).ToList();

            var storeDetails = stores.Select(store => new ChanelViewModel
            {
                StoreId = store.StoreId,
                StoreName = store.StoreName,
                LogoUrl = store.StoreLogoUrl,
                LastScrapeDate = store.ScrapHistories.OrderByDescending(sh => sh.Date).FirstOrDefault()?.Date,    
             
            }).ToList();

            return View("~/Views/Panel/Chanel/Index.cshtml", storeDetails);
        }
    }
}
