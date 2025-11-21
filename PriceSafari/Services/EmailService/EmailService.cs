using Microsoft.AspNetCore.Identity.UI.Services;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using PriceSafari.Services.EmailService;

public class EmailService : IEmailSender, IAppEmailSender
{
    private readonly ILogger<EmailService> _logger;
    private readonly string _mailServer;
    private readonly int _mailPort;
    private readonly string _senderName;
    private readonly string _sender;
    private readonly string _password;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _logger = logger;
        _mailServer = configuration["MAIL_SERVER"];
        _mailPort = int.Parse(configuration["MAIL_PORT"]);
        _senderName = configuration["SENDER_NAME"];
        _sender = configuration["MAIL_SENDER"];
        _password = configuration["MAIL_PASSWORD"];
    }

    // Starsza implementacja dla kompatybilności, wywołuje nową
    public Task<bool> SendEmailAsync(string email, string subject, string htmlMessage)
    {
        return SendEmailAsync(email, subject, htmlMessage, null);
    }

    // NOWA, GŁÓWNA implementacja z obsługą obrazków
    public async Task<bool> SendEmailAsync(string email, string subject, string htmlMessage,
                                         Dictionary<string, string> inlineImages = null,
                                         Dictionary<string, byte[]> attachments = null)
    {
        try
        {
            using var client = new SmtpClient(_mailServer, _mailPort)
            {
                Credentials = new NetworkCredential(_sender, _password),
                EnableSsl = true,
                Timeout = 20000
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_sender, _senderName),
                Subject = subject,
                IsBodyHtml = true,
            };
            mailMessage.To.Add(email);

            var alternateView = AlternateView.CreateAlternateViewFromString(htmlMessage, null, MediaTypeNames.Text.Html);

            // 1. Obrazki w treści (Inline Images)
            if (inlineImages != null)
            {
                foreach (var image in inlineImages)
                {
                    if (File.Exists(image.Value))
                    {
                        var linkedResource = new LinkedResource(image.Value) { ContentId = image.Key };
                        alternateView.LinkedResources.Add(linkedResource);
                    }
                }
            }
            mailMessage.AlternateViews.Add(alternateView);
            mailMessage.Body = htmlMessage;

            // 2. ZAŁĄCZNIKI (PDF) - TO JEST NOWA CZĘŚĆ
            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    // Tworzymy strumień z bajtów
                    var stream = new MemoryStream(attachment.Value);
                    // Dodajemy do wiadomości
                    mailMessage.Attachments.Add(new Attachment(stream, attachment.Key, "application/pdf"));
                }
            }

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation("Email sent to {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", email);
            return false;
        }
    }

    // Implementacja dla standardowego IEmailSender z Identity
    async Task IEmailSender.SendEmailAsync(string email, string subject, string htmlMessage)
    {
        // Wywołuje naszą główną metodę bez obrazków
        await SendEmailAsync(email, subject, htmlMessage, null);
    }
}