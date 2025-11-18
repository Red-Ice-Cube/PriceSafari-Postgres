using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models.ViewModels;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Member")]
    public class ChanelController : Controller
    {
        private readonly PriceSafariContext _context;

        public ChanelController(PriceSafariContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStores = await _context.UserStores
                .Where(us => us.UserId == userId)
                .Include(us => us.StoreClass)
                    .ThenInclude(s => s.ScrapHistories)
                .Include(us => us.StoreClass)
                    .ThenInclude(s => s.AllegroScrapeHistories)
                .ToListAsync();

            var stores = userStores.Select(us => us.StoreClass).ToList();

            var storeDetails = stores.Select(store => new ChanelViewModel
            {
                StoreId = store.StoreId,
                StoreName = store.StoreName,
                LogoUrl = store.StoreLogoUrl,

                LastScrapeDate = store.ScrapHistories.OrderByDescending(sh => sh.Date).FirstOrDefault()?.Date,

                AllegroLastScrapeDate = store.AllegroScrapeHistories.OrderByDescending(ash => ash.Date).FirstOrDefault()?.Date,

                OnCeneo = store.OnCeneo,
                OnGoogle = store.OnGoogle,
                OnAllegro = store.OnAllegro
            }).ToList();

            return View("~/Views/Panel/Chanel/Index.cshtml", storeDetails);
        }
    }
}