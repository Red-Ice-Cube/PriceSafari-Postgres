using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.SchedulePlan
{
 
    public class DayDetail
    {
        [Key]
        public int Id { get; set; }

        // Lista zadań tego dnia
        public ICollection<ScheduleTask> Tasks { get; set; } = new List<ScheduleTask>();
    }
}
