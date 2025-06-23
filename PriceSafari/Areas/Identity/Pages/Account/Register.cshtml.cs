using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

// Upewnij się, że namespace jest poprawny dla Twojego projektu
namespace PriceSafari.Areas.Identity.Pages.Account
{
    // Zakładamy, że ta strona nie wymaga autoryzacji
    public class RegisterModel : PageModel
    {
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly ILogger<RegisterModel> _logger;
        private readonly IEmailSender _emailSender;

        public RegisterModel(
            UserManager<PriceSafariUser> userManager,
            ILogger<RegisterModel> logger,
            IEmailSender emailSender)
        {
            _userManager = userManager;
            _logger = logger;
            _emailSender = emailSender;
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

                // Ustawiamy wartości początkowe dla nowego użytkownika,
                // nadpisując domyślne z konstruktora modelu.
                IsActive = false,
                IsMember = false,
                Status = UserStatus.PendingEmailVerification // Zaczynamy od tego statusu
            };

            var result = await _userManager.CreateAsync(user, Input.Password);

            if (result.Succeeded)
            {
                _logger.LogInformation("User created a new account with password.");

                // Nadaj nowo utworzonemu użytkownikowi rolę "PreMember"
                await _userManager.AddToRoleAsync(user, "PreMember");
                _logger.LogInformation("User was assigned the PreMember role.");

                // Generowanie i zapisywanie 6-cyfrowego kodu weryfikacyjnego
                var code = new Random().Next(100000, 999999).ToString();
                user.VerificationCode = code;
                user.VerificationCodeExpires = DateTime.UtcNow.AddMinutes(15); // Kod ważny 15 minut
                await _userManager.UpdateAsync(user);

                // Wysyłanie emaila z kodem
                await _emailSender.SendEmailAsync(Input.Email, "Potwierdź swoje konto w Price Safari",
                    $"Witaj! Dziękujemy za rejestrację. Twój kod weryfikacyjny to: <h1>{code}</h1>. Podaj go na następnej stronie, aby kontynuować.");

                // Przekieruj na stronę weryfikacji, przekazując email w query string
                // Tę stronę stworzymy w następnym kroku.
                return RedirectToPage("./VerifyEmail", new { email = Input.Email });
            }

            foreach (var error in result.Errors)
            {
                // Jeśli użytkownik już istnieje, dajemy bardziej ogólny komunikat
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
    }
}