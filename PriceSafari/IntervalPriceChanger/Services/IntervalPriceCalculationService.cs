using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Enums;
using PriceSafari.IntervalPriceChanger.Models;
using PriceSafari.IntervalPriceChanger.Models.ViewModels;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;

namespace PriceSafari.IntervalPriceChanger.Services
{
    public class IntervalPriceCalculationService
    {
        private readonly PriceSafariContext _context;

        public IntervalPriceCalculationService(PriceSafariContext context)
        {
            _context = context;
        }

        // ═══════════════════════════════════════════════════════
        // GŁÓWNA METODA — przygotowanie danych dla Details
        // ═══════════════════════════════════════════════════════

        public async Task<IntervalPriceDetailsViewModel> PrepareDetailsDataAsync(IntervalPriceRule rule)
        {
            var parent = rule.AutomationRule;
            var store = parent?.Store;
            bool isMarketplace = parent.SourceType == AutomationSourceType.Marketplace;

            var model = new IntervalPriceDetailsViewModel
            {
                IntervalRuleId = rule.Id,
                IntervalName = rule.Name,
                ColorHex = rule.ColorHex,
                IsActive = rule.IsActive,
                IsEffectivelyActive = rule.IsEffectivelyActive,

                // Kroki A/B/C
                PriceStep = rule.PriceStep,
                IsPriceStepPercent = rule.IsPriceStepPercent,
                IsStepAActive = rule.IsStepAActive,
                PriceStepB = rule.PriceStepB,
                IsPriceStepPercentB = rule.IsPriceStepPercentB,
                IsStepBActive = rule.IsStepBActive,
                PriceStepC = rule.PriceStepC,
                IsPriceStepPercentC = rule.IsPriceStepPercentC,
                IsStepCActive = rule.IsStepCActive,

                ScheduleJson = rule.ScheduleJson,
                ActiveSlotsCount = rule.ActiveSlotsCount,
                PreferredBlockSize = rule.PreferredBlockSize,

                ParentRuleId = parent.Id,
                ParentRuleName = parent.Name,
                ParentColorHex = parent.ColorHex,
                ParentIsActive = parent.IsActive,
                SourceType = parent.SourceType,
                StrategyMode = parent.StrategyMode,

                EnforceMinimalMarkup = parent.EnforceMinimalMarkup,
                IsMinimalMarkupPercent = parent.IsMinimalMarkupPercent,
                MinimalMarkupValue = parent.MinimalMarkupValue,
                EnforceMaxMarkup = parent.EnforceMaxMarkup,
                IsMaxMarkupPercent = parent.IsMaxMarkupPercent,
                MaxMarkupValue = parent.MaxMarkupValue,
                MarketplaceIncludeCommission = parent.MarketplaceIncludeCommission,

                StoreId = parent.StoreId,
                StoreName = store?.StoreName ?? "Nieznany sklep",
            };

            // Następne globalne wykonanie — z uwzględnieniem aktywności kroków A/B/C
            var nextExec = rule.FindNextActiveExecution(DateTime.Now);
            model.NextGlobalExecution = nextExec?.time;
            model.NextGlobalExecutionStepIdx = nextExec?.stepIdx;

            // Liczba produktów w automacie-rodzicu
            model.ParentProductCount = await _context.AutomationProductAssignments
                .CountAsync(a => a.AutomationRuleId == parent.Id);

            if (isMarketplace)
                await PrepareMarketplaceProducts(rule, model);
            else
                await PrepareComparisonProducts(rule, model);

            model.TotalProducts = model.Products.Count;
            model.AvailableStoreFlags = await GetStoreFlagsAsync(parent.StoreId, isMarketplace);

            return model;
        }

        // ═══════════════════════════════════════════════════════
        // MARKETPLACE (Allegro)
        // ═══════════════════════════════════════════════════════

        private async Task PrepareMarketplaceProducts(IntervalPriceRule rule, IntervalPriceDetailsViewModel model)
        {
            var parent = rule.AutomationRule;
            string myStoreName = parent.Store.StoreNameAllegro ?? "";

            // Pobierz przypisania interwału
            var assignments = await _context.IntervalPriceProductAssignments
                .Where(a => a.IntervalPriceRuleId == rule.Id && a.AllegroProductId.HasValue)
                .Include(a => a.AllegroProduct)
                .ToListAsync();

            if (!assignments.Any()) return;

            var productIds = assignments.Select(a => a.AllegroProductId.Value).ToList();

            // Ostatni scrap
            var latestScrap = await _context.AllegroScrapeHistories
                .Where(sh => sh.StoreId == parent.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            model.LastScrapDate = latestScrap?.Date;
            model.LatestScrapId = latestScrap?.Id;

            if (latestScrap == null) return;
            int scrapId = latestScrap.Id;

            // Historia cen ze scrapu
            var priceHistories = await _context.AllegroPriceHistories
                .Where(ph => ph.AllegroScrapeHistoryId == scrapId && productIds.Contains(ph.AllegroProductId))
                .ToListAsync();

            priceHistories = priceHistories
                .GroupBy(ph => new { ph.AllegroProductId, ph.IdAllegro })
                .Select(g => g.First())
                .ToList();

            // Extended info (dopłaty, prowizja)
            var extendedInfos = await _context.AllegroPriceHistoryExtendedInfos
                .Where(x => x.ScrapHistoryId == scrapId && productIds.Contains(x.AllegroProductId))
                .ToListAsync();

            // Committed changes z automatu-rodzica
            var committedChanges = await _context.AllegroPriceBridgeItems
                .Include(i => i.PriceBridgeBatch)
                .Where(i => i.PriceBridgeBatch.StoreId == parent.StoreId
                         && i.PriceBridgeBatch.AllegroScrapeHistoryId == scrapId
                         && i.Success)
                .ToListAsync();

            var committedLookup = committedChanges
                .GroupBy(i => i.AllegroProductId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.PriceBridgeBatch.ExecutionDate).First());

            // Flagi hurtowo
            var productFlagsLookup = await _context.ProductFlags
                .Where(pf => pf.AllegroProductId.HasValue
                          && productIds.Contains(pf.AllegroProductId.Value)
                          && pf.Flag != null
                          && pf.Flag.IsMarketplace)
                .GroupBy(pf => pf.AllegroProductId.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            DateTime? nextExecution = model.NextGlobalExecution;
            int? nextExecStepIdx = model.NextGlobalExecutionStepIdx;

            // ═══ OSTATNIE WYKONANIA INTERWAŁU (z literą kroku) ═══
            // Pobieramy surowo i grupujemy client-side — EF ma problem z wyborem całego rekordu z grupy
            var intervalExecutionRaw = await _context.Set<IntervalPriceExecutionItem>()
                .Where(i => i.Batch.IntervalPriceRuleId == rule.Id
                         && i.Success
                         && i.AllegroProductId.HasValue)
                .Select(i => new
                {
                    AllegroProductId = i.AllegroProductId.Value,
                    Price = i.PriceAfterVerified ?? i.PriceAfterTarget,
                    Date = i.Batch.ExecutionDate,
                    StepLetter = i.StepLetter
                })
                .ToListAsync();

            var intervalExecutionData = intervalExecutionRaw
                .GroupBy(x => x.AllegroProductId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var latest = g.OrderByDescending(x => x.Date).First();
                        return new
                        {
                            LatestPrice = latest.Price,
                            LatestDate = latest.Date,
                            StepLetter = latest.StepLetter,
                            TotalSteps = g.Count()
                        };
                    });

            foreach (var item in assignments)
            {
                var p = item.AllegroProduct;
                if (p == null) continue;

                var histories = priceHistories.Where(h => h.AllegroProductId == p.AllegroProductId).ToList();

                long? targetOfferId = null;
                if (long.TryParse(p.IdOnAllegro, out var parsedId))
                    targetOfferId = parsedId;

                AllegroPriceHistory myHistory = null;
                if (targetOfferId.HasValue)
                    myHistory = histories.FirstOrDefault(h => h.IdAllegro == targetOfferId.Value);
                else
                    myHistory = histories.FirstOrDefault(h => h.SellerName != null && h.SellerName.Equals(myStoreName, StringComparison.OrdinalIgnoreCase));

                var extInfo = extendedInfos.FirstOrDefault(x => x.AllegroProductId == p.AllegroProductId);

                // Najlepszy konkurent
                var competitors = histories
                    .Where(h => h.Price > 0
                        && (targetOfferId == null || h.IdAllegro != targetOfferId)
                        && (h.SellerName == null || !h.SellerName.Equals(myStoreName, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(h => h.Price)
                    .ToList();

                var bestCompetitor = competitors.FirstOrDefault();

                // Sprawdź tańszą własną ofertę
                bool hasCheaperOwn = false;
                if (myHistory != null && myHistory.Price > 0)
                {
                    hasCheaperOwn = histories
                        .Any(h => h.SellerName != null
                            && h.SellerName.Equals(myStoreName, StringComparison.OrdinalIgnoreCase)
                            && h.IdAllegro != myHistory.IdAllegro
                            && h.Price > 0 && h.Price < myHistory.Price);
                }

                // Ranking
                var competitorPrices = competitors.Select(c => c.Price).ToList();
                string currentRank = "-";
                if (myHistory != null && myHistory.Price > 0)
                    currentRank = CalculateRanking(competitorPrices, myHistory.Price);

                decimal? marketPrice = myHistory?.Price;
                decimal? committedPrice = null;
                decimal? committedCommission = null;

                if (committedLookup.TryGetValue(p.AllegroProductId, out var ci))
                {
                    committedPrice = ci.PriceAfter_Verified ?? ci.PriceAfter_Simulated;
                    committedCommission = ci.CommissionAfter_Verified;
                }

                var row = new IntervalPriceProductRowViewModel
                {
                    ProductId = p.AllegroProductId,
                    Name = p.AllegroProductName,
                    Identifier = p.IdOnAllegro,
                    ImageUrl = null,

                    PurchasePrice = p.AllegroMarginPrice,
                    PurchasePriceUpdatedDate = p.AllegroMarginPriceUpdatedDate,

                    MarketCurrentPrice = marketPrice,
                    ApiAllegroPriceFromUser = extInfo?.ApiAllegroPriceFromUser,

                    BestCompetitorPrice = bestCompetitor?.Price,
                    CompetitorName = bestCompetitor?.SellerName,

                    CurrentRankingAllegro = currentRank,

                    IsBestPriceGuarantee = myHistory?.IsBestPriceGuarantee ?? false,
                    IsSuperPrice = myHistory?.SuperPrice ?? false,
                    IsTopOffer = myHistory?.TopOffer ?? false,
                    CompetitorIsBestPriceGuarantee = bestCompetitor?.IsBestPriceGuarantee ?? false,
                    CompetitorIsSuperPrice = bestCompetitor?.SuperPrice ?? false,
                    CompetitorIsTopOffer = bestCompetitor?.TopOffer ?? false,

                    IsSubsidyActive = extInfo?.IsSubsidyActive ?? false,
                    IsInAnyCampaign = extInfo?.AnyPromoActive ?? false,

                    CommissionAmount = committedCommission ?? extInfo?.ApiAllegroCommission,
                    IsCommissionIncluded = parent.MarketplaceIncludeCommission,

                    HasCheaperOwnOffer = hasCheaperOwn,

                    FlagIds = productFlagsLookup.ContainsKey(p.AllegroProductId)
                        ? productFlagsLookup[p.AllegroProductId]
                        : new List<int>(),

                    LastKnownPrice = null,
                    LastKnownPriceDate = null,
                    LastKnownSource = LastKnownPriceSource.None,
                };

                // ═══ OSTATNIA ZNANA CENA — najnowsza z: interwał, automat, scrap ═══
                {
                    decimal? lkPrice = null;
                    DateTime? lkDate = null;
                    var lkSource = LastKnownPriceSource.None;
                    int? lkStepIdx = null;

                    // Kandydat 1: Interwał
                    if (intervalExecutionData.TryGetValue(p.AllegroProductId, out var execData))
                    {
                        lkPrice = execData.LatestPrice;
                        lkDate = execData.LatestDate;
                        lkSource = LastKnownPriceSource.Interval;
                        lkStepIdx = LetterToStepIdx(execData.StepLetter);
                    }

                    // Kandydat 2: Automat (committed) — jeśli nowszy
                    if (committedLookup.TryGetValue(p.AllegroProductId, out var ciLk))
                    {
                        var commitDate = ciLk.PriceBridgeBatch.ExecutionDate;
                        var commitPrice = ciLk.PriceAfter_Verified ?? ciLk.PriceAfter_Simulated;
                        if (!lkDate.HasValue || commitDate > lkDate.Value)
                        {
                            lkPrice = commitPrice;
                            lkDate = commitDate;
                            lkSource = LastKnownPriceSource.Automation;
                            lkStepIdx = null; // Automat nie ma kroku A/B/C
                        }
                    }

                    // Kandydat 3: Scrap — fallback
                    if (!lkPrice.HasValue && marketPrice.HasValue && marketPrice.Value > 0)
                    {
                        lkPrice = marketPrice;
                        lkDate = latestScrap?.Date;
                        lkSource = LastKnownPriceSource.Market;
                        lkStepIdx = null;
                    }

                    row.LastKnownPrice = lkPrice;
                    row.LastKnownPriceDate = lkDate;
                    row.LastKnownSource = lkSource;
                    row.LastKnownStepIdx = lkStepIdx;
                }

                // Następny krok — globalny dla wszystkich produktów interwału
                row.NextStepIdx = nextExecStepIdx;
                row.NextExecutionTime = nextExecution;

                // Limity min/max
                CalculatePriceLimits(parent, row);

                // Efektywna cena bazowa i status (używa NextStepIdx → musi być ustawione wcześniej)
                DetermineEffectivePriceAndStatus(rule, parent, row, myHistory != null && myHistory.Price > 0, committedPrice);

                // Narzut obecny
                CalculateCurrentMarkup(row);

                // Projected step (używa NextStepIdx)
                CalculateProjectedStep(rule, row);
                CalculateProjectedMarkup(row);

                row.WillNextExecutionRun = rule.IsEffectivelyActive && row.Status == IntervalProductStatus.Ready;

                model.Products.Add(row);
            }
        }

        // ═══════════════════════════════════════════════════════
        // COMPARISON (Ceneo / Google)
        // ═══════════════════════════════════════════════════════

        private async Task PrepareComparisonProducts(IntervalPriceRule rule, IntervalPriceDetailsViewModel model)
        {
            var parent = rule.AutomationRule;
            string myStoreName = parent.Store.StoreName ?? "";

            var assignments = await _context.IntervalPriceProductAssignments
                .Where(a => a.IntervalPriceRuleId == rule.Id && a.ProductId.HasValue)
                .Include(a => a.Product)
                .ToListAsync();

            if (!assignments.Any()) return;

            var productIds = assignments.Select(a => a.ProductId.Value).ToList();

            var latestScrap = await _context.ScrapHistories
                .Where(sh => sh.StoreId == parent.StoreId)
                .OrderByDescending(sh => sh.Date)
                .Select(sh => new { sh.Id, sh.Date })
                .FirstOrDefaultAsync();

            model.LastScrapDate = latestScrap?.Date;
            model.LatestScrapId = latestScrap?.Id;

            if (latestScrap == null) return;
            int scrapId = latestScrap.Id;

            var priceHistories = await _context.PriceHistories
                .Where(ph => ph.ScrapHistoryId == scrapId && productIds.Contains(ph.ProductId))
                .ToListAsync();

            // Committed changes z automatu-rodzica
            var committedChanges = await _context.PriceBridgeItems
                .Include(i => i.Batch)
                .Where(i => i.Batch.StoreId == parent.StoreId
                         && i.Batch.ScrapHistoryId == scrapId
                         && i.Success)
                .ToListAsync();

            var committedLookup = committedChanges
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Batch.ExecutionDate).First());

            var productFlagsLookup = await _context.ProductFlags
                .Where(pf => pf.ProductId.HasValue
                          && productIds.Contains(pf.ProductId.Value)
                          && pf.Flag != null
                          && !pf.Flag.IsMarketplace)
                .GroupBy(pf => pf.ProductId.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Select(pf => pf.FlagId).ToList());

            // ═══ OSTATNIE WYKONANIA INTERWAŁU (z literą kroku) ═══
            var intervalExecutionRaw = await _context.Set<IntervalPriceExecutionItem>()
                .Where(i => i.Batch.IntervalPriceRuleId == rule.Id
                         && i.Success
                         && i.ProductId.HasValue)
                .Select(i => new
                {
                    ProductId = i.ProductId.Value,
                    Price = i.PriceAfterVerified ?? i.PriceAfterTarget,
                    Date = i.Batch.ExecutionDate,
                    StepLetter = i.StepLetter
                })
                .ToListAsync();

            var intervalExecutionData = intervalExecutionRaw
                .GroupBy(x => x.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var latest = g.OrderByDescending(x => x.Date).First();
                        return new
                        {
                            LatestPrice = latest.Price,
                            LatestDate = latest.Date,
                            StepLetter = latest.StepLetter,
                            TotalSteps = g.Count()
                        };
                    });

            DateTime? nextExecution = model.NextGlobalExecution;
            int? nextExecStepIdx = model.NextGlobalExecutionStepIdx;

            foreach (var item in assignments)
            {
                var p = item.Product;
                if (p == null) continue;

                var histories = priceHistories.Where(h => h.ProductId == p.ProductId).ToList();

                var myHistory = histories.FirstOrDefault(h => h.StoreName != null && h.StoreName.Contains(myStoreName, StringComparison.OrdinalIgnoreCase))
                                ?? histories.FirstOrDefault(h => h.StoreName == null);

                var competitors = histories
                    .Where(h => h.Price > 0 && h != myHistory
                        && (h.StoreName == null || !h.StoreName.Contains(myStoreName, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(h => h.Price)
                    .ToList();

                var bestCompetitor = competitors.FirstOrDefault();

                var googlePrices = competitors.Where(c => c.IsGoogle == true).Select(c => c.Price).ToList();
                var ceneoPrices = competitors.Where(c => c.IsGoogle != true).Select(c => c.Price).ToList();

                string rankGoogle = null, rankCeneo = null;
                if (myHistory != null && myHistory.Price > 0)
                {
                    rankGoogle = CalculateRanking(googlePrices, myHistory.Price);
                    rankCeneo = CalculateRanking(ceneoPrices, myHistory.Price);
                }

                decimal? committedPrice = null;
                if (committedLookup.TryGetValue(p.ProductId, out var ci))
                    committedPrice = ci.PriceAfter;

                var row = new IntervalPriceProductRowViewModel
                {
                    ProductId = p.ProductId,
                    Name = p.ProductName,
                    Identifier = p.Ean,
                    ImageUrl = p.MainUrl,

                    PurchasePrice = p.MarginPrice,
                    PurchasePriceUpdatedDate = p.MarginPriceUpdatedDate,

                    MarketCurrentPrice = myHistory?.Price,

                    BestCompetitorPrice = bestCompetitor?.Price,
                    CompetitorName = bestCompetitor?.StoreName,

                    CurrentRankingGoogle = rankGoogle,
                    CurrentRankingCeneo = rankCeneo,

                    IsCommissionIncluded = false,

                    FlagIds = productFlagsLookup.ContainsKey(p.ProductId)
                        ? productFlagsLookup[p.ProductId]
                        : new List<int>(),

                    LastKnownPrice = null,
                    LastKnownPriceDate = null,
                    LastKnownSource = LastKnownPriceSource.None,
                };

                // ═══ OSTATNIA ZNANA CENA ═══
                {
                    decimal? lkPrice = null;
                    DateTime? lkDate = null;
                    var lkSource = LastKnownPriceSource.None;
                    int? lkStepIdx = null;

                    if (intervalExecutionData.TryGetValue(p.ProductId, out var execData))
                    {
                        lkPrice = execData.LatestPrice;
                        lkDate = execData.LatestDate;
                        lkSource = LastKnownPriceSource.Interval;
                        lkStepIdx = LetterToStepIdx(execData.StepLetter);
                    }

                    if (committedLookup.TryGetValue(p.ProductId, out var ciLk))
                    {
                        var commitDate = ciLk.Batch.ExecutionDate;
                        var commitPrice = ciLk.PriceAfter;
                        if (!lkDate.HasValue || commitDate > lkDate.Value)
                        {
                            lkPrice = commitPrice;
                            lkDate = commitDate;
                            lkSource = LastKnownPriceSource.Automation;
                            lkStepIdx = null;
                        }
                    }

                    if (!lkPrice.HasValue && (myHistory?.Price ?? 0) > 0)
                    {
                        lkPrice = myHistory.Price;
                        lkDate = latestScrap?.Date;
                        lkSource = LastKnownPriceSource.Market;
                        lkStepIdx = null;
                    }

                    row.LastKnownPrice = lkPrice;
                    row.LastKnownPriceDate = lkDate;
                    row.LastKnownSource = lkSource;
                    row.LastKnownStepIdx = lkStepIdx;
                }

                // Następny krok
                row.NextStepIdx = nextExecStepIdx;
                row.NextExecutionTime = nextExecution;

                CalculatePriceLimits(parent, row);
                DetermineEffectivePriceAndStatus(rule, parent, row, myHistory != null && myHistory.Price > 0, committedPrice);
                CalculateCurrentMarkup(row);
                CalculateProjectedStep(rule, row);
                CalculateProjectedMarkup(row);

                row.WillNextExecutionRun = rule.IsEffectivelyActive && row.Status == IntervalProductStatus.Ready;

                model.Products.Add(row);
            }
        }

        // ═══════════════════════════════════════════════════════
        // KALKULACJE
        // ═══════════════════════════════════════════════════════

        private void CalculatePriceLimits(AutomationRule parent, IntervalPriceProductRowViewModel row)
        {
            decimal extraCost = 0;
            if (parent.SourceType == AutomationSourceType.Marketplace
                && parent.MarketplaceIncludeCommission
                && row.CommissionAmount.HasValue)
            {
                extraCost = row.CommissionAmount.Value;
            }

            if (parent.EnforceMinimalMarkup && row.PurchasePrice.HasValue && row.PurchasePrice > 0)
            {
                decimal minLimit;
                if (parent.IsMinimalMarkupPercent)
                    minLimit = row.PurchasePrice.Value + (row.PurchasePrice.Value * (parent.MinimalMarkupValue / 100)) + extraCost;
                else
                    minLimit = row.PurchasePrice.Value + parent.MinimalMarkupValue + extraCost;

                row.MinPriceLimit = Math.Round(minLimit, 2);
            }

            if (parent.EnforceMaxMarkup && row.PurchasePrice.HasValue && row.PurchasePrice > 0)
            {
                decimal maxLimit;
                if (parent.IsMaxMarkupPercent)
                    maxLimit = row.PurchasePrice.Value + (row.PurchasePrice.Value * (parent.MaxMarkupValue / 100)) + extraCost;
                else
                    maxLimit = row.PurchasePrice.Value + parent.MaxMarkupValue + extraCost;

                row.MaxPriceLimit = Math.Round(maxLimit, 2);
            }
        }

        private void DetermineEffectivePriceAndStatus(
            IntervalPriceRule rule,
            AutomationRule parent,
            IntervalPriceProductRowViewModel row,
            bool hasScrapedPrice,
            decimal? committedPrice)
        {
            decimal? effectivePrice = row.LastKnownPrice
                ?? row.ApiAllegroPriceFromUser
                ?? row.MarketCurrentPrice;

            row.EffectiveCurrentPrice = effectivePrice;

            if (!hasScrapedPrice && row.LastKnownPrice == null)
            { ApplyBlock(row, "Brak danych ze scrapu"); return; }

            if (!effectivePrice.HasValue || effectivePrice.Value <= 0)
            { ApplyBlock(row, "Brak ceny bazowej"); return; }

            if (!row.PurchasePrice.HasValue || row.PurchasePrice.Value <= 0)
            { ApplyBlock(row, "Brak ceny zakupu"); return; }

            if (!row.MinPriceLimit.HasValue)
            { ApplyBlock(row, "Brak limitu Min"); return; }

            if (row.MinPriceLimit.HasValue && row.MaxPriceLimit.HasValue
                && row.MinPriceLimit.Value > row.MaxPriceLimit.Value)
            { ApplyBlock(row, "Konflikt Min/Max"); return; }

            // Sprawdź limit używając kroku który BĘDZIE wykonany (NextStepIdx)
            if (row.NextStepIdx.HasValue && row.NextStepIdx.Value > 0)
            {
                decimal stepVal = rule.GetStepValue(row.NextStepIdx.Value);
                if (row.MinPriceLimit.HasValue && stepVal < 0 && effectivePrice.Value <= row.MinPriceLimit.Value)
                {
                    row.Status = IntervalProductStatus.LimitReached;
                    row.BlockReason = "Osiągnięto Min";
                    return;
                }
                if (row.MaxPriceLimit.HasValue && stepVal > 0 && effectivePrice.Value >= row.MaxPriceLimit.Value)
                {
                    row.Status = IntervalProductStatus.LimitReached;
                    row.BlockReason = "Osiągnięto Max";
                    return;
                }
            }

            if (!rule.IsEffectivelyActive)
            {
                row.Status = IntervalProductStatus.Paused;
                row.BlockReason = rule.IsActive ? "Rodzic wyłączony" : "Interwał wyłączony";
                return;
            }

            row.Status = IntervalProductStatus.Ready;
        }

        private void CalculateProjectedStep(IntervalPriceRule rule, IntervalPriceProductRowViewModel row)
        {
            if (!row.EffectiveCurrentPrice.HasValue || row.EffectiveCurrentPrice.Value <= 0)
                return;

            int stepIdx = row.NextStepIdx ?? 0;
            if (stepIdx == 0) return;

            decimal stepVal = rule.GetStepValue(stepIdx);
            bool isPct = rule.IsStepPercent(stepIdx);

            row.NextStepValue = stepVal;
            row.NextStepIsPercent = isPct;

            decimal basePrice = row.EffectiveCurrentPrice.Value;
            decimal step = isPct ? basePrice * (stepVal / 100m) : stepVal;

            decimal projected = basePrice + step;

            if (row.MinPriceLimit.HasValue && projected < row.MinPriceLimit.Value)
            {
                projected = row.MinPriceLimit.Value;
                row.IsLimitedByMin = true;
            }
            if (row.MaxPriceLimit.HasValue && projected > row.MaxPriceLimit.Value)
            {
                projected = row.MaxPriceLimit.Value;
                row.IsLimitedByMax = true;
            }

            row.ProjectedNextPrice = Math.Round(projected, 2);
            row.ProjectedPriceChange = Math.Round(row.ProjectedNextPrice.Value - Math.Round(basePrice, 2), 2);

            if (row.PurchasePrice.HasValue && row.PurchasePrice > 0)
            {
                decimal commCost = (row.IsCommissionIncluded && row.CommissionAmount.HasValue) ? row.CommissionAmount.Value : 0;
                decimal projectedMarkup = projected - row.PurchasePrice.Value - commCost;
                if (projectedMarkup < 0) row.IsMarginWarning = true;
            }
        }

        private void CalculateCurrentMarkup(IntervalPriceProductRowViewModel row)
        {
            decimal basePrice = row.EffectiveCurrentPrice ?? 0;
            if (basePrice <= 0 || !row.PurchasePrice.HasValue || row.PurchasePrice.Value <= 0)
                return;

            decimal commCost = (row.IsCommissionIncluded && row.CommissionAmount.HasValue) ? row.CommissionAmount.Value : 0;
            row.CurrentMarkupAmount = basePrice - row.PurchasePrice.Value - commCost;
            row.CurrentMarkupPercent = (row.CurrentMarkupAmount.Value / row.PurchasePrice.Value) * 100;
        }

        private void CalculateProjectedMarkup(IntervalPriceProductRowViewModel row)
        {
            if (!row.ProjectedNextPrice.HasValue || !row.PurchasePrice.HasValue || row.PurchasePrice.Value <= 0)
                return;

            decimal commCost = (row.IsCommissionIncluded && row.CommissionAmount.HasValue) ? row.CommissionAmount.Value : 0;
            row.ProjectedMarkupAmount = row.ProjectedNextPrice.Value - row.PurchasePrice.Value - commCost;
            row.ProjectedMarkupPercent = (row.ProjectedMarkupAmount.Value / row.PurchasePrice.Value) * 100;
        }

        private void ApplyBlock(IntervalPriceProductRowViewModel row, string reason)
        {
            row.Status = IntervalProductStatus.Blocked;
            row.BlockReason = reason;
            row.ProjectedNextPrice = null;
            row.ProjectedPriceChange = null;
        }

        // ═══════════════════════════════════════════════════════
        // HARMONOGRAM — LEGACY (zachowane dla wstecznej kompatybilności)
        // ═══════════════════════════════════════════════════════

        public static DateTime? CalculateNextExecutionTime(string scheduleJson)
        {
            // Stara wersja — zwraca pierwszy slot NIEZALEŻNIE od aktywności kroków.
            // Nowy kod używa IntervalPriceRule.FindNextActiveExecution.
            if (string.IsNullOrEmpty(scheduleJson)) return null;

            int[][] schedule;
            try
            {
                schedule = System.Text.Json.JsonSerializer.Deserialize<int[][]>(scheduleJson);
                if (schedule == null || schedule.Length != 7) return null;
            }
            catch { return null; }

            var now = DateTime.Now;
            int currentDayIndex = ((int)now.DayOfWeek + 6) % 7;
            int currentSlot = (now.Hour * 60 + now.Minute) / 10;

            for (int dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                int dayIdx = (currentDayIndex + dayOffset) % 7;
                var daySlots = schedule[dayIdx];
                if (daySlots == null || daySlots.Length != 144) continue;

                int startSlot = (dayOffset == 0) ? currentSlot + 1 : 0;

                for (int s = startSlot; s < 144; s++)
                {
                    if (daySlots[s] > 0)
                    {
                        var targetDate = now.Date.AddDays(dayOffset);
                        return targetDate.AddMinutes(s * 10);
                    }
                }
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════
        // HELPERY
        // ═══════════════════════════════════════════════════════

        private static int? LetterToStepIdx(string letter) => letter switch
        {
            "A" => 1,
            "B" => 2,
            "C" => 3,
            _ => (int?)null
        };

        private string CalculateRanking(List<decimal> competitors, decimal myPrice)
        {
            var allPrices = new List<decimal>(competitors) { myPrice };
            allPrices.Sort();
            int firstIndex = allPrices.IndexOf(myPrice);
            if (firstIndex == -1) return "-";
            int lastIndex = allPrices.LastIndexOf(myPrice);
            int startRank = firstIndex + 1;
            int endRank = lastIndex + 1;
            int totalCount = allPrices.Count;
            return startRank == endRank
                ? $"{startRank}/{totalCount}"
                : $"{startRank}-{endRank}/{totalCount}";
        }

        private async Task<List<FlagViewModel>> GetStoreFlagsAsync(int storeId, bool isMarketplace)
        {
            return await _context.Flags
                .Where(f => f.StoreId == storeId && f.IsMarketplace == isMarketplace)
                .Select(f => new FlagViewModel
                {
                    FlagId = f.FlagId,
                    FlagName = f.FlagName,
                    FlagColor = f.FlagColor,
                    IsMarketplace = f.IsMarketplace
                })
                .ToListAsync();
        }
    }
}