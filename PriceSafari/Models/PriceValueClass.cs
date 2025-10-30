namespace PriceSafari.Models
{
    public class PriceValueClass
    {
        public int PriceValueClassId { get; set; }
        public int StoreId { get; set; }
        public decimal SetPrice1 { get; set; } = 2.00m;
        public decimal SetPrice2 { get; set; } = 2.00m;
       
        public bool UsePriceDiff { get; set; } = true;

        public decimal PriceStep { get; set; } = -0.01m;

        public decimal SetSafariPrice1 { get; set; } = 2.00m;
        public decimal SetSafariPrice2 { get; set; } = 2.00m;
        public bool UsePriceDiffSafari { get; set; } = true;



        public bool UsePriceWithDelivery { get; set; } = false;

        public string IdentifierForSimulation { get; set; } = "EAN";
        public bool UseMarginForSimulation { get; set; } = true;

        public bool EnforceMinimalMargin { get; set; } = true;
        public decimal MinimalMarginPercent { get; set; } = 0.00m;







        // sekcja allegro


        public bool AllegroUsePriceDiff { get; set; } = true;
        public decimal AllegroPriceStep { get; set; } = -0.01m;
        public decimal AllegroSetPrice1 { get; set; } = 2.00m;
        public decimal AllegroSetPrice2 { get; set; } = 2.00m;


      

        public string AllegroIdentifierForSimulation { get; set; } = "ID";
        public bool AllegroUseMarginForSimulation { get; set; } = true;

        public bool AllegroEnforceMinimalMargin { get; set; } = true;
        public decimal AllegroMinimalMarginPercent { get; set; } = 0.00m;

        //nowe element allegro - czy uwzgledniac prowizje w zmianie ceny
        public bool AllegroIncludeCommisionInPriceChange { get; set; } = false;



        public StoreClass Store { get; set; }
    }
}
