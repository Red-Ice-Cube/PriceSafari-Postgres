using Microsoft.AspNetCore.Identity.UI.Services;
using PriceSafari.Services.EmailService;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;

// Implementujemy OBA interfejsy:
// - IEmailSender dla kompatybilności wstecznej z systemem Identity.
// - IAppEmailSender dla naszej nowej, niezawodnej logiki.
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
        // Odczytujemy konfigurację wstrzykniętą przez system, zamiast bezpośrednio ze zmiennych środowiskowych.
        _mailServer = configuration["MAIL_SERVER"];
        _mailPort = int.Parse(configuration["MAIL_PORT"]);
        _senderName = configuration["SENDER_NAME"];
        _sender = configuration["MAIL_SENDER"];
        _password = configuration["MAIL_PASSWORD"];
    }

    // Publiczna implementacja dla naszego nowego, niezawodnego interfejsu.
    public async Task<bool> SendEmailAsync(string email, string subject, string htmlMessage)
    {
        try
        {
            using var client = new SmtpClient(_mailServer, _mailPort)
            {
                Credentials = new NetworkCredential(_sender, _password),
                EnableSsl = true,
                Timeout = 20000 // 20-sekundowy limit czasu
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_sender, _senderName),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true,
            };
            mailMessage.To.Add(email);

            if (htmlMessage.Contains("cid:signatureImage"))
            {
                // Twoja logika załączników
                var signaturePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "cid", "signature.png");
                if (File.Exists(signaturePath))
                {
                    var signatureAttachment = new Attachment(signaturePath)
                    {
                        ContentId = "signatureImage",
                        ContentDisposition = { Inline = true, DispositionType = DispositionTypeNames.Inline }
                    };
                    mailMessage.Attachments.Add(signatureAttachment);
                }
            }

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation("Email to {Email} with subject '{Subject}' sent successfully.", email, subject);
            return true; // SUKCES
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email} with subject '{Subject}'.", email, subject);
            return false; // PORAŻKA
        }
    }

    async Task IEmailSender.SendEmailAsync(string email, string subject, string htmlMessage)
    {
        await SendEmailAsync(email, subject, htmlMessage);
    }
}