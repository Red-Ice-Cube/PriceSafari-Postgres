using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Heat_Lead.Areas.Identity.Pages.Account
{
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<Heat_LeadUser> _userManager;
        private readonly Heat_LeadContext _context;

        public ConfirmEmailModel(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        [TempData]
        public string StatusMessage { get; set; }

        public string UserId { get; set; }

        public async Task<IActionResult> OnGetAsync(string userId, string code)
        {
            if (userId == null || code == null)
            {
                return RedirectToPage("/Index");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound($"Nie można załadować użytkownika.");
            }

            code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var result = await _userManager.ConfirmEmailAsync(user, code);

            if (result.Succeeded)
            {
                UserId = userId;

                // Sprawdzenie, czy ustawienia wymagają weryfikacji i czy użytkownik jest członkiem
                var settings = await _context.Settings.FirstOrDefaultAsync();
                if (!user.IsMember)
                {
                    // Użytkownik jest Adminem/Managerem
                    StatusMessage = "Dziękujemy za potwierdzenie adresu e-mail. Jako administrator/manager jesteś już zweryfikowany.";
                }
                else if (settings != null && settings.VerificationRequired)
                {
                    // Wymagana jest weryfikacja afiliacyjna
                    StatusMessage = "Dziękujemy za potwierdzenie adresu e-mail. Proszę uzupełnić weryfikację afiliacyjną.";
                }
                else
                {
                    // Weryfikacja nie jest wymagana
                    StatusMessage = "Dziękujemy za potwierdzenie adresu e-mail.";
                }
            }
            else
            {
                StatusMessage = "Błąd podczas potwierdzania adresu e-mail.";
            }

            return Page();
        }
    }
}