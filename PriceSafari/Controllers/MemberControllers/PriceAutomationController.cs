// --- 1. DODAJ TE OBLICZENIA (Kopiuj z sekcji Marketplace) ---

// -------------------------------------------------------------

// --- 2. ZAKTUALIZUJ WYWOŁANIE SERWISU ---

// Przekazujemy obliczone statystyki:

// lub "-"

// lub "-"

// --- FIX: Obliczamy nowy ranking TYLKO jeśli źródło jest włączone ---

// --- DODAJ TEN FRAGMENT (to naprawia problem) ---

// Sprawdzamy czy nowa wyliczona cena różni się od tej już wgranej

// ------------------------------------------------

// Wywołujemy główny silnik obliczeniowy (ten sam, co przy ExecuteAutomation)

// Przepisujemy wyniki do modelu widoku

// Ustawiamy daty ostatniego scrapingu

// Dla Allegro bierzemy zweryfikowaną cenę (jeśli API potwierdziło zmianę) lub symulowaną

// NOWOŚĆ: Sprawdzamy rozbieżność

//private async Task SavePriceComparisonBatch(AutomationExecutionRequest request, string? userId, AutomationRule rule)

//{

//}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PriceSafari.Attributes;
using PriceSafari.Data;
using PriceSafari.Enums;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using PriceSafari.Services.PriceAutomationService;

using System;

using System.Security.Claims;
using System.Threading.Tasks;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Member")]
    public class PriceAutomationController : Controller
    {
        private readonly PriceSafariContext _context;

        private readonly PriceAutomationService _automationService;

        public PriceAutomationController(
            PriceSafariContext context,
            PriceAutomationService automationService)
        {
            _context = context;
            _automationService = automationService;
        }

        [HttpGet]
        [RequireUserAccess(UserAccessRequirement.ViewPriceAutomation)]
        public async Task<IActionResult> Details(int id)
        {

            var rule = await _context.AutomationRules
                .Include(r => r.Store)
                .Include(r => r.CompetitorPreset)
                    .ThenInclude(cp => cp.CompetitorItems)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (rule == null) return NotFound("Nie znaleziono reguły.");

            var model = new AutomationDetailsViewModel
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                RuleColor = rule.ColorHex,
                SourceType = rule.SourceType,
                StrategyMode = rule.StrategyMode,
                IsActive = rule.IsActive,
                StoreId = rule.StoreId,
                StoreName = rule.Store?.StoreName ?? "Nieznany sklep"
            };

            if (rule.SourceType == AutomationSourceType.PriceComparison)
            {
                await _automationService.PreparePriceComparisonData(rule, model);
            }
            else if (rule.SourceType == AutomationSourceType.Marketplace)
            {
                await _automationService.PrepareMarketplaceData(rule, model);
            }

            return View("~/Views/Panel/PriceAutomation/Details.cshtml", model);
        }

        [HttpPost]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> ExecuteAutomation([FromBody] AutomationTriggerRequest request)
        {

            if (request == null || request.RuleId <= 0) return BadRequest("Nieprawidłowe żądanie.");

            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var result = await _automationService.ExecuteAutomationAsync(request.RuleId, userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetAutomationHistory([FromBody] HistoryRequest request)
        {

            if (request == null || request.RuleId <= 0) return BadRequest();

            var result = await _automationService.GetAutomationHistoryAsync(request.RuleId, request.Limit);

            if (result == null) return NotFound();
            return Ok(result);
        }
    }
}