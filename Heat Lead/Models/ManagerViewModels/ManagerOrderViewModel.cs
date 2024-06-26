namespace Heat_Lead.Models.ManagerViewModels
{
    public class ManagerOrderViewModel
    {
        public ManagerOrderViewModel()
        {
            ManagerOrders = new List<ManagerOrders>();
        }

        public List<ManagerOrders> ManagerOrders { get; set; }
    }

    public class ManagerOrders
    {
        public int OrderId { get; set; }
        public string OrderNumber { get; set; }
        public string AffiliateName { get; set; }
        public string AffiliateSurname { get; set; }
        public string CategoryName { get; set; }
        public string ProductName { get; set; }
        public int Amount { get; set; }
        public decimal ProductPrice { get; set; }
        public decimal Earnings { get; set; }
        public DateTime CreationDate { get; set; }
        public DateTime? ValidationEndDate { get; set; }
        public string ValidationStatus { get; set; }

        public bool Accepted { get; set; }
        public bool IsCancelled { get; set; }

        public bool InValidation { get; set; }
    }
}