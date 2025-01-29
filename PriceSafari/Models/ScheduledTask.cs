namespace PriceSafari.Models
{
    public class ScheduledTask
    {
        public int Id { get; set; }

        // Zadanie nr 1 (BASE_SCAL)
        public TimeSpan ScheduledTime { get; set; }
        public bool IsEnabled { get; set; }

        // Zadanie nr 2 (URL_SCAL)
        public TimeSpan UrlScheduledTime { get; set; }
        public bool UrlIsEnabled { get; set; }


        // 3) GOO_CRAW
        public TimeSpan GoogleScheduledTime { get; set; }
        public bool GoogleIsEnabled { get; set; }

    }


}
