using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using System.Linq;
using System;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class InvoiceController : Controller
    {
        private readonly PriceSafariContext _context;

        public InvoiceController(PriceSafariContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var invoices = await _context.Invoices
                .Include(i => i.Store)
                .Include(i => i.Plan)
                .ToListAsync();
            return View("~/Views/ManagerPanel/Invoices/Index.cshtml", invoices);
        }

        public async Task<IActionResult> Create()
        {
            var stores = await _context.Stores.Include(s => s.Plan).ToListAsync();
            ViewBag.Stores = new SelectList(stores, "StoreId", "StoreName");

            var plans = await _context.Plans.ToListAsync();
            ViewBag.Plans = new SelectList(plans, "PlanId", "PlanName");

            return View("~/Views/ManagerPanel/Invoices/Create.cshtml");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(InvoiceClass invoice)
        {
            if (ModelState.IsValid)
            {
                _context.Add(invoice);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            var stores = await _context.Stores.Include(s => s.Plan).ToListAsync();
            ViewBag.Stores = new SelectList(stores, "StoreId", "StoreName");
            var plans = await _context.Plans.ToListAsync();
            ViewBag.Plans = new SelectList(plans, "PlanId", "PlanName");

            return View("~/Views/ManagerPanel/Invoices/Create.cshtml", invoice);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var invoice = await _context.Invoices
                .Include(i => i.Store)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.InvoiceId == id);
            if (invoice == null) return NotFound();

            return View("~/Views/ManagerPanel/Invoices/Edit.cshtml", invoice);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, InvoiceClass invoice)
        {
            if (id != invoice.InvoiceId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(invoice);
                    await _context.SaveChangesAsync();

                    if (invoice.IsPaid)
                    {
                        var store = await _context.Stores.Include(s => s.Plan).FirstOrDefaultAsync(s => s.StoreId == invoice.StoreId);
                        if (store != null)
                        {
                            store.RemainingDays += invoice.DaysIncluded;
                            store.ProductsToScrap = store.Plan.ProductsToScrap;
                            await _context.SaveChangesAsync();
                        }
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!InvoiceExists(invoice.InvoiceId))
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

            invoice.Store = await _context.Stores.FindAsync(invoice.StoreId);
            invoice.Plan = await _context.Plans.FindAsync(invoice.PlanId);

            return View("~/Views/ManagerPanel/Invoices/Edit.cshtml", invoice);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsPaid(int id)
        {
            var invoice = await _context.Invoices
                .Include(i => i.Store)
                .Include(i => i.Plan)
                .FirstOrDefaultAsync(i => i.InvoiceId == id);
            if (invoice == null)
            {
                return NotFound();
            }

            if (!invoice.IsPaid)
            {
                invoice.IsPaid = true;
                invoice.PaymentDate = DateTime.Now;

                if (invoice.InvoiceNumber.StartsWith("FP/"))
                {
                    invoice.OriginalProformaNumber = invoice.InvoiceNumber;

                    int invoiceNumber = await GetNextInvoiceNumberAsync();

                    invoice.InvoiceNumber = $"PS/{invoiceNumber.ToString("D6")}/sDB/{invoice.IssueDate.Year}";
                }

                var store = invoice.Store;
                if (store != null)
                {

                    store.PlanId = invoice.PlanId;

                    store.RemainingDays += invoice.DaysIncluded;

                    store.ProductsToScrap = invoice.Plan.ProductsToScrap;
                }

                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<int> GetNextInvoiceNumberAsync()
        {
            var currentYear = DateTime.Now.Year;
            var counter = await _context.InvoiceCounters.FirstOrDefaultAsync(c => c.Year == currentYear);
            if (counter == null)
            {
                counter = new InvoiceCounter { Year = currentYear, LastProformaNumber = 0, LastInvoiceNumber = 0 };
                _context.InvoiceCounters.Add(counter);
                await _context.SaveChangesAsync();
            }

            counter.LastInvoiceNumber++;
            await _context.SaveChangesAsync();
            return counter.LastInvoiceNumber;
        }

        private bool InvoiceExists(int id)
        {
            return _context.Invoices.Any(e => e.InvoiceId == id);
        }
    }
}