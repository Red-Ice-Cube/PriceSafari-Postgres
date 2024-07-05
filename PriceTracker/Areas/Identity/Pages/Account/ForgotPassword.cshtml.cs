
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;

namespace PriceTracker.Areas.Identity.Pages.Account
{
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<PriceTrackerUser> _userManager;
        private readonly IEmailSender _emailSender;

        public ForgotPasswordModel(UserManager<PriceTrackerUser> userManager, IEmailSender emailSender)
        {
            _userManager = userManager;
            _emailSender = emailSender;
        }


        [BindProperty]
        public InputModel Input { get; set; }


        public class InputModel
        {


            [Required(ErrorMessage = "Podanie adresu email jest wymagane.")]
            [EmailAddress(ErrorMessage = "Proszę podać prawidłowy adres email.")]
            public string Email { get; set; }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(Input.Email);
                if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)))
                {

                    return RedirectToPage("./ForgotPasswordConfirmation");
                }


                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ResetPassword",
                    pageHandler: null,
                    values: new { area = "Identity", code },
                    protocol: Request.Scheme);

                await _emailSender.SendEmailAsync(
                     Input.Email,
                     "Resetowanie Hasła",
                     $"<h1>Zresetuj swoje hasło</h1>" +
                     $"<p>Otrzymaliśmy prośbę o zresetowanie hasła do Twojego konta. Jeśli to Ty wysłałeś tę prośbę, prosimy o zresetowanie hasła, klikając w poniższy link.</p>" +
                     $"<p><a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>Zresetuj hasło</a></p>" +
                     $"<p>Jeżeli nie żądałeś resetowania hasła, zignoruj tę wiadomość. Twoje obecne hasło pozostanie niezmienione.</p>" +
                     $"<p>Bezpieczeństwo Twojego konta jest dla nas bardzo ważne. Jeśli masz jakiekolwiek obawy dotyczące bezpieczeństwa Twojego konta, prosimy o kontakt z naszym zespołem wsparcia.</p>");



                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            return Page();
        }
    }
}
