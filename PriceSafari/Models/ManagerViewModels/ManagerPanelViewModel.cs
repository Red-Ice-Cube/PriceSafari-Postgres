namespace PriceSafari.Models.ManagerViewModels
{
    public class ManagerPanelViewModel
    {
        public ManagerPanelViewModel()
        {
        
            ManagerClickCount = new List<ManagerClickCountData>();
        }

     
        public List<ManagerClickCountData> ManagerClickCount { get; set; }

        public int PendingAffiliates { get; set; }

        public int TotalAffiliates { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class ManagerCategoryClick
    {
        public string CategoryName { get; set; }
        public int CategoryClickCount { get; set; }
    }

    public class ManagerClickCountData
    {
        public DateTime Date { get; set; }
        public int Count { get; set; }
    }

    public class ManagerCodeEarnings
    {
        public string Category { get; set; }
        public decimal Earnings { get; set; }
        public DateTime CreationDate { get; set; }
    }

    public class ManagerCodeOrders
    {
        public string Category { get; set; }
        public int Orders { get; set; }
        public DateTime CreationDate { get; set; }
    }
}