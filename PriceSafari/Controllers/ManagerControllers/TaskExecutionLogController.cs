using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.IntervalPriceChanger.Models;
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

        // ═══════════════════════════════════════════════════════
        // LOGI ZADAŃ (istniejące)
        // ═══════════════════════════════════════════════════════

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
            await _context.TaskExecutionLogs.ExecuteDeleteAsync();
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

        // ═══════════════════════════════════════════════════════
        // LOGI INTERWAŁÓW (nowe)
        // ═══════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> IntervalExecutionLog()
        {
            var batches = await _context.Set<IntervalPriceExecutionBatch>()
                .Include(b => b.IntervalRule)
                    .ThenInclude(r => r.AutomationRule)
                        .ThenInclude(ar => ar.Store)
                .Include(b => b.Items)
                .OrderByDescending(b => b.ExecutionDate)
                .Take(2000)
                .ToListAsync();

            return View("~/Views/ManagerPanel/Settings/IntervalExecutionLog.cshtml", batches);
        }

        [HttpPost]
        public async Task<IActionResult> ClearAllIntervalLogs()
        {
            // Bezpieczna kolejność: najpierw dzieci, potem rodzice.
            // Każde ExecuteDeleteAsync to atomowy SQL DELETE — działa z retry strategy
            // i nie ładuje rekordów do pamięci.
            await _context.Set<IntervalPriceExecutionItem>().ExecuteDeleteAsync();
            await _context.Set<IntervalPriceExecutionBatch>().ExecuteDeleteAsync();

            return RedirectToAction(nameof(IntervalExecutionLog));
        }

        [HttpGet]
        public async Task<IActionResult> DeleteIntervalBatch(int id)
        {
            // Najpierw kasujemy itemy tego batcha, potem sam batch.
            // Bez Include — szybciej i bez ładowania do RAM.
            await _context.Set<IntervalPriceExecutionItem>()
                .Where(i => i.BatchId == id)
                .ExecuteDeleteAsync();

            await _context.Set<IntervalPriceExecutionBatch>()
                .Where(b => b.Id == id)
                .ExecuteDeleteAsync();

            return RedirectToAction(nameof(IntervalExecutionLog));
        }
    }
}