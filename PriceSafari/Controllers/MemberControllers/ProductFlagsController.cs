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
            else
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

