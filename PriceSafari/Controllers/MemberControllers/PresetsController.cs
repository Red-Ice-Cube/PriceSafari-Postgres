using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using PriceSafari.ViewModels;
using System.Security.Claims;
using Newtonsoft.Json;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Manager, Member")]
    [ApiController]
    [Route("api/[controller]")]
    public class PresetsController : ControllerBase
    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;

        public PresetsController(PriceSafariContext context, UserManager<PriceSafariUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        private async Task<bool> UserHasAccessToStore(int storeId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            var isAdminOrManager = await _userManager.IsInRoleAsync(user, "Admin") || await _userManager.IsInRoleAsync(user, "Manager");

            if (!isAdminOrManager)
            {
                var hasAccess = await _context.UserStores.AnyAsync(us => us.UserId == userId && us.StoreId == storeId);
                return hasAccess;
            }

            return true;
        }

        [HttpGet("list/{storeId}")]
        public async Task<IActionResult> GetPresets(int storeId, [FromQuery] PresetType? type)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return BadRequest("Nie ma takiego sklepu lub brak dostępu.");
            }

            var query = _context.CompetitorPresets.Where(p => p.StoreId == storeId);

            if (type.HasValue)
            {
                query = query.Where(p => p.Type == type.Value);
            }

            var presets = await query.Select(p => new {
                p.PresetId,
                p.PresetName,
                p.NowInUse
            }).ToListAsync();

            return Ok(presets);
        }

        [HttpGet("details/{presetId}")]
        public async Task<IActionResult> GetPresetDetails(int presetId)
        {
            var preset = await _context.CompetitorPresets
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.PresetId == presetId);

            if (preset == null)
                return NotFound("Preset nie istnieje.");

            if (!await UserHasAccessToStore(preset.StoreId))
                return BadRequest("Brak dostępu do sklepu.");

            var result = new
            {
                presetId = preset.PresetId,
                presetName = preset.PresetName,
                type = preset.Type,
                nowInUse = preset.NowInUse,
                sourceGoogle = preset.SourceGoogle,
                sourceCeneo = preset.SourceCeneo,
                useUnmarkedStores = preset.UseUnmarkedStores,
                competitorItems = preset.CompetitorItems
                    .Select(ci => new
                    {
                        ci.StoreName,
                        ci.DataSource,
                        ci.UseCompetitor
                    }).ToList()
            };

            return Ok(result);
        }

        [HttpGet("competitor-data/{storeId}")]
        public async Task<IActionResult> GetCompetitorStoresData(int storeId, string ourSource = "All")
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return BadRequest(new { error = "Nie ma takiego sklepu" });
            }

            var storeName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreName)
                .FirstOrDefaultAsync();

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            if (latestScrap == null)
            {
                return Ok(new { data = new List<object>() });
            }

            var basePricesQuery = _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == latestScrap.Id);

            switch (ourSource?.ToLower())
            {
                case "google":
                    basePricesQuery = basePricesQuery.Where(ph => ph.IsGoogle);
                    break;
                case "ceneo":

                    basePricesQuery = basePricesQuery.Where(ph => !ph.IsGoogle);
                    break;
            }

            var myProductIds = await basePricesQuery
                .Where(ph => ph.StoreName.ToLower() == storeName.ToLower())
                .Select(ph => ph.ProductId)
                .Distinct()
                .ToListAsync();

            if (!myProductIds.Any())
            {

                var storeCounts = await basePricesQuery
                    .GroupBy(ph => new { ph.StoreName, ph.IsGoogle })
                    .Select(g => new
                    {
                        StoreName = g.Key.StoreName,
                        DataSource = g.Key.IsGoogle ? "Google" : "Ceneo",
                        CommonProductsCount = g.Count()
                    })
                    .OrderByDescending(s => s.CommonProductsCount)
                    .ToListAsync();

                return Ok(new { data = storeCounts, analysisType = "fullScan" });
            }
            else
            {

                var competitorPrices = await basePricesQuery
                    .Where(ph => ph.StoreName.ToLower() != storeName.ToLower())
                    .ToListAsync();

                var competitors = competitorPrices
                    .GroupBy(ph => new { NormalizedName = ph.StoreName.ToLower(), ph.IsGoogle })
                    .Select(g =>
                    {
                        var storeNameInGroup = g.First().StoreName;
                        bool isGoogle = g.Key.IsGoogle;

                        var competitorProductIds = g
                            .Select(x => x.ProductId)
                            .Distinct();

                        int commonProductsCount = myProductIds
                            .Count(pid => competitorProductIds.Contains(pid));

                        return new
                        {
                            StoreName = storeNameInGroup,
                            DataSource = isGoogle ? "Google" : "Ceneo",
                            CommonProductsCount = commonProductsCount
                        };
                    })
                    .Where(c => c.CommonProductsCount >= 1)
                    .OrderByDescending(c => c.CommonProductsCount)
                    .ToList();

                return Ok(new { data = competitors, analysisType = "commonProducts" });
            }
        }




        // W pliku: PresetsController.cs

        [HttpGet("allegro-competitors/{storeId}")]
        public async Task<IActionResult> GetAllegroCompetitorsData(int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return BadRequest(new { error = "Nie ma takiego sklepu lub brak dostępu." });
            }

            // Pobierz nazwę sprzedawcy Allegro dla danego sklepu
            var storeAllegroName = await _context.Stores
                .Where(s => s.StoreId == storeId)
                .Select(s => s.StoreNameAllegro)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(storeAllegroName))
            {
                return BadRequest(new { error = "Sklep nie ma skonfigurowanej nazwy sprzedawcy Allegro." });
            }

            // Znajdź ostatnie skanowanie Allegro
            var latestScrap = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == storeId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            if (latestScrap == 0)
            {
                return Ok(new { data = new List<object>() });
            }

            // Pobierz wszystkie oferty z ostatniego skanowania
            var allOffers = await _context.AllegroPriceHistories
                .Where(aph => aph.AllegroScrapeHistoryId == latestScrap)
                .Select(aph => new { aph.SellerName, aph.AllegroProductId })
                .ToListAsync();

            // Wyciągnij ID produktów, które oferuje nasz sklep
            var myProductIds = allOffers
                .Where(o => o.SellerName.Equals(storeAllegroName, StringComparison.OrdinalIgnoreCase))
                .Select(o => o.AllegroProductId)
                .ToHashSet();

            // Policz wspólne produkty dla każdego konkurenta
            var competitors = allOffers
                .Where(o => !o.SellerName.Equals(storeAllegroName, StringComparison.OrdinalIgnoreCase))
                .GroupBy(o => o.SellerName)
                .Select(g => new
                {
                    StoreName = g.Key,
                    DataSource = "Allegro", // Zwracamy string, tak jak w GetCompetitorStoresData
                    CommonProductsCount = g.Select(o => o.AllegroProductId).Distinct().Count(pid => myProductIds.Contains(pid))
                })
                .Where(c => c.CommonProductsCount > 0)
                .OrderByDescending(c => c.CommonProductsCount)
                .ToList();

            return Ok(new { data = competitors });
        }



        [HttpPost("save")]
        public async Task<IActionResult> SaveOrUpdatePreset([FromBody] CompetitorPresetViewModel model)
        {
            if (!await UserHasAccessToStore(model.StoreId))
            {
                return BadRequest("Brak dostępu do sklepu.");
            }

            CompetitorPresetClass preset;
            if (model.PresetId == 0)
            {
                preset = new CompetitorPresetClass
                {
                    StoreId = model.StoreId,
                    Type = model.Type
                };
                _context.CompetitorPresets.Add(preset);
            }
            else
            {
                preset = await _context.CompetitorPresets
                    .Include(p => p.CompetitorItems)
                    .FirstOrDefaultAsync(p => p.PresetId == model.PresetId);

                if (preset == null) return BadRequest("Taki preset nie istnieje.");
                if (preset.StoreId != model.StoreId) return BadRequest("Błędny storeId dla tego presetu.");
            }

            preset.PresetName = string.IsNullOrWhiteSpace(model.PresetName) ? "No Name" : model.PresetName.Trim();

            if (model.NowInUse)
            {

                var others = await _context.CompetitorPresets
                    .Where(p => p.StoreId == model.StoreId && p.Type == model.Type && p.PresetId != preset.PresetId && p.NowInUse)
                    .ToListAsync();
                foreach (var o in others) o.NowInUse = false;
            }
            preset.NowInUse = model.NowInUse;

            preset.SourceGoogle = model.SourceGoogle;
            preset.SourceCeneo = model.SourceCeneo;
            preset.UseUnmarkedStores = model.UseUnmarkedStores;

            preset.CompetitorItems.Clear();
            if (model.Competitors != null)
            {
                foreach (var c in model.Competitors)
                {
                    preset.CompetitorItems.Add(new CompetitorPresetItem
                    {
                        StoreName = c.StoreName,

                        DataSource = c.DataSource,
                        UseCompetitor = c.UseCompetitor
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true, presetId = preset.PresetId });
        }

        [HttpPost("deactivate-all/{storeId}")]
        public async Task<IActionResult> DeactivateAllPresets([FromRoute] int storeId)
        {
            if (!await UserHasAccessToStore(storeId))
            {
                return BadRequest("Brak dostępu do sklepu.");
            }

            var activePresets = await _context.CompetitorPresets
                .Where(p => p.StoreId == storeId && p.NowInUse)
                .ToListAsync();

            foreach (var preset in activePresets)
            {
                preset.NowInUse = false;
            }

            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        [HttpPost("delete/{presetId}")]
        public async Task<IActionResult> DeletePreset([FromRoute] int presetId)
        {
            var preset = await _context.CompetitorPresets
                .Include(p => p.CompetitorItems)
                .FirstOrDefaultAsync(p => p.PresetId == presetId);

            if (preset == null)
                return NotFound("Preset nie istnieje.");

            if (!await UserHasAccessToStore(preset.StoreId))
                return BadRequest("Brak dostępu do sklepu.");

            _context.CompetitorPresets.Remove(preset);

            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }
    }
}