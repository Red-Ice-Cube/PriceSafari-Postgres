using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Models.SchedulePlan
{
    public class SchedulePlan
    {
        [Key]
        public int Id { get; set; }

     
        public int? MondayId { get; set; }
        public DayDetail Monday { get; set; }

        public int? TuesdayId { get; set; }
        public DayDetail Tuesday { get; set; }

        public int? WednesdayId { get; set; }
        public DayDetail Wednesday { get; set; }

        public int? ThursdayId { get; set; }
        public DayDetail Thursday { get; set; }

        public int? FridayId { get; set; }
        public DayDetail Friday { get; set; }

        public int? SaturdayId { get; set; }
        public DayDetail Saturday { get; set; }

        public int? SundayId { get; set; }
        public DayDetail Sunday { get; set; }
    }
}
