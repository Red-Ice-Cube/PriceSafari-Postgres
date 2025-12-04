using System.Collections.Generic;
using System.Threading.Tasks;

namespace PriceSafari.Services.EmailService
{
    public interface IAppEmailSender
    {
        // Stara metoda
        Task<bool> SendEmailAsync(string email, string subject, string htmlMessage,
                                  Dictionary<string, string> inlineImages = null,
                                  Dictionary<string, byte[]> attachments = null);

        // --- DODAJ TĘ LINIJKĘ ---
        Task<bool> SendEmailFromAccountAsync(string email, string subject, string htmlMessage, string accountType);
    }
}