using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels; // Dodaj namespace do ViewModelu
using System.Linq;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin")]
    public class AutomationRulesController : Controller
    {
        private readonly PriceSafariContext _context;

        public AutomationRulesController(PriceSafariContext context)
        {
            _context = context;
        }

        // NOWA AKCJA: Dashboard - Lista sklepów z licznikami reguł
        // GET: AutomationRules/Dashboard
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            // Pobieramy wszystkie sklepy (bo to panel Admina)
            // Włączamy AutomationRules, żeby policzyć ile ich jest
            var stores = await _context.Stores
                .Include(s => s.AutomationRules)
                .AsNoTracking()
                .ToListAsync();

            var model = stores.Select(s => new AutomationStoreListViewModel
            {
                StoreId = s.StoreId,
                StoreName = s.StoreName,
                LogoUrl = s.StoreLogoUrl,
                OnCeneo = s.OnCeneo,
                OnGoogle = s.OnGoogle,
                OnAllegro = s.OnAllegro,

                // Liczymy reguły dla Porównywarek (Enum = 0)
                ComparisonRulesCount = s.AutomationRules.Count(r => r.SourceType == AutomationSourceType.PriceComparison),

                // Liczymy reguły dla Marketplace (Enum = 1)
                MarketplaceRulesCount = s.AutomationRules.Count(r => r.SourceType == AutomationSourceType.Marketplace)
            }).ToList();

            return View("~/Views/ManagerPanel/AutomationRules/Dashboard.cshtml", model);
        }

        // ZMODYFIKOWANA AKCJA: Index - Lista reguł dla konkretnego sklepu
        // GET: AutomationRules/Index?storeId=5&filterType=Marketplace
        public async Task<IActionResult> Index(int? storeId, AutomationSourceType? filterType)
        {
            if (storeId == null)
            {
                return RedirectToAction(nameof(Dashboard));
            }

            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            if (storeName == null) return NotFound("Sklep nie istnieje.");

            // Pobieranie reguł
            var query = _context.AutomationRules
                .Where(r => r.StoreId == storeId);

            // Jeśli wybrano filtr (np. kliknięto w ikonę Allegro w Dashboardzie), filtrujemy listę
            if (filterType.HasValue)
            {
                query = query.Where(r => r.SourceType == filterType.Value);
                ViewBag.CurrentFilter = filterType.Value; // Żeby wiedzieć co zaznaczyć w widoku
            }

            var rules = await query
                .OrderByDescending(r => r.Id)
                .ToListAsync();

            ViewBag.StoreId = storeId;
            ViewBag.StoreName = storeName;

            return View("~/Views/ManagerPanel/AutomationRules/Index.cshtml", rules);
        }

        // ... Reszta metod (Create, Edit, Delete) pozostaje bez zmian ...
        // ... (Create, Edit, Delete wklejone w poprzedniej odpowiedzi) ...

        // GET: AutomationRules/Create?storeId=5
        public IActionResult Create(int storeId)
        {
            var model = new AutomationRule
            {
                StoreId = storeId,
                ColorHex = "#4e73df"
            };
            return View("~/Views/ManagerPanel/AutomationRules/CreateOrEdit.cshtml", model);
        }

        // POST: AutomationRules/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AutomationRule rule)
        {
            rule.Id = 0;
            if (ModelState.IsValid)
            {
                _context.AutomationRules.Add(rule);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index), new { storeId = rule.StoreId });
            }
            return View("~/Views/ManagerPanel/AutomationRules/CreateOrEdit.cshtml", rule);
        }

        // GET: AutomationRules/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var rule = await _context.AutomationRules.FindAsync(id);
            if (rule == null) return NotFound();
            return View("~/Views/ManagerPanel/AutomationRules/CreateOrEdit.cshtml", rule);
        }

        // POST: AutomationRules/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, AutomationRule rule)
        {
            if (id != rule.Id) return NotFound();
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(rule);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AutomationRuleExists(rule.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index), new { storeId = rule.StoreId });
            }
            return View("~/Views/ManagerPanel/AutomationRules/CreateOrEdit.cshtml", rule);
        }

        // POST: AutomationRules/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var rule = await _context.AutomationRules.FindAsync(id);
            if (rule != null)
            {
                int storeId = rule.StoreId;
                _context.AutomationRules.Remove(rule);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index), new { storeId = storeId });
            }
            return NotFound();
        }

        private bool AutomationRuleExists(int id)
        {
            return _context.AutomationRules.Any(e => e.Id == id);
        }
    }
}