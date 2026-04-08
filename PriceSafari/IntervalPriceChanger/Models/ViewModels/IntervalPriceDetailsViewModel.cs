using PriceSafari.Models;
using PriceSafari.Models.ViewModels;

namespace PriceSafari.IntervalPriceChanger.Models.ViewModels
{
    public class IntervalPriceDetailsViewModel
    {

        public int IntervalRuleId { get; set; }
        public string IntervalName { get; set; }
        public string ColorHex { get; set; }
        public bool IsActive { get; set; }

        public int StoreIntervalLimit { get; set; }
        public int StoreIntervalUsed { get; set; }
        public bool IsEffectivelyActive { get; set; }

        public decimal PriceStep { get; set; }
        public bool IsPriceStepPercent { get; set; }
        public bool IsStepAActive { get; set; }

        public decimal PriceStepB { get; set; }
        public bool IsPriceStepPercentB { get; set; }
        public bool IsStepBActive { get; set; }

        public decimal PriceStepC { get; set; }
        public bool IsPriceStepPercentC { get; set; }
        public bool IsStepCActive { get; set; }

        public DateTime? NextGlobalExecution { get; set; }

        // <summary>Który krok (1/2/3) zostanie wykonany jako następny globalnie.</summary>

        public int? NextGlobalExecutionStepIdx { get; set; }
        public string ScheduleJson { get; set; }
        public int ActiveSlotsCount { get; set; }
        public int PreferredBlockSize { get; set; }

        public int ParentRuleId { get; set; }
        public string ParentRuleName { get; set; }
        public string ParentColorHex { get; set; }
        public bool ParentIsActive { get; set; }
        public AutomationSourceType SourceType { get; set; }
        public AutomationStrategyMode StrategyMode { get; set; }

        public bool EnforceMinimalMarkup { get; set; }
        public bool IsMinimalMarkupPercent { get; set; }
        public decimal MinimalMarkupValue { get; set; }
        public bool EnforceMaxMarkup { get; set; }
        public bool IsMaxMarkupPercent { get; set; }
        public decimal MaxMarkupValue { get; set; }
        public bool MarketplaceIncludeCommission { get; set; }

        public int StoreId { get; set; }
        public string StoreName { get; set; }

        public List<IntervalPriceProductRowViewModel> Products { get; set; } = new();
        public int TotalProducts { get; set; }
        public int ParentProductCount { get; set; }

        public DateTime? LastScrapDate { get; set; }
        public int? LatestScrapId { get; set; }

        public List<FlagViewModel> AvailableStoreFlags { get; set; } = new();

        public int CountReady => Products.Count(p => p.Status == IntervalProductStatus.Ready);
        public int CountBlocked => Products.Count(p => p.Status == IntervalProductStatus.Blocked);
        public int CountLimitReached => Products.Count(p => p.Status == IntervalProductStatus.LimitReached);
        public int CountPaused => Products.Count(p => p.Status == IntervalProductStatus.Paused);

    }
}