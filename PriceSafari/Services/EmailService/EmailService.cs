using Microsoft.AspNetCore.Identity.UI.Services;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;

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
        using var client = new SmtpClient(_mailServer, _mailPort)
        {
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_sender, _password),
            EnableSsl = true
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress(_sender, _senderName),
            Subject = subject,
            Body = htmlMessage,
            IsBodyHtml = true,
        };
        mailMessage.To.Add(email);

     
        if (htmlMessage.Contains("cid:signatureImage", StringComparison.OrdinalIgnoreCase))
        {
            var signaturePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "cid",
                "signature.png"
            );

            if (File.Exists(signaturePath))
            {
                var signatureAttachment = new Attachment(signaturePath);
                signatureAttachment.ContentId = "signatureImage";
                signatureAttachment.ContentDisposition.Inline = true;
                signatureAttachment.ContentDisposition.DispositionType = DispositionTypeNames.Inline;
                mailMessage.Attachments.Add(signatureAttachment);
            }
        }

        await client.SendMailAsync(mailMessage);
    }
}
