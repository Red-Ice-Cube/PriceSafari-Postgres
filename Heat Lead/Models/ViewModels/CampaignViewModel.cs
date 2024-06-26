namespace Heat_Lead.Models.ViewModels
{
    public class CampaignViewModel
    {
        public int CampaignId { get; set; }
        public string CampaignName { get; set; }

        public int ProductsCount { get; set; }

        public bool IsActive { get; set; }
        public int Position { get; set; }

        public List<AffiliateLinkViewModel> AffiliateLink { get; set; }

        public List<ClickDataViewModel> ClickData { get; set; }
        public List<SalesDataViewModel> SalesData { get; set; }
    }

    public class CampaignsData
    {
        public int CampaignId { get; set; }
        public string CampaignName { get; set; }

        public List<ClickDataViewModel> Clicks { get; set; }
        public List<SalesDataViewModel> Sales { get; set; }

        public int Orders { get; set; }
        public int FullOrders { get; set; }

        public int Click { get; set; }
        public int FullClick { get; set; }
        public decimal Earnings { get; set; }
        public decimal FullEarnings { get; set; }

        public DateTime CreationDate { get; set; }
    }

    public class AffiliateLinkViewModel
    {
        public int Id { get; set; }
        public int ClickCount { get; set; }
        public int ExactSoldProductsCount { get; set; }

        public string? AffiliateURL { get; set; }

        public string? ProductImage { get; set; }
        public string? ProductName { get; set; }

        public bool IsActive { get; set; }

        public decimal? SoldValue { get; set; } = 0;

        public List<DateTime> ClickTimes { get; set; }
    }

    public class ClickDataViewModel
    {
        public DateTime ClickTime { get; set; }
        public DateTime Date { get; set; }
        public int Count { get; set; }
        public int ClickCount { get; set; }
    }

    public class SalesDataViewModel
    {
        public DateTime SaleTime { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public int SoldQuantity { get; set; }
    }
}