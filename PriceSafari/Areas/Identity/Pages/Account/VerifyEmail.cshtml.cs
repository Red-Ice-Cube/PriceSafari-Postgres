using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PriceSafari.Services.EmailService;
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Areas.Identity.Pages.Account
{
    public class VerifyEmailModel : PageModel
    {
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly IAppEmailSender _emailSender; // Używamy naszego nowego interfejsu
        private readonly ILogger<VerifyEmailModel> _logger;

        public bool VerificationSuccess { get; set; } = false;

        public VerifyEmailModel(UserManager<PriceSafariUser> userManager,
                                IAppEmailSender emailSender,
                                ILogger<VerifyEmailModel> logger)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
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

        // Metoda OnGetAsync pozostaje bez zmian
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

        // Metoda OnPostAsync do weryfikacji kodu pozostaje bez zmian
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
                foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        // Finalna wersja metody ponownego wysłania, zwracająca JsonResult
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

            // Wywołujemy metodę z naszego niezawodnego serwisu i sprawdzamy wynik
            bool emailSent = await _emailSender.SendEmailAsync(user.Email, "Twój nowy kod weryfikacyjny - Price Safari",
                $"Witaj ponownie! Oto Twój nowy kod weryfikacyjny: <h1>{newCode}</h1>");

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
    }
}