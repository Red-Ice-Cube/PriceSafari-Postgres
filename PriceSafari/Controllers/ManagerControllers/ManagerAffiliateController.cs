
using PriceSafari.Models;
using PriceSafari.Models.ManagerViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using PriceSafari.Data;

namespace PriceSafari.Controllers.ManagerControllers
{
    [Authorize(Roles = "Manager, Admin")]
    public class ManagerAffiliateController : Controller

    {
        private readonly PriceSafariContext _context;
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly IEmailSender _emailSender;

        public ManagerAffiliateController(UserManager<PriceSafariUser> userManager, PriceSafariContext context, IEmailSender emailSender)
        {
            _userManager = userManager;
            _context = context;
            _emailSender = emailSender;
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
                .Include(u => u.AffiliateVerification) // Dołącz informacje o weryfikacji
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
                Status = user.IsActive,
                Verification = user.AffiliateVerification?.IsVerified ?? false,
                Role = string.Join(", ", _userManager.GetRolesAsync(user).Result)
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
              .FirstOrDefaultAsync(p => p.CodePAR == codePAR);

            if (user == null)
            {
                return NotFound();
            }

            var managerUserProfileViewModel = new ManagerUserProfileViewModel
            {
                UserName = user.PartnerName,
                UserSurname = user.PartnerSurname,
                UserEmail = user.Email,
                UserCode = user.CodePAR,
                UserJoin = user.CreationDate,
                Status = user.IsActive,
                Verification = user.AffiliateVerification?.IsVerified ?? false,
                //Description = user.AffiliateVerification?.AffiliateDescription
            };

            return View("~/Views/ManagerPanel/Affiliates/UserProfile.cshtml", managerUserProfileViewModel);
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

            // Logika zezwalająca tylko administratorowi na blokowanie dowolnego użytkownika
            if (isCurrentUserAdmin)
            {
                userToBlock.IsActive = false;
                _context.Update(userToBlock);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Accounts));
            }
            // Logika zezwalająca menedżerowi na blokowanie tylko członków
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

            var emailSubject = "Witamy w programie partnerskim!";
            var loginUrl = $"{Request.Scheme}://{Request.Host}/Identity/Account/Login";
            var emailBody = $"<h1>Witaj na pokładzie!</h1>" +
                            $"<p>Dziękujemy za cierpliwość, po przejrzeniu Twojej aplikacji przyjęliśmy Cię do naszego programu partnerskiego! " +
                            $"Możesz się teraz zalogować i korzystać z pełni możliwości naszego programu.</p>" +
                            $"<p><a href='{loginUrl}'>Kliknij tutaj, aby się zalogować</a></p>";

            await _emailSender.SendEmailAsync(userToVerify.Email, emailSubject, emailBody);

            return RedirectToAction(nameof(UserProfile), new { codePAR = codePAR });
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
    }
}