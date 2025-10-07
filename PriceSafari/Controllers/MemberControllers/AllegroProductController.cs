using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using System.Security.Claims;
using PriceSafari.Models.ViewModels;
using System.Globalization;
using PriceSafari.Models;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    public class AllegroProductController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly ILogger<AllegroProductController> _logger;

        public AllegroProductController(PriceSafariContext context, ILogger<AllegroProductController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> AllegroProductList(int storeId)
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
            ViewBag.StoreLogoUrl = store.StoreLogoUrl;
            ViewBag.ProductCount = store.ProductsToScrapAllegro; // Limit dla Allegro
            ViewBag.StoreId = storeId;

            // Pobieramy flagi przeznaczone dla marketplace (Allegro)
            var flags = await _context.Flags
                .Where(f => f.IsMarketplace)
                .Select(f => new FlagViewModel
                {
                    FlagId = f.FlagId,
                    FlagName = f.FlagName,
                    FlagColor = f.FlagColor
                })
                .ToListAsync();
            ViewBag.Flags = flags;

            return View("~/Views/Panel/Product/AllegroProductList.cshtml");
        }

        [HttpGet]
        public async Task<IActionResult> GetAllegroProducts(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null)
            {
                return Forbid();
            }

            var products = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId)
                .Include(p => p.ProductFlags)
                .Select(p => new
                {
                    p.AllegroProductId,
                    p.AllegroProductName,
                    p.AllegroOfferUrl,
                    p.IsScrapable,
                    p.IsRejected,
                    p.MarginPrice,
                    p.AddedDate,
                    FlagIds = p.ProductFlags.Select(pf => pf.FlagId).ToList()
                })
                .ToListAsync();

            return Json(products);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateScrapableProduct(int storeId, [FromBody] int allegroProductId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null) return Forbid();

            var product = await _context.AllegroProducts.Include(p => p.Store)
                .FirstOrDefaultAsync(p => p.AllegroProductId == allegroProductId && p.StoreId == storeId);

            if (product == null) return NotFound(new { success = false, message = "Product not found." });

            var scrapableCount = await _context.AllegroProducts.CountAsync(p => p.StoreId == storeId && p.IsScrapable);

            if (!product.IsScrapable && scrapableCount >= product.Store.ProductsToScrapAllegro)
            {
                return Json(new { success = false, message = "Przekroczono limit produktów do scrapowania dla Allegro." });
            }

            product.IsScrapable = !product.IsScrapable;
            await _context.SaveChangesAsync();
            return Json(new { success = true, newIsScrapable = product.IsScrapable });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateMultipleScrapableProducts(int storeId, [FromBody] List<int> productIds)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var userStore = await _context.UserStores
                .FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);

            if (userStore == null) return Forbid();

            var store = await _context.Stores.Include(s => s.AllegroProducts).FirstOrDefaultAsync(s => s.StoreId == storeId);
            if (store == null) return NotFound();

            int currentScrapableCount = store.AllegroProducts.Count(p => p.IsScrapable);
            int availableCount = (int)(store.ProductsToScrapAllegro - currentScrapableCount);

            var productsToUpdate = store.AllegroProducts.Where(p => productIds.Contains(p.AllegroProductId)).ToList();

            if (productsToUpdate.Count > availableCount)
            {
                productsToUpdate = productsToUpdate.Take(availableCount).ToList();
            }

            foreach (var product in productsToUpdate)
            {
                product.IsScrapable = true;
            }

            await _context.SaveChangesAsync();

            if (productsToUpdate.Count < productIds.Count)
            {
                return Json(new { success = true, message = $"Zaktualizowano {productsToUpdate.Count} z {productIds.Count} produktów. Przekroczono limit." });
            }

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> ResetMultipleScrapableProducts(int storeId, [FromBody] List<int> productIds)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var userStore = await _context.UserStores.FirstOrDefaultAsync(us => us.UserId == userId && us.StoreId == storeId);
            if (userStore == null) return Forbid();

            var productsToUpdate = await _context.AllegroProducts
                .Where(p => p.StoreId == storeId && productIds.Contains(p.AllegroProductId))
                .ToListAsync();

            foreach (var product in productsToUpdate)
            {
                product.IsScrapable = false;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
}