namespace PriceSafari.Models.ViewModels
{
    public class PaymentViewModel
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public string LogoUrl { get; set; }
        public string PlanName { get; set; }
        public decimal PlanPrice { get; set; }
        public int ProductsToScrap { get; set; }
        public int ScrapesPerInvoice { get; set; }
        public DateTime? LastScrapeDate { get; set; }
        public bool HasUnpaidInvoice { get; set; }
    }
}
