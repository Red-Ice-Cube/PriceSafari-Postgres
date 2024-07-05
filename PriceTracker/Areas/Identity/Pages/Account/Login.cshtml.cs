#nullable disable


using PriceTracker.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;

namespace PriceTracker.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<PriceTrackerUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly PriceTrackerContext _context;

        public LoginModel(SignInManager<PriceTrackerUser> signInManager, ILogger<LoginModel> logger, PriceTrackerContext context)
        {
            _signInManager = signInManager;
            _logger = logger;
            _context = context;
        }

        [BindProperty]
        public InputModel Input { get; set; }
        public IList<AuthenticationScheme> ExternalLogins { get; set; }
        public string ReturnUrl { get; set; }
        [TempData]
        public string ErrorMessage { get; set; }
        public class InputModel
        {
            [Required(ErrorMessage = "Podanie adresu email jest wymagane.")]
            [EmailAddress(ErrorMessage = "Proszę podać prawidłowy adres email.")]
            public string Email { get; set; }

            [Required(ErrorMessage = "Podanie hasła jest wymagane.")]
            [DataType(DataType.Password, ErrorMessage = "Niepoprawny format hasła.")]
            public string Password { get; set; }

            [Display(Name = "Zapisać dane logowania?")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");


            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                var user = await _signInManager.UserManager.FindByEmailAsync(Input.Email);
                if (user != null && !user.IsActive)
                {
                    _logger.LogWarning("Próba logowania na zdezaktywowane konto.");
                    ModelState.AddModelError(string.Empty, "To konto zostało zdezaktywowane.");
                    return Page();
                }

                // Sprawdź, czy weryfikacja afiliacyjna jest wymagana
                var settings = await _context.Settings.FirstOrDefaultAsync();
                if (settings != null && settings.VerificationRequired)
                {
                    // Sprawdź, czy użytkownik jest zweryfikowany
                    var affiliateVerification = await _context.AffiliateVerification.FirstOrDefaultAsync(av => av.UserId == user.Id);
                    if (affiliateVerification == null || !affiliateVerification.IsVerified)
                    {
                        ModelState.AddModelError(string.Empty, "Twoje konto oczekuje na weryfikację przez administratora programu. Po zatwierdzeniu wiadomości z opisem, będziesz mógł się zalogować.");
                        return Page();
                    }
                }

                // Kontynuuj proces logowania, jeśli weryfikacja nie jest wymagana lub użytkownik jest już zweryfikowany
                var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    _logger.LogInformation("User logged in.");
                    // Pobierz role użytkownika
                    var roles = await _signInManager.UserManager.GetRolesAsync(user);

                    // Przekieruj na odpowiednią stronę w zależności od roli użytkownika

                    if (roles.Contains("Admin") || roles.Contains("Manager"))
                    {
                        return RedirectToAction("Index", "Store");
                    }
                    else if (roles.Contains("Member"))
                    {
                        return RedirectToAction("Index", "Chanel");
                    }
                    else
                    {
                        return LocalRedirect(returnUrl);
                    }
                }
                else
                {
                    // Obsługa innych wyników logowania
                    ModelState.AddModelError(string.Empty, "Błędne dane logowania.");
                    return Page();
                }

            }

            return Page();
        }



    }
}
