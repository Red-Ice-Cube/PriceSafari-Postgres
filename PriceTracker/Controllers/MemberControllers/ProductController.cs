using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;
using PriceTracker.Models.ViewModels;

namespace PriceTracker.Controllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    public class ProductController : Controller
    {
        private readonly PriceTrackerContext _context;

        public ProductController(PriceTrackerContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> StoreList()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStores = await _context.UserStores
                .Where(us => us.UserId == userId)
                .Include(us => us.StoreClass)
                .ThenInclude(s => s.Products)
                .ToListAsync();

            var stores = userStores.Select(us => us.StoreClass).ToList();

            var storeDetails = stores.Select(store => new ChanelViewModel
            {
                StoreId = store.StoreId,
                StoreName = store.StoreName,
                ProductCount = store.Products.Count(p => p.IsScrapable),
                AllowedProducts = store.ProductsToScrap
            }).ToList();

            return View("~/Views/Panel/Product/StoreList.cshtml", storeDetails);
        }

        [HttpGet]
        public async Task<IActionResult> ProductList(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return NotFound();

            ViewBag.StoreName = store.StoreName;
            ViewBag.StoreId = storeId;

            return View("~/Views/Panel/Product/ProductList.cshtml");
        }
        [HttpGet]
        public async Task<IActionResult> GetProducts(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

            var products = await _context.Products
                .Where(p => p.StoreId == storeId)
                .Select(p => new
                {
                    p.ProductId,
                    p.ProductName,
                    p.Category,
                    p.OfferUrl,
                    p.IsScrapable,
                    p.IsRejected,
                    p.MainUrl,
                    p.Url,
                    p.CatalogNumber
                })
                .ToListAsync();

            return Json(products);
        }


        [HttpPut]
        public async Task<IActionResult> UpdateScrapableProduct([FromBody] ProductUpdateRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var product = await _context.Products.Include(p => p.Store).FirstOrDefaultAsync(p => p.ProductId == request.ProductId);
            if (product == null)
            {
                return NotFound();
            }

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == product.StoreId);

            if (userStore == null)
            {
                return Forbid();
            }

            var scrapableCount = await _context.Products.CountAsync(p => p.StoreId == product.StoreId && p.IsScrapable);
            if (request.IsScrapable && scrapableCount >= product.Store.ProductsToScrap)
            {
                return Json(new { success = false, message = "Przekroczono limit produktów do scrapowania." });
            }

            product.IsScrapable = request.IsScrapable;
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        public class ProductUpdateRequest
        {
            public int ProductId { get; set; }
            public bool IsScrapable { get; set; }
        }


        [HttpPost]
        public async Task<IActionResult> UpdateMultipleScrapableProducts(int storeId, [FromBody] List<int> productIds)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

            var store = await _context.Stores.Include(s => s.Products).FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null)
            {
                return NotFound();
            }

            var currentScrapableCount = store.Products.Count(p => p.IsScrapable);
            var availableCount = store.ProductsToScrap - currentScrapableCount;

            var productsToUpdate = store.Products.Where(p => productIds.Contains(p.ProductId)).ToList();

            if (productsToUpdate.Count > availableCount)
            {
                return Json(new { success = false, message = "Przekroczono limit produktów do scrapowania." });
            }

            foreach (var product in productsToUpdate)
            {
                product.IsScrapable = true;
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ResetMultipleScrapableProducts(int storeId, [FromBody] List<int> productIds)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

            var store = await _context.Stores.Include(s => s.Products).FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null)
            {
                return NotFound();
            }

            var productsToUpdate = store.Products.Where(p => productIds.Contains(p.ProductId)).ToList();

            foreach (var product in productsToUpdate)
            {
                product.IsScrapable = false;
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }
    }
}
