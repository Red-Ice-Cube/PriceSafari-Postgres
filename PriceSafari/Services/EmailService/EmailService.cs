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

      
            var imagesToEmbed = new Dictionary<string, string>()
        {
            { "Image1", "Panel_PriceSafari.png" },
            { "Image2", "Ranking_PriceSafari.png" },
            { "Image3", "Wykres_PriceSafari.png" },
            { "Image4", "Eu_PriceSafari.png" },
            { "Image5", "Czechy_PriceSafari.png" },
            { "Image6", "Produkt_Czechy_PriceSafari.png" },

        };

            foreach (var pair in imagesToEmbed)
            {
                var contentId = pair.Key;     // np. "Image1"
                var fileName = pair.Value;    // np. "1.png"

                // Ścieżka do pliku w katalogu wwwroot/mail
                var imagePath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "mail",
                    fileName
                );

                if (File.Exists(imagePath))
                {
                    // Tworzymy Attachment ze ścieżki do pliku
                    var inline = new Attachment(imagePath);

                    // Ustawiamy ContentId = temu, co używasz w HTML (cid:Image1, etc.)
                    inline.ContentId = contentId;
                    inline.ContentDisposition.Inline = true;
                    inline.ContentDisposition.DispositionType = DispositionTypeNames.Inline;

                    mailMessage.Attachments.Add(inline);
                }
                else
                {
                 
                }
            }

            var signaturePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "cid", // jeśli masz osobny folder 'cid'
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

            // Wysyłamy maila
            await client.SendMailAsync(mailMessage);
        }
    }


}
