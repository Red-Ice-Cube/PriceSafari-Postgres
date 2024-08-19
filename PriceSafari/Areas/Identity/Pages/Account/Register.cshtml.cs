
//using PriceSafari.Data;
//using PriceSafari.Models;
//using Microsoft.AspNetCore.Authentication;
//using Microsoft.AspNetCore.Identity;
//using Microsoft.AspNetCore.Identity.UI.Services;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.AspNetCore.Mvc.RazorPages;
//using Microsoft.AspNetCore.WebUtilities;
//using Microsoft.EntityFrameworkCore;
//using System.ComponentModel.DataAnnotations;
//using System.Text;
//using System.Text.Encodings.Web;

//namespace PriceSafari.Areas.Identity.Pages.Account
//{
//    public class RegisterModel : PageModel
//    {
//        private readonly SignInManager<PriceSafariUser> _signInManager;
//        private readonly UserManager<PriceSafariUser> _userManager;
//        private readonly IUserStore<PriceSafariUser> _userStore;
//        private readonly IUserEmailStore<PriceSafariUser> _emailStore;
//        private readonly ILogger<RegisterModel> _logger;
//        private readonly IEmailSender _emailSender;
//        private readonly PriceSafariContext _context;

//        public RegisterModel(
//            UserManager<PriceSafariUser> userManager,
//            IUserStore<PriceSafariUser> userStore,
//            SignInManager<PriceSafariUser> signInManager,
//            ILogger<RegisterModel> logger,
//            IEmailSender emailSender,
//            PriceSafariContext context)
//        {
//            _userManager = userManager;
//            _userStore = userStore;
//            _emailStore = GetEmailStore();
//            _signInManager = signInManager;
//            _logger = logger;
//            _emailSender = emailSender;
//            _context = context;
//        }

//        [BindProperty]
//        public InputModel Input { get; set; }

//        public string ReturnUrl { get; set; }
//        public IList<AuthenticationScheme> ExternalLogins { get; set; }

//        public class InputModel
//        {
//            [Required(ErrorMessage = "Pole Imię jest wymagane.")]
//            [StringLength(20, ErrorMessage = "Imię musi mieć przynajmniej {2} i maksymalnie {1} znaków długości.", MinimumLength = 2)]
//            public string Imie { get; set; }

//            [Required(ErrorMessage = "Pole Nazwisko jest wymagane.")]
//            [StringLength(20, ErrorMessage = "Nazwisko musi mieć przynajmniej {2} i maksymalnie {1} znaków długości.", MinimumLength = 2)]
//            public string Nazwisko { get; set; }

//            [Required(ErrorMessage = "Pole Email jest wymagane.")]
//            [EmailAddress(ErrorMessage = "Nieprawidłowy format adresu email.")]
//            [Display(Name = "Email")]
//            public string Email { get; set; }

//            [Required(ErrorMessage = "Pole Hasło jest wymagane.")]
//            [StringLength(50, ErrorMessage = "Hasło musi mieć przynajmniej {2} i maksymalnie {1} znaków długości.", MinimumLength = 8)]
//            [DataType(DataType.Password)]
//            [Display(Name = "Hasło")]
//            public string Password { get; set; }

//            [DataType(DataType.Password)]
//            [Required(ErrorMessage = "Powtórzenie hasła jest wymagane.")]
//            [Display(Name = "Potwierdź hasło")]
//            [Compare("Password", ErrorMessage = "Hasło i hasło potwierdzające nie są identyczne.")]
//            public string ConfirmPassword { get; set; }

//            [Required(ErrorMessage = "Musisz zaakceptować regulamin, aby kontynuować.")]
//            [Display(Name = "Akceptuję regulamin")]
//            public bool AcceptsTerms { get; set; }
//        }

//        public async Task OnGetAsync(string returnUrl = null)
//        {
//            ReturnUrl = returnUrl;
//            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
//        }

//        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
//        {
//            returnUrl ??= Url.Content("~/");
//            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
//            if (ModelState.IsValid)
//            {
//                if (Input.AcceptsTerms != true)
//                {
//                    ModelState.AddModelError("Input.AcceptsTerms", "Musisz zaakceptować regulamin, aby kontynuować.");
//                    return Page();
//                }

//                var existingUser = await _userManager.FindByEmailAsync(Input.Email);
//                if (existingUser != null && !existingUser.IsActive)
//                {
//                    ModelState.AddModelError(string.Empty, "Konto z tym adresem E-mail zostało zdezaktywowane. Użyj innego adresu E-mail.");
//                    return Page();
//                }

//                var user = CreateUser();

//                user.PartnerName = Input.Imie;
//                user.PartnerSurname = Input.Nazwisko;
//                user.IsMember = true;
//                await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
//                await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
//                var isFirstUser = !_userManager.Users.Any();

//                var result = await _userManager.CreateAsync(user, Input.Password);

//                if (result.Succeeded)
//                {
//                    var settings = await _context.Settings.FirstOrDefaultAsync();

//                    if (!settings.VerificationRequired)
//                    {
//                        var affiliateVerification = new AffiliateVerification
//                        {
//                            UserId = user.Id,
//                            AffiliateDescription = "Brak opisu",
//                            IsVerified = true
//                        };
//                        _context.AffiliateVerification.Add(affiliateVerification);
//                        await _context.SaveChangesAsync();
//                    }

//                    string userRole = "Member";

//                    if (isFirstUser)
//                    {
//                        await _userManager.AddToRoleAsync(user, "Admin");
//                        userRole = "Admin";
//                        user.IsMember = false;
//                    }
//                    else
//                    {
//                        await _userManager.AddToRoleAsync(user, "Member");
//                    }

//                    var userId = await _userManager.GetUserIdAsync(user);
//                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
//                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
//                    var callbackUrl = Url.Page(
//                        "/Account/ConfirmEmail",
//                        pageHandler: null,
//                        values: new { area = "Identity", userId = userId, code = code, returnUrl = returnUrl },
//                        protocol: Request.Scheme);

                    
//                    await _userManager.UpdateAsync(user);

//                    await _emailSender.SendEmailAsync(Input.Email, "Potwierdzenie adresu e-mail",
//                        $@"
//                        <div style='font-family: Arial, sans-serif;'>                          
//                            <h2>Dziękujemy za rejestrację w naszym programie partnerskim!</h2>
//                            <p>Cieszymy się, że do nas dołączyłeś. Aby aktywować swoje konto i zacząć korzystać z pełni możliwości programu, konieczne jest potwierdzenie adresu e-mail.</p>
//                            <p>Prosimy o potwierdzenie adresu e-mail, klikając <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>tutaj</a>. Proces ten jest szybki i pozwoli Ci na pełne korzystanie z naszych usług.</p>
//                            <p>Jeśli masz jakiekolwiek pytania lub potrzebujesz pomocy, nasz zespół wsparcia jest do Twojej dyspozycji. Możesz się z nami skontaktować w dowolnym momencie.</p>
//                            <div style='margin-top: 24px;'>
//                                <div style='display: flex; align-items: center; margin-bottom: 10px;'>
//                                    <img src='https://eksperci.myjki.com/images/Mail-b.svg' alt='Email' style='width: 24px; height: 24px; margin-right: 8px;' />
//                                    <span>{settings.ContactEmail}</span>
//                                </div>
//                                <div style='display: flex; align-items: center;'>
//                                    <img src='https://eksperci.myjki.com/images/Phone-b.svg' alt='Phone' style='width: 24px; height: 24px; margin-right: 8px;' />
//                                    <span>{settings.ContactNumber}</span>
//                                </div>
//                            </div>
//                        </div>");

//                    if (_userManager.Options.SignIn.RequireConfirmedAccount)
//                    {
//                        return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = returnUrl });
//                    }
//                    else
//                    {
//                        await _signInManager.SignInAsync(user, isPersistent: false);

//                        if (userRole == "Admin" || userRole == "Manager")
//                        {
//                            return RedirectToAction("Index", "ManagerPanel");
//                        }
//                        else
//                        {
//                            return RedirectToAction("Index", "Panel");
//                        }
//                    }
//                }
//                else
//                {
//                    foreach (var error in result.Errors)
//                    {
//                        if (error.Code == "DuplicateUserName")
//                        {
//                            ModelState.AddModelError(string.Empty, "Użytkownik z tym adresem e-mail już istnieje.");
//                        }
//                        else if (error.Code == "PasswordRequiresNonAlphanumeric")
//                        {
//                            ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jeden znak niealfanumeryczny.");
//                        }
//                        else if (error.Code == "PasswordRequiresDigit")
//                        {
//                            ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jedną cyfrę ('0'-'9').");
//                        }
//                        else if (error.Code == "PasswordRequiresUpper")
//                        {
//                            ModelState.AddModelError(string.Empty, "Hasło musi zawierać przynajmniej jedną dużą literę ('A'-'Z').");
//                        }
//                        else
//                        {
//                            ModelState.AddModelError(string.Empty, error.Description);
//                        }
//                    }
//                }
//            }

//            return Page();
//        }

//        private PriceSafariUser CreateUser()
//        {
//            try
//            {
//                return Activator.CreateInstance<PriceSafariUser>();
//            }
//            catch
//            {
//                throw new InvalidOperationException($"Can't create an instance of '{nameof(PriceSafariUser)}'. " +
//                    $"Ensure that '{nameof(PriceSafariUser)}' is not an abstract class and has a parameterless constructor, or alternatively " +
//                    $"override the register page in /Areas/Identity/Pages/Account/Register.cshtml");
//            }
//        }

//        private IUserEmailStore<PriceSafariUser> GetEmailStore()
//        {
//            if (!_userManager.SupportsUserEmail)
//            {
//                throw new NotSupportedException("The default UI requires a user store with email support.");
//            }
//            return (IUserEmailStore<PriceSafariUser>)_userStore;
//        }
//    }
//}
