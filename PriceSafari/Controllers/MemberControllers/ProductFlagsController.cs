using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Security.Claims;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    public class ProductFlagsController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        // Dodajemy UserManager do wstrzykiwania zależności
        public ProductFlagsController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // Prywatna metoda do sprawdzania dostępu (przeniesiona tutaj)
        private async Task<bool> UserHasAccessToStore(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            if (await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "Manager"))
            {
                return true;
            }
            return await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
        }

        [HttpGet]
        public async Task<IActionResult> GetFlagsForProduct(int? productId, int? allegroProductId)
        {
            List<int> flagIds;

            if (productId.HasValue) // Sprawdzamy, czy to standardowy produkt
            {
                var product = await _context.Products.FindAsync(productId.Value);
                if (product == null) return NotFound();
                if (!await UserHasAccessToStore(product.StoreId)) return Forbid();

                flagIds = await _context.ProductFlags
                    .Where(pf => pf.ProductId == productId.Value)
                    .Select(pf => pf.FlagId)
                    .ToListAsync();
            }
            else if (allegroProductId.HasValue) // Sprawdzamy, czy to produkt Allegro
            {
                var allegroProduct = await _context.AllegroProducts.FindAsync(allegroProductId.Value);
                if (allegroProduct == null) return NotFound();
                if (!await UserHasAccessToStore(allegroProduct.StoreId)) return Forbid();

                flagIds = await _context.ProductFlags
                    .Where(pf => pf.AllegroProductId == allegroProductId.Value)
                    .Select(pf => pf.FlagId)
                    .ToListAsync();
            }
            else
            {
                return BadRequest("Musisz podać productId lub allegroProductId.");
            }

            return Json(flagIds);
        }

        [HttpPost]
        public async Task<IActionResult> AssignFlagsToProduct([FromBody] AssignFlagsViewModel model)
        {
            if (model == null || model.FlagIds == null) return BadRequest("Nieprawidłowe dane.");

            if (model.ProductId.HasValue) // Logika dla standardowego produktu
            {
                var product = await _context.Products.FindAsync(model.ProductId.Value);
                if (product == null) return NotFound();
                if (!await UserHasAccessToStore(product.StoreId)) return Forbid();

                var existingFlags = await _context.ProductFlags.Where(pf => pf.ProductId == model.ProductId.Value).ToListAsync();
                _context.ProductFlags.RemoveRange(existingFlags);

                var newFlags = model.FlagIds.Select(flagId => new ProductFlag { ProductId = model.ProductId.Value, FlagId = flagId });
                _context.ProductFlags.AddRange(newFlags);
            }
            else if (model.AllegroProductId.HasValue) // Logika dla produktu Allegro
            {
                var allegroProduct = await _context.AllegroProducts.FindAsync(model.AllegroProductId.Value);
                if (allegroProduct == null) return NotFound();
                if (!await UserHasAccessToStore(allegroProduct.StoreId)) return Forbid();

                var existingFlags = await _context.ProductFlags.Where(pf => pf.AllegroProductId == model.AllegroProductId.Value).ToListAsync();
                _context.ProductFlags.RemoveRange(existingFlags);

                var newFlags = model.FlagIds.Select(flagId => new ProductFlag { AllegroProductId = model.AllegroProductId.Value, FlagId = flagId });
                _context.ProductFlags.AddRange(newFlags);
            }
            else
            {
                return BadRequest("Musisz podać productId lub allegroProductId.");
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }

    // Ten ViewModel będzie obsługiwał oba typy produktów
    public class AssignFlagsViewModel
    {
        public int? ProductId { get; set; }
        public int? AllegroProductId { get; set; }
        public List<int> FlagIds { get; set; }
    }
}