using System.Collections.Generic;

namespace PriceSafari.Models.ViewModels.SchedulePlanViewModels
{
    public class AddTaskViewModel
    {
        public string SessionName { get; set; }

        public string StartTime { get; set; }

        public string EndTime { get; set; }

        public bool UrlEnabled { get; set; }
        public bool CeneoEnabled { get; set; }
        public bool GoogleEnabled { get; set; }
        public bool ApiBotEnabled { get; set; }
        public bool BaseEnabled { get; set; }

        public bool UrlScalAleEnabled { get; set; }
        public bool AleCrawEnabled { get; set; }

        public bool AleApiBotEnabled { get; set; }

        public bool AleBaseEnabled { get; set; }

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

