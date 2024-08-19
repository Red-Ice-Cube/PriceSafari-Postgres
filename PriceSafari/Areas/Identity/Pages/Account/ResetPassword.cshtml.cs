
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

using System.ComponentModel.DataAnnotations;
using System.Text;

namespace PriceSafari.Areas.Identity.Pages.Account
{
    public class ResetPasswordModel : PageModel
    {
        private readonly UserManager<PriceSafariUser> _userManager;

        public ResetPasswordModel(UserManager<PriceSafariUser> userManager)
        {
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {

            [Required(ErrorMessage = "Pole Email jest wymagane.")]
            [EmailAddress(ErrorMessage = "Nieprawidłowy format adresu email.")]
            [Display(Name = "Email")]
            public string Email { get; set; }


            [Required(ErrorMessage = "Pole Hasło jest wymagane.")]
            [StringLength(100, ErrorMessage = "Hasło musi mieć przynajmniej {2} i maksymalnie {1} znaków długości.", MinimumLength = 8)]
            [DataType(DataType.Password)]
            [Display(Name = "Hasło")]
            public string Password { get; set; }


            [DataType(DataType.Password)]
            [Display(Name = "Potwierdź hasło")]
            [Compare("Password", ErrorMessage = "Hasło i hasło potwierdzające nie są identyczne.")]
            public string ConfirmPassword { get; set; }


            [Required]
            public string Code { get; set; }

        }

        public IActionResult OnGet(string code = null)
        {
            if (code == null)
            {
                return BadRequest("A code must be supplied for password reset.");
            }
            else
            {
                Input = new InputModel
                {
                    Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code))
                };
                return Page();
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user == null)
            {
                // Don't reveal that the user does not exist
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            var result = await _userManager.ResetPasswordAsync(user, Input.Code, Input.Password);
            if (result.Succeeded)
            {
                return RedirectToPage("./ResetPasswordConfirmation");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }
    }
}
