namespace PriceSafari.Models.DTOs
{
    public class AllegroPriceBridgeItemRequest
    {
       
            public int ProductId { get; set; }      // Wewnętrzne ID (AllegroProductId)
            public string OfferId { get; set; }     // ID oferty Allegro (np. 123456789)

            public decimal? MarginPrice { get; set; }
            public bool IncludeCommissionInMargin { get; set; }

            public decimal PriceBefore { get; set; }
            public decimal? CommissionBefore { get; set; }
            public string RankingBefore { get; set; }

            public decimal PriceAfter_Simulated { get; set; }
            public string RankingAfter_Simulated { get; set; }

            // Pola sterujące logiką (dla logów)
            public string? Mode { get; set; }
            public decimal? PriceIndexTarget { get; set; }
            public decimal? StepPriceApplied { get; set; }

            // Dodatkowe pola limitów (opcjonalne, ale przydatne do logowania w batchu)
            public decimal? MinPriceLimit { get; set; }
            public decimal? MaxPriceLimit { get; set; }
            public bool? WasLimitedByMin { get; set; }
            public bool? WasLimitedByMax { get; set; }
        
    }
}
