using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;

namespace PriceSafari.Controllers
{
    public class GoogleShoppingProductsController : Controller
    {
        

        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        public GoogleShoppingProductsController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
        {
            _context = context;
            _userManager = userManager;
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
                .Where(p => p.StoreId == storeId)
                .ToListAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var jsonProducts = products.Select(p => new
                {
                    p.ProductId,
                    p.ProductNameInStoreForGoogle,
                    p.Url,
                    p.FoundOnGoogle,
                    p.GoogleUrl
                }).ToList();

                return Json(jsonProducts);
            }

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreId = storeId;
            return View("~/Views/ManagerPanel/GoogleShoppingProducts/Index.cshtml", products);
        }

       
    }
}
