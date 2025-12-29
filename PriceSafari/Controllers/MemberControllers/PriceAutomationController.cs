using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Manager")]
    public class PriceAutomationController : Controller
    {
        private readonly PriceSafariContext _context;

        public PriceAutomationController(PriceSafariContext context)
        {
            _context = context;
        }

        [HttpGet]
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
                await PreparePriceComparisonData(rule, model);
            }
            else if (rule.SourceType == AutomationSourceType.Marketplace)
            {
                await PrepareMarketplaceData(rule, model);
            }

            return View("~/Views/ManagerPanel/PriceAutomation/Details.cshtml", model);
        }

        private async Task PreparePriceComparisonData(AutomationRule rule, AutomationDetailsViewModel model)
        {
            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == rule.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            model.LastScrapDate = latestScrap?.Date;

            var assignments = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == rule.Id && a.ProductId.HasValue)
                .Include(a => a.Product)
                .ToListAsync();

            if (!assignments.Any()) return;

            var productIds = assignments.Select(a => a.ProductId.Value).ToList();
            int scrapId = latestScrap?.Id ?? 0;

            var priceHistories = new List<PriceHistoryClass>();
            if (scrapId > 0)
            {
                priceHistories = await _context.PriceHistories
                    .Where(ph => ph.ScrapHistoryId == scrapId && productIds.Contains(ph.ProductId))
                    .ToListAsync();
            }

            var competitorRules = GetCompetitorRulesForComparison(rule.CompetitorPreset);
            string myStoreNameLower = rule.Store.StoreName.ToLower().Trim();

            foreach (var item in assignments)
            {
                var p = item.Product;
                if (p == null) continue;

                var histories = priceHistories.Where(h => h.ProductId == p.ProductId).ToList();

                var myHistory = histories.FirstOrDefault(h => h.StoreName != null && h.StoreName.Contains(rule.Store.StoreName, StringComparison.OrdinalIgnoreCase))
                                ?? histories.FirstOrDefault(h => h.StoreName == null);

                var rawCompetitors = histories.Where(h => h != myHistory && h.Price > 0).ToList();
                var filteredCompetitors = new List<PriceHistoryClass>();

                if (rule.CompetitorPreset != null)
                {
                    bool blockGoogle = !rule.CompetitorPreset.SourceGoogle;
                    bool blockCeneo = !rule.CompetitorPreset.SourceCeneo;

                    foreach (var comp in rawCompetitors)
                    {
                        bool isGoogle = comp.IsGoogle;

                        if (isGoogle && blockGoogle) continue;
                        if (!isGoogle && blockCeneo) continue;

                        // Używamy dedykowanej metody dla PriceComparison (bez rzutowania)
                        if (IsCompetitorAllowedComparison(comp.StoreName, isGoogle, competitorRules, rule.CompetitorPreset.UseUnmarkedStores))
                        {
                            filteredCompetitors.Add(comp);
                        }
                    }
                }
                else
                {
                    filteredCompetitors = rawCompetitors;
                }

                filteredCompetitors = filteredCompetitors.OrderBy(h => h.Price).ToList();
                var bestCompetitor = filteredCompetitors.FirstOrDefault();

                decimal? marketAvg = null;
                if (filteredCompetitors.Any())
                {
                    marketAvg = CalculateMedian(filteredCompetitors.Select(c => c.Price).ToList());
                }

                var row = new AutomationProductRowViewModel
                {
                    ProductId = p.ProductId,
                    Name = p.ProductName,
                    ImageUrl = p.MainUrl,
                    Identifier = p.Ean,
                    CurrentPrice = myHistory?.Price,
                    PurchasePrice = p.MarginPrice,
                    BestCompetitorPrice = bestCompetitor?.Price,
                    CompetitorName = bestCompetitor?.StoreName,
                    MarketAveragePrice = marketAvg,
                    IsInStock = true
                };

                if (myHistory != null && myHistory.Price > 0)
                {
                    var allPrices = filteredCompetitors.Select(c => c.Price).Concat(new[] { myHistory.Price }).OrderBy(x => x).ToList();
                    var myRank = allPrices.IndexOf(myHistory.Price) + 1;
                    row.CurrentRanking = $"{myRank}/{allPrices.Count}";
                }
                else
                {
                    row.CurrentRanking = "-";
                }

                CalculateSuggestedPrice(rule, row);
                model.Products.Add(row);
            }

            model.TotalProducts = model.Products.Count;
        }

        private async Task PrepareMarketplaceData(AutomationRule rule, AutomationDetailsViewModel model)
        {
            var latestScrap = await _context.AllegroScrapeHistories
               .Where(sh => sh.StoreId == rule.StoreId)
               .OrderByDescending(sh => sh.Date)
               .Select(sh => new { sh.Id, sh.Date })
               .FirstOrDefaultAsync();

            model.LastScrapDate = latestScrap?.Date;

            var assignments = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == rule.Id && a.AllegroProductId.HasValue)
                .Include(a => a.AllegroProduct)
                .ToListAsync();

            if (!assignments.Any()) return;

            var productIds = assignments.Select(a => a.AllegroProductId.Value).ToList();
            int scrapId = latestScrap?.Id ?? 0;

            var priceHistories = new List<AllegroPriceHistory>();
            if (scrapId > 0)
            {
                priceHistories = await _context.AllegroPriceHistories
                    .Where(ph => ph.AllegroScrapeHistoryId == scrapId && productIds.Contains(ph.AllegroProductId))
                    .ToListAsync();
            }

            var competitorRules = GetCompetitorRulesForMarketplace(rule.CompetitorPreset);
            string myStoreNameAllegro = rule.Store.StoreNameAllegro;

            foreach (var item in assignments)
            {
                var p = item.AllegroProduct;
                if (p == null) continue;

                var histories = priceHistories.Where(h => h.AllegroProductId == p.AllegroProductId).ToList();

                var myHistory = histories.FirstOrDefault(h => h.SellerName != null && h.SellerName.Equals(myStoreNameAllegro, StringComparison.OrdinalIgnoreCase));

                var rawCompetitors = histories.Where(h => h != myHistory && h.Price > 0).ToList();
                var filteredCompetitors = new List<AllegroPriceHistory>();

                if (rule.CompetitorPreset != null)
                {
                    foreach (var comp in rawCompetitors)
                    {
                        // Używamy dedykowanej metody dla Marketplace (bez rzutowania)
                        if (IsCompetitorAllowedMarketplace(comp.SellerName, competitorRules, rule.CompetitorPreset.UseUnmarkedStores))
                        {
                            filteredCompetitors.Add(comp);
                        }
                    }
                }
                else
                {
                    filteredCompetitors = rawCompetitors;
                }

                filteredCompetitors = filteredCompetitors.OrderBy(h => h.Price).ToList();
                var bestCompetitor = filteredCompetitors.FirstOrDefault();

                decimal? marketAvg = null;
                if (filteredCompetitors.Any())
                {
                    marketAvg = CalculateMedian(filteredCompetitors.Select(c => c.Price).ToList());
                }

                var row = new AutomationProductRowViewModel
                {
                    ProductId = p.AllegroProductId,
                    Name = p.AllegroProductName,
                    ImageUrl = null,
                    Identifier = p.AllegroOfferUrl,
                    CurrentPrice = myHistory?.Price,
                    PurchasePrice = p.AllegroMarginPrice,
                    BestCompetitorPrice = bestCompetitor?.Price,
                    CompetitorName = bestCompetitor?.SellerName,
                    MarketAveragePrice = marketAvg,
                    IsInStock = true
                };

                if (myHistory != null)
                {
                    var allPrices = filteredCompetitors.Select(c => c.Price).Concat(new[] { myHistory.Price }).OrderBy(x => x).ToList();
                    var myRank = allPrices.IndexOf(myHistory.Price) + 1;
                    row.CurrentRanking = $"{myRank}/{allPrices.Count}";
                }
                else
                {
                    row.CurrentRanking = "-";
                }

                CalculateSuggestedPrice(rule, row);
                model.Products.Add(row);
            }

            model.TotalProducts = model.Products.Count;
        }

        // --- Helpery ---

        private Dictionary<(string Store, DataSourceType Source), bool> GetCompetitorRulesForComparison(CompetitorPresetClass preset)
        {
            if (preset == null) return null;
            return preset.CompetitorItems
                .Where(ci => ci.DataSource == DataSourceType.Ceneo || ci.DataSource == DataSourceType.Google)
                .ToDictionary(
                    ci => (Store: ci.StoreName.ToLower().Trim(), Source: ci.DataSource),
                    ci => ci.UseCompetitor
                );
        }

        private Dictionary<string, bool> GetCompetitorRulesForMarketplace(CompetitorPresetClass preset)
        {
            if (preset == null) return null;
            return preset.CompetitorItems
                .Where(ci => ci.DataSource == DataSourceType.Allegro)
                .GroupBy(ci => ci.StoreName.ToLower().Trim())
                .ToDictionary(
                    g => g.Key,
                    g => g.First().UseCompetitor
                );
        }

        // DEDYKOWANA METODA DLA COMPARISON (rozwiązuje problem rzutowania)
        private bool IsCompetitorAllowedComparison(
            string storeName,
            bool isGoogle,
            Dictionary<(string Store, DataSourceType Source), bool> rulesDict,
            bool useUnmarkedStores)
        {
            if (string.IsNullOrEmpty(storeName)) return false;
            string keyName = storeName.ToLower().Trim();

            if (rulesDict == null) return true;

            DataSourceType source = isGoogle ? DataSourceType.Google : DataSourceType.Ceneo;

            if (rulesDict.TryGetValue((keyName, source), out bool useCompetitor))
            {
                return useCompetitor;
            }

            return useUnmarkedStores;
        }

        // DEDYKOWANA METODA DLA MARKETPLACE (rozwiązuje problem rzutowania)
        private bool IsCompetitorAllowedMarketplace(
            string storeName,
            Dictionary<string, bool> rulesDict,
            bool useUnmarkedStores)
        {
            if (string.IsNullOrEmpty(storeName)) return false;
            string keyName = storeName.ToLower().Trim();

            if (rulesDict == null) return true;

            if (rulesDict.TryGetValue(keyName, out bool useCompetitor))
            {
                return useCompetitor;
            }

            return useUnmarkedStores;
        }

        private decimal? CalculateMedian(List<decimal> prices)
        {
            if (prices == null || prices.Count == 0) return null;
            var sortedPrices = prices.OrderBy(x => x).ToList();
            int count = sortedPrices.Count;
            if (count % 2 == 0)
                return (sortedPrices[count / 2 - 1] + sortedPrices[count / 2]) / 2m;
            else
                return sortedPrices[count / 2];
        }

        private void CalculateSuggestedPrice(AutomationRule rule, AutomationProductRowViewModel row)
        {
            decimal suggested = row.CurrentPrice ?? 0;
            bool calculationPossible = false;

            if (rule.StrategyMode == AutomationStrategyMode.Competitiveness)
            {
                if (row.BestCompetitorPrice.HasValue)
                {
                    if (rule.IsPriceStepPercent)
                        suggested = row.BestCompetitorPrice.Value + (row.BestCompetitorPrice.Value * (rule.PriceStep / 100));
                    else
                        suggested = row.BestCompetitorPrice.Value + rule.PriceStep;

                    calculationPossible = true;
                }
            }
            else if (rule.StrategyMode == AutomationStrategyMode.Profit)
            {
                if (row.MarketAveragePrice.HasValue && row.MarketAveragePrice.Value > 0)
                {
                    suggested = row.MarketAveragePrice.Value * (rule.PriceIndexTargetPercent / 100);
                    calculationPossible = true;
                }
            }

            if (!calculationPossible)
            {
                row.SuggestedPrice = null;
                return;
            }

            if (rule.EnforceMinimalMargin && row.PurchasePrice.HasValue)
            {
                decimal minLimit;
                if (rule.IsMinimalMarginPercent)
                    minLimit = row.PurchasePrice.Value + (row.PurchasePrice.Value * (rule.MinimalMarginValue / 100));
                else
                    minLimit = row.PurchasePrice.Value + rule.MinimalMarginValue;

                row.MinPriceLimit = minLimit;

                if (suggested < minLimit)
                {
                    suggested = minLimit;
                    row.IsMarginWarning = true;
                }
            }

            if (rule.EnforceMaxMargin && row.PurchasePrice.HasValue)
            {
                decimal maxLimit;
                if (rule.IsMaxMarginPercent)
                    maxLimit = row.PurchasePrice.Value + (row.PurchasePrice.Value * (rule.MaxMarginValue / 100));
                else
                    maxLimit = row.PurchasePrice.Value + rule.MaxMarginValue;

                row.MaxPriceLimit = maxLimit; // <--- NOWE PRZYPISANIE DO MODELU

                if (suggested > maxLimit)
                {
                    suggested = maxLimit;
                    // Opcjonalnie: można tu dodać flagę ostrzegawczą, jeśli potrzebujesz wiedzieć, że cena została obcięta z góry
                }
            }

            row.SuggestedPrice = Math.Round(suggested, 2);

            if (row.CurrentPrice.HasValue)
            {
                row.PriceChange = row.SuggestedPrice.Value - row.CurrentPrice.Value;
            }
        }


        [HttpPost]
        public async Task<IActionResult> ExecuteAutomation([FromBody] AutomationExecutionRequest request)
        {
            if (request == null || !request.Products.Any()) return BadRequest("Brak danych.");

            // KROK 1: Nie pobieramy Usera, bo to akcja systemowa
            // string userId = _userManager.GetUserId(User); -> USUNIĘTE

            // Pobierz regułę, by mieć pewność co do powiązania
            var rule = await _context.AutomationRules.FindAsync(request.RuleId);
            if (rule == null) return NotFound("Reguła nie istnieje.");

            if (request.SourceType == AutomationSourceType.PriceComparison)
            {
                // Przekazujemy null zamiast userId
                await SavePriceComparisonBatch(request, null, rule);
            }
            else
            {
                // Przekazujemy null zamiast userId
                await SaveMarketplaceBatch(request, null, rule);
            }

            return Ok(new { success = true, count = request.Products.Count });
        }

        private async Task SavePriceComparisonBatch(AutomationExecutionRequest request, string? userId, AutomationRule rule)
        {
            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == request.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            var batch = new PriceBridgeBatch
            {
                StoreId = request.StoreId,
                ScrapHistoryId = latestScrap,
                UserId = userId, // Tutaj trafi NULL
                ExecutionDate = DateTime.Now,
                SuccessfulCount = request.Products.Count,
                ExportMethod = PriceExportMethod.Api,
                IsAutomation = true,        // Flaga, że to automat
                AutomationRuleId = rule.Id, // Powiązanie z regułą (zamiast nazwy użytkownika)
                BridgeItems = new List<PriceBridgeItem>()
            };

            foreach (var p in request.Products)
            {
                batch.BridgeItems.Add(new PriceBridgeItem
                {
                    ProductId = p.ProductId,
                    PriceBefore = p.CurrentPrice,
                    PriceAfter = p.NewPrice,
                    MarginPrice = p.PurchasePrice,
                    RankingGoogleBefore = p.CurrentRanking,
                    RankingGoogleAfterSimulated = p.NewRanking,

                    Mode = rule.StrategyMode.ToString(),
                    PriceIndexTarget = rule.StrategyMode == AutomationStrategyMode.Profit ? rule.PriceIndexTargetPercent : (decimal?)null,
                    StepPriceApplied = rule.StrategyMode == AutomationStrategyMode.Competitiveness ? rule.PriceStep : (decimal?)null,

                    MinPriceLimit = p.MinPriceLimit,
                    MaxPriceLimit = p.MaxPriceLimit,

                    // Przypisanie nullable bool
                    WasLimitedByMin = p.WasLimitedByMin,
                    WasLimitedByMax = p.WasLimitedByMax,

                    Success = true
                });
            }

            _context.PriceBridgeBatches.Add(batch);
            await _context.SaveChangesAsync();
        }

        private async Task SaveMarketplaceBatch(AutomationExecutionRequest request, string? userId, AutomationRule rule)
        {
            var latestScrap = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == request.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => sh.Id)
                .FirstOrDefaultAsync();

            var batch = new AllegroPriceBridgeBatch
            {
                StoreId = request.StoreId,
                AllegroScrapeHistoryId = latestScrap,
                UserId = userId, // Tutaj trafi NULL
                ExecutionDate = DateTime.UtcNow,
                SuccessfulCount = request.Products.Count,
                IsAutomation = true,        // Flaga
                AutomationRuleId = rule.Id  // Powiązanie
            };

            foreach (var p in request.Products)
            {
                batch.BridgeItems.Add(new AllegroPriceBridgeItem
                {
                    AllegroProductId = p.ProductId,
                    AllegroOfferId = p.Identifier,
                    PriceBefore = p.CurrentPrice,
                    PriceAfter_Simulated = p.NewPrice,
                    PriceAfter_Verified = p.NewPrice,
                    MarginPrice = p.PurchasePrice,
                    RankingBefore = p.CurrentRanking,
                    RankingAfter_Simulated = p.NewRanking,

                    Mode = rule.StrategyMode.ToString(),
                    PriceIndexTarget = rule.StrategyMode == AutomationStrategyMode.Profit ? rule.PriceIndexTargetPercent : (decimal?)null,
                    StepPriceApplied = rule.StrategyMode == AutomationStrategyMode.Competitiveness ? rule.PriceStep : (decimal?)null,

                    MinPriceLimit = p.MinPriceLimit,
                    MaxPriceLimit = p.MaxPriceLimit,

                    // Przypisanie nullable bool
                    WasLimitedByMin = p.WasLimitedByMin,
                    WasLimitedByMax = p.WasLimitedByMax,

                    Success = true,
                    IncludeCommissionInMargin = rule.MarketplaceIncludeCommission
                });
            }

            _context.AllegroPriceBridgeBatches.Add(batch);
            await _context.SaveChangesAsync();
        }
    }
}