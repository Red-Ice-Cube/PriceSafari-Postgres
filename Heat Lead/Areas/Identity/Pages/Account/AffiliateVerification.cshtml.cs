using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Heat_Lead.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Heat_Lead.Areas.Identity.Pages.Account
{


    public class AffiliateVerificationModel : PageModel
    {
        private readonly UserManager<Heat_LeadUser> _userManager;
        private readonly Heat_LeadContext _context;

        public AffiliateVerificationModel(UserManager<Heat_LeadUser> userManager, Heat_LeadContext context)
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
