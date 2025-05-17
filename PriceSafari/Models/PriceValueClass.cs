namespace PriceSafari.Models
{
    public class PriceValueClass
    {
        public int PriceValueClassId { get; set; }
        public int StoreId { get; set; }
        public decimal SetPrice1 { get; set; } = 2.00m;
        public decimal SetPrice2 { get; set; } = 2.00m;
       
        public bool UsePriceDiff { get; set; } = true;

        public decimal PriceStep { get; set; } = 2.00m;

        public decimal SetSafariPrice1 { get; set; } = 2.00m;
        public decimal SetSafariPrice2 { get; set; } = 2.00m;
        public bool UsePriceDiffSafari { get; set; } = true;



        public bool UsePriceWithDelivery { get; set; } = false;

        public bool UseEanForSimulation { get; set; } = true;
        public bool UseMarginForSimulation { get; set; } = true;

        public bool EnforceMinimalMargin { get; set; } = true;
        public decimal MinimalMarginPercent { get; set; } = 0.00m;

        public StoreClass Store { get; set; }
    }
}
