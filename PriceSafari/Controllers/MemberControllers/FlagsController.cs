using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Member, Manager")]
    public class FlagsController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        public FlagsController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
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
        // W pliku FlagsController.cs
        public async Task<IActionResult> List(int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return Content("Nie ma takiego sklepu lub brak dostępu.");
            }

            var store = await _context.Stores.FindAsync(storeId);
            if (store == null)
            {
                return NotFound("Nie znaleziono sklepu.");
            }

            // Pobierz wszystkie flagi dla sklepu
            var allFlags = await _context.Flags.Where(f => f.StoreId == storeId).ToListAsync();

            // Podziel flagi na dwie grupy
            var standardFlags = allFlags.Where(f => !f.IsMarketplace).ToList();
            var marketplaceFlags = allFlags.Where(f => f.IsMarketplace).ToList();

            // Stwórz model widoku, który przekaże wszystko
            var viewModel = new FlagsListViewModel
            {
                StandardFlags = standardFlags,
                MarketplaceFlags = marketplaceFlags,
                StoreId = storeId,
                StoreName = store.StoreName,
                HasAllegro = !string.IsNullOrEmpty(store.StoreNameAllegro)
            };

            return View("~/Views/Panel/Flags/List.cshtml", viewModel);
        }
        // W pliku FlagsController.cs
        [HttpPost]
        public async Task<IActionResult> Create([Bind("FlagName,FlagColor,StoreId,IsMarketplace")] FlagsClass flag)
        {
            if (!await UserHasAccessToStore(flag.StoreId))
            {
                return Forbid();
            }

            if (ModelState.IsValid)
            {
                _context.Add(flag);
                await _context.SaveChangesAsync();
                return Ok();
            }

            return BadRequest(ModelState);
        }
        // W pliku FlagsController.cs

        // Usuń metody UpdateFlagName i UpdateFlagColor i zastąp je tą jedną
        [HttpPost]
        public async Task<IActionResult> UpdateFlag(int id, string flagName, string flagColor, bool isMarketplace)
        {
            var flag = await _context.Flags.FindAsync(id);
            if (flag == null)
            {
                return NotFound();
            }

            if (!await UserHasAccessToStore(flag.StoreId))
            {
                return Forbid();
            }

            flag.FlagName = flagName;
            flag.FlagColor = flagColor;
            flag.IsMarketplace = isMarketplace; // Aktualizujemy też ten stan

            _context.Update(flag);
            await _context.SaveChangesAsync();

            return Ok();
        }

        // Model widoku, który stworzyliśmy
        public class FlagsListViewModel
        {
            public List<FlagsClass> StandardFlags { get; set; }
            public List<FlagsClass> MarketplaceFlags { get; set; }
            public int StoreId { get; set; }
            public string StoreName { get; set; }
            public bool HasAllegro { get; set; }
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
