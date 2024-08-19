using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using System.Text;

namespace PriceSafari.Areas.Identity.Pages.Account
{
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly PriceSafariContext _context;

        public ConfirmEmailModel(UserManager<PriceSafariUser> userManager, PriceSafariContext context)
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

                var affiliateVerification = await _context.AffiliateVerification.FirstOrDefaultAsync(av => av.UserId == userId);
                if (affiliateVerification != null && affiliateVerification.IsVerified)
                {
                    // Użytkownik jest zweryfikowany
                    StatusMessage = "Dziękujemy za weryfikację adresu email. Twoje konto jest już gotowe i możesz się zalogować.";
                }
                else
                {
                    // Użytkownik nie jest jeszcze zweryfikowany
                    StatusMessage = "Dziękujemy za potwierdzenie maila. Obecnie konfigurujemy Twój panel. Gdy będzie gotowy, powiadomimy Cię o tym mailowo i będziesz mógł się zalogować.";
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