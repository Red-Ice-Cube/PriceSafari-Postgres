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








        public class AssignProductsDto
        {
            public int RuleId { get; set; } // Ignorowane przy Unassign
            public List<int> ProductIds { get; set; } = new List<int>();
            public bool IsAllegro { get; set; } // <--- Nowe pole, kluczowe!
        }

        public class RulesStatusRequest
        {
            public int StoreId { get; set; }
            public AutomationSourceType SourceType { get; set; }
            public List<int> ProductIds { get; set; } = new List<int>();
            public bool IsAllegro { get; set; }
        }

        public class RuleStatusViewModel
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string ColorHex { get; set; }
            public bool IsActive { get; set; }
            public int StrategyMode { get; set; }

            // Ile z ZAZNACZONYCH produktów jest już w tej regule
            public int MatchingCount { get; set; }

            // Ile w ogóle produktów jest w tej regule (informacyjnie)
            public int TotalCount { get; set; }
        }

        // =====================================================================
        // NOWE METODY API
        // =====================================================================

        // 1. Pobieranie listy reguł z licznikami (zastępuje stare GetRulesForModal)
        [HttpPost]
        public async Task<IActionResult> GetRulesStatusForProducts([FromBody] RulesStatusRequest request)
        {
            if (request == null) return BadRequest();

            // A. Pobierz wszystkie reguły danego typu dla sklepu
            var rules = await _context.AutomationRules
                .Where(r => r.StoreId == request.StoreId && r.SourceType == request.SourceType)
                .AsNoTracking()
                .ToListAsync();

            var result = new List<RuleStatusViewModel>();

            // B. Pobierz przypisania TYLKO dla produktów z listy (zaznaczonych)
            // Dzięki temu wiemy, gdzie one teraz "siedzą"
            var relevantAssignments = await _context.AutomationProductAssignments
                .Where(a => request.IsAllegro
                    ? (a.AllegroProductId.HasValue && request.ProductIds.Contains(a.AllegroProductId.Value))
                    : (a.ProductId.HasValue && request.ProductIds.Contains(a.ProductId.Value)))
                .Select(a => new { a.AutomationRuleId })
                .ToListAsync();

            // C. Dla każdej reguły policz statystyki
            foreach (var rule in rules)
            {
                // Ile z ZAZNACZONYCH jest tutaj?
                int matchingCount = relevantAssignments.Count(a => a.AutomationRuleId == rule.Id);

                // Ile w ogóle jest w tej regule? (To zapytanie jest szybkie na indeksach)
                int totalCount = await _context.AutomationProductAssignments
                    .CountAsync(a => a.AutomationRuleId == rule.Id);

                result.Add(new RuleStatusViewModel
                {
                    Id = rule.Id,
                    Name = rule.Name,
                    ColorHex = rule.ColorHex,
                    IsActive = rule.IsActive,
                    StrategyMode = (int)rule.StrategyMode,
                    MatchingCount = matchingCount,
                    TotalCount = totalCount
                });
            }

            // Sortowanie: najpierw te, gdzie już coś mamy, potem alfabetycznie
            return Json(result.OrderByDescending(r => r.MatchingCount).ThenBy(r => r.Name));
        }

        // 2. Przypisywanie produktów (Zastępuje stare AssignProducts)
        [HttpPost]
        public async Task<IActionResult> AssignProducts([FromBody] AssignProductsDto model)
        {
            if (model == null || model.ProductIds == null || !model.ProductIds.Any())
            {
                return BadRequest("Brak danych.");
            }

            var rule = await _context.AutomationRules.FindAsync(model.RuleId);
            if (rule == null) return NotFound("Reguła nie istnieje.");

            // Pobierz istniejące przypisania dla tych produktów, aby je zaktualizować (przenieść)
            // Zamiast tworzyć duplikaty (co wywaliłoby błąd UNIQUE constraint)
            var existingAssignments = await _context.AutomationProductAssignments
                .Where(a => model.IsAllegro
                    ? (a.AllegroProductId.HasValue && model.ProductIds.Contains(a.AllegroProductId.Value))
                    : (a.ProductId.HasValue && model.ProductIds.Contains(a.ProductId.Value)))
                .ToListAsync();

            foreach (var prodId in model.ProductIds)
            {
                // Sprawdź czy ten produkt ma już jakąś regułę
                var existing = model.IsAllegro
                    ? existingAssignments.FirstOrDefault(a => a.AllegroProductId == prodId)
                    : existingAssignments.FirstOrDefault(a => a.ProductId == prodId);

                if (existing != null)
                {
                    // SCENARIUSZ A: Aktualizacja (Przeniesienie do innej grupy)
                    // Zmieniamy tylko ID reguły
                    if (existing.AutomationRuleId != model.RuleId)
                    {
                        existing.AutomationRuleId = model.RuleId;
                        existing.AssignedDate = DateTime.UtcNow;
                        _context.Update(existing);
                    }
                }
                else
                {
                    // SCENARIUSZ B: Nowe przypisanie
                    var newAssignment = new AutomationProductAssignment
                    {
                        AutomationRuleId = model.RuleId,
                        AssignedDate = DateTime.UtcNow,
                        // Ustawiamy odpowiednie ID w zależności od kontekstu
                        ProductId = model.IsAllegro ? null : prodId,
                        AllegroProductId = model.IsAllegro ? prodId : null
                    };
                    await _context.AutomationProductAssignments.AddAsync(newAssignment);
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = $"Pomyślnie przypisano {model.ProductIds.Count} produktów do grupy: {rule.Name}" });
        }

        // 3. Odpinanie produktów (Nowa metoda dla czerwonego kafelka)
        [HttpPost]
        public async Task<IActionResult> UnassignProducts([FromBody] AssignProductsDto model)
        {
            if (model == null || model.ProductIds == null || !model.ProductIds.Any())
            {
                return BadRequest("Brak danych.");
            }

            // Znajdź wszystkie przypisania dla wybranych produktów
            var assignmentsToRemove = await _context.AutomationProductAssignments
                .Where(a => model.IsAllegro
                    ? (a.AllegroProductId.HasValue && model.ProductIds.Contains(a.AllegroProductId.Value))
                    : (a.ProductId.HasValue && model.ProductIds.Contains(a.ProductId.Value)))
                .ToListAsync();

            if (assignmentsToRemove.Any())
            {
                _context.AutomationProductAssignments.RemoveRange(assignmentsToRemove);
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, message = $"Usunięto przypisanie automatyzacji dla {assignmentsToRemove.Count} produktów." });
        }
    }
}