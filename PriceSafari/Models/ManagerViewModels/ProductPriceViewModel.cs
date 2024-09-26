namespace PriceSafari.Models.ManagerViewModels
{
    public class GroupedProductViewModel
    {
        public string GoogleUrl { get; set; }
        public int RaportId { get; set; } // Dodane pole RaportId
        public List<string> ProductNames { get; set; } = new List<string>();
        public List<RegionPriceViewModel> RegionPrices { get; set; } = new List<RegionPriceViewModel>();
    }

    public class RegionPriceViewModel
    {
        public int RegionId { get; set; }
        public decimal Price { get; set; }
        public string RawPrice { get; set; }
        public decimal PriceWithDelivery { get; set; }
        public string RawPriceWithDelivery { get; set; }
        public string StoreName { get; set; }
        public string OfferUrl { get; set; }
    }
}
