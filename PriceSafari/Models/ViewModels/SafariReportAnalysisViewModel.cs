namespace PriceSafari.Models.ViewModels
{
    public class SafariReportAnalysisViewModel
    {
        public string ReportName { get; set; }
        public DateTime CreatedDate { get; set; }
        public string StoreName { get; set; }  
        public List<ProductPriceViewModel> ProductPrices { get; set; }
   
    }
    public class ProductPriceViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string GoogleUrl { get; set; }
        public decimal Price { get; set; }
        public decimal PriceWithDelivery { get; set; }
        public decimal CalculatedPrice { get; set; }
        public decimal CalculatedPriceWithDelivery { get; set; }
        public string StoreName { get; set; }
        public string MyStoreName { get; set; }
        public int RegionId { get; set; }
        public decimal OurCalculatedPrice { get; set; }
        public List<int> FlagIds { get; set; }
        public string MainUrl { get; set; }
        public PriceSafari.Models.ProductClass Product { get; set; } // Dodane pole
    }



}
