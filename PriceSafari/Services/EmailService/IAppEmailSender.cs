namespace PriceSafari.Services.EmailService
{
    public interface IAppEmailSender
    {
        Task<bool> SendEmailAsync(string email, string subject, string htmlMessage,
                              Dictionary<string, string> inlineImages = null,
                              Dictionary<string, byte[]> attachments = null);
    }
}
