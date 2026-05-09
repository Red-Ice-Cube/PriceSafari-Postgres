namespace PriceSafari.IntervalPriceChanger.Models.ViewModels
{
    public class IntervalPriceProductRowViewModel
    {
        public int ProductId { get; set; }
        public string Name { get; set; }
        public string Identifier { get; set; }
        public string ImageUrl { get; set; }

        public bool IsPaused { get; set; }
        public DateTime? PausedUntil { get; set; }
        public List<int> FlagIds { get; set; } = new();

        public decimal? PurchasePrice { get; set; }
        public DateTime? PurchasePriceUpdatedDate { get; set; }
        public decimal? MinPriceLimit { get; set; }
        public decimal? MaxPriceLimit { get; set; }

        public decimal? MarketCurrentPrice { get; set; }

        public decimal? ApiAllegroPriceFromUser { get; set; }

        public decimal? BestCompetitorPrice { get; set; }
        public string CompetitorName { get; set; }

        public string CurrentRankingAllegro { get; set; }
        public string CurrentRankingGoogle { get; set; }
        public string CurrentRankingCeneo { get; set; }

        public bool IsBestPriceGuarantee { get; set; }
        public bool IsSuperPrice { get; set; }
        public bool IsTopOffer { get; set; }
        public bool CompetitorIsBestPriceGuarantee { get; set; }
        public bool CompetitorIsSuperPrice { get; set; }
        public bool CompetitorIsTopOffer { get; set; }

        public bool IsSubsidyActive { get; set; }
        public bool IsInAnyCampaign { get; set; }

        public decimal? CommissionAmount { get; set; }
        public bool IsCommissionIncluded { get; set; }

        public bool HasCheaperOwnOffer { get; set; }

        public decimal? IntervalCurrentPrice { get; set; }

        public DateTime? IntervalLastChangeDate { get; set; }

        public int IntervalExecutedSteps { get; set; } = 0;

        public decimal? EffectiveCurrentPrice { get; set; }

        public decimal? ProjectedNextPrice { get; set; }

        public decimal? ProjectedPriceChange { get; set; }

        public DateTime? NextExecutionTime { get; set; }

        public bool WillNextExecutionRun { get; set; }

        public decimal? CurrentMarkupAmount { get; set; }
        public decimal? CurrentMarkupPercent { get; set; }

        public decimal? ProjectedMarkupAmount { get; set; }
        public decimal? ProjectedMarkupPercent { get; set; }

        public IntervalProductStatus Status { get; set; } = IntervalProductStatus.Ready;
        public string BlockReason { get; set; }

        public bool IsLimitedByMin { get; set; }
        public bool IsLimitedByMax { get; set; }

        public bool IsMarginWarning { get; set; }

        public decimal? LastKnownPrice { get; set; }

        public DateTime? LastKnownPriceDate { get; set; }

        public LastKnownPriceSource LastKnownSource { get; set; } = LastKnownPriceSource.None;

        public int? NextStepIdx { get; set; }

        public decimal? NextStepValue { get; set; }

        public bool NextStepIsPercent { get; set; }

        public int? LastKnownStepIdx { get; set; }

        public int? TotalPopularity { get; set; }
        public int? MyTotalPopularity { get; set; }
        public decimal? MarketSharePercentage { get; set; }
    }

    public enum LastKnownPriceSource
    {
        None,
        Interval,
        Automation,
        Market
    }
    public enum IntervalProductStatus
    {
        Ready,
        Blocked,
        LimitReached,
        Paused
    }
}