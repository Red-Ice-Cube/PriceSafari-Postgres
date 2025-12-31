using PriceSafari.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PriceSafari.Models.ViewModels
{
    // Nowy Enum definiujący 4 stany produktu
    public enum AutomationCalculationStatus
    {
        TargetMet,        // Zielony: Warunki spełnione, cena zmieniona
        TargetMaintained, // Niebieski: Warunki spełnione, cena bez zmian (idealna)
        PriceLimited,     // Żółty: Cena zmieniona, ale ograniczona przez widełki/marżę
        Blocked           // Czerwony: Nie można wyliczyć ceny (błąd danych, kampania itp.)
    }

    public class AutomationDetailsViewModel
    {
        public int RuleId { get; set; }
        public string RuleName { get; set; }
        public string RuleColor { get; set; }
        public AutomationSourceType SourceType { get; set; }
        public AutomationStrategyMode StrategyMode { get; set; }
        public bool IsActive { get; set; }
        public int StoreId { get; set; }
        public string StoreName { get; set; }

        public int TotalProducts { get; set; }
        public DateTime? LastScrapDate { get; set; }

        public int? LatestScrapId { get; set; }

        public List<AutomationProductRowViewModel> Products { get; set; } = new List<AutomationProductRowViewModel>();

        // --- STATYSTYKI OPARTE O NOWY STATUS ---

        // Ile produktów ma status BLOKADA (Czerwony)
        public int CountBlocked => Products.Count(p => p.Status == AutomationCalculationStatus.Blocked);

        // Ile produktów ma status LIMIT (Żółty)
        public int CountLimited => Products.Count(p => p.Status == AutomationCalculationStatus.PriceLimited);

        // Ile produktów ma status CEL UTRZYMANY (Niebieski)
        public int CountMaintained => Products.Count(p => p.Status == AutomationCalculationStatus.TargetMaintained);

        // Ile produktów ma status CEL OSIĄGNIĘTY (Zielony - zmiana ceny)
        public int CountMet => Products.Count(p => p.Status == AutomationCalculationStatus.TargetMet);

        // Statystyki dynamiki (Wzrosty/Spadki) - liczymy dla wszystkich, którzy nie są zablokowani
        public int CountIncreased => Products.Count(p => p.Status != AutomationCalculationStatus.Blocked && p.PriceChange > 0);
        public int CountDecreased => Products.Count(p => p.Status != AutomationCalculationStatus.Blocked && p.PriceChange < 0);

        // Ogólna skuteczność (wszystko co nie jest błędem/blokadą liczymy jako sukces procesu)
        public int SuccessRate => TotalProducts > 0
            ? (int)Math.Round((double)(CountMet + CountMaintained + CountLimited) / TotalProducts * 100)
            : 0;
    }

    public class AutomationProductRowViewModel
    {
        public int ProductId { get; set; }

        public string Name { get; set; }
        public string ImageUrl { get; set; }
        public string Identifier { get; set; }

        public decimal? CurrentPrice { get; set; }
        public decimal? PurchasePrice { get; set; }

        public decimal? MinPriceLimit { get; set; }
        public decimal? MaxPriceLimit { get; set; }

        public decimal? BestCompetitorPrice { get; set; }
        public string CompetitorName { get; set; }
        public decimal? MarketAveragePrice { get; set; }

        public string CurrentRankingAllegro { get; set; }
        public string NewRankingAllegro { get; set; }

        public string CurrentRankingGoogle { get; set; }
        public string CurrentRankingCeneo { get; set; }
        public string NewRankingGoogle { get; set; }
        public string NewRankingCeneo { get; set; }

        public decimal? SuggestedPrice { get; set; }
        public decimal? PriceChange { get; set; }

        public bool IsInStock { get; set; }
        public bool IsMarginWarning { get; set; }

        public decimal? CommissionAmount { get; set; }
        public bool IsCommissionIncluded { get; set; }

        public decimal? MarkupAmount { get; set; }
        public decimal? MarkupPercent { get; set; }

        public decimal? CurrentMarkupAmount { get; set; }
        public decimal? CurrentMarkupPercent { get; set; }

        public bool IsBestPriceGuarantee { get; set; }
        public bool IsSuperPrice { get; set; }
        public bool IsTopOffer { get; set; }

        public bool IsInAnyCampaign { get; set; }
        public bool IsSubsidyActive { get; set; }

        public bool CompetitorIsBestPriceGuarantee { get; set; }
        public bool CompetitorIsSuperPrice { get; set; }
        public bool CompetitorIsTopOffer { get; set; }

        public decimal? ApiAllegroPriceFromUser { get; set; }

        // --- NOWE POLA STATUSU ---

        // Główny status wiersza (zastępuje stare flagi)
        public AutomationCalculationStatus Status { get; set; }

        // Powód blokady (tekstowy)
        public string BlockReason { get; set; }

        // Helper dla kompatybilności wstecznej (czy w ogóle udało się wyliczyć cenę)
        // Zwraca true dla Zielonego, Niebieskiego i Żółtego
        public bool IsTargetMet => Status != AutomationCalculationStatus.Blocked;

        // Helper sprawdzający czy wiersz jest zablokowany (dla kompatybilności widoku)
        public bool IsBlockedByStatus => Status == AutomationCalculationStatus.Blocked;
    }

    public class AutomationExecutionRequest
    {
        public int RuleId { get; set; }
        public int StoreId { get; set; }
        public AutomationSourceType SourceType { get; set; }
        public List<AutomationProductResultDto> Products { get; set; }
    }

    public class AutomationProductResultDto
    {
        public int ProductId { get; set; }
        public string Identifier { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal NewPrice { get; set; }
        public decimal? PurchasePrice { get; set; }

        public string CurrentRankingAllegro { get; set; }
        public string NewRankingAllegro { get; set; }
        public string CurrentRankingGoogle { get; set; }
        public string CurrentRankingCeneo { get; set; }
        public string NewRankingGoogle { get; set; }
        public string NewRankingCeneo { get; set; }

        public decimal? MinPriceLimit { get; set; }
        public decimal? MaxPriceLimit { get; set; }

        public bool? WasLimitedByMin { get; set; }
        public bool? WasLimitedByMax { get; set; }

        public bool IsTargetMet { get; set; }
    }
}