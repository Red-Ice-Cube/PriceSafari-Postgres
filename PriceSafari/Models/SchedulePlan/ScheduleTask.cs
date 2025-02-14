using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.SchedulePlan
{
    public class ScheduleTask
    {
        [Key]
        public int Id { get; set; }

        public string SessionName { get; set; }

        // Godzina startu w obrębie jednego dnia
        public TimeSpan StartTime { get; set; }

        // Godzina końca w obrębie jednego dnia (EndTime > StartTime)
        public TimeSpan EndTime { get; set; }

        // Flagi
        public bool BaseEnabled { get; set; }
        public bool UrlEnabled { get; set; }
        public bool GoogleEnabled { get; set; }
        public bool CeneoEnabled { get; set; }

        // Relacja do DayDetail (np. Monday)
        public int DayDetailId { get; set; }
        public DayDetail DayDetail { get; set; }

        // M:N: Sklepy
        public ICollection<ScheduleTaskStore> TaskStores { get; set; }
            = new List<ScheduleTaskStore>();
    }
}
