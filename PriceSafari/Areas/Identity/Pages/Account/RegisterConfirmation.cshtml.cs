
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Identity;
//using Microsoft.AspNetCore.Identity.UI.Services;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Mvc.RazorPages;

//namespace PriceSafari.Areas.Identity.Pages.Account
//{
//    [AllowAnonymous]
//    public class RegisterConfirmationModel : PageModel
//    {
//        private readonly UserManager<PriceSafariUser> _userManager;
//        private readonly IEmailSender _sender;

//        public RegisterConfirmationModel(UserManager<PriceSafariUser> userManager, IEmailSender sender)
//        {
//            _userManager = userManager;
//            _sender = sender;
//        }

//        public string Email { get; set; }

//        public async Task<IActionResult> OnGetAsync(string email, string returnUrl = null)
//        {
//            if (email == null)
//            {
//                return RedirectToPage("/Index");
//            }
//            returnUrl = returnUrl ?? Url.Content("~/");

//            var user = await _userManager.FindByEmailAsync(email);
//            if (user == null)
//            {
//                return NotFound($"Unable to load user with email '{email}'.");
//            }

//            Email = email;

//            return Page();
//        }
//    }
//}
