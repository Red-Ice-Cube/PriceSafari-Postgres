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
    [Authorize(Roles = "Admin, Member")]
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

        private string CalculateRanking(List<decimal> competitors, decimal myPrice)
        {

            var allPrices = new List<decimal>(competitors);

            allPrices.Add(myPrice);

            allPrices.Sort();

            int firstIndex = allPrices.IndexOf(myPrice);
            int lastIndex = allPrices.LastIndexOf(myPrice);

            if (firstIndex == -1) return "-";

            int startRank = firstIndex + 1;
            int endRank = lastIndex + 1;
            int totalCount = allPrices.Count;

            if (startRank == endRank)
            {

                return $"{startRank}/{totalCount}";
            }
            else
            {

                return $"{startRank}-{endRank}/{totalCount}";
            }
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

                string myStoreName = rule.Store.StoreName ?? "";

                var myHistory = histories.FirstOrDefault(h => h.StoreName != null && h.StoreName.Contains(myStoreName, StringComparison.OrdinalIgnoreCase))
                                ?? histories.FirstOrDefault(h => h.StoreName == null);

                var rawCompetitors = histories
                    .Where(h =>
                        h.Price > 0 &&

                        h != myHistory &&

                        (h.StoreName == null || !h.StoreName.Contains(myStoreName, StringComparison.OrdinalIgnoreCase))

                    )
                    .ToList();

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

                var googleCompetitorPrices = filteredCompetitors
                    .Where(c => c.IsGoogle == true && c.Price > 0)
                    .Select(c => c.Price).ToList();

                var ceneoCompetitorPrices = filteredCompetitors
                    .Where(c => (c.IsGoogle == false || c.IsGoogle == null) && c.Price > 0)
                    .Select(c => c.Price).ToList();

                var myGoogleRecord = histories.FirstOrDefault(h => h.StoreName != null && h.StoreName.Contains(rule.Store.StoreName, StringComparison.OrdinalIgnoreCase) && h.IsGoogle == true);
                var myCeneoRecord = histories.FirstOrDefault(h => h.StoreName != null && h.StoreName.Contains(rule.Store.StoreName, StringComparison.OrdinalIgnoreCase) && (h.IsGoogle == false || h.IsGoogle == null));

                string currentRankGoogle = "-";
                string currentRankCeneo = "-";

                if (myGoogleRecord != null && myGoogleRecord.Price > 0)
                {
                    currentRankGoogle = CalculateRanking(new List<decimal>(googleCompetitorPrices), myGoogleRecord.Price);
                }

                if (myCeneoRecord != null && myCeneoRecord.Price > 0)
                {
                    currentRankCeneo = CalculateRanking(new List<decimal>(ceneoCompetitorPrices), myCeneoRecord.Price);
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
                    IsInStock = true,

                    CurrentRankingGoogle = currentRankGoogle,
                    CurrentRankingCeneo = currentRankCeneo,

                    CurrentRankingAllegro = null
                };

                CalculateSuggestedPrice(rule, row);
                CalculateCurrentMarkup(row);

                string newRankGoogle = "-";
                string newRankCeneo = "-";

                if (row.SuggestedPrice.HasValue)
                {
                    decimal newPrice = row.SuggestedPrice.Value;

                    newRankGoogle = CalculateRanking(new List<decimal>(googleCompetitorPrices), newPrice);
                    newRankCeneo = CalculateRanking(new List<decimal>(ceneoCompetitorPrices), newPrice);
                }

                row.NewRankingGoogle = newRankGoogle;
                row.NewRankingCeneo = newRankCeneo;
                row.NewRankingAllegro = null;

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
            int scrapId = latestScrap?.Id ?? 0;

            var assignments = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == rule.Id && a.AllegroProductId.HasValue)
                .Include(a => a.AllegroProduct)
                .ToListAsync();

            if (!assignments.Any()) return;

            var productIds = assignments.Select(a => a.AllegroProductId.Value).ToList();

            var priceHistories = new List<AllegroPriceHistory>();
            var extendedInfos = new List<AllegroPriceHistoryExtendedInfoClass>();

            if (scrapId > 0)
            {

                priceHistories = await _context.AllegroPriceHistories
                    .Where(ph => ph.AllegroScrapeHistoryId == scrapId && productIds.Contains(ph.AllegroProductId))
                    .ToListAsync();

                extendedInfos = await _context.AllegroPriceHistoryExtendedInfos
                    .Where(x => x.ScrapHistoryId == scrapId && productIds.Contains(x.AllegroProductId))
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

                var extInfo = extendedInfos.FirstOrDefault(x => x.AllegroProductId == p.AllegroProductId);

                var rawCompetitors = histories
                    .Where(h =>
                        h.Price > 0 &&
                        h != myHistory &&
                        (h.SellerName == null || !h.SellerName.Equals(myStoreNameAllegro, StringComparison.OrdinalIgnoreCase))

                    )
                    .ToList();

                var filteredCompetitors = new List<AllegroPriceHistory>();

                if (rule.CompetitorPreset != null)
                {
                    foreach (var comp in rawCompetitors)
                    {
                        if (IsCompetitorAllowedMarketplace(comp.SellerName, competitorRules, rule.CompetitorPreset.UseUnmarkedStores))
                            filteredCompetitors.Add(comp);
                    }
                }
                else
                {
                    filteredCompetitors = rawCompetitors;
                }

                var competitorPrices = filteredCompetitors.Select(c => c.Price).ToList();
                filteredCompetitors = filteredCompetitors.OrderBy(h => h.Price).ToList();
                var bestCompetitor = filteredCompetitors.FirstOrDefault();

                decimal? marketAvg = null;
                if (filteredCompetitors.Any()) marketAvg = CalculateMedian(competitorPrices);

                string currentRankAllegro = "-";
                if (myHistory != null && myHistory.Price > 0)
                    currentRankAllegro = CalculateRanking(new List<decimal>(competitorPrices), myHistory.Price);

                var row = new AutomationProductRowViewModel
                {
                    ProductId = p.AllegroProductId,
                    Name = p.AllegroProductName,
                    Identifier = p.AllegroOfferUrl,

                    CurrentPrice = myHistory?.Price,

                    PurchasePrice = p.AllegroMarginPrice,

                    BestCompetitorPrice = bestCompetitor?.Price,
                    CompetitorName = bestCompetitor?.SellerName,
                    MarketAveragePrice = marketAvg,
                    CurrentRankingAllegro = currentRankAllegro,
                    IsInStock = true,

                    CommissionAmount = extInfo?.ApiAllegroCommission,
                    ApiAllegroPriceFromUser = extInfo?.ApiAllegroPriceFromUser,

                    IsInAnyCampaign = extInfo?.AnyPromoActive ?? false,
                    IsSubsidyActive = extInfo?.IsSubsidyActive ?? false,

                    IsBestPriceGuarantee = myHistory?.IsBestPriceGuarantee ?? false,
                    IsSuperPrice = myHistory?.SuperPrice ?? false,
                    IsTopOffer = myHistory?.TopOffer ?? false,
                    CompetitorIsBestPriceGuarantee = bestCompetitor?.IsBestPriceGuarantee ?? false,
                    CompetitorIsSuperPrice = bestCompetitor?.SuperPrice ?? false,
                    CompetitorIsTopOffer = bestCompetitor?.TopOffer ?? false,

                    IsCommissionIncluded = rule.MarketplaceIncludeCommission
                };

                CalculateSuggestedPrice(rule, row);
                CalculateCurrentMarkup(row);

                string newRankAllegro = "-";
                if (row.SuggestedPrice.HasValue)
                {
                    newRankAllegro = CalculateRanking(new List<decimal>(competitorPrices), row.SuggestedPrice.Value);
                }
                row.NewRankingAllegro = newRankAllegro;

                model.Products.Add(row);
            }

            model.TotalProducts = model.Products.Count;
        }

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
            if (rule.SourceType == AutomationSourceType.Marketplace)
            {
                if (row.IsInAnyCampaign && !rule.MarketplaceChangePriceForBadgeInCampaign)
                {
                    ApplyBlock(row, "Aktywna Kampania");
                    return;
                }

                if (row.IsSuperPrice && !rule.MarketplaceChangePriceForBadgeSuperPrice)
                {
                    ApplyBlock(row, "Super Cena");
                    return;
                }

                if (row.IsTopOffer && !rule.MarketplaceChangePriceForBadgeTopOffer)
                {
                    ApplyBlock(row, "Top Oferta");
                    return;
                }

                if (row.IsBestPriceGuarantee && !rule.MarketplaceChangePriceForBadgeBestPriceGuarantee)
                {
                    ApplyBlock(row, "Gwarancja Ceny");
                    return;
                }
            }

            // NOWE: Sprawdzenie krytyczne - jeśli reguła wymaga marży, a nie ma ceny zakupu -> BLOKADA
            if ((rule.EnforceMinimalMargin || rule.EnforceMaxMargin) && (!row.PurchasePrice.HasValue || row.PurchasePrice <= 0))
            {
                ApplyBlock(row, "Brak ceny zakupu");
                return;
            }

            decimal basePrice = row.ApiAllegroPriceFromUser ?? row.CurrentPrice ?? 0;

            if (basePrice == 0)
            {
                row.SuggestedPrice = null;
                // NOWE: Ustawiamy status Blocked zamiast IsTargetMet = false
                row.Status = AutomationCalculationStatus.Blocked;
                row.BlockReason = "Brak ceny obecnej";
                return;
            }

            decimal suggested = basePrice;
            bool calculationPossible = false;

            if (rule.StrategyMode == AutomationStrategyMode.Competitiveness)
            {
                if (row.BestCompetitorPrice.HasValue)
                {
                    decimal targetVisiblePrice;
                    if (rule.IsPriceStepPercent)
                        targetVisiblePrice = row.BestCompetitorPrice.Value + (row.BestCompetitorPrice.Value * (rule.PriceStep / 100));
                    else
                        targetVisiblePrice = row.BestCompetitorPrice.Value + rule.PriceStep;

                    decimal subsidyAmount = (row.ApiAllegroPriceFromUser.HasValue && row.CurrentPrice.HasValue)
                                            ? row.ApiAllegroPriceFromUser.Value - row.CurrentPrice.Value
                                            : 0;

                    suggested = targetVisiblePrice + subsidyAmount;

                    calculationPossible = true;
                }
            }
            else if (rule.StrategyMode == AutomationStrategyMode.Profit)
            {
                if (row.MarketAveragePrice.HasValue && row.MarketAveragePrice.Value > 0)
                {
                    decimal targetVisiblePrice = row.MarketAveragePrice.Value * (rule.PriceIndexTargetPercent / 100);

                    decimal subsidyAmount = (row.ApiAllegroPriceFromUser.HasValue && row.CurrentPrice.HasValue)
                                            ? row.ApiAllegroPriceFromUser.Value - row.CurrentPrice.Value
                                            : 0;

                    suggested = targetVisiblePrice + subsidyAmount;
                    calculationPossible = true;
                }
            }

            if (!calculationPossible)
            {
                row.SuggestedPrice = null;
                // NOWE: Ustawiamy status Blocked
                row.Status = AutomationCalculationStatus.Blocked;
                row.BlockReason = "Brak danych do wyliczenia";
                return;
            }

            decimal idealPrice = suggested;

            decimal extraCost = 0;
            if (rule.SourceType == AutomationSourceType.Marketplace &&
                rule.MarketplaceIncludeCommission &&
                row.CommissionAmount.HasValue)
            {
                extraCost = row.CommissionAmount.Value;
            }

            // NOWE: Flaga sprawdzająca, czy cena została ograniczona przez widełki
            bool wasLimited = false;

            if (rule.EnforceMinimalMargin && row.PurchasePrice.HasValue)
            {
                decimal minLimit;
                if (rule.IsMinimalMarginPercent)
                    minLimit = row.PurchasePrice.Value + (row.PurchasePrice.Value * (rule.MinimalMarginValue / 100)) + extraCost;
                else
                    minLimit = row.PurchasePrice.Value + rule.MinimalMarginValue + extraCost;

                row.MinPriceLimit = minLimit;

                if (suggested < minLimit)
                {
                    suggested = minLimit;
                    row.IsMarginWarning = true;
                    wasLimited = true; // Oznaczamy, że uderzyliśmy w podłogę
                }
            }

            if (rule.EnforceMaxMargin && row.PurchasePrice.HasValue)
            {
                decimal maxLimit;
                if (rule.IsMaxMarginPercent)
                    maxLimit = row.PurchasePrice.Value + (row.PurchasePrice.Value * (rule.MaxMarginValue / 100)) + extraCost;
                else
                    maxLimit = row.PurchasePrice.Value + rule.MaxMarginValue + extraCost;

                row.MaxPriceLimit = maxLimit;

                if (suggested > maxLimit)
                {
                    suggested = maxLimit;
                    wasLimited = true; // Oznaczamy, że uderzyliśmy w sufit
                }
            }

            row.SuggestedPrice = Math.Round(suggested, 2);

            decimal finalSuggested = row.SuggestedPrice.Value;
            decimal finalBase = Math.Round(basePrice, 2);

            // Obliczamy zmianę
            row.PriceChange = Math.Round(finalSuggested - finalBase, 2);

            // NOWE: Logika przypisywania statusu (Żółty vs Niebieski vs Zielony)
            if (wasLimited)
            {
                // Cena zmieniona/ustalona, ale ograniczona przez limity -> ŻÓŁTY
                row.Status = AutomationCalculationStatus.PriceLimited;
            }
            else if (row.PriceChange == 0)
            {
                // Cena wyliczona jest idealna i równa obecnej -> NIEBIESKI
                row.Status = AutomationCalculationStatus.TargetMaintained;
            }
            else
            {
                // Cena wyliczona jest inna i mieści się w limitach -> ZIELONY
                row.Status = AutomationCalculationStatus.TargetMet;
            }

            CalculateMarkup(row);
        }

        private void CalculateMarkup(AutomationProductRowViewModel row)
        {
            if (row.SuggestedPrice.HasValue && row.PurchasePrice.HasValue && row.PurchasePrice.Value > 0)
            {

                decimal sellPrice = row.SuggestedPrice.Value;
                decimal purchase = row.PurchasePrice.Value;
                decimal commissionCost = 0;

                if (row.IsCommissionIncluded && row.CommissionAmount.HasValue)
                {
                    commissionCost = row.CommissionAmount.Value;
                }

                row.MarkupAmount = sellPrice - purchase - commissionCost;
                row.MarkupPercent = (row.MarkupAmount.Value / purchase) * 100;
            }
        }

        private void CalculateCurrentMarkup(AutomationProductRowViewModel row)
        {

            if (row.CurrentPrice.HasValue && row.PurchasePrice.HasValue && row.PurchasePrice.Value > 0)
            {
                decimal sellPrice = row.CurrentPrice.Value;
                decimal purchase = row.PurchasePrice.Value;
                decimal commissionCost = 0;

                if (row.IsCommissionIncluded && row.CommissionAmount.HasValue)
                {
                    commissionCost = row.CommissionAmount.Value;
                }

                row.CurrentMarkupAmount = sellPrice - purchase - commissionCost;
                row.CurrentMarkupPercent = (row.CurrentMarkupAmount.Value / purchase) * 100;
            }
        }

        private void ApplyBlock(AutomationProductRowViewModel row, string reason)
        {
            // Ustawiamy status na BLOCKED (Czerwony) - to automatycznie ustawi flagi pomocnicze w modelu
            row.Status = AutomationCalculationStatus.Blocked;

            row.BlockReason = reason;

            // W przypadku blokady sugerowana cena to cena obecna (brak zmian)
            row.SuggestedPrice = row.ApiAllegroPriceFromUser ?? row.CurrentPrice;

            row.PriceChange = 0;

            CalculateMarkup(row);
        }

        [HttpPost]
        public async Task<IActionResult> ExecuteAutomation([FromBody] AutomationExecutionRequest request)
        {
            if (request == null || !request.Products.Any()) return BadRequest("Brak danych.");

            var rule = await _context.AutomationRules.FindAsync(request.RuleId);
            if (rule == null) return NotFound("Reguła nie istnieje.");

            if (request.SourceType == AutomationSourceType.PriceComparison)
            {

                await SavePriceComparisonBatch(request, null, rule);
            }
            else
            {

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

            int totalCount = request.Products.Count;
            int metCount = request.Products.Count(p => p.IsTargetMet);
            int unmetCount = request.Products.Count(p => !p.IsTargetMet);

            var batch = new PriceBridgeBatch
            {
                StoreId = request.StoreId,
                ScrapHistoryId = latestScrap,
                UserId = userId,
                ExecutionDate = DateTime.Now,

                SuccessfulCount = totalCount,

                TotalProductsCount = totalCount,
                TargetMetCount = metCount,
                TargetUnmetCount = unmetCount,

                ExportMethod = PriceExportMethod.Api,
                IsAutomation = true,
                AutomationRuleId = rule.Id,
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
                    RankingGoogleBefore = p.CurrentRankingGoogle,
                    RankingCeneoBefore = p.CurrentRankingCeneo,
                    RankingGoogleAfterSimulated = p.NewRankingGoogle,
                    RankingCeneoAfterSimulated = p.NewRankingCeneo,
                    Mode = rule.StrategyMode.ToString(),
                    PriceIndexTarget = rule.StrategyMode == AutomationStrategyMode.Profit ? rule.PriceIndexTargetPercent : (decimal?)null,
                    StepPriceApplied = rule.StrategyMode == AutomationStrategyMode.Competitiveness ? rule.PriceStep : (decimal?)null,
                    MinPriceLimit = p.MinPriceLimit,
                    MaxPriceLimit = p.MaxPriceLimit,
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

            int totalCount = request.Products.Count;
            int metCount = request.Products.Count(p => p.IsTargetMet);
            int unmetCount = request.Products.Count(p => !p.IsTargetMet);

            var batch = new AllegroPriceBridgeBatch
            {
                StoreId = request.StoreId,
                AllegroScrapeHistoryId = latestScrap,
                UserId = userId,
                ExecutionDate = DateTime.UtcNow,

                SuccessfulCount = totalCount,

                TotalProductsCount = totalCount,
                TargetMetCount = metCount,
                TargetUnmetCount = unmetCount,

                IsAutomation = true,
                AutomationRuleId = rule.Id,
                BridgeItems = new List<AllegroPriceBridgeItem>()

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
                    RankingBefore = p.CurrentRankingAllegro,
                    RankingAfter_Simulated = p.NewRankingAllegro,
                    Mode = rule.StrategyMode.ToString(),
                    PriceIndexTarget = rule.StrategyMode == AutomationStrategyMode.Profit ? rule.PriceIndexTargetPercent : (decimal?)null,
                    StepPriceApplied = rule.StrategyMode == AutomationStrategyMode.Competitiveness ? rule.PriceStep : (decimal?)null,
                    MinPriceLimit = p.MinPriceLimit,
                    MaxPriceLimit = p.MaxPriceLimit,
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