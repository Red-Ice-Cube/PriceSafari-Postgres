namespace PriceSafari.Models.ViewModels
{
    public class SafariReportAnalysisViewModel
    {
        public string ReportName { get; set; }
        public DateTime CreatedDate { get; set; }
        public string StoreName { get; set; }  // Nazwa sklepu, który utworzył raport
        public List<ProductPriceViewModel> ProductPrices { get; set; }
    }

    public class ProductPriceViewModel
    {
        public string ProductName { get; set; }
        public string GoogleUrl { get; set; }
        public decimal Price { get; set; }
        public decimal PriceWithDelivery { get; set; }
        public decimal CalculatedPrice { get; set; }
        public decimal CalculatedPriceWithDelivery { get; set; }
        public string StoreName { get; set; }
        public string OfferUrl { get; set; }
        public int RegionId { get; set; }
        public decimal OurCalculatedPrice { get; set; }  // Nasza przeliczona cena
    }


}
