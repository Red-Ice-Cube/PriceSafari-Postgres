namespace PriceSafari.Models
{
    public enum ProducerComparisonSource
    {
        /// <summary>Cena bieżąca z naszego sklepu (jak w GetPrices - myPriceEntry).</summary>
        StorePrice = 0,

        /// <summary>Cena MAP - na razie pobierana z Product.MarginPrice.</summary>
        MapPrice = 1
    }
}