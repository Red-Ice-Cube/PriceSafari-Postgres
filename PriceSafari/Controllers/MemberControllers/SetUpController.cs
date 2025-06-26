using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using PriceSafari.ViewModels; // Upewnij się, że ten using jest poprawny
using System.ComponentModel.DataAnnotations;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "PreMember")]
    public class SetUpController : Controller
    {
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly ILogger<SetUpController> _logger;

        public SetUpController(UserManager<PriceSafariUser> userManager, ILogger<SetUpController> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        // ZMIANA: Akcja Index teraz wczytuje dane i przekazuje do widoku
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Challenge();
            }

            _logger.LogInformation("User {UserName} accessed the setup page.", user.UserName);

            // Tworzymy ViewModel i wypełniamy go istniejącymi danymi z bazy
            var viewModel = new SetUpViewModel
            {
                UserName = user.PartnerName,
                PendingStoreNameCeneo = user.PendingStoreNameCeneo,
                PendingCeneoFeedUrl = user.PendingCeneoFeedUrl,
                PendingStoreNameGoogle = user.PendingStoreNameGoogle,
                PendingGoogleFeedUrl = user.PendingGoogleFeedUrl
            };

            return View("~/Views/Panel/SetUp/Index.cshtml", viewModel);
        }

        // ZMIANA: Nowa, w pełni funkcjonalna akcja zapisu dla Ceneo
        [HttpPost]
        public async Task<IActionResult> SaveCeneo(CeneoSetupModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Proszę wypełnić oba pola poprawnie." });
            }

            var user = await _userManager.GetUserAsync(User);
            user.PendingStoreNameCeneo = model.StoreName;
            user.PendingCeneoFeedUrl = model.FeedUrl;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                _logger.LogInformation("User {user} updated Ceneo setup.", user.UserName);
                return Json(new { success = true, message = "Konfiguracja Ceneo została zapisana!" });
            }

            return Json(new { success = false, message = "Wystąpił błąd podczas zapisu." });
        }

        // ZMIANA: Nowa, w pełni funkcjonalna akcja zapisu dla Google
        [HttpPost]
        public async Task<IActionResult> SaveGoogleShopping(GoogleSetupModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Proszę wypełnić oba pola poprawnie." });
            }

            var user = await _userManager.GetUserAsync(User);
            user.PendingStoreNameGoogle = model.StoreName;
            user.PendingGoogleFeedUrl = model.FeedUrl;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                _logger.LogInformation("User {user} updated Google Shopping setup.", user.UserName);
                return Json(new { success = true, message = "Konfiguracja Google Shopping została zapisana!" });
            }

            return Json(new { success = false, message = "Wystąpił błąd podczas zapisu." });
        }
    }

    // ZMIANA: Nowe, pełne modele dla danych z formularzy
    public class CeneoSetupModel
    {
        [Required]
        public string StoreName { get; set; }
        [Required, Url]
        public string FeedUrl { get; set; }
    }

    public class GoogleSetupModel
    {
        [Required]
        public string StoreName { get; set; }
        [Required, Url]
        public string FeedUrl { get; set; }
    }
}