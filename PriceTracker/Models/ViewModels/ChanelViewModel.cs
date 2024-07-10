namespace PriceTracker.Models.ViewModels
{
    public class ChanelViewModel
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public DateTime? LastScrapeDate { get; set; }
        public int? ProductCount { get; set; }
    }
}
