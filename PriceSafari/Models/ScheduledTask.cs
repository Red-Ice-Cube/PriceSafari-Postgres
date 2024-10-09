namespace PriceSafari.Models
{
    public class ScheduledTask
    {
        public int Id { get; set; }
        public TimeSpan ScheduledTime { get; set; } // Time of day to run the task
        public bool IsEnabled { get; set; }
    }

}
