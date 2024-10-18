using Microsoft.AspNetCore.Identity.UI.Services;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Threading.Tasks;

public class EmailService : IEmailSender
{
    private readonly string _mailServer;
    private readonly int _mailPort;
    private readonly string _senderName;
    private readonly string _sender;
    private readonly string _password;

    public EmailService()
    {
        _mailServer = Environment.GetEnvironmentVariable("MAIL_SERVER");
        _mailPort = int.Parse(Environment.GetEnvironmentVariable("MAIL_PORT"));
        _senderName = Environment.GetEnvironmentVariable("SENDER_NAME");
        _sender = Environment.GetEnvironmentVariable("MAIL_SENDER");
        _password = Environment.GetEnvironmentVariable("MAIL_PASSWORD");
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        using (var client = new SmtpClient(_mailServer, _mailPort))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(_sender, _password);
            client.EnableSsl = true;

            var mailMessage = new MailMessage
            {
                From = new MailAddress(_sender, _senderName),
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true,
            };
            mailMessage.To.Add(email);

            // Ścieżka do obrazka
            var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "cid", "signature.png");

            // Sprawdzenie, czy plik istnieje
            if (File.Exists(imagePath))
            {
                // Utworzenie załącznika
                Attachment inline = new Attachment(imagePath);
                inline.ContentId = "signatureImage";
                inline.ContentDisposition.Inline = true;
                inline.ContentDisposition.DispositionType = DispositionTypeNames.Inline;

                // Dodanie załącznika do wiadomości
                mailMessage.Attachments.Add(inline);
            }
            else
            {
               
            }

            await client.SendMailAsync(mailMessage);
        }
    }

}
