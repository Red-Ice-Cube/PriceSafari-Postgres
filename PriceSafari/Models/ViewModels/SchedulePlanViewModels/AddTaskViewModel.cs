namespace PriceSafari.Models.ViewModels.SchedulePlanViewModels
{
    public class AddTaskViewModel
    {
        public int TaskId { get; set; }         // do edycji istniejącego zadania
        public int DayDetailId { get; set; }    // do którego dnia należy

        public string StartTime { get; set; }   // "HH:mm"

        public bool BaseEnabled { get; set; }
        public bool UrlEnabled { get; set; }
        public bool GoogleEnabled { get; set; }
        public bool CeneoEnabled { get; set; }

        public bool TaskComplete { get; set; }

        // Jeśli chcesz dać użytkownikowi możliwość wpisania
        // godziny zakończenia w formularzu (np. "2025-02-20 15:30"):
        public string CompletedAt { get; set; }  // np. "yyyy-MM-dd HH:mm"
    }
}
