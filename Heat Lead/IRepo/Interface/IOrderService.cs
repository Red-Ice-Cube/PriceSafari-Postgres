namespace Heat_Lead.Services
{
    public interface IOrderService
    {
        Task FetchAndProcessOrders();

        Task ProcessOrder(string orderId);

        Task CalculateOrders();
    }
}