using System.Collections.Generic;

namespace PriceTracker.Models.ManagerViewModels
{
    public class UserStoresViewModel
    {
        public string UserId { get; set; }
        public string FullName { get; set; }
        public List<StoreClass> Stores { get; set; }
    }
}
