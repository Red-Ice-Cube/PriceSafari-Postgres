namespace PriceSafari.Models.ManagerViewModels
{
    public class UserStoresViewModel
    {
        public string UserId { get; set; }
        public string FullName { get; set; }
        public List<StoreClass> Stores { get; set; }


        public bool AccesToViewSafari { get; set; }
        public bool AccesToCreateSafari { get; set; }
        public bool AccesToViewMargin { get; set; }
        public bool AccesToSetMargin { get; set; }

        public bool AccesToViewPriceAutomation { get; set; }
        public bool AccesToEditPriceAutomation { get; set; }
    }
}
