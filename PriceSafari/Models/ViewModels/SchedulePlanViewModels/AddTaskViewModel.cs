using System.Collections.Generic;

namespace PriceSafari.Models.ViewModels.SchedulePlanViewModels
{
    public class AddTaskViewModel
    {
        public string SessionName { get; set; }

        // W formacie HH:mm (np. "01:00")
        public string StartTime { get; set; }

        // W formacie HH:mm (np. "02:30")
        public string EndTime { get; set; }

        public bool BaseEnabled { get; set; }
        public bool UrlEnabled { get; set; }
        public bool GoogleEnabled { get; set; }
        public bool CeneoEnabled { get; set; }
        public bool AleBaseEnabled { get; set; }

        // Lista sklepów (checkboxy)
        public List<StoreCheckboxItem> Stores { get; set; }
            = new List<StoreCheckboxItem>();
    }

    public class StoreCheckboxItem
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public bool IsSelected { get; set; }
    }
}
