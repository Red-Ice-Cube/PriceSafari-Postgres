using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PriceSafari.Services.EmailService;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Hosting; // <-- NOWOŚĆ: Potrzebne do IWebHostEnvironment

namespace PriceSafari.Areas.Identity.Pages.Account
{
    public class VerifyEmailModel : PageModel
    {
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly IAppEmailSender _emailSender;
        private readonly ILogger<VerifyEmailModel> _logger;
        private readonly IWebHostEnvironment _webHostEnvironment; // <-- NOWOŚĆ

        public bool VerificationSuccess { get; set; } = false;

        public VerifyEmailModel(
            UserManager<PriceSafariUser> userManager,
            IAppEmailSender emailSender,
            ILogger<VerifyEmailModel> logger,
            IWebHostEnvironment webHostEnvironment) // <-- NOWOŚĆ
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
            _webHostEnvironment = webHostEnvironment; // <-- NOWOŚĆ
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Required]
            public string Email { get; set; }

            [Required(ErrorMessage = "Kod weryfikacyjny jest wymagany.")]
            [Display(Name = "Kod weryfikacyjny")]
            [StringLength(6, ErrorMessage = "Kod musi mieć 6 cyfr.")]
            public string Code { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string email)
        {
            if (string.IsNullOrEmpty(email)) return RedirectToPage("./Login");

            var user = await _userManager.FindByEmailAsync(email);
            if (user != null && user.Status == UserStatus.Active)
            {
                return RedirectToPage("./Login");
            }

            Input = new InputModel { Email = email };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null || user.VerificationCode != Input.Code || user.VerificationCodeExpires < DateTime.UtcNow)
            {
                ModelState.AddModelError(string.Empty, "Nieprawidłowy lub przeterminowany kod weryfikacyjny.");
                return Page();
            }

            user.EmailConfirmed = true;
            user.Status = UserStatus.PendingSetup;
            user.VerificationCode = null;
            user.VerificationCodeExpires = null;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                VerificationSuccess = true;
            }
            else
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
            }
            return Page();
        }

        public async Task<JsonResult> OnPostResendCodeAsync()
        {
            if (string.IsNullOrEmpty(Input.Email))
            {
                return new JsonResult(new { success = false, message = "Brak adresu e-mail." });
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null || user.Status != UserStatus.PendingEmailVerification)
            {
                return new JsonResult(new { success = false, message = "Nie można wysłać kodu dla tego użytkownika." });
            }

            _logger.LogInformation("Attempting to resend verification code to user {Email}", Input.Email);
            var newCode = new Random().Next(100000, 999999).ToString();
            user.VerificationCode = newCode;
            user.VerificationCodeExpires = DateTime.UtcNow.AddMinutes(15);
            await _userManager.UpdateAsync(user);

            // --- POCZĄTEK ZMIAN: Logika osadzania obrazka ---

            // 1. Zdefiniuj ścieżkę do logo w wwwroot
            var logoPath = Path.Combine(_webHostEnvironment.WebRootPath, "cid", "PriceSafari.png");

            // 2. Przygotuj słownik z obrazkami do osadzenia (Content-ID, ścieżka)
            var inlineImages = new Dictionary<string, string>
            {
                { "PriceSafariLogo", logoPath }
            };

            // 3. Wygeneruj treść e-maila
            var emailSubject = "Twój kod weryfikacyjny do Price Safari";
            var emailBody = GenerateVerificationEmailBody(user.PartnerName, newCode);

            // 4. Wyślij e-mail wraz z obrazkiem do osadzenia
            bool emailSent = await _emailSender.SendEmailAsync(user.Email, emailSubject, emailBody, inlineImages);

            // --- KONIEC ZMIAN ---

            if (emailSent)
            {
                _logger.LogInformation("Successfully resent code to {Email}", Input.Email);
                return new JsonResult(new { success = true, message = "Nowy kod weryfikacyjny został wysłany." });
            }
            else
            {
                _logger.LogError("Failed to resend code to {Email} via email service.", Input.Email);
                return new JsonResult(new { success = false, message = "Błąd serwera podczas wysyłania e-maila. Spróbuj ponownie." });
            }
        }
        private string GenerateVerificationEmailBody(string userName, string code)
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
                    background-color: #222222; 
margin-bottom:10px;
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
                    <p>Na Twoją prośbę ponownie wysyłamy kod weryfikacyjny. Poniższy kod zastępuje wszystkie poprzednie.</p>
                    
                    <div class=""code-label"">Kod weryfikacyjny</div>
                    <div class=""code"">{code}</div>


                    <p style=""margin-top: 16px; font-size: 16px;"">
                        Kod jest ważny przez 15 minut. Jeśli to nie Ty prosiłeś/aś o nowy kod, możesz bezpiecznie zignorować tę wiadomość.
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