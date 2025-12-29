using PriceSafari.Models;
using System;
using System.Collections.Generic;

namespace PriceSafari.Models.ViewModels
{
    public class AutomationDetailsViewModel
    {
        // Info o regule
        public int RuleId { get; set; }
        public string RuleName { get; set; }
        public string RuleColor { get; set; }
        public AutomationSourceType SourceType { get; set; }
        public AutomationStrategyMode StrategyMode { get; set; }
        public bool IsActive { get; set; }
        public int StoreId { get; set; }
        public string StoreName { get; set; }

        // Statystyki
        public int TotalProducts { get; set; }
        public DateTime? LastScrapDate { get; set; }

        // Lista produktów
        public List<AutomationProductRowViewModel> Products { get; set; } = new List<AutomationProductRowViewModel>();
    }

    public class AutomationProductRowViewModel
    {
        public int ProductId { get; set; } // ID produktu z tabeli Products lub AllegroProducts
        public string Name { get; set; }
        public string ImageUrl { get; set; }
        public string Identifier { get; set; } // EAN lub ID oferty

        // Ceny i Koszty
        public decimal? CurrentPrice { get; set; }
        public decimal? PurchasePrice { get; set; } // Cena zakupu / MarginPrice
        public decimal? MinPriceLimit { get; set; }
        public decimal? MaxPriceLimit { get; set; }

        // Dane z rynku (zależne od SourceType)
        public decimal? BestCompetitorPrice { get; set; }
        public string CompetitorName { get; set; }
        public decimal? MarketAveragePrice { get; set; } // Dla strategii Profit
        public string CurrentRankingAllegro { get; set; }
        public string NewRankingAllegro { get; set; }

        // Dla Price Comparison
        public string CurrentRankingGoogle { get; set; }
        public string CurrentRankingCeneo { get; set; }
        public string NewRankingGoogle { get; set; }
        public string NewRankingCeneo { get; set; }

        // Wynik kalkulacji automatyzacji
        public decimal? SuggestedPrice { get; set; }
        public decimal? PriceChange { get; set; } // Różnica Suggested - Current

        // Statusy
        public bool IsInStock { get; set; }
        public bool IsMarginWarning { get; set; } // Czy sugerowana cena łamie marżę
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

        // ZMIANA NA NULLABLE BOOL
        public bool? WasLimitedByMin { get; set; }
        public bool? WasLimitedByMax { get; set; }
    }
}