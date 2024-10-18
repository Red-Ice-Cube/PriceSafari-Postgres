namespace PriceSafari.Services.ViewRenderService
{
    public interface IViewRenderService
    {
        Task<string> RenderToStringAsync(string viewName, object model);
    }

}
