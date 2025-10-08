using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class TaskExecutionLogController : Controller
    {
        private readonly PriceSafariContext _context;

        public TaskExecutionLogController(PriceSafariContext context)
        {
            _context = context;
        }

        
        [HttpGet]
        public async Task<IActionResult> TaskExecutionLog()
        {
            var logs = await _context.TaskExecutionLogs
                .OrderByDescending(t => t.StartTime)
                .ToListAsync();

        
            return View("~/Views/ManagerPanel/Settings/TaskExecutionLog.cshtml", logs);
        }

        [HttpPost]
        public async Task<IActionResult> ClearAll()
        {
            var allRecords = _context.TaskExecutionLogs.ToList();
            _context.TaskExecutionLogs.RemoveRange(allRecords);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(TaskExecutionLog));
        }


        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var log = await _context.TaskExecutionLogs.FindAsync(id);
            if (log != null)
            {
                _context.TaskExecutionLogs.Remove(log);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(TaskExecutionLog));
        }
    }
}
