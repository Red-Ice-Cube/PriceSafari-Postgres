namespace PriceSafari.Models.DTOs
{

    public class PriceBridgeItemRequest
    {
        public int ProductId { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal NewPrice { get; set; }
        public decimal? MarginPrice { get; set; }
        public string? CurrentGoogleRanking { get; set; }
        public string? CurrentCeneoRanking { get; set; }
        public string? NewGoogleRanking { get; set; }
        public string? NewCeneoRanking { get; set; }
        public string? Mode { get; set; }           
        public decimal? PriceIndexTarget { get; set; } 
        public decimal? StepPriceApplied { get; set; }
    }
}
