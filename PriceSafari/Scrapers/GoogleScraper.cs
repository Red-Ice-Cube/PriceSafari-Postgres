using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
// ZMIANA 1: Usunięto błędną dyrektywę 'using static ScraperResult<T>;'

public class ScraperResult<T>
{
    public T Data { get; set; }
    public bool IsSuccess { get; set; }
    public bool CaptchaEncountered { get; set; }
    public string ErrorMessage { get; set; }

    public ScraperResult(T data)
    {
        Data = data;
        IsSuccess = true;
        CaptchaEncountered = false;
        ErrorMessage = null;
    }

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

// ZMIANA 2: Klasa ProductSearchResult została przeniesiona na zewnątrz ScraperResult<T>
public class ProductSearchResult
{
    public string Cid { get; set; }
    public string Title { get; set; }
}

public class GoogleScraper
{
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

    public async Task<ScraperResult<List<ProductSearchResult>>> SearchInitialProductCIDsAsync(string title, int maxCIDsToExtract = 10)
    {
        var searchResults = new List<ProductSearchResult>();
        Console.WriteLine($"Navigating to Google Shopping with product title: {title} to extract initial CIDs and Titles.");
        IsCaptchaEncountered = false;

        try
        {
            if (_browser == null || _page == null || _page.IsClosed)
            {
                await InitializeBrowserAsync();
            }

            var encodedTitle = HttpUtility.UrlEncode(title);
            var url = $"https://www.google.com/search?gl=pl&tbm=shop&q={encodedTitle}";

            await _page.GoToAsync(url, new NavigationOptions
            {
                Timeout = 60000,
                WaitUntil = new[] { WaitUntilNavigation.Load }
            });

            var currentUrl = _page.Url;
            if (currentUrl.Contains("/sorry/") || currentUrl.Contains("/captcha"))
            {
                Console.WriteLine("Natrafiono na stronę CAPTCHA podczas wyszukiwania.");
                IsCaptchaEncountered = true;
                return ScraperResult<List<ProductSearchResult>>.Captcha(searchResults);
            }

            var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
            if (rejectButton != null)
            {
                Console.WriteLine("Znaleziono przycisk 'Odrzuć wszystko'. Klikam...");
                await rejectButton.ClickAsync();
                await Task.Delay(1000);
            }

            Console.WriteLine("Strona wyników Google Shopping załadowana pomyślnie.");

            try
            {
                await _page.WaitForSelectorAsync("div.sh-dgr__content", new WaitForSelectorOptions { Timeout = 100 });
            }
            catch (WaitTaskTimeoutException)
            {
                try
                {
                    await _page.WaitForSelectorAsync("div.MtXiu", new WaitForSelectorOptions { Timeout = 500 });
                }
                catch (WaitTaskTimeoutException ex)
                {
                    Console.WriteLine($"Nie znaleziono boksów produktów (ani .sh-dgr__content ani .MtXiu) w określonym czasie: {ex.Message}. Strona mogła się zmienić lub brak wyników.");
                    // ZMIANA 3: Poprawiono typ i nazwę zmiennej w tej linii
                    return ScraperResult<List<ProductSearchResult>>.Fail("Nie znaleziono boksów produktów.", searchResults);
                }
            }

            var productBoxes = await _page.QuerySelectorAllAsync("div.sh-dgr__content");
            if (productBoxes.Length == 0)
            {
                productBoxes = await _page.QuerySelectorAllAsync("div.MtXiu");
            }

            Console.WriteLine($"Znaleziono {productBoxes.Length} boksów produktów na stronie wyników.");

            if (productBoxes.Length == 0)
            {
                return ScraperResult<List<ProductSearchResult>>.Success(searchResults);
            }

            int count = 0;
            foreach (var box in productBoxes)
            {
                if (count >= maxCIDsToExtract) break;

                string cid = null;
                var linkElement = await box.QuerySelectorAsync("a[data-cid]");
                if (linkElement != null)
                {
                    cid = await linkElement.EvaluateFunctionAsync<string>("element => element.getAttribute('data-cid')");
                }
                else
                {
                    cid = await box.EvaluateFunctionAsync<string>("element => element.getAttribute('data-cid')");
                }

                if (string.IsNullOrEmpty(cid))
                {
                    var productLink = await box.QuerySelectorAsync("a[data-docid]");
                    if (productLink != null)
                    {
                        cid = await productLink.EvaluateFunctionAsync<string>("element => element.getAttribute('data-docid')");
                    }
                }

                string productTitle = string.Empty;
                var titleElement = await box.QuerySelectorAsync("div.gkQHve");
                if (titleElement != null)
                {
                    productTitle = await titleElement.EvaluateFunctionAsync<string>("el => el.textContent.trim()");
                }

                if (!string.IsNullOrEmpty(cid) && !string.IsNullOrEmpty(productTitle))
                {
                    searchResults.Add(new ProductSearchResult { Cid = cid, Title = productTitle });
                    Console.WriteLine($"Ekstrahowano: CID={cid}, Tytuł='{productTitle}'");
                    count++;
                }
                else
                {
                    Console.WriteLine("Nie udało się wyekstrahować CID lub Tytułu z boksu produktu.");
                }
            }
            return ScraperResult<List<ProductSearchResult>>.Success(searchResults);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd podczas wyszukiwania i ekstrakcji CID-ów/Tytułów: {ex.Message}");
            return ScraperResult<List<ProductSearchResult>>.Fail($"Błąd ekstrakcji: {ex.Message}", searchResults);
        }
    }


    public async Task<ScraperResult<List<string>>> NavigateToProductPageAndExpandOffersAsync(string cid)
    {
        Console.WriteLine($"Rozpoczynam zbieranie ofert produktu Google Shopping (CID: {cid})...");
        var allStoreUrls = new List<string>();
        IsCaptchaEncountered = false;

        int start = 0;
        const int pageSize = 20;

        try
        {
            if (_browser == null || _page == null || _page.IsClosed)
                await InitializeBrowserAsync();

            while (true)
            {
                var url = $"https://www.google.com/shopping/product/{cid}/offers?prds=cid:{cid},cond:1,cs:1,start:{start}&gl=pl&hl=pl";
                Console.WriteLine($"– Nawigacja do: {url}");
                await _page.GoToAsync(url, new NavigationOptions
                {
                    Timeout = 60000,
                    WaitUntil = new[] { WaitUntilNavigation.Load }
                });

                var currentUrl = _page.Url;
                if (currentUrl.Contains("/sorry/") || currentUrl.Contains("/captcha"))
                {
                    Console.WriteLine("❌ CAPTCHA napotkana podczas nawigacji.");
                    IsCaptchaEncountered = true;
                    return ScraperResult<List<string>>.Captcha(allStoreUrls);
                }

                var extractResult = await ExtractStoreOffersAsync(_page);

                if (extractResult.CaptchaEncountered)
                    return ScraperResult<List<string>>.Captcha(allStoreUrls);

                if (!extractResult.IsSuccess)
                    return ScraperResult<List<string>>.Fail(extractResult.ErrorMessage, allStoreUrls);

                var urls = extractResult.Data;
                Console.WriteLine($"– Zebrano {urls.Count} linków na stronie start={start}.");
                allStoreUrls.AddRange(urls);

                if (urls.Count < pageSize)
                    break;

                start += pageSize;
            }

            return ScraperResult<List<string>>.Success(allStoreUrls);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd podczas paginacji ofert (CID: {cid}): {ex.Message}");
            return ScraperResult<List<string>>.Fail($"Błąd paginacji ofert: {ex.Message}", allStoreUrls);
        }
    }

    public async Task<ScraperResult<List<string>>> ExtractStoreOffersAsync(IPage page)
    {
        var storeUrls = new List<string>();
        IsCaptchaEncountered = false;

        try
        {
            if (page.Url.Contains("/sorry/") || page.Url.Contains("/captcha"))
            {
                Console.WriteLine("Strona ofert to CAPTCHA. Przerywam ekstrakcję.");
                IsCaptchaEncountered = true;
                return ScraperResult<List<string>>.Captcha(storeUrls);
            }

            try
            {
                await page.WaitForSelectorAsync("a.sh-osd__seller-link", new WaitForSelectorOptions { Timeout = 500 });
            }
            catch (WaitTaskTimeoutException)
            {
                try
                {
                    await page.WaitForSelectorAsync("div.UAVKwf > a.UxuaJe.shntl.FkMp", new WaitForSelectorOptions { Timeout = 500 });
                }
                catch (WaitTaskTimeoutException ex)
                {
                    Console.WriteLine($"Nie znaleziono linków ofert: {ex.Message}. Zwracam pustą listę.");
                    return ScraperResult<List<string>>.Success(storeUrls);
                }
            }

            var offerLinkElements = await page.QuerySelectorAllAsync("a.sh-osd__seller-link");
            if (offerLinkElements.Length == 0)
                offerLinkElements = await page.QuerySelectorAllAsync("div.UAVKwf > a.UxuaJe.shntl.FkMp");

            Console.WriteLine($"Znaleziono {offerLinkElements.Length} linków ofert.");

            foreach (var linkElement in offerLinkElements)
            {
                var rawStoreUrl = await linkElement.EvaluateFunctionAsync<string>("el => el.href");
                if (string.IsNullOrEmpty(rawStoreUrl))
                {
                    Console.WriteLine("Pusty href, pomijam...");
                    continue;
                }

                string extractedStoreUrl = rawStoreUrl.Contains("google.com/url")
                    ? ExtractStoreUrlFromGoogleRedirect(rawStoreUrl)
                    : rawStoreUrl;

                var cleanedStoreUrl = CleanUrlParameters(extractedStoreUrl);
                storeUrls.Add(cleanedStoreUrl);
                Console.WriteLine($"Ekstrahowano i oczyszczono URL: {cleanedStoreUrl}");
            }

            return ScraperResult<List<string>>.Success(storeUrls);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd przy ekstrakcji ofert sklepów: {ex.Message}");
            return ScraperResult<List<string>>.Fail($"Błąd ekstrakcji ofert: {ex.Message}", storeUrls);
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

            Console.WriteLine($"DEBUG: W ExtractStoreUrlFromGoogleRedirect: Parametr 'q' lub 'url' (zakodowany) = {storeUrlEncoded}");

            if (!string.IsNullOrEmpty(storeUrlEncoded))
            {
                var storeUrl = System.Web.HttpUtility.UrlDecode(storeUrlEncoded);
                Console.WriteLine($"DEBUG: W ExtractStoreUrlFromGoogleRedirect: Odkodowany URL sklepu = {storeUrl}");
                return storeUrl;
            }
            else
            {
                Console.WriteLine($"DEBUG: Parametr 'q' ani 'url' nie znaleziony w query string. Zwracam cały URL jako fallback: {googleRedirectUrl}");
                return googleRedirectUrl;
            }
        }
        catch (UriFormatException ex)
        {
            Console.WriteLine($"BŁĄD: Nieprawidłowy format URL przekierowania w ExtractStoreUrlFromGoogleRedirect: '{googleRedirectUrl}'. Błąd: {ex.Message}. Zwracam go bez zmian.");
            return googleRedirectUrl;
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
            try
            {
                await _page.CloseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas zamykania strony: {ex.Message}");
            }
        }
        if (_browser != null)
        {
            try
            {
                await _browser.CloseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas zamykania przeglądarki: {ex.Message}");
            }
        }
        _browser = null;
        _page = null;
        IsCaptchaEncountered = false;
    }
}







// AKCJA SCRAPERA BEZ MODYFIKACJI DO TRYBU TRAFINIA POSREDNIEGO, TZN DOPASOWANIE DO NAZWY BOKSU KODU PRODUKTU

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

//    public ScraperResult(T data)
//    {
//        Data = data;
//        IsSuccess = true;
//        CaptchaEncountered = false;
//        ErrorMessage = null;
//    }

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
//              "--no-sandbox",
//                    "--disable-setuid-sandbox",
//                    "--disable-gpu",
//                    "--disable-blink-features=AutomationControlled",
//                    "--disable-software-rasterizer",
//                    "--disable-extensions",
//                    "--disable-dev-shm-usage",
//                    "--disable-features=IsolateOrigins,site-per-process",
//                    "--disable-infobars"

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

//            await _page.GoToAsync(url, new NavigationOptions
//            {
//                Timeout = 60000,
//                WaitUntil = new[] { WaitUntilNavigation.Load }
//            });

//            var currentUrl = _page.Url;
//            if (currentUrl.Contains("/sorry/") || currentUrl.Contains("/captcha"))
//            {
//                Console.WriteLine("Natrafiono na stronę CAPTCHA podczas wyszukiwania.");
//                IsCaptchaEncountered = true;

//                return ScraperResult<List<string>>.Captcha(cids);
//            }

//            var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
//            if (rejectButton != null)
//            {
//                Console.WriteLine("Znaleziono przycisk 'Odrzuć wszystko'. Klikam...");
//                await rejectButton.ClickAsync();
//                await Task.Delay(1000);
//            }

//            Console.WriteLine("Strona wyników Google Shopping załadowana pomyślnie.");

//            try
//            {
//                await _page.WaitForSelectorAsync("div.sh-dgr__content", new WaitForSelectorOptions { Timeout = 100 });
//            }
//            catch (WaitTaskTimeoutException)
//            {

//                try
//                {
//                    await _page.WaitForSelectorAsync("div.MtXiu", new WaitForSelectorOptions { Timeout = 500 });
//                }
//                catch (WaitTaskTimeoutException ex)
//                {
//                    Console.WriteLine($"Nie znaleziono boksów produktów (ani .sh-dgr__content ani .MtXiu) w określonym czasie: {ex.Message}. Strona mogła się zmienić lub brak wyników.");
//                    return ScraperResult<List<string>>.Fail("Nie znaleziono boksów produktów.", cids);
//                }
//            }

//            var productBoxes = await _page.QuerySelectorAllAsync("div.sh-dgr__content");
//            if (productBoxes.Length == 0)
//            {

//                productBoxes = await _page.QuerySelectorAllAsync("div.MtXiu.mZ9c3d.wYFOId.M919M.W5CKGc.wTrwWd");
//            }

//            Console.WriteLine($"Znaleziono {productBoxes.Length} boksów produktów na stronie wyników.");

//            if (productBoxes.Length == 0)
//            {
//                return ScraperResult<List<string>>.Success(cids);
//            }

//            int count = 0;
//            foreach (var box in productBoxes)
//            {
//                if (count >= maxCIDsToExtract) break;

//                string cid = null;
//                var linkElement = await box.QuerySelectorAsync("a[data-cid]");
//                if (linkElement != null)
//                {
//                    cid = await linkElement.EvaluateFunctionAsync<string>("element => element.getAttribute('data-cid')");
//                }
//                else
//                {

//                    cid = await box.EvaluateFunctionAsync<string>("element => element.getAttribute('data-cid')");
//                }

//                if (!string.IsNullOrEmpty(cid))
//                {
//                    cids.Add(cid);
//                    Console.WriteLine($"Ekstrahowano CID: {cid}");
//                    count++;
//                }
//                else
//                {

//                    var productLink = await box.QuerySelectorAsync("a[data-docid]");
//                    if (productLink != null)
//                    {
//                        cid = await productLink.EvaluateFunctionAsync<string>("element => element.getAttribute('data-docid')");
//                        if (!string.IsNullOrEmpty(cid))
//                        {
//                            cids.Add(cid);
//                            Console.WriteLine($"Ekstrahowano CID (z data-docid): {cid}");
//                            count++;
//                        }
//                    }
//                    else
//                    {
//                        Console.WriteLine("Nie udało się wyekstrahować CID z boksu produktu (ani data-cid, ani data-docid w linku).");
//                    }
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

//    public async Task<ScraperResult<List<string>>> NavigateToProductPageAndExpandOffersAsync(string cid)
//    {
//        Console.WriteLine($"Rozpoczynam zbieranie ofert produktu Google Shopping (CID: {cid})...");
//        var allStoreUrls = new List<string>();
//        IsCaptchaEncountered = false;

//        int start = 0;
//        const int pageSize = 20;

//        try
//        {
//            if (_browser == null || _page == null || _page.IsClosed)
//                await InitializeBrowserAsync();

//            while (true)
//            {
//                var url = $"https://www.google.com/shopping/product/{cid}/offers?prds=cid:{cid},cond:1,cs:1,start:{start}&gl=pl&hl=pl";
//                Console.WriteLine($"– Nawigacja do: {url}");
//                await _page.GoToAsync(url, new NavigationOptions
//                {
//                    Timeout = 60000,
//                    WaitUntil = new[] { WaitUntilNavigation.Load }
//                });

//                var currentUrl = _page.Url;
//                if (currentUrl.Contains("/sorry/") || currentUrl.Contains("/captcha"))
//                {
//                    Console.WriteLine("❌ CAPTCHA napotkana podczas nawigacji.");
//                    IsCaptchaEncountered = true;
//                    return ScraperResult<List<string>>.Captcha(allStoreUrls);
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
//                await page.WaitForSelectorAsync("a.sh-osd__seller-link", new WaitForSelectorOptions { Timeout = 500 });
//            }
//            catch (WaitTaskTimeoutException)
//            {
//                try
//                {
//                    await page.WaitForSelectorAsync("div.UAVKwf > a.UxuaJe.shntl.FkMp", new WaitForSelectorOptions { Timeout = 500 });
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

//            Console.WriteLine($"DEBUG: W ExtractStoreUrlFromGoogleRedirect: Parametr 'q' lub 'url' (zakodowany) = {storeUrlEncoded}");

//            if (!string.IsNullOrEmpty(storeUrlEncoded))
//            {
//                var storeUrl = System.Web.HttpUtility.UrlDecode(storeUrlEncoded);
//                Console.WriteLine($"DEBUG: W ExtractStoreUrlFromGoogleRedirect: Odkodowany URL sklepu = {storeUrl}");
//                return storeUrl;
//            }
//            else
//            {
//                Console.WriteLine($"DEBUG: Parametr 'q' ani 'url' nie znaleziony w query string. Zwracam cały URL jako fallback: {googleRedirectUrl}");
//                return googleRedirectUrl;
//            }
//        }
//        catch (UriFormatException ex)
//        {
//            Console.WriteLine($"BŁĄD: Nieprawidłowy format URL przekierowania w ExtractStoreUrlFromGoogleRedirect: '{googleRedirectUrl}'. Błąd: {ex.Message}. Zwracam go bez zmian.");
//            return googleRedirectUrl;
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
//            try
//            {
//                await _page.CloseAsync();
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Błąd podczas zamykania strony: {ex.Message}");
//            }
//        }
//        if (_browser != null)
//        {
//            try
//            {
//                await _browser.CloseAsync();
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Błąd podczas zamykania przeglądarki: {ex.Message}");
//            }
//        }
//        _browser = null;
//        _page = null;
//        IsCaptchaEncountered = false;
//    }
//}

