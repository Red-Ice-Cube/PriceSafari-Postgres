using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceTracker.Data;
using PriceTracker.Models;
using PriceTracker.ViewModels;

namespace PriceTracker.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    public class ProductFlagsController : Controller
    {
        private readonly PriceTrackerContext _context;

        public ProductFlagsController(PriceTrackerContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetFlagsForProduct(int productId)
        {
            var flagIds = await _context.ProductFlags
                .Where(pf => pf.ProductId == productId)
                .Select(pf => pf.FlagId)
                .ToListAsync();

            return Json(flagIds);
        }

        [HttpPost]
        public async Task<IActionResult> AssignFlagsToProduct([FromBody] AssignFlagsViewModel model)
        {
            if (model == null || model.ProductId <= 0 || model.FlagIds == null)
            {
                return BadRequest("Invalid data.");
            }

            var existingFlags = await _context.ProductFlags
                .Where(pf => pf.ProductId == model.ProductId)
                .ToListAsync();

            _context.ProductFlags.RemoveRange(existingFlags);

            foreach (var flagId in model.FlagIds)
            {
                _context.ProductFlags.Add(new ProductFlag
                {
                    ProductId = model.ProductId,
                    FlagId = flagId
                });
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
}
