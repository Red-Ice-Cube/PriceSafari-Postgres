using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Attributes;
using PriceSafari.Data;
using PriceSafari.Enums;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using System.Security.Claims;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Member")]
    public class AutomationRulesController : Controller
    {
        private readonly PriceSafariContext _context;

        public AutomationRulesController(PriceSafariContext context)
        {
            _context = context;
        }

        [HttpGet]
        [RequireUserAccess(UserAccessRequirement.ViewPriceAutomation)]
        public async Task<IActionResult> Dashboard()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // KROK 1: Pobierz listę sklepów dostępnych dla użytkownika (tylko podstawowe dane)
            // Nie dołączamy tu AutomationRules, żeby nie obciążać zapytania
            var userStores = await _context.UserStores
                .Where(us => us.UserId == userId)
                .Select(us => new
                {
                    us.StoreClass.StoreId,
                    us.StoreClass.StoreName,
                    us.StoreClass.StoreLogoUrl,
                    us.StoreClass.OnCeneo,
                    us.StoreClass.OnGoogle,
                    us.StoreClass.OnAllegro
                })
                .AsNoTracking()
                .ToListAsync();

            // Wyciągnij listę ID sklepów, żeby użyć jej w zapytaniu o reguły
            var storeIds = userStores.Select(s => s.StoreId).ToList();

            // KROK 2: Pobierz liczniki reguł BEZPOŚREDNIO z tabeli reguł (tak jak robi to Index)
            // Grupujemy po StoreId i SourceType, aby policzyć ile jest reguł danego typu w danym sklepie
            var rulesCounts = await _context.AutomationRules
                .Where(r => storeIds.Contains(r.StoreId))
                .GroupBy(r => new { r.StoreId, r.SourceType })
                .Select(g => new
                {
                    StoreId = g.Key.StoreId,
                    SourceType = g.Key.SourceType,
                    Count = g.Count()
                })
                .AsNoTracking()
                .ToListAsync();

            // KROK 3: Połącz dane w pamięci (Client-side)
            // Mapujemy surowe dane do ViewModelu
            var model = userStores.Select(store => new AutomationStoreListViewModel
            {
                StoreId = store.StoreId,
                StoreName = store.StoreName,
                LogoUrl = store.StoreLogoUrl,
                OnCeneo = store.OnCeneo,
                OnGoogle = store.OnGoogle,
                OnAllegro = store.OnAllegro,

                // Szukamy w pobranych licznikach odpowiedniej wartości dla tego sklepu
                ComparisonRulesCount = rulesCounts
                    .Where(r => r.StoreId == store.StoreId && r.SourceType == AutomationSourceType.PriceComparison)
                    .Sum(r => r.Count),

                MarketplaceRulesCount = rulesCounts
                    .Where(r => r.StoreId == store.StoreId && r.SourceType == AutomationSourceType.Marketplace)
                    .Sum(r => r.Count)
            }).ToList();

            return View("~/Views/Panel/AutomationRules/Dashboard.cshtml", model);
        }


        [RequireUserAccess(UserAccessRequirement.ViewPriceAutomation)]
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

            return View("~/Views/Panel/AutomationRules/Index.cshtml", rules);
        }

        [HttpGet]
        [RequireUserAccess(UserAccessRequirement.ViewPriceAutomation)]
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

        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public IActionResult Create(int storeId, AutomationSourceType? sourceType)
        {
            var model = new AutomationRule
            {
                StoreId = storeId,
                ColorHex = "#4e73df",

                SourceType = sourceType ?? AutomationSourceType.PriceComparison,
                StrategyMode = AutomationStrategyMode.Competitiveness

            };
            return View("~/Views/Panel/AutomationRules/CreateOrEdit.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
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
            return View("~/Views/Panel/AutomationRules/CreateOrEdit.cshtml", rule);
        }

        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var rule = await _context.AutomationRules
                .Include(r => r.CompetitorPreset)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null) return NotFound();

            return View("~/Views/Panel/AutomationRules/CreateOrEdit.cshtml", rule);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
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
            return View("~/Views/Panel/AutomationRules/CreateOrEdit.cshtml", rule);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
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
            public int RuleId { get; set; }

            public List<int> ProductIds { get; set; } = new List<int>();
            public bool IsAllegro { get; set; }

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

            public int MatchingCount { get; set; }

            public int TotalCount { get; set; }
        }

        [HttpPost]
        [RequireUserAccess(UserAccessRequirement.ViewPriceAutomation)]
        public async Task<IActionResult> GetRulesStatusForProducts([FromBody] RulesStatusRequest request)
        {
            if (request == null) return BadRequest();

            var rules = await _context.AutomationRules
                .Where(r => r.StoreId == request.StoreId && r.SourceType == request.SourceType)
                .AsNoTracking()
                .ToListAsync();

            var result = new List<RuleStatusViewModel>();

            var relevantAssignments = await _context.AutomationProductAssignments
                .Where(a => request.IsAllegro
                    ? (a.AllegroProductId.HasValue && request.ProductIds.Contains(a.AllegroProductId.Value))
                    : (a.ProductId.HasValue && request.ProductIds.Contains(a.ProductId.Value)))
                .Select(a => new { a.AutomationRuleId })
                .ToListAsync();

            foreach (var rule in rules)
            {

                int matchingCount = relevantAssignments.Count(a => a.AutomationRuleId == rule.Id);

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

            return Json(result.OrderByDescending(r => r.MatchingCount).ThenBy(r => r.Name));
        }

        [HttpPost]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> AssignProducts([FromBody] AssignProductsDto model)
        {
            if (model == null || model.ProductIds == null || !model.ProductIds.Any())
            {
                return BadRequest("Brak danych.");
            }

            var rule = await _context.AutomationRules.FindAsync(model.RuleId);
            if (rule == null) return NotFound("Reguła nie istnieje.");

            var existingAssignments = await _context.AutomationProductAssignments
                .Where(a => model.IsAllegro
                    ? (a.AllegroProductId.HasValue && model.ProductIds.Contains(a.AllegroProductId.Value))
                    : (a.ProductId.HasValue && model.ProductIds.Contains(a.ProductId.Value)))
                .ToListAsync();

            foreach (var prodId in model.ProductIds)
            {

                var existing = model.IsAllegro
                    ? existingAssignments.FirstOrDefault(a => a.AllegroProductId == prodId)
                    : existingAssignments.FirstOrDefault(a => a.ProductId == prodId);

                if (existing != null)
                {

                    if (existing.AutomationRuleId != model.RuleId)
                    {
                        existing.AutomationRuleId = model.RuleId;
                        existing.AssignedDate = DateTime.UtcNow;
                        _context.Update(existing);
                    }
                }
                else
                {

                    var newAssignment = new AutomationProductAssignment
                    {
                        AutomationRuleId = model.RuleId,
                        AssignedDate = DateTime.UtcNow,

                        ProductId = model.IsAllegro ? null : prodId,
                        AllegroProductId = model.IsAllegro ? prodId : null
                    };
                    await _context.AutomationProductAssignments.AddAsync(newAssignment);
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = $"Pomyślnie przypisano {model.ProductIds.Count} produktów do grupy: {rule.Name}" });
        }

        [HttpPost]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> UnassignProducts([FromBody] AssignProductsDto model)
        {
            if (model == null || model.ProductIds == null || !model.ProductIds.Any())
            {
                return BadRequest("Brak danych.");
            }

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