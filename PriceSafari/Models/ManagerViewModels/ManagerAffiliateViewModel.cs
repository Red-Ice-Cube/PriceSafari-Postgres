namespace PriceSafari.Models.ManagerViewModels
{
    public class ManagerAffiliateViewModel
    {
        public ManagerAffiliateViewModel()
        {
            ManagerAffiliate = new List<ManagerAffiliate>();
        }

        public List<ManagerAffiliate> ManagerAffiliate { get; set; }
    }

    public class ManagerAffiliate
    {
        public string Name { get; set; }
        public string Surname { get; set; }
        public string CodePAR { get; set; }
        public bool Status { get; set; }
        public bool Verification { get; set; }
        public string? Role { get; set; }
        public string? UserName { get; set; }
    }

    // DTOs używane dla szczegółowych danych afiliantów
    public class AffiliateData
    {
        public string Affiliate { get; set; }
        public int TotalClicks { get; set; }
        public decimal TotalSales { get; set; }
        public int FullClicks { get; set; }
        public decimal FullSales { get; set; }
        public int FullOrders { get; set; }
        public int TotalOrders { get; set; }
        public List<ClickData> Clicks { get; set; }
        public List<SalesData> Sales { get; set; }
        public DateTime CreationDate { get; set; }
    }

    public class ClickData
    {
        public DateTime ClickTime { get; set; }
        public int Count { get; set; }
    }

    public class SalesData
    {
        public DateTime SaleTime { get; set; }
        public decimal Amount { get; set; }
    }
}