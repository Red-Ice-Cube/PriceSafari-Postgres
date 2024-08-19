
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Areas.Identity.Pages.Account.Manage
{
    public class ChangePasswordModel : PageModel
    {
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly SignInManager<PriceSafariUser> _signInManager;
        private readonly ILogger<ChangePasswordModel> _logger;

        public ChangePasswordModel(
            UserManager<PriceSafariUser> userManager,
            SignInManager<PriceSafariUser> signInManager,
            ILogger<ChangePasswordModel> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string StatusMessage { get; set; }


        public class InputModel
        {
            [Required(ErrorMessage = "Pole 'Obecne hasło' jest wymagane.")]
            [DataType(DataType.Password)]
            [Display(Name = "Obecne hasło")]
            public string OldPassword { get; set; }

            [Required(ErrorMessage = "Pole 'Nowe hasło' jest wymagane.")]
            [StringLength(100, ErrorMessage = "Pole '{0}' musi mieć przynajmniej {2} i maksymalnie {1} znaków.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "Nowe hasło")]
            public string NewPassword { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Potwierdź nowe hasło")]
            [Compare("NewPassword", ErrorMessage = "Hasła nie są identyczne.")]
            public string ConfirmPassword { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Nie można załadować użytkownika o ID '{_userManager.GetUserId(User)}'.");
            }

            var hasPassword = await _userManager.HasPasswordAsync(user);
            if (!hasPassword)
            {
                return RedirectToPage("./SetPassword");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Nie można załadować użytkownika o ID '{_userManager.GetUserId(User)}'.");
            }

            var changePasswordResult = await _userManager.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
            if (!changePasswordResult.Succeeded)
            {
                foreach (var error in changePasswordResult.Errors)
                {
                    switch (error.Code)
                    {
                        case "PasswordTooShort":
                            ModelState.AddModelError(string.Empty, "Hasło jest za krótkie.");
                            break;

                        case "PasswordRequiresNonAlphanumeric":
                            ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jeden znak niealfanumeryczny.");
                            break;

                        case "PasswordRequiresDigit":
                            ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jedną cyfrę ('0'-'9').");
                            break;

                        case "PasswordRequiresUpper":
                            ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jedną dużą literę ('A'-'Z').");
                            break;

                        case "PasswordRequiresLower":
                            ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jedną małą literę ('a'-'z').");
                            break;

                        case "PasswordMismatch":
                            ModelState.AddModelError(string.Empty, "Niepoprawne hasło.");
                            break;

                        default:
                            ModelState.AddModelError(string.Empty, error.Description);
                            break;
                    }
                }
                return Page();
            }


            await _signInManager.RefreshSignInAsync(user);
            _logger.LogInformation("Użytkownik pomyślnie zmienił swoje hasło.");
            StatusMessage = "Twoje hasło zostało zmienione.";

            return RedirectToPage();
        }

    }
}
