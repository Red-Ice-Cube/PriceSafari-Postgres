using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Attributes;
using PriceSafari.Data;
using PriceSafari.Enums;
using PriceSafari.IntervalPriceChanger.Models;
using PriceSafari.Models;
using System.Text.Json;

namespace PriceSafari.IntervalPriceChanger.Controllers
{
    [Authorize(Roles = "Admin, Member")]
    public class IntervalPriceController : Controller
    {
        private readonly PriceSafariContext _context;

        public IntervalPriceController(PriceSafariContext context)
        {
            _context = context;
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
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null) return NotFound();

            var assignedCount = await _context.IntervalPriceProductAssignments
                .CountAsync(a => a.IntervalPriceRuleId == id);

            var parentProductCount = await _context.AutomationProductAssignments
                .CountAsync(a => a.AutomationRuleId == rule.AutomationRuleId);

            ViewBag.AssignedProductsCount = assignedCount;
            ViewBag.ParentProductCount = parentProductCount;

            return View("~/Views/Panel/IntervalPriceChanger/Details.cshtml", rule);
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

            int added = 0, skipped = 0;

            foreach (var pid in model.ProductIds)
            {
                if (!parentSet.Contains(pid) || otherSet.Contains(pid))
                { skipped++; continue; }

                if (existingIds.Contains(pid)) continue;

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

            return Ok(new
            {
                success = true,
                added,
                skipped,
                message = $"Przypisano {added} produktów do interwału: {rule.Name}"
                    + (skipped > 0 ? $" (pominięto {skipped})" : "")
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

        // ═══════════════════════════════════════════════════════
        // HELPERY
        // ═══════════════════════════════════════════════════════
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
                    if (day.Any(v => v < 0 || v > 6)) return false;
                }
                return true;
            }
            catch { return false; }
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