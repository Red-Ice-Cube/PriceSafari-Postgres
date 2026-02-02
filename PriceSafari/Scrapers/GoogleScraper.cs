


using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

public record GoogleProductIdentifier(string Cid, string Gid, string Hid = null);

public record GoogleProductDetails(string MainTitle, List<string> OfferTitles);

public record GoogleApiDetailsResult(GoogleProductDetails Details, string RequestUrl, string RawResponse);
public class ScraperResult<T>
{
    public T Data { get; set; }
    public bool IsSuccess { get; set; }
    public bool CaptchaEncountered { get; set; }
    public string ErrorMessage { get; set; }

    public ScraperResult(T data, bool isSuccess, bool captchaEncountered, string errorMessage = null)
    {
        Data = data;
        IsSuccess = isSuccess;
        CaptchaEncountered = captchaEncountered;
        ErrorMessage = errorMessage;
    }

    public static ScraperResult<T> Success(T data) => new ScraperResult<T>(data, true, false);
    public static ScraperResult<T> Fail(string errorMessage, T defaultValue = default) => new ScraperResult<T>(defaultValue, false, false, errorMessage);
    public static ScraperResult<T> Captcha(T defaultValue = default) => new ScraperResult<T>(defaultValue, false, true, "CAPTCHA encountered.");
}

public class GoogleScraper
{
    private static readonly HttpClient _httpClient;

    private static readonly Random _random = new Random();

    static GoogleScraper()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.0.0 Safari/537.36");
    }

    private IBrowser _browser;
    private IPage _page;

    public IPage CurrentPage => _page;
    public bool IsCaptchaEncountered { get; private set; }

    public async Task InitializeBrowserAsync()
    {
        IsCaptchaEncountered = false;
        Console.WriteLine("Starting browser initialization (Images & JS Enabled)...");

        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();

        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = false,

            Args = new[]
            {
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-gpu",
            "--disable-blink-features=AutomationControlled",

            "--disable-software-rasterizer",
            "--disable-extensions",
            "--disable-dev-shm-usage",
            "--disable-features=IsolateOrigins,site-per-process",
            "--disable-infobars"

        }
        });

        if (_browser == null)
        {
            throw new Exception("Browser failed to launch.");
        }

        _page = await _browser.NewPageAsync();
        if (_page == null)
        {
            throw new Exception("Failed to create a new page.");
        }

        await _page.SetJavaScriptEnabledAsync(true);

        await _page.EvaluateFunctionOnNewDocumentAsync(@"() => {
        Object.defineProperty(navigator, 'webdriver', { get: () => false, configurable: true });
        Object.defineProperty(navigator, 'plugins', {
            get: () => [
                { name: 'Chrome PDF Viewer' },
                { name: 'Native Client' },
                { name: 'Widevine Content Decryption Module' }
            ],
            configurable: true
        });
    }");

        var commonResolutions = new List<(int width, int height)>
    {
     
        (1920, 1080)
    };

        var random = new Random();
        var randomResolution = commonResolutions[random.Next(commonResolutions.Count)];
        await _page.SetViewportAsync(new ViewPortOptions { Width = randomResolution.width, Height = randomResolution.height });

    }

    public async Task<ScraperResult<List<GoogleProductIdentifier>>> SearchInitialProductIdentifiersAsync(string title, int maxItemsToExtract = 20, int udmValue = 3)
    {
        var identifiers = new List<GoogleProductIdentifier>();
        Console.WriteLine($"[Scraper] Szukam: '{title}' (UDM: {udmValue})...");

        try
        {
            if (_browser == null || _page == null || _page.IsClosed) await InitializeBrowserAsync();

            var encodedTitle = HttpUtility.UrlEncode(title);

            var url = $"https://www.google.com/search?q={encodedTitle}&gl=pl&hl=pl&udm={udmValue}";

            await _page.GoToAsync(url, new NavigationOptions { Timeout = 60000, WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded } });

            Console.WriteLine("[Scraper] Czekam 2 sekund na pełne załadowanie strony...");
            await Task.Delay(2000);

            if (_page.Url.Contains("/sorry/") || _page.Url.Contains("/captcha"))
                return ScraperResult<List<GoogleProductIdentifier>>.Captcha(identifiers);

            string pageContent = await _page.GetContentAsync();

            var regex = new System.Text.RegularExpressions.Regex(
                @"\""[\w-]+\""\s*:\s*\[\s*\""([^\""]*)\""\s*,\s*\""([^\""]*)\""\s*,\s*\""([^\""]*)\""\s*,\s*\""([^\""]*)\""\s*,\s*\""([^\""]*)\""\s*,\s*\""([^\""]*)\""",
                System.Text.RegularExpressions.RegexOptions.Compiled
            );

            var matches = regex.Matches(pageContent);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (identifiers.Count >= maxItemsToExtract) break;

                string idx1 = match.Groups[2].Value;

                string idx2 = match.Groups[3].Value;

                string idx3 = match.Groups[4].Value;

                string idx5 = match.Groups[6].Value;

                string cid = "";
                string gid = "";
                string hid = "";

                if (!string.IsNullOrEmpty(idx1))
                {

                    cid = idx1;
                    hid = idx3;
                    gid = idx5;
                }
                else if (!string.IsNullOrEmpty(idx2))
                {

                    cid = "";

                    hid = idx3;
                    gid = idx2;

                }

                bool isValid = (cid + gid + hid).Any(char.IsDigit) && (hid.Length > 10 || gid.Length > 10);

                if (isValid)
                {
                    if (!identifiers.Any(x => x.Hid == hid && x.Cid == cid))
                    {
                        Console.WriteLine($"   [DEBUG] ZNALAZŁEM -> CID: {cid,-20} | GID: {gid,-20} | HID: {hid}");
                        identifiers.Add(new GoogleProductIdentifier(cid, gid, hid));
                    }
                }
            }

            Console.WriteLine($"[Scraper] Zakończono. Unikalnych produktów: {identifiers.Count}.");
            return ScraperResult<List<GoogleProductIdentifier>>.Success(identifiers);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd: {ex.Message}");
            return ScraperResult<List<GoogleProductIdentifier>>.Fail(ex.Message, identifiers);
        }
    }
    public async Task<ScraperResult<List<string>>> FindStoreUrlsFromApiAsync(string cid, string gid)
    {
        // --- [POPRAWKA START] ---
        // Jeśli nie ma CID, nie ma sensu pytać API o "catalogid", bo zwróci 0 wyników.
        if (string.IsNullOrWhiteSpace(cid))
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [SKIP] Brak CID (jest pusty). Pomijam pobieranie ofert dla GID: {gid}.");
            return ScraperResult<List<string>>.Success(new List<string>());
        }
        // --- [POPRAWKA KONIEC] ---

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Rozpoczynam zbieranie URL-i z API dla CID: {cid}, GID: {gid}");
        var allStoreUrls = new List<string>();

        string urlTemplate = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{cid},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,query:1,pvt:hg,_fmt:jspb";

        int startIndex = 0;
        const int pageSize = 10; // Oczekujemy paczek po 10
        int lastFetchCount;
        const int maxRetries = 3;

        try
        {
            do
            {
                string currentUrl = string.Format(urlTemplate, startIndex);
                List<string> newUrls = new List<string>();

                // [LOG]
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"\n[DEBUG-HTTP] >>> Konstrukcja zapytania (Start: {startIndex}):");
                Console.WriteLine($"[DEBUG-HTTP] URL: {currentUrl}");
                Console.ResetColor();

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        string rawResponse = await _httpClient.GetStringAsync(currentUrl);
                        newUrls = GoogleApiUrlParser.Parse(rawResponse);

                        // [LOG]
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"[DEBUG-HTTP] <<< Odpowiedź odebrana. Znaleziono {newUrls.Count} surowych URLi.");
                        foreach (var rawUrl in newUrls)
                        {
                            Console.WriteLine($"   -> [RAW]: {rawUrl}");
                        }
                        Console.WriteLine("[DEBUG-HTTP] -----------------------------------------------------------");
                        Console.ResetColor();

                        // Jeśli cokolwiek znaleziono lub odpowiedź jest bardzo krótka (błąd), wychodzimy z pętli retry
                        if (newUrls.Any() || rawResponse.Length < 100)
                        {
                            break;
                        }

                        if (attempt < maxRetries) await Task.Delay(2000);
                    }
                    catch (HttpRequestException ex)
                    {
                        Console.WriteLine($"Błąd HTTP (próba {attempt}/{maxRetries}) dla CID {cid}: {ex.Message}");
                        if (attempt == maxRetries)
                        {
                            return ScraperResult<List<string>>.Fail($"Nie udało się pobrać ofert z API po {maxRetries} próbach.", allStoreUrls);
                        }
                        await Task.Delay(2500);
                    }
                }

                lastFetchCount = newUrls.Count;

                foreach (var url in newUrls)
                {
                    string extractedUrl = ExtractStoreUrlFromGoogleRedirect(url);
                    string cleanedUrl = CleanUrlParameters(extractedUrl);
                    allStoreUrls.Add(cleanedUrl);
                }

                Console.WriteLine($"– Zebrano {newUrls.Count} linków z API na stronie start={startIndex}.");

                startIndex += pageSize;

                // Małe opóźnienie, żeby nie zajechać serwera Google
                if (lastFetchCount >= pageSize) await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(500, 800)));

                // --- TU JEST KLUCZOWA ZMIANA ---
                // Warunek: Kontynuuj TYLKO JEŚLI pobrano tyle ile wynosi pageSize (10) LUB WIĘCEJ.
                // Jeśli pobierze 8 (lastFetchCount < pageSize), warunek będzie fałszywy i pętla się zakończy.
            } while (lastFetchCount >= pageSize);

            return ScraperResult<List<string>>.Success(allStoreUrls.Distinct().ToList());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd krytyczny podczas paginacji API dla CID {cid}: {ex.Message}");
            return ScraperResult<List<string>>.Fail($"Błąd paginacji API: {ex.Message}", allStoreUrls);
        }
    }

    private string ExtractStoreUrlFromGoogleRedirect(string googleRedirectUrl)
    {
        try
        {
            string fullUrl = googleRedirectUrl;
            if (googleRedirectUrl.StartsWith("/url?q="))
            {
                fullUrl = "https://www.google.com" + googleRedirectUrl;
            }

            var uri = new Uri(fullUrl);
            var queryParams = HttpUtility.ParseQueryString(uri.Query);
            var storeUrlEncoded = queryParams["q"] ?? queryParams["url"];

            if (!string.IsNullOrEmpty(storeUrlEncoded))
            {
                var storeUrl = System.Web.HttpUtility.UrlDecode(storeUrlEncoded);
                return storeUrl;
            }
            else
            {
                return googleRedirectUrl;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"BŁĄD podczas ekstrakcji URL sklepu z przekierowania: {ex.Message}");
            return googleRedirectUrl;
        }
    }

    public async Task<ScraperResult<GoogleApiDetailsResult>> GetProductDetailsFromApiAsync(string cid, string gid, string hid = null, string targetCode = null)
    {
        string identifierParam = !string.IsNullOrEmpty(cid) ? $"catalogid:{cid}" : $"headlineOfferDocid:{hid}";
        var gpcidPart = !string.IsNullOrEmpty(gid) ? $"gpcid:{gid}," : "";
        var url = $"https://www.google.com/async/oapv?udm=3&gl=pl&hl=pl&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async={gpcidPart}{identifierParam},pvo:3,fs:%2Fshopping%2Foffers,sori:0,mno:10,query:1,pvt:hg,_fmt:jspb";

        Console.WriteLine($"\n--- [API REQUEST] ---------------------------------------");
        Console.WriteLine($"[Tryb]: {(!string.IsNullOrEmpty(cid) ? "CATALOG" : "OFFER")}");
        if (!string.IsNullOrEmpty(targetCode)) Console.WriteLine($"[Szukany Kod]: {targetCode}");
        Console.WriteLine($"[URL]: {url}");

        try
        {
            var responseString = await _httpClient.GetStringAsync(url);

            string debugSnippet = responseString.Length > 200 ? responseString.Substring(0, 200) : responseString;
            Console.WriteLine($"[RAW]: {debugSnippet.Replace("\n", " ").Replace("\r", " ")}");

            if (responseString.Contains("/sorry/Captcha")) return ScraperResult<GoogleApiDetailsResult>.Captcha();

            string cleanedJson = responseString.StartsWith(")]}'") ? responseString.Substring(5) : responseString;

            string mainTitle = null;
            var offerTitles = new HashSet<string>();
            bool isGhostResponse = false;

            using (JsonDocument doc = JsonDocument.Parse(cleanedJson))
            {
                if (doc.RootElement.TryGetProperty("ProductDetailsResult", out var dr) && dr.ValueKind == JsonValueKind.Array)
                {
                    if (dr.GetArrayLength() > 0 && dr[0].ValueKind == JsonValueKind.String)
                    {
                        mainTitle = dr[0].GetString();
                    }

                    if (dr.GetArrayLength() == 0 || string.IsNullOrWhiteSpace(mainTitle))
                    {
                        isGhostResponse = true;
                    }
                }
                else
                {
                    isGhostResponse = true;
                }

                void FindTitlesRecursive(JsonElement el)
                {
                    if (el.ValueKind == JsonValueKind.Array) foreach (var i in el.EnumerateArray()) FindTitlesRecursive(i);
                    else if (el.ValueKind == JsonValueKind.Object) foreach (var p in el.EnumerateObject()) FindTitlesRecursive(p.Value);
                    else if (el.ValueKind == JsonValueKind.String)
                    {
                        string t = el.GetString();
                        if (t.Length > 10 && t.Contains(' ') && !t.StartsWith("http") && !t.Contains("??")) offerTitles.Add(t);
                    }
                }
                FindTitlesRecursive(doc.RootElement);
            }

            if (isGhostResponse && offerTitles.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[BŁĄD API] Odpowiedź nie zawiera żadnych danych produktu (Empty Result).");
                Console.ResetColor();
                return ScraperResult<GoogleApiDetailsResult>.Fail("API Fail: Empty Response");
            }

            bool isCodeFound = false;
            if (!string.IsNullOrEmpty(targetCode))
            {
                isCodeFound = responseString.Contains(targetCode, StringComparison.OrdinalIgnoreCase) ||
                              responseString.Replace(" ", "").Contains(targetCode.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
            }

            var details = new GoogleProductDetails(mainTitle ?? offerTitles.FirstOrDefault(), offerTitles.ToList());

            if (string.IsNullOrEmpty(targetCode) || isCodeFound)
            {
                if (isCodeFound)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[SUKCES] Kod '{targetCode}' ZNALEZIONY w odpowiedzi!");
                }
                else
                {

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[DANE POBRANE] Pobrano dane produktu (weryfikacja kodu pominięta/zewnętrzna).");
                }

                Console.WriteLine($"[PRODUKT]: {details.MainTitle}");
                Console.ResetColor();
                return ScraperResult<GoogleApiDetailsResult>.Success(new GoogleApiDetailsResult(details, url, responseString));
            }
            else
            {

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[DANE OK] Odpowiedź poprawna, ale kod '{targetCode}' NIE występuje w treści.");
                Console.WriteLine($"[PRODUKT]: {details.MainTitle}");
                Console.ResetColor();
                return ScraperResult<GoogleApiDetailsResult>.Fail($"Match fail: {targetCode} not in response");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine($"[API EXCEPTION] {ex.Message}");
            Console.ResetColor();
            return ScraperResult<GoogleApiDetailsResult>.Fail(ex.Message);
        }
    }

    public string CleanUrlParameters(string url)
    {
        if (string.IsNullOrEmpty(url))
            return url;

        int qm = url.IndexOf("?");
        if (qm > 0)
            url = url.Substring(0, qm);

        int htmlIdx = url.LastIndexOf(".html");
        if (htmlIdx > 0)
        {
            var basePart = url.Substring(0, htmlIdx);
            int dot = basePart.LastIndexOf(".");
            if (dot > 0)
            {
                var suffix = basePart[(dot + 1)..];
                if (new[] { "google", "shopping", "merchant", "gshop", "product" }
                    .Any(s => suffix.Contains(s, StringComparison.OrdinalIgnoreCase)))
                {
                    url = basePart.Substring(0, dot) + ".html";
                }
            }
        }

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate("https://" + url, UriKind.Absolute, out var u1))
                url = u1.ToString();
            else if (Uri.TryCreate("http://" + url, UriKind.Absolute, out var u2))
                url = u2.ToString();
        }

        int hash = url.IndexOf("#");
        if (hash > 0)
            url = url.Substring(0, hash);

        return url;
    }

    public async Task CloseBrowserAsync()
    {
        if (_page != null && !_page.IsClosed)
        {
            try { await _page.CloseAsync(); } catch (Exception ex) { Console.WriteLine($"Błąd podczas zamykania strony: {ex.Message}"); }
        }
        if (_browser != null)
        {
            try { await _browser.CloseAsync(); } catch (Exception ex) { Console.WriteLine($"Błąd podczas zamykania przeglądarki: {ex.Message}"); }
        }
        _browser = null;
        _page = null;
        IsCaptchaEncountered = false;
    }

    public async Task ResetBrowserAsync()
    {
        Console.WriteLine("Wykonywanie PEŁNEGO RESETU PRZEGLĄDARKI...");
        await CloseBrowserAsync();
        await InitializeBrowserAsync();
        Console.WriteLine("Pełny reset przeglądarki zakończony.");
    }

    public static class GoogleApiUrlParser
    {
        public static List<string> Parse(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse)) return new List<string>();

            try
            {
                string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
                using JsonDocument doc = JsonDocument.Parse(cleanedJson);
                JsonElement root = doc.RootElement.Clone();

                var allUrls = new List<string>();
                FindAndParseOfferUrls(root, allUrls);
                return allUrls.Distinct().ToList();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        private static void FindAndParseOfferUrls(JsonElement node, List<string> allUrls)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in node.EnumerateArray())
                {
                    FindAndParseOfferUrls(element, allUrls);
                }
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in node.EnumerateObject())
                {
                    FindAndParseOfferUrls(property.Value, allUrls);
                }
            }
            else if (node.ValueKind == JsonValueKind.String)
            {
                string? potentialUrl = node.GetString();
                if (!string.IsNullOrEmpty(potentialUrl) &&
                    potentialUrl.StartsWith("http") &&
                    !potentialUrl.Contains("google.com") &&
                    !potentialUrl.Contains("gstatic.com"))
                {
                    allUrls.Add(potentialUrl);
                }
            }
        }
    }
}