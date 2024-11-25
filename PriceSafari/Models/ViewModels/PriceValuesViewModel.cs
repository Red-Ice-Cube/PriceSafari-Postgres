namespace PriceSafari.ViewModels
{
    public class PriceValuesViewModel
    {
        public int StoreId { get; set; }
        public decimal SetPrice1 { get; set; } = 2.00m;
        public decimal SetPrice2 { get; set; } = 2.00m;
        public decimal PriceStep { get; set; } = 2.00m;
        public bool usePriceDiff { get; set; } = true;
    }
}
