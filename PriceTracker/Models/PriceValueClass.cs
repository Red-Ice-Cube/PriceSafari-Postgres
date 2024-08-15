namespace PriceTracker.Models
{
    public class PriceValueClass
    {
        public int PriceValueClassId { get; set; }
        public int StoreId { get; set; }

        // Ustawione wartości bazowe
        public decimal SetPrice1 { get; set; } = 2.00m;
        public decimal SetPrice2 { get; set; } = 2.00m;

        // Ręcznie ustawiane wartości procentowe, bez obliczeń
        public decimal PercentageDifferenceFromSetPrice1 { get; set; } = 2.00m;
        public decimal PercentageDifferenceFromSetPrice2 { get; set; } = 2.00m;

        public StoreClass Store { get; set; }
    }
}
