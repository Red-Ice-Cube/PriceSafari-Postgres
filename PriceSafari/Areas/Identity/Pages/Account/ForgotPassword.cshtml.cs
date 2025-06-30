// Potrzebne usingi, upewnij się, że istnieją na górze pliku
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using PriceSafari.Services.EmailService;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;

// Pamiętaj, aby nazwa przestrzeni nazw była zgodna z Twoim projektem
namespace PriceSafari.Areas.Identity.Pages.Account
{
    public class ForgotPasswordModel : PageModel
    {
        // --- Krok 1: Aktualizacja zależności (wstrzykiwanie IAppEmailSender i IWebHostEnvironment) ---
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly IAppEmailSender _emailSender;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ForgotPasswordModel(UserManager<PriceSafariUser> userManager, IAppEmailSender emailSender, IWebHostEnvironment webHostEnvironment)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _webHostEnvironment = webHostEnvironment;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Podanie adresu email jest wymagane.")]
            [EmailAddress(ErrorMessage = "Proszę podać prawidłowy adres email.")]
            public string Email { get; set; }
        }

        // --- Krok 2: Przebudowa metody OnPostAsync ---
        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(Input.Email);
                if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
                {
                    // Nie ujawniaj, że użytkownik nie istnieje lub nie jest potwierdzony
                    return RedirectToPage("./ForgotPasswordConfirmation");
                }

                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ResetPassword",
                    pageHandler: null,
                    values: new { area = "Identity", code },
                    protocol: Request.Scheme);

                // Definiowanie treści dla szablonu
                var title = "Zresetuj swoje hasło";
                var message = $@"
                    <p>Otrzymaliśmy prośbę o zresetowanie hasła do Twojego konta. Aby kontynuować, kliknij przycisk poniżej.</p>
                    <p>Jeżeli to nie Ty prosiłeś/aś o zresetowanie hasła, możesz bezpiecznie zignorować tę wiadomość.</p>";
                var buttonText = "Zresetuj hasło";

                // Wygenerowanie pełnej treści HTML e-maila
                var emailBody = GenerateStyledEmailBody(title, message, buttonText, callbackUrl);

                // Przygotowanie osadzonego obrazka (logo)
                var logoPath = Path.Combine(_webHostEnvironment.WebRootPath, "cid", "PriceSafari.png");
                var inlineImages = new Dictionary<string, string>
                {
                    { "PriceSafariLogo", logoPath }
                };

                // Wysłanie e-maila przy użyciu nowego szablonu i usługi
                await _emailSender.SendEmailAsync(
                      Input.Email,
                      "Resetowanie Hasła",
                      emailBody,
                      inlineImages);

                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            return Page();
        }

        // --- Krok 3: Dodanie generycznej metody tworzącej szablon HTML ---
        // To jest DOKŁADNIE ta sama metoda, której użyliśmy w poprzednich krokach.
        // Wklejam ją tutaj dla kompletności rozwiązania.
        private string GenerateStyledEmailBody(string title, string messageBodyHtml, string buttonText, string buttonUrl)
        {
            var encodedButtonUrl = HtmlEncoder.Default.Encode(buttonUrl);
            var currentYear = DateTime.Now.Year;

            return $@"
            <!DOCTYPE html>
            <html lang=""pl"">
            <head>
                <meta charset=""UTF-8"">
                <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
                <style>
                    body {{
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
                        margin: 0;
                        padding: 0;
                        background-color: #f5f5f7;
                    }}
                    .container {{
                        max-width: 560px;
                        margin: 40px auto;
                        background-color: #ffffff;
                        overflow: hidden;
                    }}
                    .header {{
                        padding: 20px 0px;
            
                    }}
                    .top-bar {{
                        height: 1px;
                        background-color: #e5e5e5;
                    }}
                    .content {{
                        padding: 10px 0px; 
                        line-height: 1.6;
                        color: #1d1d1f; 
                        font-size: 16px;
                    }}
                    .content p {{
                        margin: 0 0 16px 0;
                    }}
                    h1 {{
                        font-size: 24px;
                        font-weight: 400;
                        color: #1d1d1f;
                        margin-bottom: 20px;
                    }}
                    .button {{
                        display: inline-block;
                        background-color: #41C7C7;
                        color: #ffffff !important;
                        padding: 12px 24px;
                        border-radius: 8px;
                        text-decoration: none;
                        font-weight: 500;
                        font-size: 16px;
                        margin: 10px 0 20px 0;
                        text-align: center;
                    }}
                    .footer {{
                        background-color: #f5f5f7;
                        color: #86868b;
                        padding: 20px 40px;
                        text-align: center;
                        font-size: 12px; 
                        line-height: 1.5;
                    }}
                    .footer p {{
                        margin: 0;
                    }}
                </style>
            </head>
            <body>
                <div class=""container"">
                    <div class=""header"">
                        <img src=""cid:PriceSafariLogo"" alt=""Price Safari Logo"" style=""height: 32px; width: auto;"">
                    </div>
                    <div class=""top-bar""></div>
                    <div class=""content"">
                        <h1>{title}</h1>
                        {messageBodyHtml}
                        <a href=""{encodedButtonUrl}"" class=""button"">{buttonText}</a>
                    </div>
                    <div class=""footer"">
                        <p>Z pozdrowieniami,<br>Zespół Price Safari</p>
                        <p style=""margin-top: 16px;"">&copy; {currentYear} Price Safari<br>Wszelkie prawa zastrzeżone.</p>
                    </div>
                </div>
            </body>
            </html>";
        }
    }
}