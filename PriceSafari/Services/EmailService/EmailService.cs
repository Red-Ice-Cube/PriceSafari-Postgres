//using Microsoft.AspNetCore.Identity.UI.Services;
//using System.Net;
//using System.Net.Mail;
//using System.Net.Mime;
//using PriceSafari.Services.EmailService;

//public class EmailService : IEmailSender, IAppEmailSender
//{
//    private readonly ILogger<EmailService> _logger;
//    private readonly string _mailServer;
//    private readonly int _mailPort;
//    private readonly string _senderName;
//    private readonly string _sender;
//    private readonly string _password;

//    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
//    {
//        _logger = logger;
//        _mailServer = configuration["MAIL_SERVER"];
//        _mailPort = int.Parse(configuration["MAIL_PORT"]);
//        _senderName = configuration["SENDER_NAME"];
//        _sender = configuration["MAIL_SENDER"];
//        _password = configuration["MAIL_PASSWORD"];
//    }

//    public Task<bool> SendEmailAsync(string email, string subject, string htmlMessage)
//    {
//        return SendEmailAsync(email, subject, htmlMessage, null);
//    }

//    public async Task<bool> SendEmailAsync(string email, string subject, string htmlMessage,
//                                         Dictionary<string, string> inlineImages = null,
//                                         Dictionary<string, byte[]> attachments = null)
//    {
//        try
//        {
//            using var client = new SmtpClient(_mailServer, _mailPort)
//            {
//                Credentials = new NetworkCredential(_sender, _password),
//                EnableSsl = true,
//                Timeout = 20000
//            };

//            var mailMessage = new MailMessage
//            {
//                From = new MailAddress(_sender, _senderName),
//                Subject = subject,
//                IsBodyHtml = true,
//            };
//            mailMessage.To.Add(email);

//            var alternateView = AlternateView.CreateAlternateViewFromString(htmlMessage, null, MediaTypeNames.Text.Html);

//            if (inlineImages != null)
//            {
//                foreach (var image in inlineImages)
//                {
//                    if (File.Exists(image.Value))
//                    {
//                        var linkedResource = new LinkedResource(image.Value) { ContentId = image.Key };
//                        alternateView.LinkedResources.Add(linkedResource);
//                    }
//                }
//            }
//            mailMessage.AlternateViews.Add(alternateView);
//            mailMessage.Body = htmlMessage;

//            if (attachments != null)
//            {
//                foreach (var attachment in attachments)
//                {

//                    var stream = new MemoryStream(attachment.Value);

//                    mailMessage.Attachments.Add(new Attachment(stream, attachment.Key, "application/pdf"));
//                }
//            }

//            await client.SendMailAsync(mailMessage);
//            _logger.LogInformation("Email sent to {Email}", email);
//            return true;
//        }
//        catch (Exception ex)
//        {
//            _logger.LogError(ex, "Failed to send email to {Email}", email);
//            return false;
//        }
//    }

//    async Task IEmailSender.SendEmailAsync(string email, string subject, string htmlMessage)
//    {

//        await SendEmailAsync(email, subject, htmlMessage, null);
//    }
//}




using Microsoft.AspNetCore.Identity.UI.Services;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using PriceSafari.Services.EmailService;
using Microsoft.Extensions.Configuration; // Potrzebne do odczytu Configu

public class EmailService : IEmailSender, IAppEmailSender
{
    private readonly ILogger<EmailService> _logger;
    private readonly IConfiguration _configuration; // Przechowujemy config

    // Pola domyślne (Biuro) - używane przez resztę aplikacji
    private readonly string _mailServer;
    private readonly int _mailPort;
    private readonly string _defaultSenderName;
    private readonly string _defaultSender;
    private readonly string _defaultPassword;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        // Ładujemy domyślne ustawienia (Biuro) z .env
        _mailServer = configuration["MAIL_SERVER"];
        _mailPort = int.Parse(configuration["MAIL_PORT"]);
        _defaultSenderName = configuration["SENDER_NAME"];
        _defaultSender = configuration["MAIL_SENDER"];
        _defaultPassword = configuration["MAIL_PASSWORD"];
    }

    // --- NOWA METODA: Używana w Twoim kontrolerze do wyboru konta ---
    public Task<bool> SendEmailFromAccountAsync(string email, string subject, string htmlMessage, string accountType)
    {
        // Domyślne dane (Biuro)
        string senderEmail = _defaultSender;
        string senderName = _defaultSenderName;
        string password = _defaultPassword;

        // Jeśli wybrano Jakuba, nadpisujemy dane danymi z .env
        if (accountType == "Jakub")
        {
            senderEmail = _configuration["JAKUB_MAIL_SENDER"];
            password = _configuration["JAKUB_MAIL_PASSWORD"];
            senderName = "Jakub"; // Lub pobierz z configu jeśli chcesz
        }

        // Wywołujemy wspólną logikę wysyłki
        return SendEmailInternalAsync(email, subject, htmlMessage, senderEmail, senderName, password);
    }

    // --- STARA METODA (dla Identity, resetu haseł itp.) ---
    public Task<bool> SendEmailAsync(string email, string subject, string htmlMessage)
    {
        // Używa domyślnych danych (_defaultSender, _defaultPassword)
        return SendEmailInternalAsync(email, subject, htmlMessage, _defaultSender, _defaultSenderName, _defaultPassword, null, null);
    }

    // --- STARA METODA (z załącznikami/obrazkami) ---
    public async Task<bool> SendEmailAsync(string email, string subject, string htmlMessage,
                                           Dictionary<string, string> inlineImages = null,
                                           Dictionary<string, byte[]> attachments = null)
    {
        // Używa domyślnych danych
        return await SendEmailInternalAsync(email, subject, htmlMessage, _defaultSender, _defaultSenderName, _defaultPassword, inlineImages, attachments);
    }

    // --- WSPÓLNY SILNIK WYSYŁANIA (Prywatny) ---
    private async Task<bool> SendEmailInternalAsync(string email, string subject, string htmlMessage,
                                                    string senderEmail, string senderName, string password,
                                                    Dictionary<string, string> inlineImages = null,
                                                    Dictionary<string, byte[]> attachments = null)
    {
        try
        {
            // Tworzymy klienta z dynamicznie przekazanym hasłem i loginem
            using var client = new SmtpClient(_mailServer, _mailPort)
            {
                Credentials = new NetworkCredential(senderEmail, password),
                EnableSsl = true,
                Timeout = 20000
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(senderEmail, senderName),
                Subject = subject,
                IsBodyHtml = true,
            };
            mailMessage.To.Add(email);

            var alternateView = AlternateView.CreateAlternateViewFromString(htmlMessage, null, MediaTypeNames.Text.Html);

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

            if (attachments != null)
            {
                foreach (var attachment in attachments)
                {
                    var stream = new MemoryStream(attachment.Value);
                    mailMessage.Attachments.Add(new Attachment(stream, attachment.Key, "application/pdf"));
                }
            }

            await client.SendMailAsync(mailMessage);
            _logger.LogInformation($"Email sent to {email} from {senderEmail}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to send email to {email} from {senderEmail}");
            return false;
        }
    }

    // Implementacja interfejsu IEmailSender (wymagana przez Identity)
    async Task IEmailSender.SendEmailAsync(string email, string subject, string htmlMessage)
    {
        await SendEmailAsync(email, subject, htmlMessage);
    }
}