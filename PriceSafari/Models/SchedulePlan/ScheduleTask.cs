using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.SchedulePlan
{
    public class ScheduleTask
    {
        [Key]
        public int Id { get; set; }

        // Nazwa sesji, np. "Ranna sesja"
        public string SessionName { get; set; }

        // Godzina startu (HH:mm)
        public TimeSpan StartTime { get; set; }

        public bool BaseEnabled { get; set; }
        public bool UrlEnabled { get; set; }
        public bool GoogleEnabled { get; set; }
        public bool CeneoEnabled { get; set; }

        public DateTime? CompletedAt { get; set; }

        // Należy do jednego DayDetail (np. Monday)
        public int DayDetailId { get; set; }
        public DayDetail DayDetail { get; set; }

        // M:N: Sklepy
        public ICollection<ScheduleTaskStore> TaskStores { get; set; } = new List<ScheduleTaskStore>();
    }

}
