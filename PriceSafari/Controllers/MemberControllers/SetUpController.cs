using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PriceSafari.Data;
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
        private readonly PriceSafariContext _context; // <-- DODAJ TO POLE

        public SetUpController(
            UserManager<PriceSafariUser> userManager,
            IHttpClientFactory httpClientFactory,
            ILogger<SetUpController> logger,
            PriceSafariContext context) // <-- DODAJ W KONSTRUKTORZE
        {
            _httpClientFactory = httpClientFactory;
            _userManager = userManager;
            _logger = logger;
            _context = context; // <-- ZAINICJALIZUJ POLE
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var unreadMessage = await _context.UserMessages
            .FirstOrDefaultAsync(m => m.UserId == user.Id && !m.IsRead);

            var viewModel = new SetUpViewModel
            {
                UserName = user.PartnerName,
                PendingStoreNameCeneo = user.PendingStoreNameCeneo,
                PendingCeneoFeedUrl = user.PendingCeneoFeedUrl,
                PendingStoreNameGoogle = user.PendingStoreNameGoogle,
                PendingGoogleFeedUrl = user.PendingGoogleFeedUrl,
                IsCeneoSubmitted = user.CeneoFeedSubmittedOn.HasValue,
                IsGoogleSubmitted = user.GoogleFeedSubmittedOn.HasValue,
                AdminMessageId = unreadMessage?.Id,
                AdminMessageContent = unreadMessage?.Content
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


        // Dodaj tę nową akcję na końcu klasy SetUpController

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkMessageAsRead([FromBody] MarkAsReadRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            var message = await _context.UserMessages
                .FirstOrDefaultAsync(m => m.Id == request.MessageId && m.UserId == user.Id);

            if (message == null)
            {
                return NotFound(new { success = false, message = "Nie znaleziono wiadomości." });
            }

            message.IsRead = true;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Wiadomość została oznaczona jako przeczytana." });
        }

        // Dodaj klasę pomocniczą dla żądania
        public class MarkAsReadRequest
        {
            public int MessageId { get; set; }
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FetchXmlFeed([FromBody] FeedRequestModel model)
        {

            if (string.IsNullOrEmpty(model.Url) || !Uri.IsWellFormedUriString(model.Url, UriKind.Absolute))
                return BadRequest("Podany adres URL jest nieprawidłowy. Sprawdź jego format.");

            var uri = new Uri(model.Url);

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return BadRequest("Adres URL musi używać protokołu HTTP lub HTTPS.");

            if (uri.IsLoopback)
                return BadRequest("Ze względów bezpieczeństwa, adresy lokalne (localhost) nie są dozwolone.");

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(120);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/xml, text/xml, */*");

                client.DefaultRequestHeaders.UserAgent.ParseAdd("PriceSafari-Feed-Validator/1.0");

                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = response.StatusCode;
                    string errorMessage;

                    switch (statusCode)
                    {
                        case System.Net.HttpStatusCode.NotFound:
                            errorMessage = "Nie znaleziono pliku pod podanym adresem URL (błąd 404). Sprawdź, czy link jest poprawny i spróbuj ponownie.";
                            break;
                        case System.Net.HttpStatusCode.Forbidden:
                        case System.Net.HttpStatusCode.Unauthorized:
                            errorMessage = "Dostęp do pliku jest zablokowany (błąd 401/403). Feed produktowy nie może być chroniony hasłem. Upewnij się, że jest on publicznie dostępny.";
                            break;
                        default:
                            if ((int)statusCode >= 500 && (int)statusCode < 600)
                            {
                                errorMessage = $"Wystąpił błąd na serwerze udostępniającym feed (błąd {(int)statusCode}). Spróbuj ponownie później lub skontaktuj się z administratorem swojego sklepu.";
                            }
                            else
                            {
                                errorMessage = $"Serwer źródłowy zwrócił nieoczekiwany błąd (status: {(int)statusCode}).";
                            }
                            break;
                    }

                    _logger.LogWarning("Błąd podczas pobierania feedu z {Url}. Status: {StatusCode}, Komunikat: {ErrorMessage}", model.Url, statusCode, errorMessage);
                    return StatusCode((int)statusCode, errorMessage);
                }

                long? contentLength = response.Content.Headers.ContentLength;
                const long maxFileSize = 50 * 1024 * 1024;

                _logger.LogInformation("Odpowiedź z {Url}: Status={Status}, Content-Length={Length}",
                    model.Url, response.StatusCode, contentLength.HasValue ? contentLength.Value.ToString() : "nieznany");

                if (contentLength.HasValue && contentLength.Value > maxFileSize)
                {
                    return BadRequest($"Plik jest zbyt duży. Maksymalny dozwolony rozmiar to {maxFileSize / (1024 * 1024)} MB.");
                }

                var stream = await response.Content.ReadAsStreamAsync();
                return File(stream, response.Content.Headers.ContentType?.ToString() ?? "application/xml");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Przekroczono limit czasu (120s) podczas pobierania pliku z {Url}", model.Url);

                return StatusCode(408, "Serwer źródłowy odpowiada zbyt wolno lub przekroczono limit czasu (2 minuty).");
            }

            catch (HttpRequestException ex)
            {

                _logger.LogWarning(ex, "Błąd sieciowy podczas próby połączenia z {Url}", model.Url);
                return StatusCode(503, "Nie można połączyć się z podanym adresem URL. Sprawdź, czy adres jest poprawny, czy serwer działa i czy nie ma problemów z siecią (np. DNS).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Wystąpił nieoczekiwany błąd podczas pobierania pliku z {Url}", model.Url);
                return StatusCode(500, "Wystąpił nieoczekiwany wewnętrzny błąd. Spróbuj ponownie później.");
            }
        }

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