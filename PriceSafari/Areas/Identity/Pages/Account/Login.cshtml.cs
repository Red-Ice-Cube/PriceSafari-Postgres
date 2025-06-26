using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace PriceSafari.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<PriceSafariUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly PriceSafariContext _context;

        public LoginModel(SignInManager<PriceSafariUser> signInManager, ILogger<LoginModel> logger, PriceSafariContext context)
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
        // W pliku Login.cshtml.cs
        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                var user = await _signInManager.UserManager.FindByEmailAsync(Input.Email);

                if (user == null)
                {
                    ModelState.AddModelError(string.Empty, "Błędne dane logowania.");
                    return Page();
                }

                var roles = await _signInManager.UserManager.GetRolesAsync(user);

                // --- PRZEBUDOWANA LOGIKA ---

                // ** PRZYPADEK 1: Użytkownik to PreMember **
                if (roles.Contains("PreMember"))
                {
                    // Jeśli PreMember nie zweryfikował jeszcze e-maila, przekieruj go do weryfikacji
                    if (user.Status == UserStatus.PendingEmailVerification)
                    {
                        _logger.LogInformation("Login attempt for unverified PreMember {Email}. Redirecting to verification page.", Input.Email);
                        return RedirectToPage("./VerifyEmail", new { email = Input.Email });
                    }

                    // Jeśli PreMember zweryfikował e-mail, próbujemy go zalogować, ignorując flagę IsActive
                    _logger.LogInformation("Verified PreMember {Email} is attempting to log in to Setup.", Input.Email);
                    var preMemberResult = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: false);

                    if (preMemberResult.Succeeded)
                    {
                        _logger.LogInformation("PreMember logged in successfully. Redirecting to /Setup page.");
                        // Logowanie udane, przekierowujemy do specjalnej strony /Setup
                        return RedirectToAction("Index", "SetUp");
                    }
                    else
                    {
                        ModelState.AddModelError(string.Empty, "Błędne dane logowania.");
                        return Page();
                    }
                }

                // ** PRZYPADEK 2: Użytkownik to zwykły klient lub administrator (nie PreMember) **
                else
                {
                    // Sprawdzamy, czy konto jest aktywne - TEN WARUNEK DOTYCZY TERAZ TYLKO ZWYKŁYCH KONT
                    if (!user.IsActive)
                    {
                        _logger.LogWarning("Login attempt for a deactivated account: {Email}", Input.Email);
                        ModelState.AddModelError(string.Empty, "To konto zostało zdezaktywowane lub wymaga aktywacji przez administratora.");
                        return Page();
                    }

                    // Sprawdzanie weryfikacji przez Affilate (jeśli wymagane)
                    var settings = await _context.Settings.FirstOrDefaultAsync();
                    if (settings != null && settings.VerificationRequired)
                    {
                        var affiliateVerification = await _context.AffiliateVerification.FirstOrDefaultAsync(av => av.UserId == user.Id);
                        if (affiliateVerification == null || !affiliateVerification.IsVerified)
                        {
                            ModelState.AddModelError(string.Empty, "Konto jest jeszcze przygotowywane. Po zakończonej konfiguracji powiadomimy Cię o tym mailowo.");
                            return Page();
                        }
                    }

                    // Logowanie dla pozostałych ról
                    var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: false);

                    if (result.Succeeded)
                    {
                        _logger.LogInformation("User {Email} logged in.", Input.Email);

                        user.LastLoginDateTime = DateTime.Now;
                        user.LoginCount += 1;
                        await _signInManager.UserManager.UpdateAsync(user);

                        // Przekierowanie na podstawie roli
                        if (roles.Contains("Admin"))
                        {
                            return RedirectToAction("Index", "Store");
                        }
                        else if (roles.Contains("Manager"))
                        {
                            return RedirectToAction("Index", "ClientProfile");
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
                        ModelState.AddModelError(string.Empty, "Błędne dane logowania.");
                        return Page();
                    }
                }
            }

            return Page();
        }
    }
    
}
