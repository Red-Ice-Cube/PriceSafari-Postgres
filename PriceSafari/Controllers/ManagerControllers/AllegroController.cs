using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http; // dla HttpContext.Session
using PriceSafari.Models;

public class AllegroController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;

    // Production API (jeżeli pracujesz z prawdziwym kontem Allegro)
    private static readonly string AllegroAuthUrl = "https://allegro.pl/auth/oauth/token";
    private static readonly string AllegroApiUrl = "https://api.allegro.pl/offers/listing";

    // Własne ID i Secret
    private static readonly string ClientId = "3af8adf76b094ff7902decd063367c3b";
    private static readonly string ClientSecret = "g5S0Yyv4j4rPCLiOnBw805UAozqX9DXKbmOkdjIl7aVQsZuRDgIeWdOUocwt6J4A";

    // Upewnij się, że ten adres jest wpisany w Panelu Allegro w „Adresy URI do przekierowania”
    private static readonly string RedirectUri = "https://localhost:7213/callback";

    public AllegroController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public IActionResult Index()
    {
        // Wyświetlanie widoku z formularzem, np. input "phrase" / przycisk "Zaloguj się"
        return View("~/Views/ManagerPanel/Allegro/Index.cshtml");
    }

    [HttpGet("login")]
    public IActionResult Login()
    {
        // Generujemy URL autoryzacji do Allegro (OAuth2).
        // Upewnij się, że w parametrach jest EXACT ten sam redirectUri.
        var authUrl = $"https://allegro.pl/auth/oauth/authorize?response_type=code&client_id={ClientId}&redirect_uri={RedirectUri}";

        // Przekierowanie użytkownika do Allegro, by się zalogował i wyraził zgodę
        return Redirect(authUrl);
    }

    // Kiedy Allegro skończy autoryzację, przekieruje nas z powrotem na to URL: /callback (HTTPS)
    [HttpGet("/callback")]
    public async Task<IActionResult> Callback(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            ViewBag.Error = "Błąd autoryzacji Allegro – brak kodu.";
            return View("~/Views/ManagerPanel/Allegro/Index.cshtml");
        }

        try
        {
            // Wymieniamy "code" na właściwy access_token
            var accessToken = await GetAccessToken(code);

            // Zapisujemy token do Session (musisz mieć włączoną obsługę Session w Startup/Program.cs)
            HttpContext.Session.SetString("AllegroAccessToken", accessToken);

            // Przechodzimy ponownie do widoku głównego
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            ViewBag.Error = $"Błąd: {ex.Message}";
            return View("~/Views/ManagerPanel/Allegro/Index.cshtml");
        }
    }

    // Zamiast "ean" - param "phrase" (np. "Smartfon Samsung")
    [HttpPost]
    public async Task<IActionResult> GetOffers(string phrase)
    {
        if (string.IsNullOrEmpty(phrase))
        {
            ViewBag.Error = "Fraza nie może być pusta.";
            return View("~/Views/ManagerPanel/Allegro/Index.cshtml");
        }

        // Odczytujemy token zapisany w Session
        var accessToken = HttpContext.Session.GetString("AllegroAccessToken");
        if (string.IsNullOrEmpty(accessToken))
        {
            ViewBag.Error = "Brak ważnego tokena. Zaloguj się ponownie przez Allegro.";
            return View("~/Views/ManagerPanel/Allegro/Index.cshtml");
        }

        try
        {
            // Tworzymy klienta HTTP z nagłówkiem Bearer token
            using var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            // Tworzymy URL z parametrem ?phrase=...
            var url = $"{AllegroApiUrl}?phrase={System.Web.HttpUtility.UrlEncode(phrase)}";
            // (UrlEncode - żeby obsłużyć spacje i znaki specjalne, np. "Smartfon Samsung S20")

            var response = await client.GetAsync(url);

            // Jeżeli status nie jest OK (np. 403, 400, 500 itp.), logujemy zawartość:
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                ViewBag.Error = $"Błąd API Allegro: {response.StatusCode}\nTreść odpowiedzi: {errorBody}";
                return View("~/Views/ManagerPanel/Allegro/Index.cshtml");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AllegroOffersResponse>(
                jsonResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            // Tutaj w result?.Items?.Regular masz oferty
            return View("~/Views/ManagerPanel/Allegro/Index.cshtml", result?.Items?.Regular);
        }
        catch (Exception ex)
        {
            ViewBag.Error = $"Błąd: {ex.Message}";
            return View("~/Views/ManagerPanel/Allegro/Index.cshtml");
        }
    }

    private async Task<string> GetAccessToken(string authorizationCode)
    {
        using var client = _httpClientFactory.CreateClient();

        // Basic auth: Base64(ClientId:ClientSecret)
        var authHeader = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{ClientId}:{ClientSecret}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);

        // Wysyłamy żądanie POST do /auth/oauth/token z danymi: grant_type, code, redirect_uri
        var request = new HttpRequestMessage(HttpMethod.Post, AllegroAuthUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", authorizationCode },
                { "redirect_uri", RedirectUri }
            })
        };

        var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Błąd pobierania tokena: {response.StatusCode}. Odpowiedź Allegro: {errorContent}");
        }

        var jsonResponse = await response.Content.ReadAsStringAsync();
        var tokenData = JsonSerializer.Deserialize<AllegroTokenResponse>(
            jsonResponse,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        // Zwracamy właściwy access_token
        return tokenData.Access_token;
    }
}
