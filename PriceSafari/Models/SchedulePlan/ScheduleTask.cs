using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.SchedulePlan
{
    public class ScheduleTask
    {
        [Key]
        public int Id { get; set; }

        // Godzina (np. 06:00) startu zadania
        public TimeSpan StartTime { get; set; }

        // Boole do poszczególnych akcji
        public bool BaseEnabled { get; set; }
        public bool UrlEnabled { get; set; }
        public bool GoogleEnabled { get; set; }
        public bool CeneoEnabled { get; set; }

        // Czy zadanie zostało ukończone
        public bool TaskComplete { get; set; }

        // Opcjonalna data/godzina faktycznego zakończenia
        public DateTime? CompletedAt { get; set; }

        // Relacja do DayDetail
        public int DayDetailId { get; set; }
        public DayDetail DayDetail { get; set; }

        // M:N do sklepów (jeśli używasz)
        public ICollection<ScheduleTaskStore> TaskStores { get; set; } = new List<ScheduleTaskStore>();
    }
}
