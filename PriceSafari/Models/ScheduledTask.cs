namespace PriceSafari.Models
{
    public class ScheduledTask
    {
        public int Id { get; set; }
        public TimeSpan ScheduledTime { get; set; } // BASE SCAL 
        public bool IsEnabled { get; set; }
    }

}
