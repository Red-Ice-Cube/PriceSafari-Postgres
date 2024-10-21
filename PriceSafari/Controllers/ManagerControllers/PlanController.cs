using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using System.Linq;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class PlanController : Controller
    {
        private readonly PriceSafariContext _context;

        public PlanController(PriceSafariContext context)
        {
            _context = context;
        }

        // GET: Plan/Index
        public async Task<IActionResult> Index()
        {
            var plans = await _context.Plans.ToListAsync();
            return View("~/Views/ManagerPanel/Plan/Index.cshtml", plans);
        }

        // GET: Plan/Create
        public IActionResult Create()
        {
            return View("~/Views/ManagerPanel/Plan/Create.cshtml");
        }

        // POST: Plan/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PlanClass plan)
        {
            if (ModelState.IsValid)
            {
                _context.Add(plan);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View("~/Views/ManagerPanel/Plan/Create.cshtml", plan);
        }

        // GET: Plan/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var plan = await _context.Plans.FindAsync(id);
            if (plan == null) return NotFound();

            return View("~/Views/ManagerPanel/Plan/Edit.cshtml", plan);
        }

        // POST: Plan/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PlanClass plan)
        {
            if (id != plan.PlanId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(plan);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!PlanExists(plan.PlanId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View("~/Views/ManagerPanel/Plan/Edit.cshtml", plan);
        }

        // GET: Plan/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var plan = await _context.Plans
                .FirstOrDefaultAsync(m => m.PlanId == id);
            if (plan == null) return NotFound();

            return View("~/Views/ManagerPanel/Plan/Delete.cshtml", plan);
        }

        // POST: Plan/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var plan = await _context.Plans.FindAsync(id);
            _context.Plans.Remove(plan);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool PlanExists(int id)
        {
            return _context.Plans.Any(e => e.PlanId == id);
        }
    }
}
