using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

public record GoogleProductIdentifier(string Cid, string Gid);

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

        _page = await _browser.NewPageAsync();
    }

    //public async Task<ScraperResult<List<GoogleProductIdentifier>>> SearchInitialProductIdentifiersAsync(string title, int maxItemsToExtract = 20)

    //{
    //    var identifiers = new List<GoogleProductIdentifier>();
    //    Console.WriteLine($"Navigating to Google Shopping with product title: {title} to extract initial Identifiers (CID, GID).");

    //    IsCaptchaEncountered = false;

    //    try
    //    {
    //        if (_browser == null || _page == null || _page.IsClosed)
    //        {
    //            await InitializeBrowserAsync();
    //        }

    //        var encodedTitle = HttpUtility.UrlEncode(title);
    //        var url = $"https://www.google.com/search?gl=pl&tbm=shop&q={encodedTitle}";

    //        await _page.GoToAsync(url, new NavigationOptions { Timeout = 60000, WaitUntil = new[] { WaitUntilNavigation.Load } });

    //        if (_page.Url.Contains("/sorry/") || _page.Url.Contains("/captcha"))
    //        {
    //            IsCaptchaEncountered = true;

    //            return ScraperResult<List<GoogleProductIdentifier>>.Captcha(identifiers);

    //        }
    //        var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
    //        if (rejectButton != null)
    //        {
    //            await rejectButton.ClickAsync();
    //            await Task.Delay(1000);
    //        }

    //        Console.WriteLine("Strona wyników Google Shopping załadowana pomyślnie.");



    //        try
    //        {
    //            await _page.WaitForSelectorAsync("div.sh-dgr__content, div.MtXiu, div.LrTUQ", new WaitForSelectorOptions { Timeout = 5000 });
    //        }
    //        catch (WaitTaskTimeoutException ex)
    //        {
    //            Console.WriteLine($"Nie znaleziono żadnego znanego kontenera produktów w określonym czasie: {ex.Message}. Strona mogła się zmienić lub brak wyników.");

    //            return ScraperResult<List<GoogleProductIdentifier>>.Fail("Nie znaleziono boksów produktów.", identifiers);

    //        }

    //        var productBoxes = await _page.QuerySelectorAllAsync("div.sh-dgr__content, div.MtXiu, div.LrTUQ");

    //        Console.WriteLine($"Znaleziono {productBoxes.Length} boksów produktów na stronie wyników.");

    //        if (productBoxes.Length == 0)
    //        {

    //            return ScraperResult<List<GoogleProductIdentifier>>.Success(identifiers);

    //        }

    //        foreach (var box in productBoxes)
    //        {

    //            if (identifiers.Count >= maxItemsToExtract) break;

    //            var idData = await box.EvaluateFunctionAsync<JsonElement>(@"
    //                element => {
    //                    const cid = element.dataset.cid || 
    //                               element.querySelector('a[data-cid]')?.dataset.cid || 
    //                               element.querySelector('a[data-docid]')?.dataset.docid;
    //                    const gid = element.dataset.gid;
    //                    return { cid, gid };
    //                }
    //            ");

    //            var cid = idData.TryGetProperty("cid", out var cidProp) ? cidProp.GetString() : null;
    //            var gid = idData.TryGetProperty("gid", out var gidProp) ? gidProp.GetString() : null;

    //            if (!string.IsNullOrEmpty(cid) && !string.IsNullOrEmpty(gid))
    //            {

    //                identifiers.Add(new GoogleProductIdentifier(cid, gid));
    //                Console.WriteLine($"Ekstrahowano CID: {cid}, GID: {gid}");

    //            }
    //        }

    //        return ScraperResult<List<GoogleProductIdentifier>>.Success(identifiers);

    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"Błąd podczas wyszukiwania i ekstrakcji identyfikatorów: {ex.Message}");

    //        return ScraperResult<List<GoogleProductIdentifier>>.Fail($"Błąd ekstrakcji: {ex.Message}", identifiers);

    //    }
    //}


    public async Task<ScraperResult<List<GoogleProductIdentifier>>> SearchInitialProductIdentifiersAsync(string title, int maxItemsToExtract = 20)
    {
        var identifiers = new List<GoogleProductIdentifier>();
        Console.WriteLine($"[Scraper] Szukam: '{title}'... (Cel: {maxItemsToExtract} unikalnych produktów)");

        IsCaptchaEncountered = false;

        try
        {
            if (_browser == null || _page == null || _page.IsClosed) await InitializeBrowserAsync();

            var tcs = new TaskCompletionSource<GoogleProductIdentifier>();

            // 1. Konfiguracja przechwytywania
            await _page.SetRequestInterceptionAsync(true);

            EventHandler<RequestEventArgs> requestHandler = (sender, e) =>
            {
                e.Request.ContinueAsync();

                // Szukamy requestu oapv z catalogid
                if (e.Request.Url.Contains("/async/oapv") && e.Request.Url.Contains("catalogid:"))
                {
                    var url = e.Request.Url;
                    var cidMatch = System.Text.RegularExpressions.Regex.Match(url, @"catalogid:(\d+)");
                    var gidMatch = System.Text.RegularExpressions.Regex.Match(url, @"gpcid:(\d+)");

                    if (cidMatch.Success)
                    {
                        string cid = cidMatch.Groups[1].Value;
                        string gid = gidMatch.Success ? gidMatch.Groups[1].Value : null;
                        tcs.TrySetResult(new GoogleProductIdentifier(cid, gid));
                    }
                }
            };

            _page.Request += requestHandler;

            // 2. Nawigacja
            var encodedTitle = HttpUtility.UrlEncode(title);
            var url = $"https://www.google.com/search?gl=pl&tbm=shop&q={encodedTitle}";
            await _page.GoToAsync(url, new NavigationOptions { Timeout = 60000, WaitUntil = new[] { WaitUntilNavigation.Load } });

            // Captcha / Cookies
            if (_page.Url.Contains("/sorry/") || _page.Url.Contains("/captcha"))
            {
                _page.Request -= requestHandler;
                await _page.SetRequestInterceptionAsync(false);
                IsCaptchaEncountered = true;
                return ScraperResult<List<GoogleProductIdentifier>>.Captcha(identifiers);
            }
            var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
            if (rejectButton != null) { await rejectButton.ClickAsync(); await Task.Delay(500); } // Skrócone opóźnienie po cookies

            Console.WriteLine("[Scraper] Strona załadowana. Rozpoczynam precyzyjne zbieranie (SZYBKIE)...");

            // 3. Pobieramy elementy - celujemy w obrazki, są najbardziej "klikalne"
            var elementsToClick = await _page.QuerySelectorAllAsync(
                "div.sh-dgr__content img, " +
                "div.MtXiu img, " +
                "product-viewer-entrypoint img"
            );

            Console.WriteLine($"[Scraper] Znaleziono {elementsToClick.Length} potencjalnych produktów.");

            int successfulClicks = 0;
            int failedClicksStreak = 0; // Licznik pustych przebiegów z rzędu

            foreach (var element in elementsToClick)
            {
                // Warunek wyjścia
                if (identifiers.Count >= maxItemsToExtract) break;

                // Jeśli 5 razy z rzędu kliknęliśmy i nic nie wpadło, a mamy już jakieś wyniki, to kończymy (optymalizacja czasu)
                if (identifiers.Count > 0 && failedClicksStreak > 5)
                {
                    Console.WriteLine("[Scraper] Zbyt wiele pustych kliknięć z rzędu. Przerywam.");
                    break;
                }

                tcs = new TaskCompletionSource<GoogleProductIdentifier>();

                try
                {
                    // Przewiń do elementu
                    await element.EvaluateFunctionAsync("el => el.scrollIntoView({block: 'center', inline: 'center'})");

                    // OPTYMALIZACJA 1: Skrócone czekanie na render
                    await Task.Delay(30);

                    // Kliknięcie JS
                    await element.EvaluateFunctionAsync("el => el.click()");

                    var networkTask = tcs.Task;
                    // OPTYMALIZACJA 2: Skrócony timeout nasłuchiwania (800ms zamiast 1500ms)
                    // Google reaguje zazwyczaj w <200ms. 
                    var timeoutTask = Task.Delay(800);

                    var completedTask = await Task.WhenAny(networkTask, timeoutTask);

                    if (completedTask == networkTask)
                    {
                        var foundId = await networkTask;
                        if (!identifiers.Any(x => x.Cid == foundId.Cid))
                        {
                            identifiers.Add(foundId);
                            Console.WriteLine($"[NETWORK] + ZŁAPANO ({identifiers.Count}/{maxItemsToExtract}): CID {foundId.Cid}");
                            failedClicksStreak = 0; // Reset licznika błędów
                        }
                        successfulClicks++;
                    }
                    else
                    {
                        // Timeout
                        failedClicksStreak++;
                    }
                }
                catch (Exception)
                {
                    // Ignorujemy błędy, jedziemy dalej
                    failedClicksStreak++;
                }
            }

            // Sprzątanie
            _page.Request -= requestHandler;
            await _page.SetRequestInterceptionAsync(false);

            Console.WriteLine($"[Scraper] Zakończono. Zebrano łącznie: {identifiers.Count}. Czekam 1s...");
            await Task.Delay(1000);

            return ScraperResult<List<GoogleProductIdentifier>>.Success(identifiers);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd: {ex.Message}");
            return ScraperResult<List<GoogleProductIdentifier>>.Fail($"Błąd: {ex.Message}", identifiers);
        }
    }

    public async Task<ScraperResult<string>> GetTitleFromProductPageAsync(string cid)
    {
        try
        {
            var url = $"https://www.google.com/shopping/product/{cid}";
            await _page.GoToAsync(url, new NavigationOptions { Timeout = 60000, WaitUntil = new[] { WaitUntilNavigation.Load } });

            if (_page.Url.Contains("/sorry/") || _page.Url.Contains("/captcha"))
            {
                IsCaptchaEncountered = true;
                return ScraperResult<string>.Captcha(string.Empty);
            }

            var titleElement = await _page.WaitForSelectorAsync("span.BvQan.sh-t__title-pdp", new WaitForSelectorOptions { Timeout = 5000 });
            if (titleElement != null)
            {
                var title = await titleElement.EvaluateFunctionAsync<string>("el => el.textContent.trim()");
                return ScraperResult<string>.Success(title);
            }

            return ScraperResult<string>.Fail("Nie znaleziono elementu z tytułem na stronie produktu.");
        }
        catch (WaitTaskTimeoutException)
        {
            return ScraperResult<string>.Fail("Timeout podczas oczekiwania na tytuł na stronie produktu.");
        }
        catch (Exception ex)
        {
            return ScraperResult<string>.Fail($"Błąd podczas pobierania tytułu: {ex.Message}");
        }
    }

    public async Task<ScraperResult<List<string>>> FindStoreUrlsFromApiAsync(string cid, string gid)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Rozpoczynam zbieranie URL-i z API dla CID: {cid}, GID: {gid}");
        var allStoreUrls = new List<string>();

        string urlTemplate = $"https://www.google.com/async/oapv?udm=28&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{gid},catalogid:{cid},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";

        int startIndex = 0;
        const int pageSize = 10;
        int lastFetchCount;
        const int maxRetries = 3;

        try
        {
            do
            {
                string currentUrl = string.Format(urlTemplate, startIndex);
                List<string> newUrls = new List<string>();

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        string rawResponse = await _httpClient.GetStringAsync(currentUrl);
                        newUrls = GoogleApiUrlParser.Parse(rawResponse);

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
                if (lastFetchCount == pageSize) await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(500, 800)));

            } while (lastFetchCount == pageSize);

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

    public async Task<ScraperResult<GoogleApiDetailsResult>> GetProductDetailsFromApiAsync(string cid, string gid)
    {
        var url = $"https://www.google.com/async/oapv?udm=28&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=gpcid:{gid},catalogid:{cid},pvo:3,fs:%2Fshopping%2Foffers,sori:0,mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";

        try
        {
            var responseString = await _httpClient.GetStringAsync(url);

            if (responseString.Contains("/sorry/Captcha") || responseString.Contains("unusual traffic"))
            {
                return ScraperResult<GoogleApiDetailsResult>.Captcha();
            }

            if (string.IsNullOrWhiteSpace(responseString))
            {
                return ScraperResult<GoogleApiDetailsResult>.Fail("API zwróciło pustą odpowiedź.");
            }

            string cleanedJson = responseString.StartsWith(")]}'") ? responseString.Substring(5) : responseString;

            using (JsonDocument doc = JsonDocument.Parse(cleanedJson))
            {
                JsonElement root = doc.RootElement;
                string mainTitle = null;
                var offerTitles = new HashSet<string>();

                if (root.TryGetProperty("ProductDetailsResult", out var detailsResult) && detailsResult.ValueKind == JsonValueKind.Array)
                {
                    var titleElement = detailsResult.EnumerateArray().FirstOrDefault();
                    if (titleElement.ValueKind == JsonValueKind.String)
                    {
                        mainTitle = titleElement.GetString();
                    }
                }

                void FindOfferTitlesRecursively(JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.Array)
                    {

                        if (element.GetArrayLength() > 4 &&
                            element[0].ValueKind == JsonValueKind.String &&
                            element[1].ValueKind == JsonValueKind.String &&
                            element[2].ValueKind == JsonValueKind.Null &&

                            element[3].ValueKind == JsonValueKind.String)
                        {

                            foreach (var item in element.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    string potentialTitle = item.GetString();

                                    if (!string.IsNullOrWhiteSpace(potentialTitle) && potentialTitle.Contains(' ') && !potentialTitle.StartsWith("http"))
                                    {
                                        offerTitles.Add(potentialTitle);
                                    }
                                }
                            }
                        }
                        else

                        {
                            foreach (var item in element.EnumerateArray())
                            {
                                FindOfferTitlesRecursively(item);
                            }
                        }
                    }
                    else if (element.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in element.EnumerateObject())
                        {
                            FindOfferTitlesRecursively(property.Value);
                        }
                    }
                }

                FindOfferTitlesRecursively(root);

                var productDetails = new GoogleProductDetails(mainTitle, offerTitles.ToList());
                var apiResult = new GoogleApiDetailsResult(productDetails, url, cleanedJson);

                if (string.IsNullOrEmpty(mainTitle) && !offerTitles.Any())
                {
                    return ScraperResult<GoogleApiDetailsResult>.Fail("Nie znaleziono żadnych tytułów w odpowiedzi API.");
                }

                return ScraperResult<GoogleApiDetailsResult>.Success(apiResult);
            }
        }
        catch (Exception ex)
        {
            return ScraperResult<GoogleApiDetailsResult>.Fail($"Błąd podczas parsowania detali produktu: {ex.Message}");
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