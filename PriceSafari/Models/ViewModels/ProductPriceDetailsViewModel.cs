namespace PriceSafari.Models.ViewModels
{
    public class ProductPriceDetailsViewModel
    {
        public string ProductName { get; set; }
        public string? ProductImg { get; set; }
        public int ReportId { get; set; }
        public string? GoogleProductUrl { get; set; }
        public string MyStore { get; set; }
        public List<PriceDetailsViewModel> Prices { get; set; }
    }

    public class PriceDetailsViewModel
    {
        public string StoreName { get; set; }
        public string RegionName { get; set; }
        public decimal CalculatedPrice { get; set; }
        public decimal PriceWithDelivery { get; set; }
        public string OfferUrl { get; set; }
    }

}
