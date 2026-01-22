
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Attributes;
using PriceSafari.Data;
using PriceSafari.Enums;
using PriceSafari.Models;
using PriceSafari.Models.DTOs;

using PriceSafari.Models.ViewModels;
using PriceSafari.Services.AllegroServices;
using PriceSafari.Services.ScheduleService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PriceSafari.Controllers.MemberControllers
{
    [Authorize(Roles = "Admin, Member")]
    public class PriceAutomationController : Controller
    {
        private readonly PriceSafariContext _context;
        private readonly AllegroPriceBridgeService _allegroBridgeService;

        private readonly StorePriceBridgeService _storePriceBridgeService;

        public PriceAutomationController(
            PriceSafariContext context,
            AllegroPriceBridgeService allegroBridgeService,
            StorePriceBridgeService storePriceBridgeService)

        {
            _context = context;
            _allegroBridgeService = allegroBridgeService;
            _storePriceBridgeService = storePriceBridgeService;

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
                await PreparePriceComparisonData(rule, model);
            }
            else if (rule.SourceType == AutomationSourceType.Marketplace)
            {

                var calculationResult = await GetCalculatedMarketplaceData(rule);

                model.Products = calculationResult.Products;
                model.TotalProducts = calculationResult.Products.Count;
                model.LastScrapDate = calculationResult.ScrapDate;
                model.LatestScrapId = calculationResult.ScrapId;
            }

            return View("~/Views/Panel/PriceAutomation/Details.cshtml", model);
        }

        [HttpPost]
        [RequireUserAccess(UserAccessRequirement.EditPriceAutomation)]
        public async Task<IActionResult> ExecuteAutomation([FromBody] AutomationTriggerRequest request)
        {
            if (request == null || request.RuleId <= 0) return BadRequest("Nieprawidłowe żądanie.");

            var rule = await _context.AutomationRules
                .Include(r => r.Store)
                .Include(r => r.CompetitorPreset)
                    .ThenInclude(cp => cp.CompetitorItems)
                .FirstOrDefaultAsync(r => r.Id == request.RuleId);

            if (rule == null) return NotFound("Reguła nie istnieje.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (rule.SourceType == AutomationSourceType.PriceComparison)
            {

                var calcResult = await GetCalculatedComparisonData(rule);

                if (calcResult.ScrapId == 0) return BadRequest("Brak danych historycznych (ScrapHistory).");

                int totalProductsInRule = calcResult.Products.Count;

                var itemsToBridge = new List<PriceBridgeItemRequest>();

                foreach (var row in calcResult.Products)
                {

                    if (row.Status == AutomationCalculationStatus.Blocked || !row.SuggestedPrice.HasValue)
                        continue;

                    itemsToBridge.Add(new PriceBridgeItemRequest
                    {
                        ProductId = row.ProductId,

                        CurrentPrice = row.CurrentPrice ?? 0,
                        NewPrice = row.SuggestedPrice.Value,
                        MarginPrice = row.PurchasePrice,

                        CurrentGoogleRanking = row.CurrentRankingGoogle,
                        CurrentCeneoRanking = row.CurrentRankingCeneo,
                        NewGoogleRanking = row.NewRankingGoogle,
                        NewCeneoRanking = row.NewRankingCeneo,

                        Mode = rule.StrategyMode.ToString(),
                        PriceIndexTarget = rule.StrategyMode == AutomationStrategyMode.Profit ? rule.PriceIndexTargetPercent : (decimal?)null,
                        StepPriceApplied = rule.StrategyMode == AutomationStrategyMode.Competitiveness ? rule.PriceStep : (decimal?)null
                    });
                }

                var result = await _storePriceBridgeService.ExecuteStorePriceChangesAsync(
                    storeId: rule.StoreId,
                    scrapHistoryId: calcResult.ScrapId,
                    userId: userId,
                    itemsToBridge: itemsToBridge,
                    isAutomation: true,

                    automationRuleId: rule.Id

                );

                return Ok(new { success = true, count = result.SuccessfulCount, details = result });
            }
            else if (rule.SourceType == AutomationSourceType.Marketplace)
            {

                var calcResult = await GetCalculatedMarketplaceData(rule);

                if (calcResult.ScrapId == 0) return BadRequest("Brak danych historycznych.");

                int totalProductsInRule = calcResult.Products.Count;

                int metCount = calcResult.Products.Count(p =>
                    p.Status == AutomationCalculationStatus.TargetMet ||
                    p.Status == AutomationCalculationStatus.TargetMaintained ||
                    p.Status == AutomationCalculationStatus.PriceLimited);

                int unmetCount = calcResult.Products.Count(p =>
                    p.Status == AutomationCalculationStatus.Blocked);

                int increasedCount = calcResult.Products.Count(p =>
                    p.Status != AutomationCalculationStatus.Blocked && p.PriceChange > 0);

                int decreasedCount = calcResult.Products.Count(p =>
                    p.Status != AutomationCalculationStatus.Blocked && p.PriceChange < 0);

                int maintainedCount = calcResult.Products.Count(p =>
                    p.Status == AutomationCalculationStatus.TargetMaintained);

                var itemsToBridge = new List<AllegroPriceBridgeItemRequest>();

                foreach (var row in calcResult.Products)
                {
                    if (row.Status == AutomationCalculationStatus.Blocked || !row.SuggestedPrice.HasValue)
                        continue;

                    itemsToBridge.Add(new AllegroPriceBridgeItemRequest
                    {

                        ProductId = row.ProductId,
                        OfferId = row.Identifier,
                        MarginPrice = row.PurchasePrice,
                        IncludeCommissionInMargin = rule.MarketplaceIncludeCommission,
                        PriceBefore = row.CurrentPrice ?? 0,
                        CommissionBefore = row.CommissionAmount,
                        RankingBefore = row.CurrentRankingAllegro,
                        PriceAfter_Simulated = row.SuggestedPrice.Value,
                        RankingAfter_Simulated = row.NewRankingAllegro,
                        Mode = rule.StrategyMode.ToString(),
                        PriceIndexTarget = rule.StrategyMode == AutomationStrategyMode.Profit ? rule.PriceIndexTargetPercent : (decimal?)null,
                        StepPriceApplied = rule.StrategyMode == AutomationStrategyMode.Competitiveness ? rule.PriceStep : (decimal?)null,
                        MinPriceLimit = row.MinPriceLimit,
                        MaxPriceLimit = row.MaxPriceLimit,
                        WasLimitedByMin = (row.Status == AutomationCalculationStatus.PriceLimited && row.SuggestedPrice == row.MinPriceLimit),
                        WasLimitedByMax = (row.Status == AutomationCalculationStatus.PriceLimited && row.SuggestedPrice == row.MaxPriceLimit)
                    });
                }

                var result = await _allegroBridgeService.ExecutePriceChangesAsync(
                    storeId: rule.StoreId,
                    allegroScrapeHistoryId: calcResult.ScrapId,
                    userId: userId,
                    includeCommissionInMargin: rule.MarketplaceIncludeCommission,
                    itemsToBridge: itemsToBridge,
                    isAutomation: true,
                    automationRuleId: rule.Id,
                    totalProductsInRule: totalProductsInRule,
                    targetMetCount: metCount,
                    targetUnmetCount: unmetCount,

                    priceIncreasedCount: increasedCount,
                    priceDecreasedCount: decreasedCount,
                    priceMaintainedCount: maintainedCount
                );

                return Ok(new { success = true, count = result.SuccessfulCount });
            }

            return BadRequest("Nieznany typ źródła.");
        }

        private async Task<(List<AutomationProductRowViewModel> Products, int ScrapId, DateTime? ScrapDate)> GetCalculatedComparisonData(AutomationRule rule)
        {
            var resultProducts = new List<AutomationProductRowViewModel>();

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == rule.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            if (latestScrap == null) return (resultProducts, 0, null);

            int scrapId = latestScrap.Id;

            var committedChanges = await _context.PriceBridgeItems
                .Include(i => i.Batch)
                .Where(i => i.Batch.StoreId == rule.StoreId
                            && i.Batch.ScrapHistoryId == scrapId
                            && i.Success)
                .ToListAsync();

            var committedLookup = committedChanges
                .GroupBy(i => i.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.Batch.ExecutionDate).First()
                );

            var assignments = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == rule.Id && a.ProductId.HasValue)
                .Include(a => a.Product)
                .ToListAsync();

            if (!assignments.Any()) return (resultProducts, scrapId, latestScrap.Date);

            var productIds = assignments.Select(a => a.ProductId.Value).ToList();

            var priceHistories = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == scrapId && productIds.Contains(ph.ProductId))
                .ToListAsync();

            var competitorRules = GetCompetitorRulesForComparison(rule.CompetitorPreset);
            string myStoreName = rule.Store.StoreName ?? "";


            bool includeGoogle = rule.CompetitorPreset == null || rule.CompetitorPreset.SourceGoogle;
            bool includeCeneo = rule.CompetitorPreset == null || rule.CompetitorPreset.SourceCeneo;

            foreach (var item in assignments)
            {
                var p = item.Product;
                if (p == null) continue;

                var histories = priceHistories.Where(h => h.ProductId == p.ProductId).ToList();

                var myHistory = histories.FirstOrDefault(h => h.StoreName != null && h.StoreName.Contains(myStoreName, StringComparison.OrdinalIgnoreCase))
                                ?? histories.FirstOrDefault(h => h.StoreName == null);

                var rawCompetitors = histories
                    .Where(h => h.Price > 0 && h != myHistory && (h.StoreName == null || !h.StoreName.Contains(myStoreName, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                var filteredCompetitors = new List<PriceHistoryClass>();

                if (rule.CompetitorPreset != null)
                {
                    bool blockGoogle = !rule.CompetitorPreset.SourceGoogle;
                    bool blockCeneo = !rule.CompetitorPreset.SourceCeneo;

                    foreach (var comp in rawCompetitors)
                    {
                        bool isGoogle = comp.IsGoogle == true;
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

                filteredCompetitors = filteredCompetitors.OrderBy(c => c.Price).ToList();
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
                    IsCommissionIncluded = false,
                    PurchasePriceUpdatedDate = p.MarginPriceUpdatedDate
                };

                var googlePrices = filteredCompetitors.Where(c => c.IsGoogle == true).Select(c => c.Price).ToList();
                var ceneoPrices = filteredCompetitors.Where(c => c.IsGoogle != true).Select(c => c.Price).ToList();

                var myGoogle = histories.FirstOrDefault(h => h.StoreName != null && h.StoreName.Contains(myStoreName, StringComparison.OrdinalIgnoreCase) && h.IsGoogle == true);
                var myCeneo = histories.FirstOrDefault(h => h.StoreName != null && h.StoreName.Contains(myStoreName, StringComparison.OrdinalIgnoreCase) && h.IsGoogle != true);

                if (includeGoogle && myGoogle != null && myGoogle.Price > 0)
                    row.CurrentRankingGoogle = CalculateRanking(new List<decimal>(googlePrices), myGoogle.Price);
                else
                    row.CurrentRankingGoogle = null; // lub "-"

                if (includeCeneo && myCeneo != null && myCeneo.Price > 0)
                    row.CurrentRankingCeneo = CalculateRanking(new List<decimal>(ceneoPrices), myCeneo.Price);
                else
                    row.CurrentRankingCeneo = null; // lub "-"

                CalculateSuggestedPrice(rule, row);
                CalculateCurrentMarkup(row);

                if (row.SuggestedPrice.HasValue)
                {
                    // --- FIX: Obliczamy nowy ranking TYLKO jeśli źródło jest włączone ---
                    if (includeGoogle)
                        row.NewRankingGoogle = CalculateRanking(new List<decimal>(googlePrices), row.SuggestedPrice.Value);

                    if (includeCeneo)
                        row.NewRankingCeneo = CalculateRanking(new List<decimal>(ceneoPrices), row.SuggestedPrice.Value);
                }
                if (committedLookup.TryGetValue(p.ProductId, out var committedItem))
                {
                    row.IsAlreadyUpdated = true;
                    row.UpdatedPrice = committedItem.PriceAfter;
                    row.UpdateDate = committedItem.Batch.ExecutionDate;

                    // --- DODAJ TEN FRAGMENT (to naprawia problem) ---
                    if (row.SuggestedPrice.HasValue && row.UpdatedPrice.HasValue)
                    {
                        // Sprawdzamy czy nowa wyliczona cena różni się od tej już wgranej
                        if (Math.Round(row.SuggestedPrice.Value, 2) != Math.Round(row.UpdatedPrice.Value, 2))
                        {
                            row.IsSuggestedDifferentFromUpdated = true;
                        }
                    }
                    // ------------------------------------------------
                }

                resultProducts.Add(row);
            }

            return (resultProducts, scrapId, latestScrap.Date);
        }


        private async Task PreparePriceComparisonData(AutomationRule rule, AutomationDetailsViewModel model)
        {
            // Wywołujemy główny silnik obliczeniowy (ten sam, co przy ExecuteAutomation)
            var calculationResult = await GetCalculatedComparisonData(rule);

            // Przepisujemy wyniki do modelu widoku
            model.Products = calculationResult.Products;
            model.TotalProducts = calculationResult.Products.Count;

            // Ustawiamy daty ostatniego scrapingu
            model.LastScrapDate = calculationResult.ScrapDate;
            model.LatestScrapId = calculationResult.ScrapId;
        }


        private async Task<(List<AutomationProductRowViewModel> Products, int ScrapId, DateTime? ScrapDate)> GetCalculatedMarketplaceData(AutomationRule rule)
        {
            var resultProducts = new List<AutomationProductRowViewModel>();

            var latestScrap = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == rule.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            if (latestScrap == null) return (resultProducts, 0, null);

            int scrapId = latestScrap.Id;

            var committedChanges = await _context.AllegroPriceBridgeItems
                .Include(i => i.PriceBridgeBatch)
                .Where(i => i.PriceBridgeBatch.StoreId == rule.StoreId
                         && i.PriceBridgeBatch.AllegroScrapeHistoryId == scrapId
                         && i.Success)
                .ToListAsync();

            var committedLookup = committedChanges
                .GroupBy(i => i.AllegroProductId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.PriceBridgeBatch.ExecutionDate).First());

            var assignments = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == rule.Id && a.AllegroProductId.HasValue)
                .Include(a => a.AllegroProduct)
                .ToListAsync();

            if (!assignments.Any()) return (resultProducts, scrapId, latestScrap.Date);

            var productIds = assignments.Select(a => a.AllegroProductId.Value).ToList();

            var priceHistories = await _context.AllegroPriceHistories
                .Where(ph => ph.AllegroScrapeHistoryId == scrapId && productIds.Contains(ph.AllegroProductId))
                .ToListAsync();

            var extendedInfos = await _context.AllegroPriceHistoryExtendedInfos
                .Where(x => x.ScrapHistoryId == scrapId && productIds.Contains(x.AllegroProductId))
                .ToListAsync();

            var competitorRules = GetCompetitorRulesForMarketplace(rule.CompetitorPreset);
            string myStoreNameAllegro = rule.Store.StoreNameAllegro;

            int minDelivery = rule.CompetitorPreset?.MinDeliveryDays ?? 0;
            int maxDelivery = rule.CompetitorPreset?.MaxDeliveryDays ?? 31;

            foreach (var item in assignments)
            {
                var p = item.AllegroProduct;
                if (p == null) continue;

                var histories = priceHistories.Where(h => h.AllegroProductId == p.AllegroProductId).ToList();

                long? targetOfferId = null;
                if (long.TryParse(p.IdOnAllegro, out var parsedId))
                {
                    targetOfferId = parsedId;
                }

                AllegroPriceHistory myHistory = null;

                if (targetOfferId.HasValue)
                {
                    myHistory = histories.FirstOrDefault(h => h.IdAllegro == targetOfferId.Value);
                }
                else
                {
                    myHistory = histories.FirstOrDefault(h => h.SellerName != null && h.SellerName.Equals(myStoreNameAllegro, StringComparison.OrdinalIgnoreCase));
                }

                var extInfo = extendedInfos.FirstOrDefault(x => x.AllegroProductId == p.AllegroProductId);

                var rawCompetitors = histories
                    .Where(h => h.Price > 0

                             && (targetOfferId == null || h.IdAllegro != targetOfferId)

                             && (h.SellerName == null || !h.SellerName.Equals(myStoreNameAllegro, StringComparison.OrdinalIgnoreCase))

                             && (h.DeliveryTime ?? 31) >= minDelivery
                             && (h.DeliveryTime ?? 31) <= maxDelivery

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
                bool hasCheaperOwnOffer = false;

                if (myHistory != null && myHistory.Price > 0)
                {
                    var myOtherOffers = histories
                        .Where(h => h.SellerName != null
                                 && h.SellerName.Equals(myStoreNameAllegro, StringComparison.OrdinalIgnoreCase)
                                 && h.IdAllegro != myHistory.IdAllegro
                                 && h.Price > 0)
                        .ToList();

                    if (myOtherOffers.Any(other => other.Price < myHistory.Price))
                    {
                        hasCheaperOwnOffer = true;
                    }
                }

                if (myHistory != null && myHistory.Price > 0)
                    currentRankAllegro = CalculateRanking(new List<decimal>(competitorPrices), myHistory.Price);

                var row = new AutomationProductRowViewModel
                {
                    ProductId = p.AllegroProductId,
                    Name = p.AllegroProductName,
                    Identifier = p.IdOnAllegro,
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
                    IsCommissionIncluded = rule.MarketplaceIncludeCommission,
                    HasCheaperOwnOffer = hasCheaperOwnOffer,
                    PurchasePriceUpdatedDate = p.AllegroMarginPriceUpdatedDate,
                };

                CalculateSuggestedPrice(rule, row);
                CalculateCurrentMarkup(row);

                string newRankAllegro = "-";
                if (row.SuggestedPrice.HasValue)
                {
                    newRankAllegro = CalculateRanking(new List<decimal>(competitorPrices), row.SuggestedPrice.Value);
                }
                row.NewRankingAllegro = newRankAllegro;

                if (committedLookup.TryGetValue(p.AllegroProductId, out var committedItem))
                {
                    row.IsAlreadyUpdated = true;
                    // Dla Allegro bierzemy zweryfikowaną cenę (jeśli API potwierdziło zmianę) lub symulowaną
                    row.UpdatedPrice = committedItem.PriceAfter_Verified ?? committedItem.PriceAfter_Simulated;
                    row.UpdateDate = committedItem.PriceBridgeBatch.ExecutionDate;

                    // NOWOŚĆ: Sprawdzamy rozbieżność
                    if (row.SuggestedPrice.HasValue && row.UpdatedPrice.HasValue)
                    {
                        if (Math.Round(row.SuggestedPrice.Value, 2) != Math.Round(row.UpdatedPrice.Value, 2))
                        {
                            row.IsSuggestedDifferentFromUpdated = true;
                        }
                    }
                }

                resultProducts.Add(row);
            }

            return (resultProducts, scrapId, latestScrap.Date);
        }

        private async Task PrepareMarketplaceData(AutomationRule rule, AutomationDetailsViewModel model)
        {

            var result = await GetCalculatedMarketplaceData(rule);
            model.Products = result.Products;
            model.TotalProducts = result.Products.Count;
            model.LastScrapDate = result.ScrapDate;
            model.LatestScrapId = result.ScrapId;
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
            if (startRank == endRank) return $"{startRank}/{totalCount}";
            else return $"{startRank}-{endRank}/{totalCount}";
        }

        private void CalculateSuggestedPrice(AutomationRule rule, AutomationProductRowViewModel row)
        {

            decimal extraCost = 0;
            if (rule.SourceType == AutomationSourceType.Marketplace &&
                rule.MarketplaceIncludeCommission &&
                row.CommissionAmount.HasValue)
            {
                extraCost = row.CommissionAmount.Value;
            }

            if (rule.EnforceMinimalMarkup && row.PurchasePrice.HasValue && row.PurchasePrice > 0)
            {
                decimal minLimit;
                if (rule.IsMinimalMarkupPercent)
                    minLimit = row.PurchasePrice.Value + (row.PurchasePrice.Value * (rule.MinimalMarkupValue / 100)) + extraCost;
                else
                    minLimit = row.PurchasePrice.Value + rule.MinimalMarkupValue + extraCost;

                row.MinPriceLimit = Math.Round(minLimit, 2);
            }

            if (rule.EnforceMaxMarkup && row.PurchasePrice.HasValue && row.PurchasePrice > 0)
            {
                decimal maxLimit;
                if (rule.IsMaxMarkupPercent)
                    maxLimit = row.PurchasePrice.Value + (row.PurchasePrice.Value * (rule.MaxMarkupValue / 100)) + extraCost;
                else
                    maxLimit = row.PurchasePrice.Value + rule.MaxMarkupValue + extraCost;

                row.MaxPriceLimit = Math.Round(maxLimit, 2);
            }

            if (rule.SourceType == AutomationSourceType.Marketplace)
            {
                bool missingApiData = !row.CommissionAmount.HasValue;

                if (missingApiData)
                {
                    if (rule.MarketplaceIncludeCommission)
                    {
                        ApplyBlock(row, "Brak Prowizji");
                        return;
                    }

                    if (!rule.MarketplaceChangePriceForBadgeInCampaign)
                    {
                        ApplyBlock(row, "Ocena Kampanii");
                        return;
                    }
                }

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

            if ((rule.EnforceMinimalMarkup || rule.EnforceMaxMarkup) && (!row.PurchasePrice.HasValue || row.PurchasePrice <= 0))
            {
                ApplyBlock(row, "Brak Ceny Zakupu");
                return;
            }

            decimal basePrice = row.ApiAllegroPriceFromUser ?? row.CurrentPrice ?? 0;

            if (basePrice == 0)
            {
                row.SuggestedPrice = null;
                row.Status = AutomationCalculationStatus.Blocked;
                row.BlockReason = "Brak Ceny Obecnej";
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
                row.Status = AutomationCalculationStatus.Blocked;
                row.BlockReason = "Brak Danych";
                return;
            }

            decimal idealPrice = suggested;
            bool wasLimited = false;

            if (row.MinPriceLimit.HasValue)
            {
                if (suggested < row.MinPriceLimit.Value)
                {
                    if (rule.SkipIfMarkupLimited)
                    {
                        ApplyBlock(row, "Blokada Narzutu");
                        return;
                    }

                    suggested = row.MinPriceLimit.Value;
                    row.IsMarginWarning = true;
                    wasLimited = true;
                }
            }

            if (row.MaxPriceLimit.HasValue)
            {
                if (suggested > row.MaxPriceLimit.Value)
                {
                    suggested = row.MaxPriceLimit.Value;
                    wasLimited = true;
                }
            }

            row.SuggestedPrice = Math.Round(suggested, 2);
            decimal finalSuggested = row.SuggestedPrice.Value;
            decimal finalBase = Math.Round(basePrice, 2);
            row.PriceChange = Math.Round(finalSuggested - finalBase, 2);

            if (wasLimited)
            {
                row.Status = AutomationCalculationStatus.PriceLimited;
            }
            else if (row.PriceChange == 0)
            {
                row.Status = AutomationCalculationStatus.TargetMaintained;
            }
            else
            {
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

            decimal basePriceForMarkup = row.ApiAllegroPriceFromUser ?? row.CurrentPrice ?? 0;

            if (basePriceForMarkup > 0 && row.PurchasePrice.HasValue && row.PurchasePrice.Value > 0)
            {
                decimal sellPrice = basePriceForMarkup;
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
            row.Status = AutomationCalculationStatus.Blocked;
            row.BlockReason = reason;
            row.SuggestedPrice = row.ApiAllegroPriceFromUser ?? row.CurrentPrice;
            row.PriceChange = 0;
            CalculateMarkup(row);
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

        private bool IsCompetitorAllowedComparison(string storeName, bool isGoogle, Dictionary<(string Store, DataSourceType Source), bool> rulesDict, bool useUnmarkedStores)
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

        private bool IsCompetitorAllowedMarketplace(string storeName, Dictionary<string, bool> rulesDict, bool useUnmarkedStores)
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

       

        //private async Task SavePriceComparisonBatch(AutomationExecutionRequest request, string? userId, AutomationRule rule)
        //{

        //}

        public class AutomationTriggerRequest
        {
            public int RuleId { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> GetAutomationHistory([FromBody] HistoryRequest request)
        {
            if (request == null || request.RuleId <= 0) return BadRequest();

            int limit = request.Limit > 0 ? request.Limit : 7;

            var rule = await _context.AutomationRules.FindAsync(request.RuleId);
            if (rule == null) return NotFound();

            var model = new AutomationHistoryChartViewModel();

            var rawData = new List<dynamic>();

            if (rule.SourceType == AutomationSourceType.Marketplace)
            {
                rawData = await _context.AllegroPriceBridgeBatches
                    .Where(b => b.AutomationRuleId == rule.Id && b.IsAutomation)
                    .OrderByDescending(b => b.ExecutionDate)
                    .Take(limit)
                    .Select(b => new
                    {
                        Date = b.ExecutionDate,
                        Met = b.TargetMetCount ?? 0,
                        Unmet = b.TargetUnmetCount ?? 0,
                        Inc = b.PriceIncreasedCount ?? 0,
                        Dec = b.PriceDecreasedCount ?? 0,
                        Main = b.PriceMaintainedCount ?? 0,
                        Total = b.TotalProductsCount ?? 0

                    })
                    .ToListAsync<dynamic>();
            }
            else

            {
                rawData = await _context.PriceBridgeBatches
                    .Where(b => b.AutomationRuleId == rule.Id && b.IsAutomation)
                    .OrderByDescending(b => b.ExecutionDate)
                    .Take(limit)
                    .Select(b => new
                    {
                        Date = b.ExecutionDate,
                        Met = b.TargetMetCount ?? 0,
                        Unmet = b.TargetUnmetCount ?? 0,
                        Inc = b.PriceIncreasedCount ?? 0,
                        Dec = b.PriceDecreasedCount ?? 0,
                        Main = b.PriceMaintainedCount ?? 0,
                        Total = b.TotalProductsCount ?? 0

                    })
                    .ToListAsync<dynamic>();
            }

            var sortedData = rawData.OrderBy(x => x.Date).ToList();

            foreach (var item in sortedData)
            {
                model.Dates.Add(item.Date.ToString("dd.MM HH:mm"));

                model.TargetMet.Add(item.Met);
                model.TargetUnmet.Add(item.Unmet);

                model.Increased.Add(item.Inc);
                model.Decreased.Add(item.Dec);
                model.Maintained.Add(item.Main);

                model.TotalProducts.Add(item.Total);

            }

            return Ok(model);
        }

        public class HistoryRequest
        {
            public int RuleId { get; set; }
            public int Limit { get; set; }
        }
    }
}