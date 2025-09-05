using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
using PriceSafari.Models;
using PriceSafari.Models.ManagerViewModels;
using PriceSafari.Services.EmailService;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Admin")]
    public class ManagerAffiliateController : Controller

    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly IAppEmailSender _emailSender;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ManagerAffiliateController(UserManager<PriceSafariUser> userManager, PriceSafariContext context, IAppEmailSender emailSender, IWebHostEnvironment webHostEnvironment)
        {
            _userManager = userManager;
            _context = context;
            _emailSender = emailSender;
            _webHostEnvironment = webHostEnvironment;
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("Użytkownik nie jest zalogowany lub nie istnieje.");
            }

            var affiliates = await _context.Users
                .Where(u => u.IsMember)
                .OrderByDescending(c => c.CreationDate)
                .ToListAsync();

            var affiliatesViewModels = affiliates.Select(c => new ManagerAffiliate
            {
                Name = c.PartnerName,
                Surname = c.PartnerSurname,
                CodePAR = c.CodePAR,
                UserName = c.UserName,
            }).ToList();

            var model = new ManagerAffiliateViewModel
            {
                ManagerAffiliate = affiliatesViewModels
            };

            return View("~/Views/ManagerPanel/Affiliates/Index.cshtml", model);
        }

        [Authorize]
        public async Task<IActionResult> Accounts()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return NotFound("Użytkownik nie jest zalogowany lub nie istnieje.");
            }

            var isUserAdmin = await _userManager.IsInRoleAsync(currentUser, "Admin");

            var allUsers = await _context.Users
                .Include(u => u.AffiliateVerification)
                .OrderByDescending(u => u.CreationDate)
                .ToListAsync();

            var filteredUsers = new List<PriceSafariUser>();
            foreach (var user in allUsers)
            {
                var roles = await _userManager.GetRolesAsync(user);
                if (isUserAdmin || (!roles.Contains("Admin")))
                {
                    filteredUsers.Add(user);
                }
            }

            var affiliatesViewModels = filteredUsers.Select(user => new ManagerAffiliate
            {
                Name = user.PartnerName,
                Surname = user.PartnerSurname,
                CodePAR = user.CodePAR,
                UserName = user.UserName,
                LogCount = user.LoginCount,
                LastLogi = user.LastLoginDateTime,
                Status = user.IsActive,
                Verification = user.AffiliateVerification?.IsVerified ?? false,
                Role = string.Join(", ", _userManager.GetRolesAsync(user).Result),
                EmailConfirmed = user.EmailConfirmed
            }).ToList();

            var model = new ManagerAffiliateViewModel
            {
                ManagerAffiliate = affiliatesViewModels
            };

            return View("~/Views/ManagerPanel/Affiliates/Accounts.cshtml", model);
        }

        public async Task<IActionResult> UserProfile(string codePAR)
        {
            if (codePAR == null || _context.Users == null)
            {
                return NotFound();
            }

            var user = await _context.Users
              .Include(p => p.AffiliateVerification)
              .Include(u => u.UserMessages) // <-- DODAJ TO
              .FirstOrDefaultAsync(p => p.CodePAR == codePAR);

            if (user == null)
            {
                return NotFound();
            }

            var roles = await _userManager.GetRolesAsync(user);
            var primaryRole = roles.FirstOrDefault();

            var message = user.UserMessages.FirstOrDefault();

            var managerUserProfileViewModel = new ManagerUserProfileViewModel
            {

                UserName = user.PartnerName,
                UserSurname = user.PartnerSurname,
                UserEmail = user.Email,
                UserCode = user.CodePAR,
                UserJoin = user.CreationDate,
                Status = user.IsActive,
                Verification = user.AffiliateVerification?.IsVerified ?? false,
                UserStatus = user.Status,
                LastLogin = user.LastLoginDateTime,
                LoginCount = user.LoginCount,
                CeneoStoreName = user.PendingStoreNameCeneo,
                CeneoFeedUrl = user.PendingCeneoFeedUrl,
                CeneoFeedSubmittedOn = user.CeneoFeedSubmittedOn,
                GoogleStoreName = user.PendingStoreNameGoogle,
                GoogleFeedUrl = user.PendingGoogleFeedUrl,
                GoogleFeedSubmittedOn = user.GoogleFeedSubmittedOn,
                Role = primaryRole,
                PhoneNumber = user.PhoneNumber,
                UserMessageId = message?.Id,
                UserMessageContent = message?.Content
            };

            return View("~/Views/ManagerPanel/Affiliates/UserProfile.cshtml", managerUserProfileViewModel);
        }







        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddOrUpdateUserMessage([FromBody] UserMessageRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new { message = "Nieprawidłowe dane." });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.CodePAR == request.CodePAR);
            if (user == null)
            {
                return NotFound(new { message = "Nie znaleziono użytkownika." });
            }

            var message = await _context.UserMessages.FirstOrDefaultAsync(m => m.UserId == user.Id);

            if (message == null) // Dodaj nową wiadomość
            {
                message = new UserMessage
                {
                    UserId = user.Id,
                    Content = request.Content,
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false
                };
                _context.UserMessages.Add(message);
            }
            else // Zaktualizuj istniejącą
            {
                message.Content = request.Content;
                message.IsRead = false; // Resetuj status 'przeczytane' po edycji
                _context.UserMessages.Update(message);
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "Wiadomość została zapisana.", messageId = message.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUserMessage([FromBody] UserMessageIdRequest request)
        {
            var message = await _context.UserMessages.FindAsync(request.MessageId);
            if (message == null)
            {
                return NotFound(new { message = "Nie znaleziono wiadomości." });
            }

            _context.UserMessages.Remove(message);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Wiadomość została usunięta." });
        }

        // Klasy pomocnicze dla żądań
        public class UserMessageRequest
        {
            public string CodePAR { get; set; }
            public string Content { get; set; }
        }

        public class UserMessageIdRequest
        {
            public int MessageId { get; set; }
        }







        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PromoteToMember(string codePAR)
        {
            if (string.IsNullOrEmpty(codePAR))
            {
                return BadRequest("Nie podano kodu PAR użytkownika.");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.CodePAR == codePAR);
            if (user == null)
            {
                return NotFound("Nie znaleziono użytkownika.");
            }

            if (await _userManager.IsInRoleAsync(user, "PreMember"))
            {

                var removeResult = await _userManager.RemoveFromRoleAsync(user, "PreMember");
                if (!removeResult.Succeeded)
                {

                    TempData["ErrorMessage"] = "Nie udało się usunąć roli PreMember.";
                    return RedirectToAction("UserProfile", new { codePAR = user.CodePAR });
                }

                var addResult = await _userManager.AddToRoleAsync(user, "Member");
                if (!addResult.Succeeded)
                {

                    TempData["ErrorMessage"] = "Nie udało się dodać roli Member.";

                    await _userManager.AddToRoleAsync(user, "PreMember");
                    return RedirectToAction("UserProfile", new { codePAR = user.CodePAR });
                }

                user.Status = UserStatus.Active;
                var updateResult = await _userManager.UpdateAsync(user);

                if (updateResult.Succeeded)
                {
                    TempData["SuccessMessage"] = "Rola użytkownika została pomyślnie zmieniona na Member.";
                }
            }
            else
            {
                TempData["WarningMessage"] = "Użytkownik nie ma roli PreMember.";
            }

            return RedirectToAction("UserProfile", new { codePAR = user.CodePAR });
        }

        [HttpPost]
        public async Task<IActionResult> BlockUser(string codePAR)
        {
            if (string.IsNullOrEmpty(codePAR))
            {
                return NotFound();
            }

            var userToBlock = await _context.Users.FirstOrDefaultAsync(u => u.CodePAR == codePAR);
            if (userToBlock == null)
            {
                return NotFound("Nie znaleziono użytkownika.");
            }

            var rolesToBlock = await _userManager.GetRolesAsync(userToBlock);

            var currentUser = await _userManager.GetUserAsync(User);
            var isCurrentUserAdmin = await _userManager.IsInRoleAsync(currentUser, "Admin");
            var isCurrentUserManager = await _userManager.IsInRoleAsync(currentUser, "Manager");

            if (isCurrentUserAdmin)
            {
                userToBlock.IsActive = false;
                _context.Update(userToBlock);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Accounts));
            }

            else if (isCurrentUserManager && rolesToBlock.All(r => r == "Member"))
            {
                userToBlock.IsActive = false;
                _context.Update(userToBlock);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Accounts));
            }
            else
            {
                return View("Error", new ErrorViewModel { RequestId = "Nie masz uprawnień do zablokowania tego użytkownika." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UnblockUser(string codePAR)
        {
            if (string.IsNullOrEmpty(codePAR))
            {
                return NotFound();
            }

            var userToUnblock = await _context.Users.FirstOrDefaultAsync(u => u.CodePAR == codePAR);
            if (userToUnblock == null)
            {
                return NotFound("Nie znaleziono użytkownika.");
            }

            var currentUser = await _userManager.GetUserAsync(User);
            var isCurrentUserAdmin = await _userManager.IsInRoleAsync(currentUser, "Admin");
            var isCurrentUserManager = await _userManager.IsInRoleAsync(currentUser, "Manager");

            var rolesOfUserToUnblock = await _userManager.GetRolesAsync(userToUnblock);

            if (isCurrentUserAdmin)
            {
                userToUnblock.IsActive = true;
                _context.Update(userToUnblock);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Accounts));
            }
            else if (isCurrentUserManager && rolesOfUserToUnblock.All(r => r == "Member"))
            {
                userToUnblock.IsActive = true;
                _context.Update(userToUnblock);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Accounts));
            }
            else
            {
                return View("Error", new ErrorViewModel { RequestId = "Nie masz uprawnień do odblokowania tego użytkownika." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> VerifyUser(string codePAR)
        {
            if (string.IsNullOrEmpty(codePAR))
            {
                return NotFound("Nie podano kodu użytkownika.");
            }

            var userToVerify = await _context.Users
                .Include(u => u.AffiliateVerification)
                .FirstOrDefaultAsync(u => u.CodePAR == codePAR);

            if (userToVerify == null)
            {
                return NotFound("Nie znaleziono użytkownika.");
            }

            if (userToVerify.AffiliateVerification != null && userToVerify.AffiliateVerification.IsVerified)
            {
                return RedirectToAction(nameof(UserProfile), new { codePAR = codePAR });
            }

            if (userToVerify.AffiliateVerification == null)
            {
                userToVerify.AffiliateVerification = new AffiliateVerification { IsVerified = true };
            }
            else
            {
                userToVerify.AffiliateVerification.IsVerified = true;
            }

            _context.Update(userToVerify);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(UserProfile), new { codePAR = codePAR });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendPanelReadyEmail([FromBody] CodeParRequest request)
        {
            var codePAR = request.CodePAR;

            if (string.IsNullOrEmpty(codePAR))
            {
                return BadRequest("Kod PAR użytkownika nie może być pusty.");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.CodePAR == codePAR);
            if (user == null)
            {
                return NotFound("Nie znaleziono użytkownika o podanym kodzie PAR.");
            }

            var nameForGreeting = !string.IsNullOrWhiteSpace(user.PartnerName) ? user.PartnerName : user.Email;
            var emailSubject = "Twój panel Price Safari jest gotowy!";
            var loginUrl = $"https://panel.pricesafari.pl/Identity/Account/Login";
            var emailBody = GeneratePanelReadyEmailBody(nameForGreeting, loginUrl);

            var logoPath = Path.Combine(_webHostEnvironment.WebRootPath, "cid", "PriceSafari.png");
            var inlineImages = new Dictionary<string, string>
    {
        { "PriceSafariLogo", logoPath }
    };

            try
            {

                await _emailSender.SendEmailAsync(user.Email, emailSubject, emailBody, inlineImages);
                return Ok(new { message = "Wiadomość e-mail została pomyślnie wysłana." });
            }
            catch (Exception ex)
            {

                return StatusCode(500, new { error = "Wystąpił błąd podczas wysyłania wiadomości e-mail." });
            }
        }

        public class CodeParRequest
        {
            public string CodePAR { get; set; }
        }

        private string GeneratePanelReadyEmailBody(string userName, string loginUrl)
        {

            string guidesUrl = "https://pricesafari.pl/Guide";

            return $@"
    <!DOCTYPE html>
    <html lang=""pl"">
    <head>
        <meta charset=""UTF-8"">
        <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
        <style>
            body {{
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
                margin: 0;
                padding: 0;
                background-color: #f5f5f7;
            }}
            .container {{
                max-width: 560px;
                margin: 40px auto;
                background-color: #ffffff;
                overflow: hidden;
            }}
            .header {{
                padding: 10px 0px; 
            }}
            .top-bar {{
                height: 1px;
                margin-bottom: 10px;
                background-color: #222222; 
            }}
            .content {{
                padding-top:10px;
                padding-bottom:10px;
                line-height: 1.6;
                color: #1d1d1f; 
                font-size: 16px;
            }}
            .content p {{
                margin: 0 0 16px 0;
            }}
            .button {{
                display: inline-block;
                background-color: #41C7C7;
                color: #ffffff !important;
                padding: 12px 24px;
                border-radius: 8px;
                text-decoration: none;
                font-weight: 500;
                font-size: 16px;
                margin: 10px 0 20px 0;
            }}
            .footer {{
                background-color: #f5f5f7;
                color: #86868b;
                padding: 20px 40px;
                text-align: center;
                font-size: 16px; 
                line-height: 1.5;
            }}
        </style>
    </head>
    <body>
        <div class=""container"">
            <div class=""header"">
                <img src=""cid:PriceSafariLogo"" alt=""Price Safari Logo"" style=""height: 32px; width: auto;"">
            </div>
            <div class=""top-bar""></div>
            <div class=""content"">
                <p>Cześć {userName},</p>
                <p>Z przyjemnością informujemy, że Twój panel w Price Safari jest już gotowy do działania! Możesz się teraz zalogować i w pełni korzystać z możliwości naszej aplikacji w ramach okresu próbnego.</p>
                <p>Co dalej? Przez kolejne 7 dni będziemy codziennie aktualizować dane o cenach Twoich produktów. Twoje konto pozwala również na eksport danych i korzystanie z funkcji Pilota Cenowego w celu masowych zmian cen w Twoim sklepie internetowym.</p>
                <p>Jeśli chcesz sprawnie postawić pierwsze kroki z naszą aplikacją, przygotowaliśmy dla Ciebie sekcję z poradnikami, którą znajdziesz pod tym adresem: <a href=""{guidesUrl}"" style=""color: #41C7C7;"">przejdź do poradników</a>.</p>
                <p>Aby zobaczyć swój panel i pierwsze zebrane dane, zaloguj się na swoje konto:</p>
                <a href=""{loginUrl}"" class=""button"">Zaloguj się do panelu</a>
                <p style=""margin-top: 16px;"">Dziękujemy za zaufanie i życzymy owocnego korzystania z Price Safari!</p>
            </div>
            <div class=""footer"">
                <p>Z pozdrowieniami,<br>Zespół Price Safari</p>
                <p style=""margin-top: 16px;"">&copy; {DateTime.Now.Year} Price Safari<br>Wszelkie prawa zastrzeżone.</p>
            </div>
        </div>
    </body>
    </html>";
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetAffiliatesData()
        {
            DateTime userLocalTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Local);
            DateTime startDate = userLocalTime.Date.AddDays(-30);
            DateTime endDate = userLocalTime.Date.AddDays(1).AddTicks(-1);

            var affiliates = await _context.Users
               .Where(u => u.IsMember)
               .OrderByDescending(c => c.CreationDate)
               .ToListAsync();

            var affiliatesData = new List<AffiliateData>();

            return Json(affiliatesData);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> HardDeleteUser(string codePAR)
        {
            if (string.IsNullOrWhiteSpace(codePAR))
                return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            if (!await _userManager.IsInRoleAsync(currentUser, "Admin"))
                return Forbid();

            var user = await _context.Users
                .Include(u => u.AffiliateVerification)
                .FirstOrDefaultAsync(u => u.CodePAR == codePAR);

            if (user == null)
                return NotFound("Nie znaleziono użytkownika.");

            var deleteResult = await _userManager.DeleteAsync(user);

            if (!deleteResult.Succeeded)
            {
                foreach (var err in deleteResult.Errors)
                    ModelState.AddModelError(string.Empty, err.Description);

                return View("Error", new ErrorViewModel
                {
                    RequestId = "Błąd podczas usuwania użytkownika."
                });
            }

            TempData["SuccessMessage"] = "Użytkownik został pomyślnie usunięty.";

            return RedirectToAction(nameof(Accounts));
        }
    }
}