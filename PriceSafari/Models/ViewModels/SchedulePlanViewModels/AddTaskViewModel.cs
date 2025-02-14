using System.Collections.Generic;

namespace PriceSafari.Models.ViewModels.SchedulePlanViewModels
{
    public class AddTaskViewModel
    {
        // Nazwa sesji (np. "Ranna sesja")
        public string SessionName { get; set; }

        // Godzina startu (HH:mm)
        public string StartTime { get; set; }

        // Boole do włączenia akcji
        public bool BaseEnabled { get; set; }
        public bool UrlEnabled { get; set; }
        public bool GoogleEnabled { get; set; }
        public bool CeneoEnabled { get; set; }

        // Opcjonalna data/godzina zakończenia (w formacie np. "yyyy-MM-dd HH:mm")
        public string CompletedAt { get; set; }

        // Lista dostępnych sklepów (checkboxy)
        public List<StoreCheckboxItem> Stores { get; set; } = new List<StoreCheckboxItem>();
    }

    // Pojedyncza pozycja sklepu w checkboxach
    public class StoreCheckboxItem
    {
        public int StoreId { get; set; }
        public string StoreName { get; set; }
        public bool IsSelected { get; set; }
    }
}
