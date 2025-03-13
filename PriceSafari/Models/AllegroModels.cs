namespace PriceSafari.Models
{
    public class AllegroOffersResponse
    {
        public AllegroItems Items { get; set; }
    }

    public class AllegroItems
    {
        public List<AllegroOffer> Regular { get; set; }
    }

    public class AllegroOffer
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public AllegroSeller Seller { get; set; }
        public AllegroSellingMode SellingMode { get; set; }
    }

    public class AllegroSeller
    {
        public string Id { get; set; }
        public string Login { get; set; }
    }

    public class AllegroSellingMode
    {
        public AllegroPrice Price { get; set; }
    }

    public class AllegroPrice
    {
        public string Amount { get; set; }
        public string Currency { get; set; }
    }
}
