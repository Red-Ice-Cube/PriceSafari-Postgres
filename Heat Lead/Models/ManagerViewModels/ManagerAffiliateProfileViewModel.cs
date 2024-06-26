using System.Globalization;

namespace Heat_Lead.Models.ManagerViewModels
{
    public class ManagerAffiliateProfileViewModel
    {
        public ManagerAffiliateProfileViewModel()
        {
            AffiliateLink = new List<AffiliateLink>();
            ClickCount = new List<ClickCountData>();
            EarningsData = new List<CodeEarnings>();
            WalletData = new List<WalletData>();
            CategoryClick = new List<CategoryClick>();
            Orders = new List<Orders>();
        }

        public List<AffiliateLink> AffiliateLink { get; set; }
        public List<ClickCountData> ClickCount { get; set; }
        public List<CodeEarnings> EarningsData { get; set; }
        public List<WalletData> WalletData { get; set; }
        public List<CategoryClick> CategoryClick { get; set; }
        public List<Orders> Orders { get; set; }
        public Wallet UserWallet { get; set; }
        public decimal InValidationEarnings { get; set; }

        public string CodePAR { get; set; }
        public string PartnerName { get; set; }
        public string PartnerSurname { get; set; }
        public string Email { get; set; }
        public DateTime JoinDate { get; set; }

        public string JoinDateFormatted
        {
            get
            {
                return JoinDate.ToString("d", new CultureInfo("pl-PL"));
            }
        }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class WalletData
    {
        public DateTime Date { get; set; }
        public decimal InValidationEarnings { get; set; }
        public decimal AcceptedEarnings { get; set; }
    }

    public class Orders
    {
        public string OrderNumber { get; set; }
        public string CategoryName { get; set; }
        public string ProductName { get; set; }
        public int Amount { get; set; }
        public decimal ProductPrice { get; set; }
        public decimal Earnings { get; set; }  // To jest AffiliateCommision
        public DateTime CreationDate { get; set; }
        public DateTime? ValidationEndDate { get; set; }
        public string ValidationStatus { get; set; }

        public bool Accepted { get; set; }
        public bool IsCancelled { get; set; }
    }

    public class CategoryClick
    {
        public string CategoryName { get; set; }
        public int CategoryClickCount { get; set; }
    }

    public class ClickCountData
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
    }

    public class CodeEarnings
    {
        public string Category { get; set; }
        public string CodeAFI { get; set; }
        public decimal Earnings { get; set; }
        public DateTime CreationDate { get; set; }
    }
}