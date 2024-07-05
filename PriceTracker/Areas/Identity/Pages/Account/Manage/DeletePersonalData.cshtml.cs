
using PriceTracker.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using System.ComponentModel.DataAnnotations;

namespace PriceTracker.Areas.Identity.Pages.Account.Manage
{
    public class DeletePersonalDataModel : PageModel
    {
        private readonly UserManager<PriceTrackerUser> _userManager;
        private readonly SignInManager<PriceTrackerUser> _signInManager;
        private readonly ILogger<DeletePersonalDataModel> _logger;
        private readonly PriceTrackerContext _context;

        public DeletePersonalDataModel(
            UserManager<PriceTrackerUser> userManager,
            SignInManager<PriceTrackerUser> signInManager,
            ILogger<DeletePersonalDataModel> logger,
            PriceTrackerContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
            _context = context;
        }


        [BindProperty]
        public InputModel Input { get; set; }


        public class InputModel
        {

            [Required(ErrorMessage = "Podanie hasła jest wymagane.")]
            [DataType(DataType.Password)]
            [Display(Name = "Hasło")]
            public string Password { get; set; }
        }


        public bool RequirePassword { get; set; }

        public async Task<IActionResult> OnGet()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Nie można załadować użytkownika o ID '{_userManager.GetUserId(User)}'.");
            }

            RequirePassword = await _userManager.HasPasswordAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Nie można załadować użytkownika o ID '{_userManager.GetUserId(User)}'.");
            }

            RequirePassword = await _userManager.HasPasswordAsync(user);
            if (RequirePassword)
            {
                if (!await _userManager.CheckPasswordAsync(user, Input.Password))
                {
                    ModelState.AddModelError(string.Empty, "Nieprawidłowe hasło.");
                    return Page();
                }
            }


            var userId = await _userManager.GetUserIdAsync(user);
           
            


            user.IsActive = false;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                throw new InvalidOperationException($"Wystąpił nieoczekiwany błąd podczas aktualizacji statusu użytkownika.");
            }

            await _signInManager.SignOutAsync();

            _logger.LogInformation("Użytkownik o ID '{UserId}' zdezaktywował swoje konto.", userId);

            return Redirect("~/");
        }



    }
}
