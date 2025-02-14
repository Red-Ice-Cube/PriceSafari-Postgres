using PriceSafari.Models.SchedulePlan;
using static PriceSafari.Controllers.SchedulePlanController;

namespace PriceSafari.Models.ViewModels.SchedulePlanViewModels
{
    public class AssignStoresViewModel
    {
        public int TaskId { get; set; }
        public List<StoreAssignItem> StoreItems { get; set; }
            = new List<StoreAssignItem>();
    }

    public class StoreAssignItem
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public bool IsSelected { get; set; }
    }
}
