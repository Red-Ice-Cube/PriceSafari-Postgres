using System.Collections.Generic;
using System.Threading.Tasks;

namespace PriceSafari.Services.EmailService
{
    public interface IAppEmailSender
    {
        Task<bool> SendEmailAsync(string email, string subject, string htmlMessage,
                                  Dictionary<string, string> inlineImages = null,
                                  Dictionary<string, byte[]> attachments = null);

        // ZMIANA: Dodajemy argument inlineImages
        Task<bool> SendEmailFromAccountAsync(string email, string subject, string htmlMessage, string accountType, Dictionary<string, string> inlineImages = null);
    }
}