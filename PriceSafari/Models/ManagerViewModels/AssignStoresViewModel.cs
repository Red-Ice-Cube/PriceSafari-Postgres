

namespace PriceSafari.Models.ManagerViewModels
{
    public class AssignStoresViewModel
    {
        public List<PriceSafariUser> Users { get; set; }
        public List<StoreClass> Stores { get; set; }
        public string SelectedUserId { get; set; }
        public List<int> SelectedStoreIds { get; set; }
    }
}
