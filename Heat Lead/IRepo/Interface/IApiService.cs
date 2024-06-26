namespace Heat_Lead.IRepo.Interface
{
    public interface IApiService

    {
        Task<HttpResponseMessage> CallApiWithBasicAuth(string url, string username, string password, string xml);
    }
}