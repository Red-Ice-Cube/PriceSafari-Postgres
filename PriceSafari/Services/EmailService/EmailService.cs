using Microsoft.AspNetCore.Identity.UI.Services;
using PriceSafari.Services.EmailService;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;

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
    public async Task<bool> SendEmailAsync(string email, string subject, string htmlMessage, Dictionary<string, string> inlineImages = null)
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
                IsBodyHtml = true, // Ważne, aby IsBodyHtml było tutaj
            };
            mailMessage.To.Add(email);

            // Używamy AlternateView dla poprawnego osadzania obrazków w HTML
            var alternateView = AlternateView.CreateAlternateViewFromString(htmlMessage, null, MediaTypeNames.Text.Html);

            // NOWA, GENERYCZNA PĘTLA DLA OBRAZKÓW
            if (inlineImages != null)
            {
                foreach (var image in inlineImages)
                {
                    var contentId = image.Key;
                    var imagePath = image.Value;

                    if (File.Exists(imagePath))
                    {
                        var linkedResource = new LinkedResource(imagePath)
                        {
                            ContentId = contentId
                        };
                        alternateView.LinkedResources.Add(linkedResource);
                    }
                    else
                    {
                        _logger.LogWarning("Inline image not found at path: {ImagePath}", imagePath);
                    }
                }
            }

            mailMessage.AlternateViews.Add(alternateView);

            // UWAGA: Ustawiamy treść Body po utworzeniu AlternateViews
            mailMessage.Body = htmlMessage;

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation("Email to {Email} sent successfully.", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}.", email);
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