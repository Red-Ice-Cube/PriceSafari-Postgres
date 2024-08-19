using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ManagerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Linq;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UserManagementController : Controller
    {
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IEmailSender _sender;
        private readonly PriceSafariContext _context;

        public UserManagementController(UserManager<PriceSafariUser> userManager, RoleManager<IdentityRole> roleManager, IEmailSender sender, PriceSafariContext context)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _sender = sender;
            _context = context;
        }

        [HttpGet]
        public IActionResult CreateUser()
        {
            var roles = _roleManager.Roles.ToList();
            var model = new CreateUserViewModel
            {
                Roles = roles.Select(r => r.Name).ToList()
            };
            return View("~/Views/ManagerPanel/Affiliates/CreateUser.cshtml", model);
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser(CreateUserViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = new PriceSafariUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    PartnerName = model.Imie,
                    PartnerSurname = model.Nazwisko,
                    EmailConfirmed = !model.SendConfirmationEmail,
                    IsMember = model.SelectedRole != "Admin" && model.SelectedRole != "Manager"
                };

                var passwordValidationResult = await _userManager.PasswordValidators[0].ValidateAsync(_userManager, user, model.Password);
                if (!passwordValidationResult.Succeeded)
                {
                    foreach (var error in passwordValidationResult.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                    model.Roles = _roleManager.Roles.Select(r => r.Name).ToList();
                    return View("~/Views/ManagerPanel/Affiliates/CreateUser.cshtml", model);
                }

                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    await _context.SaveChangesAsync();
                    await _userManager.UpdateAsync(user);
                    await _userManager.AddToRoleAsync(user, model.SelectedRole);

                    // Ustawienie IsVerified na false
                    var affiliateVerification = await _context.AffiliateVerification
                        .FirstOrDefaultAsync(av => av.UserId == user.Id);

                    if (affiliateVerification == null)
                    {
                        affiliateVerification = new AffiliateVerification
                        {
                            UserId = user.Id,
                            IsVerified = false // Zawsze ustawiamy na false
                        };
                        _context.AffiliateVerification.Add(affiliateVerification);
                    }
                    else
                    {
                        affiliateVerification.IsVerified = false; // Upewniamy się, że jest ustawione na false
                    }

                    await _context.SaveChangesAsync();

                    if (model.SendConfirmationEmail)
                    {
                        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                        code = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(code));
                        var callbackUrl = Url.Page("/Account/ConfirmEmail", pageHandler: null, values: new { area = "Identity", userId = user.Id, code = code }, protocol: Request.Scheme);

                        await _sender.SendEmailAsync(model.Email, "Potwierdzenie adresu e-mail",
                            $"Rozpoczęliśmy przygotowywanie twojego konta w programie PriceSafari. Prosimy o potwierdzenie adresu e-mail klikając w ten link: <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>kliknij tutaj</a>.<br/>Twoje tymczasowe hasło logowania: {model.Password} Po zalogowaniu do panelu, prosimy o zmianę hasła.");
                    }
                    else
                    {
                        user.EmailConfirmed = true;
                        await _userManager.UpdateAsync(user);
                    }

                    return RedirectToAction("Index", "ManagerAffiliate");
                }
                else
                {
                    foreach (var error in result.Errors)
                    {
                        switch (error.Code)
                        {
                            case "DuplicateUserName":
                                ModelState.AddModelError(string.Empty, "Użytkownik z tym adresem e-mail już istnieje.");
                                break;

                            case "PasswordRequiresNonAlphanumeric":
                                ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jeden znak niealfanumeryczny.");
                                break;

                            case "PasswordRequiresDigit":
                                ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jedną cyfrę ('0'-'9').");
                                break;

                            case "PasswordRequiresUpper":
                                ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jedną dużą literę ('A'-'Z').");
                                break;

                            case "PasswordRequiresLower":
                                ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jedną małą literę ('a'-'z').");
                                break;

                            case "PasswordTooShort":
                                ModelState.AddModelError(string.Empty, "Hasło musi być dłuższe.");
                                break;

                            default:
                                ModelState.AddModelError(string.Empty, error.Description);
                                break;
                        }
                    }
                }
            }

            model.Roles = _roleManager.Roles.Select(r => r.Name).ToList();
            return View("~/Views/ManagerPanel/Affiliates/CreateUser.cshtml", model);
        }

    }
}















//using PriceSafari.Data;
//using PriceSafari.Models;
//using PriceSafari.Models.ManagerViewModels;
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Identity;
//using Microsoft.AspNetCore.Identity.UI.Services;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.WebUtilities;
//using Microsoft.EntityFrameworkCore;

//using System.Text.Encodings.Web;

//namespace PriceSafari.Controllers
//{
//    [Authorize(Roles = "Admin")]
//    public class UserManagementController : Controller
//    {
//        private readonly UserManager<PriceSafariUser> _userManager;
//        private readonly RoleManager<IdentityRole> _roleManager;
//        private readonly IEmailSender _sender;
//        private readonly PriceSafariContext _context;

//        public UserManagementController(UserManager<PriceSafariUser> userManager, RoleManager<IdentityRole> roleManager, IEmailSender sender, PriceSafariContext context)
//        {
//            _userManager = userManager;
//            _roleManager = roleManager;
//            _sender = sender;
//            _context = context;
//        }

//        [HttpGet]
//        public IActionResult CreateUser()
//        {
//            var roles = _roleManager.Roles.ToList();
//            var model = new CreateUserViewModel
//            {
//                Roles = roles.Select(r => r.Name).ToList()
//            };
//            return View("~/Views/ManagerPanel/Affiliates/CreateUser.cshtml", model);
//        }

//        [HttpPost]
//        public async Task<IActionResult> CreateUser(CreateUserViewModel model)
//        {
//            if (ModelState.IsValid)
//            {
//                var user = new PriceSafariUser
//                {
//                    UserName = model.Email,
//                    Email = model.Email,
//                    PartnerName = model.Imie,
//                    PartnerSurname = model.Nazwisko,
//                    EmailConfirmed = !model.SendConfirmationEmail,
//                    IsMember = model.SelectedRole != "Admin" && model.SelectedRole != "Manager"
//                };

//                var passwordValidationResult = await _userManager.PasswordValidators[0].ValidateAsync(_userManager, user, model.Password);
//                if (!passwordValidationResult.Succeeded)
//                {
//                    foreach (var error in passwordValidationResult.Errors)
//                    {
//                        ModelState.AddModelError(string.Empty, error.Description);
//                    }
//                    model.Roles = _roleManager.Roles.Select(r => r.Name).ToList();
//                    return View("~/Views/ManagerPanel/Affiliates/CreateUser.cshtml", model);
//                }

//                var result = await _userManager.CreateAsync(user, model.Password);
//                if (result.Succeeded)
//                {

//                    await _context.SaveChangesAsync();


//                    await _userManager.UpdateAsync(user);

//                    await _userManager.AddToRoleAsync(user, model.SelectedRole);

//                    // Ustawienie IsVerified i AffiliateDescription dla Admina lub Managera
//                    var affiliateVerificationDescription = model.SelectedRole == "Admin" ? "Admin" : model.SelectedRole == "Manager" ? "Manager" : string.Empty;

//                    if (!string.IsNullOrEmpty(affiliateVerificationDescription))
//                    {
//                        var affiliateVerification = await _context.AffiliateVerification
//                            .FirstOrDefaultAsync(av => av.UserId == user.Id);

//                        if (affiliateVerification == null)
//                        {
//                            affiliateVerification = new AffiliateVerification
//                            {
//                                UserId = user.Id,
//                                IsVerified = true,
//                                AffiliateDescription = affiliateVerificationDescription // Ustawienie opisu
//                            };
//                            _context.AffiliateVerification.Add(affiliateVerification);
//                        }
//                        else
//                        {
//                            affiliateVerification.IsVerified = true;
//                            affiliateVerification.AffiliateDescription = affiliateVerificationDescription;
//                        }
//                        await _context.SaveChangesAsync();
//                    }

//                    if (model.SendConfirmationEmail)
//                    {
//                        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
//                        code = WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(code));
//                        var callbackUrl = Url.Page("/Account/ConfirmEmail", pageHandler: null, values: new { area = "Identity", userId = user.Id, code = code }, protocol: Request.Scheme);

//                        await _sender.SendEmailAsync(model.Email, "Potwierdzenie adresu e-mail",
//                            $"Rozpoczeliśmy przygototwywanie twojego konta w programie PriceSafari. Prosimy o potwierdzenie adresu e-mail klikając w ten link: <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>kliknij tutaj</a>.<br/>Twoje tymczasowe hasło logowania: {model.Password} Po zalogowaniu do panelu, prosimy o zmianę hasła.");
//                    }
//                    else
//                    {
//                        user.EmailConfirmed = true;
//                        await _userManager.UpdateAsync(user);
//                    }

//                    return RedirectToAction("Index", "ManagerAffiliate");
//                }
//                else
//                {
//                    foreach (var error in result.Errors)
//                    {
//                        switch (error.Code)
//                        {
//                            case "DuplicateUserName":
//                                ModelState.AddModelError(string.Empty, "Użytkownik z tym adresem e-mail już istnieje.");
//                                break;

//                            case "PasswordRequiresNonAlphanumeric":
//                                ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jeden znak niealfanumeryczny.");
//                                break;

//                            case "PasswordRequiresDigit":
//                                ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jedną cyfrę ('0'-'9').");
//                                break;

//                            case "PasswordRequiresUpper":
//                                ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jedną dużą literę ('A'-'Z').");
//                                break;

//                            case "PasswordRequiresLower":
//                                ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jedną małą literę ('a'-'z').");
//                                break;

//                            case "PasswordTooShort":
//                                ModelState.AddModelError(string.Empty, "Hasło musi być dłuższe.");
//                                break;

//                            default:
//                                ModelState.AddModelError(string.Empty, error.Description);
//                                break;
//                        }
//                    }
//                }
//            }

//            model.Roles = _roleManager.Roles.Select(r => r.Name).ToList();
//            return View("~/Views/ManagerPanel/Affiliates/CreateUser.cshtml", model);
//        }
//    }
//}