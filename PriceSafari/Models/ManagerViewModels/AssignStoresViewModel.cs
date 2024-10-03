namespace PriceSafari.Models.ManagerViewModels
{
    public class AssignStoresViewModel
    {
        public List<PriceSafariUser> Users { get; set; }
        public List<StoreClass> Stores { get; set; }
        public string SelectedUserId { get; set; }
        public List<int> SelectedStoreIds { get; set; }

        // New properties for user permissions
        public bool AccesToViewSafari { get; set; }
        public bool AccesToCreateSafari { get; set; }
        public bool AccesToViewMargin { get; set; }
        public bool AccesToSetMargin { get; set; }
    }
}
