using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using PriceSafari.Services.EmailService;
using Microsoft.AspNetCore.Hosting;

namespace PriceSafari.Areas.Identity.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IAppEmailSender _emailSender;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public RegisterModel(
            UserManager<PriceSafariUser> userManager,
            ILogger<RegisterModel> logger,
            IAppEmailSender emailSender,
            IWebHostEnvironment webHostEnvironment)
        {
            _userManager = userManager;
            _logger = logger;
            _emailSender = emailSender;
            _webHostEnvironment = webHostEnvironment;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Imię jest wymagane.")]
            [Display(Name = "Imię")]
            public string FirstName { get; set; }

            [Required(ErrorMessage = "Nazwisko jest wymagane.")]
            [Display(Name = "Nazwisko")]
            public string LastName { get; set; }

            [Required(ErrorMessage = "Adres e-mail jest wymagany.")]
            [EmailAddress(ErrorMessage = "Nieprawidłowy format adresu e-mail.")]
            [Display(Name = "E-mail")]
            public string Email { get; set; }

            [Required(ErrorMessage = "Numer telefonu jest wymagany.")]
            [Phone(ErrorMessage = "Nieprawidłowy format numeru telefonu.")]
            [Display(Name = "Numer telefonu")]
            public string PhoneNumber { get; set; }

            [Required(ErrorMessage = "Hasło jest wymagane.")]
            [StringLength(100, ErrorMessage = "{0} musi mieć co najmniej {2} znaków.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Hasło")]
            public string Password { get; set; }

            [Required(ErrorMessage = "Potwierdzenie hasła jest wymagane.")]
            [DataType(DataType.Password)]
            [Display(Name = "Potwierdź hasło")]
            [Compare("Password", ErrorMessage = "Wprowadzone hasła nie są identyczne.")]
            public string ConfirmPassword { get; set; }
        }

        public void OnGet(string returnUrl = null)
        {
            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = new PriceSafariUser
            {
                UserName = Input.Email,
                Email = Input.Email,
                PartnerName = Input.FirstName,
                PartnerSurname = Input.LastName,
                PhoneNumber = Input.PhoneNumber,
                CreationDate = DateTime.UtcNow,
                IsActive = false,
                IsMember = false,
                Status = UserStatus.PendingEmailVerification
            };

            var result = await _userManager.CreateAsync(user, Input.Password);

            if (result.Succeeded)
            {
                _logger.LogInformation("User created a new account with password.");

                await _userManager.AddToRoleAsync(user, "PreMember");
                _logger.LogInformation("User was assigned the PreMember role.");

                var code = new Random().Next(100000, 999999).ToString();
                user.VerificationCode = code;
                user.VerificationCodeExpires = DateTime.UtcNow.AddMinutes(15);
                await _userManager.UpdateAsync(user);

                var logoPath = Path.Combine(_webHostEnvironment.WebRootPath, "cid", "PriceSafari.png");
                var inlineImages = new Dictionary<string, string>
                {
                    { "PriceSafariLogo", logoPath }
                };

                var emailSubject = "Witaj w Price Safari! Potwierdź swój adres e-mail";
                var emailBody = GenerateInitialVerificationEmailBody(user.PartnerName, code);

                await _emailSender.SendEmailAsync(Input.Email, emailSubject, emailBody, inlineImages);

                return RedirectToPage("./VerifyEmail", new { email = Input.Email });
            }

            foreach (var error in result.Errors)
            {
                if (error.Code == "DuplicateUserName")
                {

                    ModelState.AddModelError(string.Empty, "Konto z tym adresem e-mail już istnieje. Czy chcesz się zalogować?");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }
            return Page();
        }

        private string GenerateInitialVerificationEmailBody(string userName, string code)
        {

    

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
                    .top-bar {{
                        height: 1px;
                        margin-bottom:10px;
                        background-color: #222222; 
                    }}
                    .header {{
                               padding-top:10px;
                   padding-bottom:10px;
                    }}
                    .content {{
                   padding-top:10px;
                   padding-bottom:10px;
                        line-height: 1.6;
                        color: #1d1d1f; 
                        font-size: 16px;
                    }}

                    .content p {{
                        margin: 0 0 16px 0;
                    }}
                    .code-label {{
                        font-size: 16px;

                        margin-bottom: 4px;
                    }}
                    .code {{
                        font-size: 36px;
                        font-weight: 700;
                        letter-spacing: 4px;
                        color: #41C7C7;
                        margin-bottom: 16px;
                    }}
                    .button {{
                        display: inline-block;
                        background-color: #007B84; 
                        color: #ffffff;
                        padding: 14px 24px;
                        border-radius: 8px;
                        text-decoration: none;
                        font-weight: 500;
                        font-size: 16px;
                        margin-top: 10px;
                    }}
                    .footer {{
                        background-color: #f5f5f7;
                        color: #86868b;
                        padding: 20px 40px;
                        text-align: center;
                        font-size: 16px;
                        line-height: 1.5;
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

                            <p>Cześć {userName},</p>
                            <p>Dziękujemy za założenie konta w Price Safari! Jesteśmy o krok od zakończenia procesu rejestracji. Użyj poniższego kodu, aby zweryfikować swój adres e-mail.</p>

                            <div class=""code-label"">Kod weryfikacyjny</div>
                            <div class=""code"">{code}</div>

                            <p style=""margin-top: 16px; font-size: 16px;"">
                                Kod jest ważny przez 15 minut. Jeśli to nie Ty zakładałeś/aś konto, możesz bezpiecznie zignorować tę wiadomość.
                            </p>
                        </div>
                        <div class=""footer"">
                            <p>&copy; {DateTime.Now.Year} Price Safari<br>Wszelkie prawa zastrzeżone.</p>
                        </div>
                    </div>
                </body>
                </html>";
        }
    }
}