using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using PriceSafari.Services.EmailService;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;

namespace PriceSafari.Areas.Identity.Pages.Account.Manage
{
    public class EmailModel : PageModel
    {
        private readonly UserManager<PriceSafariUser> _userManager;

        private readonly IAppEmailSender _emailSender;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public EmailModel(
            UserManager<PriceSafariUser> userManager,
            IAppEmailSender emailSender,
            IWebHostEnvironment webHostEnvironment)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _webHostEnvironment = webHostEnvironment;
        }

        public string Email { get; set; }

        public bool IsEmailConfirmed { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Pole 'Nowy E-mail' jest wymagane.")]
            [EmailAddress(ErrorMessage = "Nieprawidłowy format adresu e-mail.")]
            [Display(Name = "Nowy E-mail")]
            public string NewEmail { get; set; }

        }

        private async Task LoadAsync(PriceSafariUser user)
        {
            var email = await _userManager.GetEmailAsync(user);
            Email = email;

            Input = new InputModel
            {
                NewEmail = email,
            };

            IsEmailConfirmed = await _userManager.IsEmailConfirmedAsync(user);
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Nie można znaleźć użytkownika '{_userManager.GetUserId(User)}'.");
            }

            await LoadAsync(user);
            return Page();
        }
        public async Task<IActionResult> OnPostChangeEmailAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Nie można znaleźć użytkownika '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            var email = await _userManager.GetEmailAsync(user);
            if (Input.NewEmail != email)
            {
                var existingUser = await _userManager.FindByEmailAsync(Input.NewEmail);
                if (existingUser != null)
                {
                    ModelState.AddModelError("Input.NewEmail", "Podany adres e-mail jest już zajęty.");
                    await LoadAsync(user);
                    return Page();
                }

                var userId = await _userManager.GetUserIdAsync(user);
                var code = await _userManager.GenerateChangeEmailTokenAsync(user, Input.NewEmail);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ConfirmEmailChange",
                    pageHandler: null,
                    values: new { area = "Identity", userId = userId, email = Input.NewEmail, code = code },
                    protocol: Request.Scheme);

                var title = "Potwierdzenie zmiany adresu e-mail";
                var message = @"<p>Otrzymaliśmy prośbę o zmianę adresu e-mail powiązanego z Twoim kontem w Price Safari.</p>
                         <p>Aby sfinalizować ten proces, proszę kliknąć w poniższy przycisk. Jeśli to nie Ty inicjowałeś/aś tę zmianę, zignoruj tę wiadomość.</p>";
                var buttonText = "Potwierdź nowy e-mail";

                var emailBody = GenerateStyledEmailBody(title, message, buttonText, callbackUrl);

                var logoPath = Path.Combine(_webHostEnvironment.WebRootPath, "cid", "PriceSafari.png");

                var inlineImages = new Dictionary<string, string>
        {
            { "PriceSafariLogo", logoPath }
        };

                await _emailSender.SendEmailAsync(
                    Input.NewEmail,
                    "Potwierdź zmianę E-mail",
                    emailBody,
                    inlineImages);

                StatusMessage = "Wiadomość z linkiem do zmiany E-mail wysłana, proszę sprawdzić pocztę.";
                return RedirectToPage();
            }

            StatusMessage = "E-mail nie został zmieniony.";
            return RedirectToPage();
        }

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
                        font-weight:400;
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

        public async Task<IActionResult> OnPostSendVerificationEmailAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Nie można znaleźć użytkownika '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            var userId = await _userManager.GetUserIdAsync(user);
            var email = await _userManager.GetEmailAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId = userId, code = code },
                protocol: Request.Scheme);
            await _emailSender.SendEmailAsync(
                email,
                "Confirm your email",
                $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

            StatusMessage = "Wiadomość weryfikacyjna wysłana, proszę sprawdzić E-mail.";
            return RedirectToPage();
        }
    }
}