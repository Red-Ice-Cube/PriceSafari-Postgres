// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using Heat_Lead.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace Heat_Lead.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel : PageModel
    {
        private readonly UserManager<Heat_LeadUser> _userManager;
        private readonly SignInManager<Heat_LeadUser> _signInManager;

        public IndexModel(
            UserManager<Heat_LeadUser> userManager,
            SignInManager<Heat_LeadUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        /// 

        public string CodePAR { get; set; }
        public string Name { get; set; }
        public string Surname { get; set; }

        public Heat_LeadUser Heat_LeadUser { get; set; }

        public string Username { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {

            [Phone]
            [Display(Name = "Phone number")]
            public string PhoneNumber { get; set; }


            [RegularExpression(@"^[a-zA-Z]{1,16}$", ErrorMessage = "Bez Polskich znaków, maksymalnie 16 liter.")]
            [Display(Name = "Name")]
            public string Name { get; set; }

            [RegularExpression(@"^[a-zA-Z]{1,16}$", ErrorMessage = "Bez Polskich znaków, maksymalnie 16 liter.")]
            [Display(Name = "Surname")]
            public string Surname { get; set; }
        }

        private async Task LoadAsync(Heat_LeadUser user)
        {

            var userName = await _userManager.GetUserNameAsync(user);
            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            var codePAR = user.CodePAR;
            var name = user.PartnerName;
            var surname = user.PartnerSurname;

            Username = userName;

            Input = new InputModel
            {
                PhoneNumber = phoneNumber,
                Name = name,
                Surname = surname,
            };

            CodePAR = codePAR;
            Name = name;
            Surname = surname;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Nie można znaleźć użytkownika '{_userManager.GetUserId(User)}'.");
            }

            await LoadAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Nie można znaleźć użytkownika '{_userManager.GetUserId(User)}'.");
            }

            if (!ModelState.IsValid)
            {
                await LoadAsync(user);
                return Page();
            }

            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            if (Input.PhoneNumber != phoneNumber)
            {
                var setPhoneResult = await _userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
                if (!setPhoneResult.Succeeded)
                {
                    StatusMessage = "Błąd podczas aktualizacji numeru telefonu.";
                    return RedirectToPage();
                }
            }

            var updateRequired = false;

            if (Input.Name != user.PartnerName)
            {
                user.PartnerName = Input.Name;
                updateRequired = true;
            }

            if (Input.Surname != user.PartnerSurname)
            {
                user.PartnerSurname = Input.Surname;
                updateRequired = true;
            }

            if (updateRequired)
            {
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    StatusMessage = "Błąd podczas aktualizacji danych użytkownika.";
                    return RedirectToPage();
                }
            }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Zaktualizowano profil.";
            return RedirectToPage();
        }

    }
}
