namespace PriceSafari.Models.DTOs
{
    public class PauseProductRequest
    {
        public int RuleId { get; set; }
        public int ProductId { get; set; }
        /// <summary>1, 4, 6, 12, 24 — godziny. -1 = do odwołania.</summary>
        public int DurationHours { get; set; }
    }

    public class UnpauseProductRequest
    {
        public int RuleId { get; set; }
        public int ProductId { get; set; }
    }
}