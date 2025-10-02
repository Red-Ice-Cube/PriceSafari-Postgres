//using PuppeteerSharp;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading.Tasks;
//using System.Web;

//public class ScraperResult<T>
//{
//    public T Data { get; set; }
//    public bool IsSuccess { get; set; }
//    public bool CaptchaEncountered { get; set; }
//    public string ErrorMessage { get; set; }

//    public ScraperResult(T data, bool isSuccess, bool captchaEncountered, string errorMessage = null)
//    {
//        Data = data;
//        IsSuccess = isSuccess;
//        CaptchaEncountered = captchaEncountered;
//        ErrorMessage = errorMessage;
//    }

//    public static ScraperResult<T> Success(T data) => new ScraperResult<T>(data, true, false);
//    public static ScraperResult<T> Fail(string errorMessage, T defaultValue = default) => new ScraperResult<T>(defaultValue, false, false, errorMessage);
//    public static ScraperResult<T> Captcha(T defaultValue = default) => new ScraperResult<T>(defaultValue, false, true, "CAPTCHA encountered.");
//}

//public class GoogleScraper
//{
//    private IBrowser _browser;
//    private IPage _page;

//    public IPage CurrentPage => _page;
//    public bool IsCaptchaEncountered { get; private set; }

//    public async Task InitializeBrowserAsync()
//    {
//        IsCaptchaEncountered = false;
//        var browserFetcher = new BrowserFetcher();
//        await browserFetcher.DownloadAsync();

//        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
//        {
//            Headless = false,
//            Args = new[]
//            {
//             "--no-sandbox",
//             "--disable-setuid-sandbox",
//             "--disable-gpu",
//             "--disable-blink-features=AutomationControlled",
//             "--disable-software-rasterizer",
//             "--disable-extensions",
//             "--disable-dev-shm-usage",
//             "--disable-features=IsolateOrigins,site-per-process",
//             "--disable-infobars"
//            }
//        });

//        _page = await _browser.NewPageAsync();
//    }

//    public async Task<ScraperResult<List<string>>> SearchInitialProductCIDsAsync(string title, int maxCIDsToExtract = 10)
//    {
//        var cids = new List<string>();
//        Console.WriteLine($"Navigating to Google Shopping with product title: {title} to extract initial CIDs.");
//        IsCaptchaEncountered = false;

//        try
//        {
//            if (_browser == null || _page == null || _page.IsClosed)
//            {
//                await InitializeBrowserAsync();
//            }

//            var encodedTitle = HttpUtility.UrlEncode(title);
//            var url = $"https://www.google.com/search?gl=pl&tbm=shop&q={encodedTitle}";

//            await _page.GoToAsync(url, new NavigationOptions { Timeout = 60000, WaitUntil = new[] { WaitUntilNavigation.Load } });

//            if (_page.Url.Contains("/sorry/") || _page.Url.Contains("/captcha"))
//            {
//                IsCaptchaEncountered = true;
//                return ScraperResult<List<string>>.Captcha(cids);
//            }

//            var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
//            if (rejectButton != null)
//            {
//                await rejectButton.ClickAsync();
//                await Task.Delay(1000);
//            }

//            Console.WriteLine("Strona wyników Google Shopping załadowana pomyślnie.");

//            try
//            {
//                await _page.WaitForSelectorAsync("div.sh-dgr__content, div.MtXiu, div.LrTUQ", new WaitForSelectorOptions { Timeout = 5000 });
//            }
//            catch (WaitTaskTimeoutException ex)
//            {
//                Console.WriteLine($"Nie znaleziono żadnego znanego kontenera produktów w określonym czasie: {ex.Message}. Strona mogła się zmienić lub brak wyników.");
//                return ScraperResult<List<string>>.Fail("Nie znaleziono boksów produktów.", cids);
//            }

//            var productBoxes = await _page.QuerySelectorAllAsync("div.sh-dgr__content, div.MtXiu, div.LrTUQ");

//            Console.WriteLine($"Znaleziono {productBoxes.Length} boksów produktów na stronie wyników.");

//            if (productBoxes.Length == 0)
//            {
//                return ScraperResult<List<string>>.Success(cids);
//            }

//            foreach (var box in productBoxes)
//            {
//                if (cids.Count >= maxCIDsToExtract) break;

//                string cid = await box.EvaluateFunctionAsync<string>(@"
//                 element => {

//                     if (element.dataset.cid) return element.dataset.cid;

//                     const linkWithCid = element.querySelector('a[data-cid]');
//                     if (linkWithCid) return linkWithCid.dataset.cid;

//                     const linkWithDocid = element.querySelector('a[data-docid]');
//                     if (linkWithDocid) return linkWithDocid.dataset.docid;

//                     return null;
//                 }
//                ");

//                if (!string.IsNullOrEmpty(cid))
//                {
//                    cids.Add(cid);
//                    Console.WriteLine($"Ekstrahowano CID: {cid}");
//                }
//            }
//            return ScraperResult<List<string>>.Success(cids);
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Błąd podczas wyszukiwania i ekstrakcji CID-ów: {ex.Message}");
//            return ScraperResult<List<string>>.Fail($"Błąd ekstrakcji CID: {ex.Message}", cids);
//        }
//    }

//    public async Task<ScraperResult<string>> GetTitleFromProductPageAsync(string cid)
//    {
//        try
//        {
//            var url = $"https://www.google.com/shopping/product/{cid}";
//            await _page.GoToAsync(url, new NavigationOptions { Timeout = 60000, WaitUntil = new[] { WaitUntilNavigation.Load } });

//            if (_page.Url.Contains("/sorry/") || _page.Url.Contains("/captcha"))
//            {
//                IsCaptchaEncountered = true;
//                return ScraperResult<string>.Captcha(string.Empty);
//            }

//            var titleElement = await _page.WaitForSelectorAsync("span.BvQan.sh-t__title-pdp", new WaitForSelectorOptions { Timeout = 5000 });
//            if (titleElement != null)
//            {
//                var title = await titleElement.EvaluateFunctionAsync<string>("el => el.textContent.trim()");
//                return ScraperResult<string>.Success(title);
//            }

//            return ScraperResult<string>.Fail("Nie znaleziono elementu z tytułem na stronie produktu.");
//        }
//        catch (WaitTaskTimeoutException)
//        {
//            return ScraperResult<string>.Fail("Timeout podczas oczekiwania na tytuł na stronie produktu.");
//        }
//        catch (Exception ex)
//        {
//            return ScraperResult<string>.Fail($"Błąd podczas pobierania tytułu: {ex.Message}");
//        }
//    }

//    public async Task<ScraperResult<List<string>>> NavigateToProductPageAndExpandOffersAsync(string cid)
//    {
//        Console.WriteLine($"Rozpoczynam zbieranie ofert produktu Google Shopping (CID: {cid})...");
//        var allStoreUrls = new List<string>();
//        IsCaptchaEncountered = false;

//        int start = 0;
//        const int pageSize = 20;
//        const int maxRetries = 2;

//        try
//        {
//            if (_browser == null || _page == null || _page.IsClosed)
//                await InitializeBrowserAsync();

//            while (true)
//            {
//                var url = $"https://www.google.com/shopping/product/{cid}/offers?prds=cid:{cid},cond:1,cs:1,start:{start}&gl=pl&hl=pl";

//                bool navigationSuccess = false;
//                for (int attempt = 0; attempt <= maxRetries; attempt++)
//                {
//                    try
//                    {
//                        if (attempt > 0)
//                        {
//                            Console.WriteLine($"Próba {attempt + 1}/{maxRetries + 1}: Nawigacja nie powiodła się. Resetuję przeglądarkę.");
//                            await ResetBrowserAsync();
//                        }

//                        Console.WriteLine($"– Nawigacja do: {url} (Próba {attempt + 1})");
//                        await _page.GoToAsync(url, new NavigationOptions
//                        {
//                            Timeout = 60000,
//                            WaitUntil = new[] { WaitUntilNavigation.Load }
//                        });

//                        var currentUrl = _page.Url;
//                        if (currentUrl.Contains("/sorry/") || currentUrl.Contains("/captcha"))
//                        {
//                            Console.WriteLine($"❌ CAPTCHA lub strona błędu wykryta na próbie {attempt + 1}.");
//                            IsCaptchaEncountered = true;

//                            continue;
//                        }

//                        if (!currentUrl.Contains(cid) || !currentUrl.Contains("/offers"))
//                        {
//                            Console.WriteLine($"❌ Wykryto przekierowanie na niespodziewany URL: {currentUrl}. Próbuję ponownie.");

//                            continue;
//                        }

//                        navigationSuccess = true;
//                        break;
//                    }
//                    catch (Exception navEx)
//                    {
//                        Console.WriteLine($"❌ Błąd krytyczny podczas nawigacji (próba {attempt + 1}): {navEx.Message}");

//                    }
//                }

//                if (!navigationSuccess)
//                {
//                    Console.WriteLine($"BŁĄD KRYTYCZNY: Nie udało się załadować strony ofert dla CID: {cid} po {maxRetries + 1} próbach.");
//                    return ScraperResult<List<string>>.Fail("Nie udało się załadować strony ofert po wielokrotnych próbach.", allStoreUrls);
//                }

//                var extractResult = await ExtractStoreOffersAsync(_page);

//                if (extractResult.CaptchaEncountered)
//                    return ScraperResult<List<string>>.Captcha(allStoreUrls);

//                if (!extractResult.IsSuccess)
//                    return ScraperResult<List<string>>.Fail(extractResult.ErrorMessage, allStoreUrls);

//                var urls = extractResult.Data;
//                Console.WriteLine($"– Zebrano {urls.Count} linków na stronie start={start}.");
//                allStoreUrls.AddRange(urls);

//                if (urls.Count < pageSize)
//                    break;

//                start += pageSize;
//            }

//            return ScraperResult<List<string>>.Success(allStoreUrls);
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Błąd podczas paginacji ofert (CID: {cid}): {ex.Message}");
//            return ScraperResult<List<string>>.Fail($"Błąd paginacji ofert: {ex.Message}", allStoreUrls);
//        }
//    }

//    public async Task<ScraperResult<List<string>>> ExtractStoreOffersAsync(IPage page)
//    {

//        var storeUrls = new List<string>();
//        IsCaptchaEncountered = false;

//        try
//        {
//            if (page.Url.Contains("/sorry/") || page.Url.Contains("/captcha"))
//            {
//                Console.WriteLine("Strona ofert to CAPTCHA. Przerywam ekstrakcję.");
//                IsCaptchaEncountered = true;
//                return ScraperResult<List<string>>.Captcha(storeUrls);
//            }

//            try
//            {
//                await page.WaitForSelectorAsync("a.sh-osd__seller-link", new WaitForSelectorOptions { Timeout = 1000 });
//            }
//            catch (WaitTaskTimeoutException)
//            {
//                try
//                {
//                    await page.WaitForSelectorAsync("div.UAVKwf > a.UxuaJe.shntl.FkMp", new WaitForSelectorOptions { Timeout = 1000 });
//                }
//                catch (WaitTaskTimeoutException ex)
//                {
//                    Console.WriteLine($"Nie znaleziono linków ofert: {ex.Message}. Zwracam pustą listę.");
//                    return ScraperResult<List<string>>.Success(storeUrls);
//                }
//            }

//            var offerLinkElements = await page.QuerySelectorAllAsync("a.sh-osd__seller-link");
//            if (offerLinkElements.Length == 0)
//                offerLinkElements = await page.QuerySelectorAllAsync("div.UAVKwf > a.UxuaJe.shntl.FkMp");

//            Console.WriteLine($"Znaleziono {offerLinkElements.Length} linków ofert.");

//            foreach (var linkElement in offerLinkElements)
//            {
//                var rawStoreUrl = await linkElement.EvaluateFunctionAsync<string>("el => el.href");
//                if (string.IsNullOrEmpty(rawStoreUrl))
//                {
//                    Console.WriteLine("Pusty href, pomijam...");
//                    continue;
//                }

//                string extractedStoreUrl = rawStoreUrl.Contains("google.com/url")
//                    ? ExtractStoreUrlFromGoogleRedirect(rawStoreUrl)
//                    : rawStoreUrl;

//                var cleanedStoreUrl = CleanUrlParameters(extractedStoreUrl);
//                storeUrls.Add(cleanedStoreUrl);
//                Console.WriteLine($"Ekstrahowano i oczyszczono URL: {cleanedStoreUrl}");
//            }

//            return ScraperResult<List<string>>.Success(storeUrls);
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Błąd przy ekstrakcji ofert sklepów: {ex.Message}");
//            return ScraperResult<List<string>>.Fail($"Błąd ekstrakcji ofert: {ex.Message}", storeUrls);
//        }
//    }

//    private string ExtractStoreUrlFromGoogleRedirect(string googleRedirectUrl)
//    {

//        try
//        {

//            string fullUrl = googleRedirectUrl;
//            if (googleRedirectUrl.StartsWith("/url?q="))
//            {
//                fullUrl = "https://www.google.com" + googleRedirectUrl;
//            }

//            var uri = new Uri(fullUrl);
//            var queryParams = HttpUtility.ParseQueryString(uri.Query);
//            var storeUrlEncoded = queryParams["q"] ?? queryParams["url"];

//            if (!string.IsNullOrEmpty(storeUrlEncoded))
//            {
//                var storeUrl = System.Web.HttpUtility.UrlDecode(storeUrlEncoded);
//                return storeUrl;
//            }
//            else
//            {
//                return googleRedirectUrl;
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"BŁĄD podczas ekstrakcji URL sklepu z przekierowania: {ex.Message}");
//            return googleRedirectUrl;
//        }
//    }

//    public string CleanUrlParameters(string url)
//    {

//        if (string.IsNullOrEmpty(url))
//            return url;

//        int qm = url.IndexOf("?");
//        if (qm > 0)
//            url = url.Substring(0, qm);

//        int htmlIdx = url.LastIndexOf(".html");
//        if (htmlIdx > 0)
//        {
//            var basePart = url.Substring(0, htmlIdx);
//            int dot = basePart.LastIndexOf(".");
//            if (dot > 0)
//            {
//                var suffix = basePart[(dot + 1)..];
//                if (new[] { "google", "shopping", "merchant", "gshop", "product" }
//                    .Any(s => suffix.Contains(s, StringComparison.OrdinalIgnoreCase)))
//                {
//                    url = basePart.Substring(0, dot) + ".html";
//                }
//            }
//        }

//        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
//            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
//        {
//            if (Uri.TryCreate("https://" + url, UriKind.Absolute, out var u1))
//                url = u1.ToString();
//            else if (Uri.TryCreate("http://" + url, UriKind.Absolute, out var u2))
//                url = u2.ToString();
//        }

//        int hash = url.IndexOf("#");
//        if (hash > 0)
//            url = url.Substring(0, hash);

//        return url;
//    }

//    public async Task CloseBrowserAsync()
//    {

//        if (_page != null && !_page.IsClosed)
//        {
//            try { await _page.CloseAsync(); } catch (Exception ex) { Console.WriteLine($"Błąd podczas zamykania strony: {ex.Message}"); }
//        }
//        if (_browser != null)
//        {
//            try { await _browser.CloseAsync(); } catch (Exception ex) { Console.WriteLine($"Błąd podczas zamykania przeglądarki: {ex.Message}"); }
//        }
//        _browser = null;
//        _page = null;
//        IsCaptchaEncountered = false;
//    }

//    public async Task ResetBrowserAsync()
//    {
//        Console.WriteLine("Wykonywanie PEŁNEGO RESETU PRZEGLĄDARKI...");
//        await CloseBrowserAsync();
//        await InitializeBrowserAsync();
//        Console.WriteLine("Pełny reset przeglądarki zakończony.");
//    }
//}







using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

public record GoogleProductIdentifier(string Cid, string Gid);

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

    public async Task<ScraperResult<List<GoogleProductIdentifier>>> SearchInitialProductIdentifiersAsync(string title, int maxItemsToExtract = 20)

    {
        var identifiers = new List<GoogleProductIdentifier>();
        Console.WriteLine($"Navigating to Google Shopping with product title: {title} to extract initial Identifiers (CID, GID).");

        IsCaptchaEncountered = false;

        try
        {
            if (_browser == null || _page == null || _page.IsClosed)
            {
                await InitializeBrowserAsync();
            }

            var encodedTitle = HttpUtility.UrlEncode(title);
            var url = $"https://www.google.com/search?gl=pl&tbm=shop&q={encodedTitle}";

            await _page.GoToAsync(url, new NavigationOptions { Timeout = 60000, WaitUntil = new[] { WaitUntilNavigation.Load } });

            if (_page.Url.Contains("/sorry/") || _page.Url.Contains("/captcha"))
            {
                IsCaptchaEncountered = true;

                return ScraperResult<List<GoogleProductIdentifier>>.Captcha(identifiers);

            }
            var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
            if (rejectButton != null)
            {
                await rejectButton.ClickAsync();
                await Task.Delay(1000);
            }

            Console.WriteLine("Strona wyników Google Shopping załadowana pomyślnie.");

            try
            {
                await _page.WaitForSelectorAsync("div.sh-dgr__content, div.MtXiu, div.LrTUQ", new WaitForSelectorOptions { Timeout = 5000 });
            }
            catch (WaitTaskTimeoutException ex)
            {
                Console.WriteLine($"Nie znaleziono żadnego znanego kontenera produktów w określonym czasie: {ex.Message}. Strona mogła się zmienić lub brak wyników.");

                return ScraperResult<List<GoogleProductIdentifier>>.Fail("Nie znaleziono boksów produktów.", identifiers);

            }

            var productBoxes = await _page.QuerySelectorAllAsync("div.sh-dgr__content, div.MtXiu, div.LrTUQ");

            Console.WriteLine($"Znaleziono {productBoxes.Length} boksów produktów na stronie wyników.");

            if (productBoxes.Length == 0)
            {

                return ScraperResult<List<GoogleProductIdentifier>>.Success(identifiers);

            }

            foreach (var box in productBoxes)
            {

                if (identifiers.Count >= maxItemsToExtract) break;

                var idData = await box.EvaluateFunctionAsync<JsonElement>(@"
                    element => {
                        const cid = element.dataset.cid || 
                                   element.querySelector('a[data-cid]')?.dataset.cid || 
                                   element.querySelector('a[data-docid]')?.dataset.docid;
                        const gid = element.dataset.gid;
                        return { cid, gid };
                    }
                ");

                var cid = idData.TryGetProperty("cid", out var cidProp) ? cidProp.GetString() : null;
                var gid = idData.TryGetProperty("gid", out var gidProp) ? gidProp.GetString() : null;

                if (!string.IsNullOrEmpty(cid) && !string.IsNullOrEmpty(gid))
                {

                    identifiers.Add(new GoogleProductIdentifier(cid, gid));
                    Console.WriteLine($"Ekstrahowano CID: {cid}, GID: {gid}");

                }
            }

            return ScraperResult<List<GoogleProductIdentifier>>.Success(identifiers);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd podczas wyszukiwania i ekstrakcji identyfikatorów: {ex.Message}");

            return ScraperResult<List<GoogleProductIdentifier>>.Fail($"Błąd ekstrakcji: {ex.Message}", identifiers);

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