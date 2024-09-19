namespace PriceSafari.Models.ManagerViewModels
{
    public class GroupedGlobalPriceReportViewModel
    {
        public string ProductName { get; set; }
        public string GoogleUrl { get; set; }
        public List<GlobalPriceReportViewModel> Prices { get; set; } = new List<GlobalPriceReportViewModel>();
    }

    public class GlobalPriceReportViewModel
    {
        public decimal Price { get; set; }
        public decimal PriceWithDelivery { get; set; }
        public string StoreName { get; set; }
        public string OfferUrl { get; set; }
        public int RegionId { get; set; }
    }
}
