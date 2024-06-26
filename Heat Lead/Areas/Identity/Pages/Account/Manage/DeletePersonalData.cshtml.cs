// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using Heat_Lead.Areas.Identity.Data;
using Heat_Lead.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace Heat_Lead.Areas.Identity.Pages.Account.Manage
{
    public class DeletePersonalDataModel : PageModel
    {
        private readonly UserManager<Heat_LeadUser> _userManager;
        private readonly SignInManager<Heat_LeadUser> _signInManager;
        private readonly ILogger<DeletePersonalDataModel> _logger;
        private readonly Heat_LeadContext _context;

        public DeletePersonalDataModel(
            UserManager<Heat_LeadUser> userManager,
            SignInManager<Heat_LeadUser> signInManager,
            ILogger<DeletePersonalDataModel> logger,
            Heat_LeadContext context)
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
            var orders = _context.Order.Where(o => o.UserId == userId).ToList();
            foreach (var order in orders)
            {
                order.IsDeleted = true;
            }


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
