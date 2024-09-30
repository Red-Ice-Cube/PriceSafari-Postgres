namespace PriceSafari.Models
{
    public class PriceValueClass
    {
        public int PriceValueClassId { get; set; }
        public int StoreId { get; set; }
        public decimal SetPrice1 { get; set; } = 2.00m;
        public decimal SetPrice2 { get; set; } = 2.00m;
        public bool UsePriceDiff { get; set; } = true;

        public decimal SetSafariPrice1 { get; set; } = 2.00m;
        public decimal SetSafariPrice2 { get; set; } = 2.00m;
        public bool UsePriceDiffSafari { get; set; } = true;

        public StoreClass Store { get; set; }
    }
}
