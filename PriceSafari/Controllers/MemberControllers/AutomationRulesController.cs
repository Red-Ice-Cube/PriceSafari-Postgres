using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;

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

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {

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

                ComparisonRulesCount = s.AutomationRules.Count(r => r.SourceType == AutomationSourceType.PriceComparison),

                MarketplaceRulesCount = s.AutomationRules.Count(r => r.SourceType == AutomationSourceType.Marketplace)
            }).ToList();

            return View("~/Views/ManagerPanel/AutomationRules/Dashboard.cshtml", model);
        }

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

            IQueryable<AutomationRule> query = _context.AutomationRules
                .Where(r => r.StoreId == storeId)
                .Include(r => r.CompetitorPreset);

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

            var viewData = new ViewDataDictionary(new Microsoft.AspNetCore.Mvc.ModelBinding.EmptyModelMetadataProvider(), new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary())
    {
        { "StoreId", storeId }
    };

            if (type == AutomationSourceType.Marketplace)

            {

                return new PartialViewResult
                {
                    ViewName = "~/Views/Shared/PartialViewsPanel/_PresetyMarketPlace.cshtml",
                    ViewData = viewData
                };
            }
            else
            {

                return new PartialViewResult
                {
                    ViewName = "~/Views/Shared/PartialViewsPanel/_PresetyPriceComparison.cshtml",
                    ViewData = viewData
                };
            }
        }

        public IActionResult Create(int storeId, AutomationSourceType? sourceType)
        {
            var model = new AutomationRule
            {
                StoreId = storeId,
                ColorHex = "#4e73df",

                SourceType = sourceType ?? AutomationSourceType.PriceComparison,
                StrategyMode = AutomationStrategyMode.Competitiveness

            };
            return View("~/Views/ManagerPanel/AutomationRules/CreateOrEdit.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AutomationRule rule)
        {
            rule.Id = 0;

            if (rule.CompetitorPresetId == 0) rule.CompetitorPresetId = null;

            if (ModelState.IsValid)
            {
                _context.AutomationRules.Add(rule);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index), new { storeId = rule.StoreId, filterType = rule.SourceType });
            }
            return View("~/Views/ManagerPanel/AutomationRules/CreateOrEdit.cshtml", rule);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var rule = await _context.AutomationRules
                .Include(r => r.CompetitorPreset)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null) return NotFound();

            return View("~/Views/ManagerPanel/AutomationRules/CreateOrEdit.cshtml", rule);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, AutomationRule rule)
        {
            if (id != rule.Id) return NotFound();

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