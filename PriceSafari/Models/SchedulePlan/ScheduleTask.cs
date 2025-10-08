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

      
        public TimeSpan StartTime { get; set; }


        public TimeSpan EndTime { get; set; }

       
        public bool BaseEnabled { get; set; }
        public bool UrlEnabled { get; set; }
        public bool GoogleEnabled { get; set; }
        public bool CeneoEnabled { get; set; }
        public bool AleBaseEnabled { get; set; }
        public bool UrlScalAleEnabled { get; set; }
        public bool AleCrawEnabled { get; set; }
        public int DayDetailId { get; set; }
        public DayDetail DayDetail { get; set; }

        public DateTime? LastRunDate { get; set; }


    
        public ICollection<ScheduleTaskStore> TaskStores { get; set; }
            = new List<ScheduleTaskStore>();
    }
}
