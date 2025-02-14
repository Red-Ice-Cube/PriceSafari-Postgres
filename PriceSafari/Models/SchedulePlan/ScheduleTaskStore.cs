namespace PriceSafari.Models.SchedulePlan
{
    public class ScheduleTaskStore
    {
        public int Id { get; set; }

        public int ScheduleTaskId { get; set; }
        public ScheduleTask ScheduleTask { get; set; }

        public int StoreId { get; set; }
        public StoreClass Store { get; set; }
    }
}
