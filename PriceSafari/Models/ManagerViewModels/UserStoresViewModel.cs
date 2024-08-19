using System.Collections.Generic;

namespace PriceSafari.Models.ManagerViewModels
{
    public class UserStoresViewModel
    {
        public string UserId { get; set; }
        public string FullName { get; set; }
        public List<StoreClass> Stores { get; set; }
    }
}
