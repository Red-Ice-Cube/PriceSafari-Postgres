using Microsoft.EntityFrameworkCore;
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
using System.Threading.Tasks;

namespace PriceSafari.Services.PriceAutomationService
{
    public class PriceAutomationService
    {
        private readonly PriceSafariContext _context;
        private readonly AllegroPriceBridgeService _allegroBridgeService;
        private readonly StorePriceBridgeService _storePriceBridgeService;

        public PriceAutomationService(
            PriceSafariContext context,
            AllegroPriceBridgeService allegroBridgeService,
            StorePriceBridgeService storePriceBridgeService)
        {
            _context = context;
            _allegroBridgeService = allegroBridgeService;
            _storePriceBridgeService = storePriceBridgeService;
        }

        public async Task<object> ExecuteAutomationAsync(int ruleId, string? userId)
        {
            var rule = await _context.AutomationRules
                .Include(r => r.Store)
                .Include(r => r.CompetitorPreset)
                    .ThenInclude(cp => cp.CompetitorItems)
                .FirstOrDefaultAsync(r => r.Id == ruleId);

            if (rule == null) throw new Exception("Reguła nie istnieje.");

            if (rule.SourceType == AutomationSourceType.PriceComparison)
            {
                var calcResult = await GetCalculatedComparisonData(rule);

                if (calcResult.ScrapId == 0) throw new Exception("Brak danych historycznych (ScrapHistory).");

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
                    automationRuleId: rule.Id,
                    totalProductsInRule: totalProductsInRule,
                    targetMetCount: metCount,
                    targetUnmetCount: unmetCount,
                    priceIncreasedCount: increasedCount,
                    priceDecreasedCount: decreasedCount,
                    priceMaintainedCount: maintainedCount
                );

                return new { success = true, count = result.SuccessfulCount, details = result };
            }
            else if (rule.SourceType == AutomationSourceType.Marketplace)
            {
                var calcResult = await GetCalculatedMarketplaceData(rule);

                if (calcResult.ScrapId == 0) throw new Exception("Brak danych historycznych.");

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

                return new { success = true, count = result.SuccessfulCount };
            }

            throw new Exception("Nieznany typ źródła.");
        }

        public async Task<AutomationHistoryChartViewModel> GetAutomationHistoryAsync(int ruleId, int limit)
        {
            if (limit <= 0) limit = 7;

            var rule = await _context.AutomationRules.FindAsync(ruleId);
            if (rule == null) return null;

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

            return model;
        }

        public async Task<(List<AutomationProductRowViewModel> Products, int ScrapId, DateTime? ScrapDate)> GetCalculatedComparisonData(AutomationRule rule)
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

                var myGoogle = histories.FirstOrDefault(h => h.StoreName != null && h.StoreName.Contains(myStoreName, StringComparison.OrdinalIgnoreCase) && h.IsGoogle == true);
                var myCeneo = histories.FirstOrDefault(h => h.StoreName != null && h.StoreName.Contains(myStoreName, StringComparison.OrdinalIgnoreCase) && h.IsGoogle != true);

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

                bool bestRivalIsCeneo = bestCompetitor != null && bestCompetitor.IsGoogle != true;
                bool bestRivalIsGoogle = bestCompetitor != null && bestCompetitor.IsGoogle == true;

                if (bestRivalIsCeneo && myCeneo == null)
                {
                    if (rule.RequireOwnOfferOnCeneo)
                    {
                        ApplyBlock(row, "Brak oferty (Ceneo)");
                        resultProducts.Add(row);
                        continue;
                    }
                    else
                    {
                        row.IsMissingPlatformWarning = true;
                        row.MissingPlatformName = "Ceneo";
                    }
                }

                if (bestRivalIsGoogle && myGoogle == null)
                {
                    if (rule.RequireOwnOfferOnGoogle)
                    {
                        ApplyBlock(row, "Brak oferty (Google)");
                        resultProducts.Add(row);
                        continue;
                    }
                    else
                    {
                        row.IsMissingPlatformWarning = true;
                        row.MissingPlatformName = "Google";
                    }
                }

                var googlePrices = filteredCompetitors.Where(c => c.IsGoogle == true).Select(c => c.Price).ToList();
                var ceneoPrices = filteredCompetitors.Where(c => c.IsGoogle != true).Select(c => c.Price).ToList();

                decimal? googlePriceCalc = (myGoogle != null && myGoogle.Price > 0) ? myGoogle.Price : row.CurrentPrice;

                if (includeGoogle && googlePriceCalc.HasValue && googlePriceCalc.Value > 0)
                    row.CurrentRankingGoogle = CalculateRanking(new List<decimal>(googlePrices), googlePriceCalc.Value);
                else
                    row.CurrentRankingGoogle = null;

                decimal? ceneoPriceCalc = (myCeneo != null && myCeneo.Price > 0) ? myCeneo.Price : row.CurrentPrice;

                if (includeCeneo && ceneoPriceCalc.HasValue && ceneoPriceCalc.Value > 0)
                    row.CurrentRankingCeneo = CalculateRanking(new List<decimal>(ceneoPrices), ceneoPriceCalc.Value);
                else
                    row.CurrentRankingCeneo = null;

                CalculateSuggestedPrice(rule, row);
                CalculateCurrentMarkup(row);

                if (row.SuggestedPrice.HasValue)
                {
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

        public async Task<(List<AutomationProductRowViewModel> Products, int ScrapId, DateTime? ScrapDate)> GetCalculatedMarketplaceData(AutomationRule rule)
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

            bool includeNoDelivery = rule.CompetitorPreset?.IncludeNoDeliveryInfo ?? true;
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
                    .Where(h =>
                    {

                        if (h.Price <= 0) return false;
                        if (targetOfferId != null && h.IdAllegro == targetOfferId) return false;
                        if (h.SellerName != null && h.SellerName.Equals(myStoreNameAllegro, StringComparison.OrdinalIgnoreCase)) return false;

                        if (h.DeliveryTime.HasValue)
                        {

                            if (h.DeliveryTime.Value < minDelivery || h.DeliveryTime.Value > maxDelivery)
                            {
                                return false;
                            }
                        }
                        else
                        {

                            if (!includeNoDelivery)
                            {
                                return false;
                            }
                        }

                        return true;
                    })
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

                    row.UpdatedPrice = committedItem.PriceAfter_Verified ?? committedItem.PriceAfter_Simulated;
                    row.UpdateDate = committedItem.PriceBridgeBatch.ExecutionDate;

                    row.UpdatedCommissionAmount = committedItem.CommissionAfter_Verified;

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

        public async Task PreparePriceComparisonData(AutomationRule rule, AutomationDetailsViewModel model)
        {
            var calculationResult = await GetCalculatedComparisonData(rule);

            model.EnforceMinimalMarkup = rule.EnforceMinimalMarkup;
            model.IsMinimalMarkupPercent = rule.IsMinimalMarkupPercent;
            model.MinimalMarkupValue = rule.MinimalMarkupValue;

            model.Products = calculationResult.Products;
            model.TotalProducts = calculationResult.Products.Count;
            model.LastScrapDate = calculationResult.ScrapDate;
            model.LatestScrapId = calculationResult.ScrapId;
        }

        public async Task PrepareMarketplaceData(AutomationRule rule, AutomationDetailsViewModel model)
        {
            var result = await GetCalculatedMarketplaceData(rule);

            model.EnforceMinimalMarkup = rule.EnforceMinimalMarkup;
            model.IsMinimalMarkupPercent = rule.IsMinimalMarkupPercent;
            model.MinimalMarkupValue = rule.MinimalMarkupValue;

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

            decimal basePrice = row.ApiAllegroPriceFromUser ?? row.CurrentPrice ?? 0;

            if (basePrice == 0)
            {
                row.SuggestedPrice = null;
                row.Status = AutomationCalculationStatus.Blocked;
                row.BlockReason = "Brak Twojej Oferty";
                return;
            }

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

            if (rule.UsePurchasePrice && (!row.PurchasePrice.HasValue || row.PurchasePrice <= 0))
            {
                ApplyBlock(row, "Brak Ceny Zakupu");
                return;
            }

            decimal suggested = basePrice;
            bool calculationPossible = false;

            if (rule.StrategyMode == AutomationStrategyMode.Competitiveness)
            {
                if (row.BestCompetitorPrice.HasValue)
                {
                    decimal targetSuggestedPrice;
                    if (rule.IsPriceStepPercent)
                        targetSuggestedPrice = row.BestCompetitorPrice.Value + (row.BestCompetitorPrice.Value * (rule.PriceStep / 100));
                    else
                        targetSuggestedPrice = row.BestCompetitorPrice.Value + rule.PriceStep;

                    suggested = targetSuggestedPrice;
                    calculationPossible = true;
                }
            }
            else if (rule.StrategyMode == AutomationStrategyMode.Profit)
            {
                if (row.MarketAveragePrice.HasValue && row.MarketAveragePrice.Value > 0)
                {
                    decimal targetSuggestedPrice = row.MarketAveragePrice.Value * (rule.PriceIndexTargetPercent / 100);

                    suggested = targetSuggestedPrice;
                    calculationPossible = true;
                }
            }

            // POPRAWIONY KOD:
            if (!calculationPossible)
            {
                // Ochrona ceny — brak konkurencji, ale nasza cena jest poniżej minimum
                if (row.MinPriceLimit.HasValue && basePrice < row.MinPriceLimit.Value)
                {
                    suggested = row.MinPriceLimit.Value;
                    row.SuggestedPrice = Math.Round(suggested, 2);
                    row.PriceChange = Math.Round(row.SuggestedPrice.Value - Math.Round(basePrice, 2), 2);
                    row.Status = AutomationCalculationStatus.PriceLimited;
                    row.BlockReason = "Ochrona Ceny";
                    row.IsMarginWarning = true;
                    CalculateMarkup(row);
                    return;
                }

                // Brak konkurencji i cena OK — nic nie robimy
                row.SuggestedPrice = null;
                row.Status = AutomationCalculationStatus.Blocked;
                row.BlockReason = "Brak Konkurencji";
                return;
            }

            if (rule.SourceType == AutomationSourceType.Marketplace && rule.BlockAtSmartValue)
            {

                decimal allegroSmartThreshold = 45.00m;

                if (suggested < allegroSmartThreshold && suggested > 0)
                {

                    if (suggested >= rule.SkipIfValueGoBelow)
                    {

                        suggested = allegroSmartThreshold;

                        row.IsSmartPriceAdjusted = true;
                    }

                }
            }

            bool wasLimited = false;

            if (row.MinPriceLimit.HasValue)
            {
                if (suggested < row.MinPriceLimit.Value)
                {

                    bool isPriceImprovement = suggested > basePrice;

                    // POPRAWIONY KOD:
                    if (isPriceImprovement && rule.SkipIfMarkupLimited)
                    {
                        // Ratowanie marży — cena rośnie, ale nie osiąga minimum.
                        // Przepuszczamy tylko gdy SkipIfMarkupLimited = true.
                        row.IsMarginWarning = true;
                    }
                    else if (rule.SkipIfMarkupLimited)
                    {
                        // Cena spada poniżej minimum — blokujemy
                        ApplyBlock(row, "Blokada Narzutu");
                        return;
                    }
                    else
                    {
                        // SkipIfMarkupLimited = false — minimum to twardy floor
                        suggested = row.MinPriceLimit.Value;
                        row.IsMarginWarning = true;
                        wasLimited = true;
                    }
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

        public async Task<AutomationBadgeHistoryViewModel> GetBadgeHistoryAsync(int ruleId, int limit)
        {
            if (limit <= 0) limit = 7;

            var rule = await _context.AutomationRules
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.Id == ruleId);

            if (rule == null || rule.Store == null) return null;
            string myStoreName = rule.Store.StoreNameAllegro ?? "";

            var currentProductIds = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == ruleId && a.AllegroProductId.HasValue)
                .Select(a => a.AllegroProductId.Value)
                .ToListAsync();

            if (!currentProductIds.Any()) return new AutomationBadgeHistoryViewModel();

            var scrapHistories = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == rule.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Take(limit)
                .Select(sh => new { sh.Id, sh.Date })
                .OrderBy(x => x.Date)
                .ToListAsync();

            var scrapIds = scrapHistories.Select(s => s.Id).ToList();

            var allBasicBadges = await _context.AllegroPriceHistories
                .Where(h => scrapIds.Contains(h.AllegroScrapeHistoryId)
                         && currentProductIds.Contains(h.AllegroProductId)
                         && h.SellerName == myStoreName)
                .Select(h => new
                {
                    h.AllegroScrapeHistoryId,
                    h.AllegroProductId,
                    h.TopOffer,
                    h.SuperPrice,
                    h.IsBestPriceGuarantee
                })
                .ToListAsync();

            var allExtendedBadges = await _context.AllegroPriceHistoryExtendedInfos
                .Where(h => scrapIds.Contains(h.ScrapHistoryId)
                         && currentProductIds.Contains(h.AllegroProductId))
                .Select(h => new
                {
                    h.ScrapHistoryId,
                    h.AllegroProductId,
                    h.AnyPromoActive,
                    h.IsSubsidyActive
                })
                .ToListAsync();

            var model = new AutomationBadgeHistoryViewModel();
            model.TotalProductsInRule = currentProductIds.Count;

            foreach (var scrap in scrapHistories)
            {
                model.Dates.Add(scrap.Date.ToString("dd.MM HH:mm"));

                var basicForScrap = allBasicBadges
                    .Where(b => b.AllegroScrapeHistoryId == scrap.Id)
                    .ToList();

                var extForScrap = allExtendedBadges
                    .Where(b => b.ScrapHistoryId == scrap.Id)
                    .ToList();

                int coveredProducts = basicForScrap
                    .Select(b => b.AllegroProductId)
                    .Distinct()
                    .Count();

                model.TopOfferCounts.Add(basicForScrap.Count(x => x.TopOffer == true));
                model.SuperPriceCounts.Add(basicForScrap.Count(x => x.SuperPrice == true));
                model.BestPriceGuaranteeCounts.Add(basicForScrap.Count(x => x.IsBestPriceGuarantee == true));
                model.CampaignCounts.Add(extForScrap.Count(x => x.AnyPromoActive == true));
                model.SubsidyCounts.Add(extForScrap.Count(x => x.IsSubsidyActive == true));
                model.CoveredProductsCounts.Add(coveredProducts);
            }

            return model;
        }

        public async Task<AutomationSalesHistoryViewModel> GetSalesHistoryAsync(int ruleId, int limit)
        {
            if (limit <= 0) limit = 7;

            var rule = await _context.AutomationRules
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.Id == ruleId);

            if (rule == null || rule.Store == null) return null;
            string myStoreName = rule.Store.StoreNameAllegro ?? "";

            var currentProductIds = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == ruleId && a.AllegroProductId.HasValue)
                .Select(a => a.AllegroProductId.Value)
                .ToListAsync();

            if (!currentProductIds.Any()) return new AutomationSalesHistoryViewModel();

            var currentProductIdSet = currentProductIds.ToHashSet();

            var scrapHistories = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == rule.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Take(limit)
                .Select(sh => new { sh.Id, sh.Date })
                .OrderBy(x => x.Date)
                .ToListAsync();

            var scrapIds = scrapHistories.Select(s => s.Id).ToList();

            var allRecords = await _context.AllegroPriceHistories
                .Where(h => scrapIds.Contains(h.AllegroScrapeHistoryId)
                         && currentProductIds.Contains(h.AllegroProductId))
                .Select(h => new
                {
                    h.AllegroScrapeHistoryId,
                    h.AllegroProductId,
                    h.SellerName,
                    h.Popularity
                })
                .ToListAsync();

            var model = new AutomationSalesHistoryViewModel();

            foreach (var scrap in scrapHistories)
            {
                model.Dates.Add(scrap.Date.ToString("dd.MM HH:mm"));

                var recordsForScrap = allRecords
                    .Where(h => h.AllegroScrapeHistoryId == scrap.Id)
                    .ToList();

                int productsWithAnyData = recordsForScrap
                    .Select(r => r.AllegroProductId)
                    .Distinct()
                    .Count();

                int productsWithMyOffer = recordsForScrap
                    .Where(r => r.SellerName == myStoreName)
                    .Select(r => r.AllegroProductId)
                    .Distinct()
                    .Count();

                long mySales = recordsForScrap
                    .Where(x => x.SellerName == myStoreName)
                    .Sum(x => x.Popularity ?? 0);

                long marketSales = recordsForScrap
                    .Sum(x => x.Popularity ?? 0);

                model.MySales.Add(mySales);
                model.MarketSales.Add(marketSales);
                model.ActiveOffersCount.Add(productsWithMyOffer);
                model.ProductsWithDataCount.Add(productsWithAnyData);
            }

            model.TotalProductsInRule = currentProductIds.Count;

            return model;
        }

        public async Task<AutomationPricePositionHistoryViewModel> GetPricePositionHistoryAsync(int ruleId, int limit)
        {
            if (limit <= 0) limit = 7;

            var rule = await _context.AutomationRules
                .Include(r => r.Store)
                .FirstOrDefaultAsync(r => r.Id == ruleId);

            if (rule == null || rule.Store == null) return null;

            if (rule.SourceType == AutomationSourceType.Marketplace)
                return await GetPricePositionMarketplace(rule, limit);
            else
                return await GetPricePositionComparison(rule, limit);
        }

        private async Task<AutomationPricePositionHistoryViewModel> GetPricePositionComparison(AutomationRule rule, int limit)
        {
            string myStoreName = rule.Store.StoreName ?? "";

            var currentProductIds = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == rule.Id && a.ProductId.HasValue)
                .Select(a => a.ProductId.Value)
                .ToListAsync();

            if (!currentProductIds.Any()) return new AutomationPricePositionHistoryViewModel();

            var scrapHistories = await _context.ScrapHistories
                .Where(sh => sh.StoreId == rule.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Take(limit)
                .Select(sh => new { sh.Id, sh.Date })
                .OrderBy(x => x.Date)
                .ToListAsync();

            var scrapIds = scrapHistories.Select(s => s.Id).ToList();

            var allRecords = await _context.PriceHistories
                .Where(h => scrapIds.Contains(h.ScrapHistoryId)
                         && currentProductIds.Contains(h.ProductId)
                         && h.Price > 0)
                .Select(h => new
                {
                    h.ScrapHistoryId,
                    h.ProductId,
                    h.StoreName,
                    h.Price
                })
                .ToListAsync();

            var model = new AutomationPricePositionHistoryViewModel();
            model.TotalProductsInRule = currentProductIds.Count;

            foreach (var scrap in scrapHistories)
            {
                model.Dates.Add(scrap.Date.ToString("dd.MM HH:mm"));

                var recordsForScrap = allRecords.Where(r => r.ScrapHistoryId == scrap.Id).ToList();

                int top1Solo = 0, top1ExAequo = 0, pos2to3 = 0, pos4to5 = 0, pos6to10 = 0, pos11plus = 0;

                var productsInScrape = new HashSet<int>();

                var byProduct = recordsForScrap.GroupBy(r => r.ProductId);

                foreach (var group in byProduct)
                {
                    productsInScrape.Add(group.Key);

                    var offers = group.ToList();

                    var myOffers = offers.Where(o =>
                        o.StoreName != null &&
                        o.StoreName.Contains(myStoreName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (!myOffers.Any()) continue;

                    decimal myBestPrice = myOffers.Min(o => o.Price);
                    var allPrices = offers.Select(o => o.Price).OrderBy(p => p).ToList();

                    int firstIndex = -1;
                    for (int i = 0; i < allPrices.Count; i++)
                    {
                        if (allPrices[i] == myBestPrice) { firstIndex = i; break; }
                    }

                    if (firstIndex == -1) continue;

                    int startRank = firstIndex + 1;
                    int sameAsBestCount = allPrices.Count(p => p == allPrices[0]);

                    if (startRank == 1)
                    {
                        if (sameAsBestCount == 1) top1Solo++;
                        else top1ExAequo++;
                    }
                    else if (startRank <= 3) pos2to3++;
                    else if (startRank <= 5) pos4to5++;
                    else if (startRank <= 10) pos6to10++;
                    else pos11plus++;
                }

                model.Top1Solo.Add(top1Solo);
                model.Top1ExAequo.Add(top1ExAequo);
                model.Position2to3.Add(pos2to3);
                model.Position4to5.Add(pos4to5);
                model.Position6to10.Add(pos6to10);
                model.Position11Plus.Add(pos11plus);

                model.ProductsWithDataCount.Add(productsInScrape.Count);
            }

            return model;
        }

        private async Task<AutomationPricePositionHistoryViewModel> GetPricePositionMarketplace(AutomationRule rule, int limit)
        {
            string myStoreName = rule.Store.StoreNameAllegro ?? "";

            var currentProductIds = await _context.AutomationProductAssignments
                .Where(a => a.AutomationRuleId == rule.Id && a.AllegroProductId.HasValue)
                .Select(a => a.AllegroProductId.Value)
                .ToListAsync();

            if (!currentProductIds.Any()) return new AutomationPricePositionHistoryViewModel();

            var scrapHistories = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == rule.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Take(limit)
                .Select(sh => new { sh.Id, sh.Date })
                .OrderBy(x => x.Date)
                .ToListAsync();

            var scrapIds = scrapHistories.Select(s => s.Id).ToList();

            var allRecords = await _context.AllegroPriceHistories
                .Where(h => scrapIds.Contains(h.AllegroScrapeHistoryId)
                         && currentProductIds.Contains(h.AllegroProductId)
                         && h.Price > 0)
                .Select(h => new
                {
                    h.AllegroScrapeHistoryId,
                    h.AllegroProductId,
                    h.SellerName,
                    h.Price
                })
                .ToListAsync();

            var model = new AutomationPricePositionHistoryViewModel();
            model.TotalProductsInRule = currentProductIds.Count;

            foreach (var scrap in scrapHistories)
            {
                model.Dates.Add(scrap.Date.ToString("dd.MM HH:mm"));

                var recordsForScrap = allRecords.Where(r => r.AllegroScrapeHistoryId == scrap.Id).ToList();

                int top1Solo = 0, top1ExAequo = 0, pos2to3 = 0, pos4to5 = 0, pos6to10 = 0, pos11plus = 0;

                var productsInScrape = new HashSet<int>();

                var byProduct = recordsForScrap.GroupBy(r => r.AllegroProductId);

                foreach (var group in byProduct)
                {
                    productsInScrape.Add(group.Key);

                    var offers = group.ToList();

                    var myOffers = offers.Where(o =>
                        o.SellerName != null &&
                        o.SellerName.Equals(myStoreName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (!myOffers.Any()) continue;

                    decimal myBestPrice = myOffers.Min(o => o.Price);
                    var allPrices = offers.Select(o => o.Price).OrderBy(p => p).ToList();

                    int firstIndex = -1;
                    for (int i = 0; i < allPrices.Count; i++)
                    {
                        if (allPrices[i] == myBestPrice) { firstIndex = i; break; }
                    }

                    if (firstIndex == -1) continue;

                    int startRank = firstIndex + 1;
                    int sameAsBestCount = allPrices.Count(p => p == allPrices[0]);

                    if (startRank == 1)
                    {
                        if (sameAsBestCount == 1) top1Solo++;
                        else top1ExAequo++;
                    }
                    else if (startRank <= 3) pos2to3++;
                    else if (startRank <= 5) pos4to5++;
                    else if (startRank <= 10) pos6to10++;
                    else pos11plus++;
                }

                model.Top1Solo.Add(top1Solo);
                model.Top1ExAequo.Add(top1ExAequo);
                model.Position2to3.Add(pos2to3);
                model.Position4to5.Add(pos4to5);
                model.Position6to10.Add(pos6to10);
                model.Position11Plus.Add(pos11plus);

                model.ProductsWithDataCount.Add(productsInScrape.Count);
            }

            return model;
        }
    }
    public class AutomationTriggerRequest
    {
        public int RuleId { get; set; }
    }

    public class HistoryRequest
    {
        public int RuleId { get; set; }
        public int Limit { get; set; }
    }

    public class AutomationSalesHistoryViewModel
    {
        public List<string> Dates { get; set; } = new();
        public List<long> MySales { get; set; } = new();
        public List<long> MarketSales { get; set; } = new();
        public List<int> ActiveOffersCount { get; set; } = new();

        public List<int> ProductsWithDataCount { get; set; } = new();
        public int TotalProductsInRule { get; set; }
    }

    public class AutomationBadgeHistoryViewModel
    {
        public List<string> Dates { get; set; } = new();
        public List<int> TopOfferCounts { get; set; } = new();
        public List<int> SuperPriceCounts { get; set; } = new();
        public List<int> BestPriceGuaranteeCounts { get; set; } = new();
        public List<int> CampaignCounts { get; set; } = new();
        public List<int> SubsidyCounts { get; set; } = new();
        public List<int> TotalProductsCounts { get; set; } = new();

        public List<int> CoveredProductsCounts { get; set; } = new();
        public int TotalProductsInRule { get; set; }
    }

    public class AutomationPricePositionHistoryViewModel
    {
        public List<string> Dates { get; set; } = new();

        // <summary>Bezwzględnie najtańsi — tylko my na pozycji 1</summary>

        public List<int> Top1Solo { get; set; } = new();

        // <summary>Najtańsi ale ex aequo — nasza cena = najlepsza, ale ktoś jeszcze ją ma</summary>

        public List<int> Top1ExAequo { get; set; } = new();

        // <summary>Pozycja 2–3</summary>

        public List<int> Position2to3 { get; set; } = new();

        // <summary>Pozycja 4–5</summary>

        public List<int> Position4to5 { get; set; } = new();

        // <summary>Pozycja 6–10</summary>

        public List<int> Position6to10 { get; set; } = new();

        // <summary>Pozycja 11+</summary>

        public List<int> Position11Plus { get; set; } = new();

        public List<int> ProductsWithDataCount { get; set; } = new();

        public int TotalProductsInRule { get; set; }
    }

}