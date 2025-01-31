namespace PriceSafari.Models
{
    public class TaskExecutionLog
    {
        public int Id { get; set; }
        public string DeviceName { get; set; }
        public string OperationName { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Comment { get; set; }
    }
}
