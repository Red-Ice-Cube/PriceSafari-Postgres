using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class EmailTemplatesController : Controller
    {
        private readonly PriceSafariContext _context;

        public EmailTemplatesController(PriceSafariContext context)
        {
            _context = context;
        }

        // Lista szablonów
        public async Task<IActionResult> Index()
        {
            return View(await _context.EmailTemplates.ToListAsync());
        }

        // Tworzenie - GET
        public IActionResult Create()
        {
            return View();
        }

        // Tworzenie - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(EmailTemplate emailTemplate)
        {
            if (ModelState.IsValid)
            {
                _context.Add(emailTemplate);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(emailTemplate);
        }

        // Edycja - GET
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var emailTemplate = await _context.EmailTemplates.FindAsync(id);
            if (emailTemplate == null) return NotFound();

            return View(emailTemplate);
        }

        // Edycja - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, EmailTemplate emailTemplate)
        {
            if (id != emailTemplate.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(emailTemplate);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.EmailTemplates.Any(e => e.Id == id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(emailTemplate);
        }

        // Usuwanie
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var emailTemplate = await _context.EmailTemplates.FirstOrDefaultAsync(m => m.Id == id);
            if (emailTemplate == null) return NotFound();

            return View(emailTemplate);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var emailTemplate = await _context.EmailTemplates.FindAsync(id);
            if (emailTemplate != null)
            {
                _context.EmailTemplates.Remove(emailTemplate);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
