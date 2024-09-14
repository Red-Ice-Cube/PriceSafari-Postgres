using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models.ViewModels;
using System.Security.Claims;

namespace PriceSafari.Controllers
{
    public class SafariController : Controller
    {
        

        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        public SafariController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }




        [HttpGet]
        public async Task<IActionResult> Chanel()
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

            return View("~/Views/Panel/Safari/Chanel.cshtml", storeDetails);
        }



        [HttpGet]
        public async Task<IActionResult> Index(int storeId)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound();
            }

            var products = await _context.Products
                 .Where(p => p.StoreId == storeId && p.OnGoogle ==true && p.FoundOnGoogle ==true)
                 .ToListAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var jsonProducts = products.Select(p => new
                {
                    p.ProductId,
                    p.ProductNameInStoreForGoogle,
                    p.CatalogNumber,
                    p.Url,
                    p.FoundOnGoogle,
                    p.GoogleUrl
                }).ToList();

                return Json(jsonProducts);
            }

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreId = storeId;
            return View("~/Views/Panel/Safari/Index.cshtml", products);
        }

       
    }
}
