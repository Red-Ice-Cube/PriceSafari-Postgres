

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Attributes;
using PriceSafari.Data;
using PriceSafari.Enums;
using PriceSafari.IntervalPriceChanger.Models;
using PriceSafari.IntervalPriceChanger.Services;
using PriceSafari.Models;
using System.Text.Json;

namespace PriceSafari.IntervalPriceChanger.Controllers
{
    [Authorize(Roles = "Admin, Member")]
    public class IntervalPriceController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly IntervalPriceCalculationService _calcService;

        public IntervalPriceController(PriceSafariContext context, IntervalPriceCalculationService calcService)
        {
            _context = context;
            _calcService = calcService;
        }

        // ═══════════════════════════════════════════════════════
        // TWORZENIE
        // ═══════════════════════════════════════════════════════

        [HttpGet]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> Create(int automationRuleId)
        {
            var parentRule = await _context.AutomationRules
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.Id == automationRuleId);

            if (parentRule == null) return NotFound("Automat cenowy nie istnieje.");

            var model = new IntervalPriceRule
            {
                AutomationRuleId = automationRuleId,
                ColorHex = "#e67e22",
                PriceStep = -0.01m,
                IsPriceStepPercent = false,
                IsStepAActive = true,
                PriceStepB = -0.01m,
                IsPriceStepPercentB = false,
                IsStepBActive = false,
                PriceStepC = -0.01m,
                IsPriceStepPercentC = false,
                IsStepCActive = false,
                PreferredBlockSize = 10
            };

            ViewBag.ParentRule = parentRule;
            return View("~/Views/Panel/IntervalPriceChanger/CreateOrEdit.cshtml", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> Create(IntervalPriceRule rule)
        {
            rule.Id = 0;

            var parentExists = await _context.AutomationRules.AnyAsync(r => r.Id == rule.AutomationRuleId);
            if (!parentExists)
                ModelState.AddModelError("AutomationRuleId", "Wybrany automat cenowy nie istnieje.");

            if (!ValidateScheduleJson(rule.ScheduleJson))
                ModelState.AddModelError("ScheduleJson", "Nieprawidłowy format harmonogramu.");

            if (ModelState.IsValid)
            {
                _context.IntervalPriceRules.Add(rule);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Details), new { id = rule.Id });
            }

            ViewBag.ParentRule = await _context.AutomationRules
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.Id == rule.AutomationRuleId);

            return View("~/Views/Panel/IntervalPriceChanger/CreateOrEdit.cshtml", rule);
        }

        // ═══════════════════════════════════════════════════════
        // EDYCJA
        // ═══════════════════════════════════════════════════════

        [HttpGet]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> Edit(int id)
        {
            var rule = await _context.IntervalPriceRules
                .Include(r => r.AutomationRule)
                    .ThenInclude(ar => ar.Store)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null) return NotFound();

            ViewBag.ParentRule = rule.AutomationRule;
            return View("~/Views/Panel/IntervalPriceChanger/CreateOrEdit.cshtml", rule);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> Edit(int id, IntervalPriceRule rule)
        {
            if (id != rule.Id) return NotFound();

            if (!ValidateScheduleJson(rule.ScheduleJson))
                ModelState.AddModelError("ScheduleJson", "Nieprawidłowy format harmonogramu.");

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(rule);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.IntervalPriceRules.AnyAsync(e => e.Id == rule.Id))
                        return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Details), new { id = rule.Id });
            }

            ViewBag.ParentRule = await _context.AutomationRules
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.Id == rule.AutomationRuleId);

            return View("~/Views/Panel/IntervalPriceChanger/CreateOrEdit.cshtml", rule);
        }

        // ═══════════════════════════════════════════════════════
        // SZCZEGÓŁY
        // ═══════════════════════════════════════════════════════

        [HttpGet]
        [RequireUserAccess(UserAccessRequirement.ViewPriceAutomation)]
        public async Task<IActionResult> Details(int id)
        {
            var rule = await _context.IntervalPriceRules
                .Include(r => r.AutomationRule)
                    .ThenInclude(ar => ar.Store)
                .Include(r => r.AutomationRule)
                    .ThenInclude(ar => ar.CompetitorPreset)
                        .ThenInclude(cp => cp.CompetitorItems)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null) return NotFound();

            var model = await _calcService.PrepareDetailsDataAsync(rule);

            return View("~/Views/Panel/IntervalPriceChanger/Details.cshtml", model);
        }

        // ═══════════════════════════════════════════════════════
        // USUWANIE
        // ═══════════════════════════════════════════════════════

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> Delete(int id)
        {
            var rule = await _context.IntervalPriceRules.FindAsync(id);
            if (rule == null) return NotFound();

            int parentId = rule.AutomationRuleId;

            var assignments = _context.IntervalPriceProductAssignments
                .Where(a => a.IntervalPriceRuleId == id);
            _context.IntervalPriceProductAssignments.RemoveRange(assignments);

            _context.IntervalPriceRules.Remove(rule);
            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "PriceAutomation", new { id = parentId });
        }

        // ═══════════════════════════════════════════════════════
        // PRZYPISYWANIE PRODUKTÓW
        // ═══════════════════════════════════════════════════════

        [HttpGet]
        [RequireUserAccess(UserAccessRequirement.ViewPriceAutomation)]
        public async Task<IActionResult> GetAvailableProducts(int intervalRuleId)
        {
            var rule = await _context.IntervalPriceRules
                .Include(r => r.AutomationRule)
                .FirstOrDefaultAsync(r => r.Id == intervalRuleId);

            if (rule == null) return NotFound();

            bool isAllegro = rule.AutomationRule.SourceType == AutomationSourceType.Marketplace;

            var parentAssignments = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == rule.AutomationRuleId)
                .Select(a => new
                {
                    ProductId = isAllegro ? a.AllegroProductId : a.ProductId,
                    ProductName = isAllegro
                        ? (a.AllegroProduct != null ? a.AllegroProduct.AllegroProductName : "?")
                        : (a.Product != null ? a.Product.ProductName : "?"),
                    Identifier = isAllegro
                        ? (a.AllegroProduct != null ? a.AllegroProduct.IdOnAllegro : "")
                        : (a.Product != null ? a.Product.Ean : "")
                })
                .ToListAsync();

            var assignedInThisInterval = await _context.IntervalPriceProductAssignments
                .Where(a => a.IntervalPriceRuleId == intervalRuleId)
                .Select(a => isAllegro ? a.AllegroProductId : a.ProductId)
                .ToListAsync();

            var assignedInOtherIntervals = await _context.IntervalPriceProductAssignments
                .Where(a => a.IntervalPriceRuleId != intervalRuleId)
                .Select(a => new
                {
                    ProductId = isAllegro ? a.AllegroProductId : a.ProductId,
                    IntervalName = a.Rule.Name
                })
                .ToListAsync();

            var otherLookup = assignedInOtherIntervals
                .Where(x => x.ProductId.HasValue)
                .GroupBy(x => x.ProductId.Value)
                .ToDictionary(g => g.Key, g => g.First().IntervalName);

            var result = parentAssignments.Select(p => new
            {
                p.ProductId,
                p.ProductName,
                p.Identifier,
                IsInThisInterval = p.ProductId.HasValue && assignedInThisInterval.Contains(p.ProductId),
                IsInOtherInterval = p.ProductId.HasValue && otherLookup.ContainsKey(p.ProductId.Value),
                OtherIntervalName = p.ProductId.HasValue && otherLookup.ContainsKey(p.ProductId.Value)
                    ? otherLookup[p.ProductId.Value] : null
            }).ToList();

            return Json(result);
        }

        [HttpPost]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> AssignProducts([FromBody] IntervalAssignProductsDto model)
        {
            if (model == null || model.ProductIds == null || !model.ProductIds.Any())
                return BadRequest("Brak danych.");

            var rule = await _context.IntervalPriceRules
                .Include(r => r.AutomationRule)
                .FirstOrDefaultAsync(r => r.Id == model.IntervalRuleId);

            if (rule == null) return NotFound("Interwał nie istnieje.");

            bool isAllegro = rule.AutomationRule.SourceType == AutomationSourceType.Marketplace;

            var parentProductIds = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == rule.AutomationRuleId)
                .Select(a => isAllegro ? a.AllegroProductId : a.ProductId)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToListAsync();

            var parentSet = new HashSet<int>(parentProductIds);

            var inOtherIntervals = await _context.IntervalPriceProductAssignments
                .Where(a => a.IntervalPriceRuleId != model.IntervalRuleId)
                .Select(a => isAllegro ? a.AllegroProductId : a.ProductId)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToListAsync();

            var otherSet = new HashSet<int>(inOtherIntervals);

            var existingIds = (await _context.IntervalPriceProductAssignments
                .Where(a => a.IntervalPriceRuleId == model.IntervalRuleId)
                .ToListAsync())
                .Select(e => isAllegro ? e.AllegroProductId : e.ProductId)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToHashSet();



            // ═══ LIMIT SKLEPU ═══
            var (limit, used) = await GetIntervalLimitInfoAsync(rule.AutomationRule.StoreId, isAllegro);
            int remaining = Math.Max(0, limit - used);

            int added = 0, skipped = 0, blockedByLimit = 0;

            foreach (var pid in model.ProductIds)
            {
                if (!parentSet.Contains(pid) || otherSet.Contains(pid))
                { skipped++; continue; }

                if (existingIds.Contains(pid)) continue;

                if (added >= remaining)
                {
                    blockedByLimit++;
                    continue;
                }

                await _context.IntervalPriceProductAssignments.AddAsync(new IntervalPriceProductAssignment
                {
                    IntervalPriceRuleId = model.IntervalRuleId,
                    AssignedDate = DateTime.UtcNow,
                    ProductId = isAllegro ? null : pid,
                    AllegroProductId = isAllegro ? pid : null
                });
                added++;
            }

            await _context.SaveChangesAsync();

            string message = $"Przypisano {added} produktów do interwału: {rule.Name}";
            if (skipped > 0) message += $" (pominięto {skipped})";
            if (blockedByLimit > 0)
                message += $" — {blockedByLimit} produktów nie dodano z powodu limitu sklepu ({limit}).";

            return Ok(new
            {
                success = true,
                added,
                skipped,
                blockedByLimit,
                limit,
                used = used + added,
                remaining = Math.Max(0, limit - (used + added)),
                message
            });
        }

        [HttpGet]
        [RequireUserAccess(UserAccessRequirement.ViewPriceAutomation)]
        public async Task<IActionResult> GetIntervalLimitInfo(int storeId, bool isAllegro)
        {
            var (limit, used) = await GetIntervalLimitInfoAsync(storeId, isAllegro);
            return Json(new
            {
                limit,
                used,
                remaining = Math.Max(0, limit - used),
                percent = limit > 0 ? Math.Round((double)used / limit * 100, 1) : 0,
                isBlocked = used >= limit
            });
        }

        [HttpPost]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> UnassignProducts([FromBody] IntervalAssignProductsDto model)
        {
            if (model == null || model.ProductIds == null || !model.ProductIds.Any())
                return BadRequest("Brak danych.");

            var rule = await _context.IntervalPriceRules
                .Include(r => r.AutomationRule)
                .FirstOrDefaultAsync(r => r.Id == model.IntervalRuleId);

            if (rule == null) return NotFound();

            bool isAllegro = rule.AutomationRule.SourceType == AutomationSourceType.Marketplace;

            var toRemove = await _context.IntervalPriceProductAssignments
                .Where(a => a.IntervalPriceRuleId == model.IntervalRuleId
                    && (isAllegro
                        ? (a.AllegroProductId.HasValue && model.ProductIds.Contains(a.AllegroProductId.Value))
                        : (a.ProductId.HasValue && model.ProductIds.Contains(a.ProductId.Value))))
                .ToListAsync();

            if (toRemove.Any())
            {
                _context.IntervalPriceProductAssignments.RemoveRange(toRemove);
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, removed = toRemove.Count });
        }

        // ═══════════════════════════════════════════════════════
        // API: Aktualizacja harmonogramu (AJAX)
        // ═══════════════════════════════════════════════════════

        [HttpPost]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> UpdateSchedule([FromBody] UpdateScheduleRequest request)
        {
            if (request == null || request.RuleId <= 0) return BadRequest();

            var rule = await _context.IntervalPriceRules.FindAsync(request.RuleId);
            if (rule == null) return NotFound();

            if (!ValidateScheduleJson(request.ScheduleJson))
                return BadRequest("Nieprawidłowy format harmonogramu.");

            rule.ScheduleJson = request.ScheduleJson;
            if (request.PreferredBlockSize > 0)
                rule.PreferredBlockSize = request.PreferredBlockSize;

            await _context.SaveChangesAsync();
            return Ok(new { success = true, activeSlotsCount = rule.ActiveSlotsCount });
        }

        [HttpGet]
        [RequireUserAccess(UserAccessRequirement.ViewPriceAutomation)]
        public async Task<IActionResult> GetAssignedProducts(int intervalRuleId)
        {
            var rule = await _context.IntervalPriceRules
                .Include(r => r.AutomationRule)
                .FirstOrDefaultAsync(r => r.Id == intervalRuleId);

            if (rule == null) return NotFound();

            bool isAllegro = rule.AutomationRule.SourceType == AutomationSourceType.Marketplace;

            var assignments = await _context.IntervalPriceProductAssignments
                .Where(a => a.IntervalPriceRuleId == intervalRuleId)
                .Select(a => new
                {
                    ProductId = isAllegro ? a.AllegroProductId : a.ProductId,
                    ProductName = isAllegro
                        ? (a.AllegroProduct != null ? a.AllegroProduct.AllegroProductName : "?")
                        : (a.Product != null ? a.Product.ProductName : "?"),
                    Identifier = isAllegro
                        ? (a.AllegroProduct != null ? a.AllegroProduct.IdOnAllegro : "")
                        : (a.Product != null ? a.Product.Ean : ""),
                    a.AssignedDate
                })
                .ToListAsync();

            return Json(assignments);
        }

        [HttpPost]
        [RequireUserAccess(UserAccessRequirement.ViewPriceAutomation)]
        public async Task<IActionResult> GetIntervalStatusForProducts([FromBody] IntervalStatusRequest request)
        {
            if (request == null || request.ProductIds == null || !request.ProductIds.Any())
                return BadRequest();

            var rule = await _context.AutomationRules
                .FirstOrDefaultAsync(r => r.Id == request.AutomationRuleId);

            if (rule == null) return NotFound();
            bool isAllegro = rule.SourceType == AutomationSourceType.Marketplace;
            var (storeLimit, storeUsed) = await GetIntervalLimitInfoAsync(rule.StoreId, isAllegro);
       

            var intervals = await _context.IntervalPriceRules
                .Where(r => r.AutomationRuleId == request.AutomationRuleId)
                .Select(r => new
                {
                    r.Id,
                    r.Name,
                    r.ColorHex,
                    r.IsActive,
                    // Krok A
                    r.PriceStep,
                    r.IsPriceStepPercent,
                    r.IsStepAActive,
                    // Krok B
                    r.PriceStepB,
                    r.IsPriceStepPercentB,
                    r.IsStepBActive,
                    // Krok C
                    r.PriceStepC,
                    r.IsPriceStepPercentC,
                    r.IsStepCActive,
                    TotalAssigned = r.ProductAssignments.Count(),
                    MatchingCount = r.ProductAssignments
                        .Count(a => isAllegro
                            ? (a.AllegroProductId.HasValue && request.ProductIds.Contains(a.AllegroProductId.Value))
                            : (a.ProductId.HasValue && request.ProductIds.Contains(a.ProductId.Value)))
                })
                .ToListAsync();

            return Json(new
            {
                intervals,
                storeLimit,
                storeUsed,
                storeRemaining = Math.Max(0, storeLimit - storeUsed),
                storeLimitPercent = storeLimit > 0 ? Math.Round((double)storeUsed / storeLimit * 100, 1) : 0
            });
        }

        public class IntervalStatusRequest
        {
            public int AutomationRuleId { get; set; }
            public List<int> ProductIds { get; set; } = new();
        }

        // ═══════════════════════════════════════════════════════
        // HELPERY
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Walidacja formatu siatki harmonogramu.
        /// Akceptuje:
        ///   - 0 (pusty slot)
        ///   - ±1..6   (legacy: krok A bez step-prefix)
        ///   - ±101..106 / ±201..206 / ±301..306 (nowy format A/B/C)
        /// </summary>
        private bool ValidateScheduleJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return true;
            try
            {
                var schedule = JsonSerializer.Deserialize<int[][]>(json);
                if (schedule == null || schedule.Length != 7) return false;
                foreach (var day in schedule)
                {
                    if (day == null || day.Length != 144) return false;
                    foreach (var v in day)
                    {
                        if (v == 0) continue;
                        int abs = Math.Abs(v);
                        // Legacy
                        if (abs >= 1 && abs <= 6) continue;
                        // Nowy format
                        if (abs >= 101 && abs <= 106) continue;
                        if (abs >= 201 && abs <= 206) continue;
                        if (abs >= 301 && abs <= 306) continue;
                        return false;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Zwraca informacje o limicie produkt\u00f3w w interwa\u0142ach dla danego sklepu i \u017ar\u00f3d\u0142a.
        /// </summary>
        private async Task<(int limit, int used)> GetIntervalLimitInfoAsync(int storeId, bool isAllegro)
        {
            var store = await _context.Stores.FindAsync(storeId);
            if (store == null) return (0, 0);

            int limit = isAllegro ? store.AllegroIntervalLimitOfProducts : store.IntervalLimitOfProducts;

            // Licz wszystkie produkty przypisane do JAKIEGOKOLWIEK interwa\u0142u tego sklepu w danym \u017ar\u00f3dle
            int used = await _context.IntervalPriceProductAssignments
                .Where(a => a.Rule.AutomationRule.StoreId == storeId
                         && a.Rule.AutomationRule.SourceType == (isAllegro
                             ? AutomationSourceType.Marketplace
                             : AutomationSourceType.PriceComparison)
                         && (isAllegro ? a.AllegroProductId.HasValue : a.ProductId.HasValue))
                .CountAsync();

            return (limit, used);
        }

        // ═══════════════════════════════════════════════════════
        // DTOs
        // ═══════════════════════════════════════════════════════

        public class UpdateScheduleRequest
        {
            public int RuleId { get; set; }
            public string ScheduleJson { get; set; }
            public int PreferredBlockSize { get; set; }
        }

        public class IntervalAssignProductsDto
        {
            public int IntervalRuleId { get; set; }
            public List<int> ProductIds { get; set; } = new();
        }
    }
}