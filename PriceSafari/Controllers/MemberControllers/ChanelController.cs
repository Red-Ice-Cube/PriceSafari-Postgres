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

            // Od razu robimy projekcję do ViewModelu za pomocą .Select()
            // Nie używamy .Include() - dzięki temu baza zwróci TYLKO te kolumny, o które prosimy.
            var storeDetails = await _context.UserStores
                .Where(us => us.UserId == userId)
                .Select(us => new ChanelViewModel
                {
                    StoreId = us.StoreClass.StoreId,
                    StoreName = us.StoreClass.StoreName,
                    LogoUrl = us.StoreClass.StoreLogoUrl,
                    OnCeneo = us.StoreClass.OnCeneo,
                    OnGoogle = us.StoreClass.OnGoogle,
                    OnAllegro = us.StoreClass.OnAllegro,

                    // Podzapytanie: PostgreSQL pobierze tylko jedną datę dla każdego sklepu
                    LastScrapeDate = us.StoreClass.ScrapHistories
                        .OrderByDescending(sh => sh.Date)
                        .Select(sh => (DateTime?)sh.Date)
                        .FirstOrDefault(),

                    // Podzapytanie dla Allegro
                    AllegroLastScrapeDate = us.StoreClass.AllegroScrapeHistories
                        .OrderByDescending(ash => ash.Date)
                        .Select(ash => (DateTime?)ash.Date)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return View("~/Views/Panel/Chanel/Index.cshtml", storeDetails);
        }
    }
}