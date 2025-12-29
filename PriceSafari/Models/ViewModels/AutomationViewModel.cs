using PriceSafari.Models;
using System;
using System.Collections.Generic;
using System.Linq; // Potrzebne do LINQ (Count)

namespace PriceSafari.Models.ViewModels
{
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

        public List<AutomationProductRowViewModel> Products { get; set; } = new List<AutomationProductRowViewModel>();

        // --- NOWE POLA POMOCNICZE (do wyświetlania licznika na dashboardzie) ---
        // Dzięki temu w Widoku @Model.CountTargetMet zwróci gotową liczbę bez pisania logiki w HTML
        public int CountTargetMet => Products.Count(p => p.IsTargetMet);
        public int CountTargetUnmet => Products.Count(p => !p.IsTargetMet && p.SuggestedPrice.HasValue);
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

        // --- NOWE POLE ---
        // Określa, czy dla tego konkretnego produktu udało się zrealizować strategię (np. być liderem),
        // czy też cena została ograniczona przez limity (Min/Max).
        // True = Zielony ptaszek, False = Czerwony wykrzyknik.
        public bool IsTargetMet { get; set; }
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

        // --- NOWE POLE ---
        // Służy do przesłania informacji z JavaScript do Kontrolera (SaveBatch).
        // Kontroler zliczy "true" w tym polu, aby wypełnić TargetMetCount w bazie.
        public bool IsTargetMet { get; set; }
    }
}