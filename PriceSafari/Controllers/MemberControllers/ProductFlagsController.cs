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

        public ProductFlagsController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

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

        // --- KLUCZOWA ZMIANA: Zaktualizowane DTO, aby obsługiwać oba typy produktów ---
        public class ProductIdsDto
        {
            public List<int> ProductIds { get; set; }
            public List<int> AllegroProductIds { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> GetFlagCountsForProducts([FromBody] ProductIdsDto data)
        {
            if (data == null || (data.ProductIds == null || !data.ProductIds.Any()) && (data.AllegroProductIds == null || !data.AllegroProductIds.Any()))
            {
                return BadRequest("Nie podano ID produktów.");
            }

            // --- KLUCZOWA ZMIANA: Logika sprawdzająca dostęp i pobierająca dane ---
            if (data.ProductIds != null && data.ProductIds.Any())
            {
                var firstProduct = await _context.Products.FindAsync(data.ProductIds.First());
                if (firstProduct == null) return NotFound("Nie znaleziono produktu.");
                if (!await UserHasAccessToStore(firstProduct.StoreId)) return Forbid();

                var counts = await _context.ProductFlags
                    .Where(pf => pf.ProductId.HasValue && data.ProductIds.Contains(pf.ProductId.Value))
                    .GroupBy(pf => pf.FlagId)
                    .Select(g => new { FlagId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.FlagId, x => x.Count);
                return Json(counts);
            }
            else // Obsługa produktów Allegro
            {
                var firstProduct = await _context.AllegroProducts.FindAsync(data.AllegroProductIds.First());
                if (firstProduct == null) return NotFound("Nie znaleziono produktu Allegro.");
                if (!await UserHasAccessToStore(firstProduct.StoreId)) return Forbid();

                var counts = await _context.ProductFlags
                    .Where(pf => pf.AllegroProductId.HasValue && data.AllegroProductIds.Contains(pf.AllegroProductId.Value))
                    .GroupBy(pf => pf.FlagId)
                    .Select(g => new { FlagId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.FlagId, x => x.Count);
                return Json(counts);
            }
        }

        // --- KLUCZOWA ZMIANA: Zaktualizowane DTO ---
        public class UpdateFlagsDto
        {
            public List<int> ProductIds { get; set; }
            public List<int> AllegroProductIds { get; set; }
            public List<int> FlagsToAdd { get; set; }
            public List<int> FlagsToRemove { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateFlagsForMultipleProducts([FromBody] UpdateFlagsDto data)
        {
            if (data == null || (data.ProductIds == null || !data.ProductIds.Any()) && (data.AllegroProductIds == null || !data.AllegroProductIds.Any()))
            {
                return Json(new { success = false, message = "Nie wybrano produktów." });
            }

            // --- Logika dla produktów marketplace ---
            if (data.ProductIds != null && data.ProductIds.Any())
            {
                var firstProduct = await _context.Products.FindAsync(data.ProductIds.First());
                if (firstProduct == null) return Json(new { success = false, message = "Nie znaleziono produktu." });
                if (!await UserHasAccessToStore(firstProduct.StoreId)) return Forbid();

                if (data.FlagsToRemove != null && data.FlagsToRemove.Any())
                {
                    var assignmentsToRemove = await _context.ProductFlags
                        .Where(pf => pf.ProductId.HasValue && data.ProductIds.Contains(pf.ProductId.Value) && data.FlagsToRemove.Contains(pf.FlagId))
                        .ToListAsync();
                    if (assignmentsToRemove.Any()) _context.ProductFlags.RemoveRange(assignmentsToRemove);
                }

                if (data.FlagsToAdd != null && data.FlagsToAdd.Any())
                {
                    // --- ZMIANA TUTAJ: Pobieramy dane do listy, a potem tworzymy Lookup ---
                    var existingAssignmentsList = await _context.ProductFlags
                        .Where(pf => pf.ProductId.HasValue && data.ProductIds.Contains(pf.ProductId.Value))
                        .ToListAsync();
                    var existingAssignments = existingAssignmentsList.ToLookup(pf => pf.ProductId.Value, pf => pf.FlagId);

                    var newAssignments = new List<ProductFlag>();
                    foreach (var productId in data.ProductIds)
                    {
                        var assignedFlags = new HashSet<int>(existingAssignments[productId]);
                        foreach (var flagId in data.FlagsToAdd)
                        {
                            if (!assignedFlags.Contains(flagId))
                            {
                                newAssignments.Add(new ProductFlag { ProductId = productId, FlagId = flagId });
                            }
                        }
                    }
                    if (newAssignments.Any()) await _context.ProductFlags.AddRangeAsync(newAssignments);
                }
            }
            // --- Logika dla produktów Allegro ---
            else if (data.AllegroProductIds != null && data.AllegroProductIds.Any())
            {
                var firstProduct = await _context.AllegroProducts.FindAsync(data.AllegroProductIds.First());
                if (firstProduct == null) return Json(new { success = false, message = "Nie znaleziono produktu Allegro." });
                if (!await UserHasAccessToStore(firstProduct.StoreId)) return Forbid();

                if (data.FlagsToRemove != null && data.FlagsToRemove.Any())
                {
                    var assignmentsToRemove = await _context.ProductFlags
                        .Where(pf => pf.AllegroProductId.HasValue && data.AllegroProductIds.Contains(pf.AllegroProductId.Value) && data.FlagsToRemove.Contains(pf.FlagId))
                        .ToListAsync();
                    if (assignmentsToRemove.Any()) _context.ProductFlags.RemoveRange(assignmentsToRemove);
                }

                if (data.FlagsToAdd != null && data.FlagsToAdd.Any())
                {
                    // --- ZMIANA TUTAJ: Pobieramy dane do listy, a potem tworzymy Lookup ---
                    var existingAssignmentsList = await _context.ProductFlags
                        .Where(pf => pf.AllegroProductId.HasValue && data.AllegroProductIds.Contains(pf.AllegroProductId.Value))
                        .ToListAsync();
                    var existingAssignments = existingAssignmentsList.ToLookup(pf => pf.AllegroProductId.Value, pf => pf.FlagId);

                    var newAssignments = new List<ProductFlag>();
                    foreach (var productId in data.AllegroProductIds)
                    {
                        var assignedFlags = new HashSet<int>(existingAssignments[productId]);
                        foreach (var flagId in data.FlagsToAdd)
                        {
                            if (!assignedFlags.Contains(flagId))
                            {
                                newAssignments.Add(new ProductFlag { AllegroProductId = productId, FlagId = flagId });
                            }
                        }
                    }
                    if (newAssignments.Any()) await _context.ProductFlags.AddRangeAsync(newAssignments);
                }
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
}



// Nowy model flagowania, bez obslugi allegro 


//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Identity;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using System.Security.Claims;

//namespace PriceSafari.Controllers.MemberControllers
//{
//    [Authorize(Roles = "Admin, Manager, Member")]
//    public class ProductFlagsController : Controller
//    {
//        private readonly PriceSafariContext _context;
//        private readonly UserManager<PriceSafariUser> _userManager;

//        public ProductFlagsController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
//        {
//            _context = context;
//            _userManager = userManager;
//        }

//        private async Task<bool> UserHasAccessToStore(int storeId)
//        {
//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
//            var user = await _userManager.FindByIdAsync(userId);
//            if (await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "Manager"))
//            {
//                return true;
//            }
//            return await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
//        }

//        // DTO dla metody GetFlagCountsForProducts
//        public class ProductIdsDto
//        {
//            public List<int> ProductIds { get; set; }
//        }

//        [HttpPost]
//        public async Task<IActionResult> GetFlagCountsForProducts([FromBody] ProductIdsDto data)
//        {
//            if (data == null || data.ProductIds == null || !data.ProductIds.Any())
//            {
//                return BadRequest("Nie podano ID produktów.");
//            }

//            var firstProduct = await _context.Products.FindAsync(data.ProductIds.First());
//            if (firstProduct == null) return NotFound("Nie znaleziono produktu.");
//            if (!await UserHasAccessToStore(firstProduct.StoreId)) return Forbid();

//            var counts = await _context.ProductFlags
//                .Where(pf => pf.ProductId.HasValue && data.ProductIds.Contains(pf.ProductId.Value))
//                .GroupBy(pf => pf.FlagId)
//                .Select(g => new { FlagId = g.Key, Count = g.Count() })
//                .ToDictionaryAsync(x => x.FlagId, x => x.Count);

//            return Json(counts);
//        }

//        // DTO dla metody UpdateFlagsForMultipleProducts
//        public class UpdateFlagsDto
//        {
//            public List<int> ProductIds { get; set; }
//            public List<int> FlagsToAdd { get; set; }
//            public List<int> FlagsToRemove { get; set; }
//        }

//        [HttpPost]
//        public async Task<IActionResult> UpdateFlagsForMultipleProducts([FromBody] UpdateFlagsDto data)
//        {
//            if (data == null || data.ProductIds == null || !data.ProductIds.Any())
//            {
//                return Json(new { success = false, message = "Nie wybrano produktów." });
//            }

//            var firstProduct = await _context.Products.FindAsync(data.ProductIds.First());
//            if (firstProduct == null) return Json(new { success = false, message = "Nie znaleziono produktu." });
//            if (!await UserHasAccessToStore(firstProduct.StoreId)) return Forbid();

//            if (data.FlagsToRemove != null && data.FlagsToRemove.Any())
//            {
//                var assignmentsToRemove = await _context.ProductFlags
//                    .Where(pf => pf.ProductId.HasValue && data.ProductIds.Contains(pf.ProductId.Value) && data.FlagsToRemove.Contains(pf.FlagId))
//                    .ToListAsync();

//                if (assignmentsToRemove.Any())
//                {
//                    _context.ProductFlags.RemoveRange(assignmentsToRemove);
//                }
//            }

//            if (data.FlagsToAdd != null && data.FlagsToAdd.Any())
//            {
//                var existingAssignments = await _context.ProductFlags
//                    .Where(pf => pf.ProductId.HasValue && data.ProductIds.Contains(pf.ProductId.Value))
//                    .Select(pf => new { pf.ProductId, pf.FlagId })
//                    .ToListAsync();

//                var newAssignments = new List<ProductFlag>();
//                foreach (var productId in data.ProductIds)
//                {
//                    foreach (var flagId in data.FlagsToAdd)
//                    {
//                        if (!existingAssignments.Any(pf => pf.ProductId == productId && pf.FlagId == flagId))
//                        {
//                            newAssignments.Add(new ProductFlag { ProductId = productId, FlagId = flagId });
//                        }
//                    }
//                }

//                if (newAssignments.Any())
//                {
//                    await _context.ProductFlags.AddRangeAsync(newAssignments);
//                }
//            }

//            await _context.SaveChangesAsync();
//            return Json(new { success = true });
//        }
//    }
//}








//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Identity;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using PriceSafari.Data;
//using PriceSafari.Models;
//using System.Security.Claims;

//namespace PriceSafari.Controllers.MemberControllers
//{
//    [Authorize(Roles = "Admin, Manager, Member")]
//    public class ProductFlagsController : Controller
//    {
//        private readonly PriceSafariContext _context;
//        private readonly UserManager<PriceSafariUser> _userManager;

//        public ProductFlagsController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
//        {
//            _context = context;
//            _userManager = userManager;
//        }

//        private async Task<bool> UserHasAccessToStore(int storeId)
//        {
//            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
//            var user = await _userManager.FindByIdAsync(userId);
//            if (await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "Manager"))
//            {
//                return true;
//            }
//            return await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
//        }

//        [HttpGet]
//        public async Task<IActionResult> GetFlagsForProduct(int? productId, int? allegroProductId)
//        {
//            List<int> flagIds;

//            if (productId.HasValue)
//            {
//                var product = await _context.Products.FindAsync(productId.Value);
//                if (product == null) return NotFound();
//                if (!await UserHasAccessToStore(product.StoreId)) return Forbid();

//                flagIds = await _context.ProductFlags
//                    .Where(pf => pf.ProductId == productId.Value)
//                    .Select(pf => pf.FlagId)
//                    .ToListAsync();
//            }
//            else if (allegroProductId.HasValue)
//            {
//                var allegroProduct = await _context.AllegroProducts.FindAsync(allegroProductId.Value);
//                if (allegroProduct == null) return NotFound();
//                if (!await UserHasAccessToStore(allegroProduct.StoreId)) return Forbid();

//                flagIds = await _context.ProductFlags
//                    .Where(pf => pf.AllegroProductId == allegroProductId.Value)
//                    .Select(pf => pf.FlagId)
//                    .ToListAsync();
//            }
//            else
//            {
//                return BadRequest("Musisz podać productId lub allegroProductId.");
//            }

//            return Json(flagIds);
//        }

//        [HttpPost]
//        public async Task<IActionResult> AssignFlagsToProduct([FromBody] AssignFlagsViewModel model)
//        {
//            if (model == null || model.FlagIds == null) return BadRequest("Nieprawidłowe dane.");

//            if (model.ProductId.HasValue)
//            {
//                var product = await _context.Products.FindAsync(model.ProductId.Value);
//                if (product == null) return NotFound();
//                if (!await UserHasAccessToStore(product.StoreId)) return Forbid();

//                var existingFlags = await _context.ProductFlags.Where(pf => pf.ProductId == model.ProductId.Value).ToListAsync();
//                _context.ProductFlags.RemoveRange(existingFlags);

//                var newFlags = model.FlagIds.Select(flagId => new ProductFlag { ProductId = model.ProductId.Value, FlagId = flagId });
//                _context.ProductFlags.AddRange(newFlags);
//            }
//            else if (model.AllegroProductId.HasValue)
//            {
//                var allegroProduct = await _context.AllegroProducts.FindAsync(model.AllegroProductId.Value);
//                if (allegroProduct == null) return NotFound();
//                if (!await UserHasAccessToStore(allegroProduct.StoreId)) return Forbid();

//                var existingFlags = await _context.ProductFlags.Where(pf => pf.AllegroProductId == model.AllegroProductId.Value).ToListAsync();
//                _context.ProductFlags.RemoveRange(existingFlags);

//                var newFlags = model.FlagIds.Select(flagId => new ProductFlag { AllegroProductId = model.AllegroProductId.Value, FlagId = flagId });
//                _context.ProductFlags.AddRange(newFlags);
//            }
//            else
//            {
//                return BadRequest("Musisz podać productId lub allegroProductId.");
//            }

//            await _context.SaveChangesAsync();
//            return Json(new { success = true });
//        }

//        public class AddFlagsToProductsDto
//        {
//            public List<int> ProductIds { get; set; }
//            public List<int> FlagIds { get; set; }
//        }

//        [HttpPost]
//        public async Task<IActionResult> AddFlagsToMultipleProducts([FromBody] AddFlagsToProductsDto data)
//        {
//            if (data == null || data.ProductIds == null || !data.ProductIds.Any())
//            {
//                return Json(new { success = false, message = "Nie wybrano produktów." });
//            }
//            if (data.FlagIds == null || !data.FlagIds.Any())
//            {
//                return Json(new { success = false, message = "Nie wybrano flag." });
//            }

//            var firstProduct = await _context.Products.FindAsync(data.ProductIds.First());
//            if (firstProduct == null)
//            {
//                return Json(new { success = false, message = "Nie znaleziono produktu." });
//            }
//            if (!await UserHasAccessToStore(firstProduct.StoreId))
//            {
//                return Forbid();
//            }

//            try
//            {

//                var existingAssignments = await _context.ProductFlags
//                    .Where(pf => data.ProductIds.Contains(pf.ProductId.Value))
//                    .ToListAsync();

//                var newAssignments = new List<ProductFlag>();

//                foreach (var productId in data.ProductIds)
//                {
//                    foreach (var flagId in data.FlagIds)
//                    {

//                        bool alreadyExists = existingAssignments
//                            .Any(pf => pf.ProductId == productId && pf.FlagId == flagId);

//                        if (!alreadyExists)
//                        {
//                            newAssignments.Add(new ProductFlag { ProductId = productId, FlagId = flagId });
//                        }
//                    }
//                }

//                if (newAssignments.Any())
//                {
//                    await _context.ProductFlags.AddRangeAsync(newAssignments);
//                    await _context.SaveChangesAsync();
//                }

//                return Json(new { success = true });
//            }
//            catch (Exception ex)
//            {

//                return Json(new { success = false, message = "Wystąpił błąd serwera." });
//            }
//        }


//        // Nowe DTO dla metody poniżej
//        public class UpdateFlagsDto
//        {
//            public List<int> ProductIds { get; set; }
//            public List<int> FlagsToAdd { get; set; }
//            public List<int> FlagsToRemove { get; set; }
//        }

//        [HttpPost]
//        public async Task<IActionResult> UpdateFlagsForMultipleProducts([FromBody] UpdateFlagsDto data)
//        {
//            if (data == null || data.ProductIds == null || !data.ProductIds.Any())
//            {
//                return Json(new { success = false, message = "Nie wybrano produktów." });
//            }

//            // Sprawdzenie uprawnień
//            var firstProduct = await _context.Products.FindAsync(data.ProductIds.First());
//            if (firstProduct == null) return Json(new { success = false, message = "Nie znaleziono produktu." });
//            if (!await UserHasAccessToStore(firstProduct.StoreId)) return Forbid();

//            // 1. Usuwanie flag
//            if (data.FlagsToRemove != null && data.FlagsToRemove.Any())
//            {
//                var assignmentsToRemove = await _context.ProductFlags
//                    .Where(pf => pf.ProductId.HasValue && data.ProductIds.Contains(pf.ProductId.Value) && data.FlagsToRemove.Contains(pf.FlagId))
//                    .ToListAsync();

//                if (assignmentsToRemove.Any())
//                {
//                    _context.ProductFlags.RemoveRange(assignmentsToRemove);
//                }
//            }

//            // 2. Dodawanie flag (z pominięciem duplikatów)
//            if (data.FlagsToAdd != null && data.FlagsToAdd.Any())
//            {
//                var existingAssignments = await _context.ProductFlags
//                    .Where(pf => pf.ProductId.HasValue && data.ProductIds.Contains(pf.ProductId.Value))
//                    .ToListAsync();

//                var newAssignments = new List<ProductFlag>();
//                foreach (var productId in data.ProductIds)
//                {
//                    foreach (var flagId in data.FlagsToAdd)
//                    {
//                        if (!existingAssignments.Any(pf => pf.ProductId == productId && pf.FlagId == flagId))
//                        {
//                            newAssignments.Add(new ProductFlag { ProductId = productId, FlagId = flagId });
//                        }
//                    }
//                }

//                if (newAssignments.Any())
//                {
//                    await _context.ProductFlags.AddRangeAsync(newAssignments);
//                }
//            }

//            await _context.SaveChangesAsync();
//            return Json(new { success = true });
//        }

//        // DTO (Data Transfer Object) dla metody poniżej
//        public class ProductIdsDto
//        {
//            public List<int> ProductIds { get; set; }
//        }

//        [HttpPost]
//        public async Task<IActionResult> GetFlagCountsForProducts([FromBody] ProductIdsDto data)
//        {
//            if (data == null || data.ProductIds == null || !data.ProductIds.Any())
//            {
//                return BadRequest("Nie podano ID produktów.");
//            }

//            // Sprawdzenie uprawnień na podstawie pierwszego produktu (zakładamy, że wszystkie są z tego samego sklepu)
//            var firstProduct = await _context.Products.FindAsync(data.ProductIds.First());
//            if (firstProduct == null) return NotFound("Nie znaleziono produktu.");
//            if (!await UserHasAccessToStore(firstProduct.StoreId)) return Forbid();

//            var counts = await _context.ProductFlags
//                .Where(pf => pf.ProductId.HasValue && data.ProductIds.Contains(pf.ProductId.Value))
//                .GroupBy(pf => pf.FlagId)
//                .Select(g => new { FlagId = g.Key, Count = g.Count() })
//                .ToDictionaryAsync(x => x.FlagId, x => x.Count);

//            return Json(counts);
//        }
//    }

//    public class AssignFlagsViewModel
//    {
//        public int? ProductId { get; set; }
//        public int? AllegroProductId { get; set; }
//        public List<int> FlagIds { get; set; }
//    }

//}