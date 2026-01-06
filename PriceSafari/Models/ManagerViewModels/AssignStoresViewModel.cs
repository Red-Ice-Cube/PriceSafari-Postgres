using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PriceSafari.Models.ManagerViewModels
{
    public class AssignStoresViewModel
    {
        [BindNever]
        public List<PriceSafariUser> Users { get; set; } = new List<PriceSafariUser>();

        [BindNever]
        public List<StoreClass> Stores { get; set; } = new List<StoreClass>();

        public string SelectedUserId { get; set; }

        public List<int> SelectedStoreIds { get; set; } = new List<int>();


        public bool AccesToViewSafari { get; set; }
        public bool AccesToCreateSafari { get; set; }
        public bool AccesToViewMargin { get; set; }
        public bool AccesToSetMargin { get; set; }
        public bool AccesToViewPriceAutomation { get; set; }
        public bool AccesToEditPriceAutomation { get; set; }
    }
}
