using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PriceSafari.Models;
using PriceSafari.Models.ViewModels;
using PriceSafari.ViewModels;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;

namespace PriceSafari.Controllers
{
    [Authorize(Roles = "PreMember")]
    public class SetUpController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly UserManager<PriceSafariUser> _userManager;
        private readonly ILogger<SetUpController> _logger;

        public SetUpController(UserManager<PriceSafariUser> userManager, IHttpClientFactory httpClientFactory, ILogger<SetUpController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var viewModel = new SetUpViewModel
            {
                UserName = user.PartnerName,
                PendingStoreNameCeneo = user.PendingStoreNameCeneo,
                PendingCeneoFeedUrl = user.PendingCeneoFeedUrl,
                PendingStoreNameGoogle = user.PendingStoreNameGoogle,
                PendingGoogleFeedUrl = user.PendingGoogleFeedUrl,

                IsCeneoSubmitted = user.CeneoFeedSubmittedOn.HasValue,
                IsGoogleSubmitted = user.GoogleFeedSubmittedOn.HasValue
            };

            return View("~/Views/Panel/SetUp/Index.cshtml", viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveCeneo(CeneoSetupModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Proszę wypełnić oba pola poprawnie." });
            }

            var user = await _userManager.GetUserAsync(User);
            user.PendingStoreNameCeneo = model.StoreName;
            user.PendingCeneoFeedUrl = model.FeedUrl;
            user.CeneoFeedSubmittedOn = DateTime.UtcNow;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                return Json(new { success = true, message = "Konfiguracja Ceneo została przyjęta do realizacji." });
            }

            return Json(new { success = false, message = "Wystąpił błąd podczas zapisu." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveGoogleShopping(GoogleSetupModel model)
        {
            if (!ModelState.IsValid)
            {
                return Json(new { success = false, message = "Proszę wypełnić oba pola poprawnie." });
            }

            var user = await _userManager.GetUserAsync(User);
            user.PendingStoreNameGoogle = model.StoreName;
            user.PendingGoogleFeedUrl = model.FeedUrl;
            user.GoogleFeedSubmittedOn = DateTime.UtcNow;

            var result = await _userManager.UpdateAsync(user);
            if (result.Succeeded)
            {
                return Json(new { success = true, message = "Konfiguracja Google Shopping została przyjęta do realizacji." });
            }

            return Json(new { success = false, message = "Wystąpił błąd podczas zapisu." });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FetchXmlFeed([FromBody] FeedRequestModel model)
        {
            // Walidacja URL (bez zmian)
            if (string.IsNullOrEmpty(model.Url) || !Uri.IsWellFormedUriString(model.Url, UriKind.Absolute))
                return BadRequest("Nieprawidłowy format adresu URL.");

            var uri = new Uri(model.Url);

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return BadRequest("Dozwolone są tylko protokoły HTTP i HTTPS.");

            if (uri.IsLoopback)
                return BadRequest("Żądania do adresów lokalnych (localhost) są zabronione.");

            try
            {
                var client = _httpClientFactory.CreateClient();
                // ZWIĘKSZAMY TIMEOUT do 2 minut jako zabezpieczenie
                client.Timeout = TimeSpan.FromSeconds(120);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/xml, text/xml, */*");

                var request = new HttpRequestMessage(HttpMethod.Get, uri);

                // KRYTYCZNA ZMIANA: Wracamy do ResponseHeadersRead, aby natychmiast rozpocząć streaming
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, "Serwer źródłowy zwrócił błąd.");
                }

                long? contentLength = response.Content.Headers.ContentLength;
                const long maxFileSize = 50 * 1024 * 1024; // 50 MB

                _logger.LogInformation("Odpowiedź z {Url}: Status={Status}, Content-Length={Length}",
                    model.Url, response.StatusCode, contentLength.HasValue ? contentLength.Value.ToString() : "nieznany");

                if (contentLength.HasValue && contentLength.Value > maxFileSize)
                {
                    return BadRequest($"Plik jest zbyt duży. Maksymalny dozwolony rozmiar to {maxFileSize / (1024 * 1024)} MB.");
                }

                // Zawsze zwracamy strumień - to najbardziej wydajne i odporne na wolne połączenia
                var stream = await response.Content.ReadAsStreamAsync();
                return File(stream, "application/xml");
            }
            catch (TaskCanceledException ex) // Specjalna obsługa timeoutu
            {
                _logger.LogWarning(ex, "Przekroczono limit czasu (120s) podczas pobierania pliku z {Url}", model.Url);
                return StatusCode(408, "Serwer źródłowy nie odpowiedział lub przesyła dane zbyt wolno (timeout).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił nieoczekiwany błąd podczas pobierania pliku z {Url}", model.Url);
                return StatusCode(500, "Wystąpił wewnętrzny błąd serwera. Prosimy spróbować ponownie później.");
            }
        }
        // Model zostaje bez zmian
        public class FeedRequestModel
        {
            public string Url { get; set; }
        }
    }

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