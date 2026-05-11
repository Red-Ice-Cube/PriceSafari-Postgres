using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PriceSafari.Models;

namespace PriceSafari.Areas.Identity.Pages.Account.Manage
{
    public class ViewPreferencesModel : PageModel
    {
        private readonly UserManager<PriceSafariUser> _userManager;

        public ViewPreferencesModel(UserManager<PriceSafariUser> userManager)
        {
            _userManager = userManager;
        }

        [TempData]
        public string StatusMessage { get; set; }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            [Display(Name = "Widok dla Porównywarek Cenowych")]
            public bool UseProducerViewForPriceComparison { get; set; }

            [Display(Name = "Widok dla Marketplace Allegro")]
            public bool UseProducerViewForMarketplace { get; set; }
        }

        private void Load(PriceSafariUser user)
        {
            Input = new InputModel
            {
                UseProducerViewForPriceComparison = user.UseProducerViewForPriceComparison,
                UseProducerViewForMarketplace = user.UseProducerViewForMarketplace
            };
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound($"Nie udało się załadować użytkownika o ID '{_userManager.GetUserId(User)}'.");

            Load(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound($"Nie udało się załadować użytkownika o ID '{_userManager.GetUserId(User)}'.");

            if (!ModelState.IsValid)
            {
                Load(user);
                return Page();
            }

            user.UseProducerViewForPriceComparison = Input.UseProducerViewForPriceComparison;
            user.UseProducerViewForMarketplace = Input.UseProducerViewForMarketplace;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);

                Load(user);
                return Page();
            }

            StatusMessage = "Ustawienia widoku zostały zapisane.";
            return RedirectToPage();
        }
    }
}