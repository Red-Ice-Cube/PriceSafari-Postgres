using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

using PriceSafari.Data;
using PriceSafari.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.EntityFrameworkCore;


namespace PriceSafari.Areas.Identity.Pages.Account
{


    public class AffiliateVerificationModel : PageModel
    {
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly PriceSafariContext _context;

        public AffiliateVerificationModel(UserManager<PriceSafariUser> userManager, PriceSafariContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string UserId { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Opis jest wymagany.")]
            [StringLength(500, ErrorMessage = "Opis nie może przekraczać 500 znaków.")]
            public string AffiliateDescription { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string userId)
        {
            UserId = userId;
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || !user.EmailConfirmed)
            {
                return NotFound("Nie można załadować użytkownika lub e-mail nie został potwierdzony.");
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string userId)
        {
            UserId = userId;
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || !user.EmailConfirmed)
            {
                return NotFound("Nie można załadować użytkownika lub e-mail nie został potwierdzony.");
            }

            var existingVerification = await _context.AffiliateVerification
                .FirstOrDefaultAsync(av => av.UserId == user.Id);

            if (existingVerification != null)
            {
                existingVerification.AffiliateDescription = Input.AffiliateDescription;
                existingVerification.IsVerified = false;
            }
            else
            {
                var newVerification = new AffiliateVerification
                {
                    UserId = user.Id,
                    AffiliateDescription = Input.AffiliateDescription,
                    IsVerified = false
                };

                _context.AffiliateVerification.Add(newVerification);
            }

            await _context.SaveChangesAsync();

            // Przekierowanie na stronę potwierdzenia
            return RedirectToPage("./AffiliateVerificationConfirmation");
        }

    }

}
