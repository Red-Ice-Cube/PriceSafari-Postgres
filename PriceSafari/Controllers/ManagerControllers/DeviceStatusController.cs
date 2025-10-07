using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class DeviceStatusController : Controller
    {
        private readonly PriceSafariContext _context;

        public DeviceStatusController(PriceSafariContext context)
        {
            _context = context;
        }

      
        [HttpGet]
        public async Task<IActionResult> DeviceStatus()
        {
         
            var deviceStatuses = await _context.DeviceStatuses
                .OrderByDescending(d => d.LastCheck)
                .ToListAsync();

          
            return View("~/Views/ManagerPanel/Settings/DeviceStatus.cshtml", deviceStatuses);
        }

    
        [HttpPost]
        public async Task<IActionResult> ClearAll()
        {
            var allRecords = _context.DeviceStatuses.ToList();
            _context.DeviceStatuses.RemoveRange(allRecords);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(DeviceStatus));
        }
    }
}
