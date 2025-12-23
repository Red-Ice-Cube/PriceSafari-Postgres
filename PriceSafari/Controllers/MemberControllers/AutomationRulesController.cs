using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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

            // --- POPRAWKA TUTAJ ---
            // Zamiast 'var query = ...', używamy jawnego typu 'IQueryable<AutomationRule> query = ...'
            IQueryable<AutomationRule> query = _context.AutomationRules
                .Where(r => r.StoreId == storeId)
                .Include(r => r.CompetitorPreset);

            // Teraz to zadziała poprawnie, bo IQueryable może przyjąć wynik .Where()
            if (filterType.HasValue)
            {
                query = query.Where(r => r.SourceType == filterType.Value);
                ViewBag.CurrentFilter = filterType.Value;
            }

            var rules = await query
                .OrderByDescending(r => r.Id)
                .ToListAsync();

            ViewBag.StoreId = storeId;
            ViewBag.StoreName = storeName;

            return View("~/Views/ManagerPanel/AutomationRules/Index.cshtml", rules);
        }

        [HttpGet]
        public IActionResult GetModalPartial(AutomationSourceType type, int storeId)
        {
            // Przekazujemy StoreId przez ViewData, tak jak ustaliliśmy wcześniej
            var viewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
    {
        { "StoreId", storeId }
    };

            if (type == AutomationSourceType.Marketplace) // Zakładam, że enum 1 to Marketplace/Allegro
            {
                // Zwracamy widok dla Allegro
                return new PartialViewResult
                {
                    ViewName = "~/Views/Shared/PartialViewsPanel/_PresetyMarketPlace.cshtml",
                    ViewData = viewData
                };
            }
            else
            {
                // Zwracamy widok dla Porównywarek (Google/Ceneo)
                return new PartialViewResult
                {
                    ViewName = "~/Views/Shared/PartialViewsPanel/_PresetyPriceComparison.cshtml",
                    ViewData = viewData
                };
            }
        }

        // GET: AutomationRules/Create
        public IActionResult Create(int storeId, AutomationSourceType? sourceType)
        {
            var model = new AutomationRule
            {
                StoreId = storeId,
                ColorHex = "#4e73df",
                // Domyślny typ źródła przekazany z dashboardu lub domyślnie Porównywarki
                SourceType = sourceType ?? AutomationSourceType.PriceComparison,
                StrategyMode = AutomationStrategyMode.Competitiveness // Domyślna strategia
            };
            return View("~/Views/ManagerPanel/AutomationRules/CreateOrEdit.cshtml", model);
        }

        // POST: AutomationRules/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AutomationRule rule)
        {
            rule.Id = 0; // Zabezpieczenie

            // Jeśli użytkownik nie wybrał presetu (zostawił domyślny), upewniamy się że jest null
            if (rule.CompetitorPresetId == 0) rule.CompetitorPresetId = null;

            if (ModelState.IsValid)
            {
                _context.AutomationRules.Add(rule);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index), new { storeId = rule.StoreId, filterType = rule.SourceType });
            }
            return View("~/Views/ManagerPanel/AutomationRules/CreateOrEdit.cshtml", rule);
        }

        // GET: AutomationRules/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var rule = await _context.AutomationRules
                .Include(r => r.CompetitorPreset)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null) return NotFound();

            return View("~/Views/ManagerPanel/AutomationRules/CreateOrEdit.cshtml", rule);
        }

        // POST: AutomationRules/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, AutomationRule rule)
        {
            if (id != rule.Id) return NotFound();

            // Obsługa nulla dla presetu
            if (rule.CompetitorPresetId == 0) rule.CompetitorPresetId = null;

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(rule);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.AutomationRules.Any(e => e.Id == rule.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index), new { storeId = rule.StoreId, filterType = rule.SourceType });
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
                var type = rule.SourceType;
                _context.AutomationRules.Remove(rule);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index), new { storeId = storeId, filterType = type });
            }
            return NotFound();
        }
    }
}