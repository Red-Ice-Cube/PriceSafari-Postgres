namespace Heat_Lead.IRepo.Interface
{
    public interface ISettingsService
    {
        Task<int> GetOrderProcessIntervalAsync();
    }
}