using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

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

    public async Task<ScraperResult<List<string>>> SearchInitialProductCIDsAsync(string title, int maxCIDsToExtract = 10)
    {
        var cids = new List<string>();
        Console.WriteLine($"Navigating to Google Shopping with product title: {title} to extract initial CIDs.");
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
                return ScraperResult<List<string>>.Captcha(cids);
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
                return ScraperResult<List<string>>.Fail("Nie znaleziono boksów produktów.", cids);
            }

            var productBoxes = await _page.QuerySelectorAllAsync("div.sh-dgr__content, div.MtXiu, div.LrTUQ");

            Console.WriteLine($"Znaleziono {productBoxes.Length} boksów produktów na stronie wyników.");

            if (productBoxes.Length == 0)
            {
                return ScraperResult<List<string>>.Success(cids);
            }

            foreach (var box in productBoxes)
            {
                if (cids.Count >= maxCIDsToExtract) break;

                string cid = await box.EvaluateFunctionAsync<string>(@"
                 element => {

                     if (element.dataset.cid) return element.dataset.cid;

                     const linkWithCid = element.querySelector('a[data-cid]');
                     if (linkWithCid) return linkWithCid.dataset.cid;

                     const linkWithDocid = element.querySelector('a[data-docid]');
                     if (linkWithDocid) return linkWithDocid.dataset.docid;

                     return null;
                 }
                ");

                if (!string.IsNullOrEmpty(cid))
                {
                    cids.Add(cid);
                    Console.WriteLine($"Ekstrahowano CID: {cid}");
                }
            }
            return ScraperResult<List<string>>.Success(cids);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd podczas wyszukiwania i ekstrakcji CID-ów: {ex.Message}");
            return ScraperResult<List<string>>.Fail($"Błąd ekstrakcji CID: {ex.Message}", cids);
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

    public async Task<ScraperResult<List<string>>> NavigateToProductPageAndExpandOffersAsync(string cid)
    {
        Console.WriteLine($"Rozpoczynam zbieranie ofert produktu Google Shopping (CID: {cid})...");
        var allStoreUrls = new List<string>();
        IsCaptchaEncountered = false;

        int start = 0;
        const int pageSize = 20;
        const int maxRetries = 2; // NOWE: Maksymalna liczba ponowień (1 próba + 2 ponowienia)

        try
        {
            if (_browser == null || _page == null || _page.IsClosed)
                await InitializeBrowserAsync();

            while (true)
            {
                var url = $"https://www.google.com/shopping/product/{cid}/offers?prds=cid:{cid},cond:1,cs:1,start:{start}&gl=pl&hl=pl";

                // =============================================================
                // NOWE: Pętla ponowień z logiką resetu przeglądarki
                // =============================================================
                bool navigationSuccess = false;
                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        if (attempt > 0)
                        {
                            Console.WriteLine($"Próba {attempt + 1}/{maxRetries + 1}: Nawigacja nie powiodła się. Resetuję przeglądarkę.");
                            await ResetBrowserAsync();
                        }

                        Console.WriteLine($"– Nawigacja do: {url} (Próba {attempt + 1})");
                        await _page.GoToAsync(url, new NavigationOptions
                        {
                            Timeout = 60000,
                            WaitUntil = new[] { WaitUntilNavigation.Load }
                        });

                        // ZMODYFIKOWANE: Weryfikacja URL po nawigacji
                        var currentUrl = _page.Url;
                        if (currentUrl.Contains("/sorry/") || currentUrl.Contains("/captcha"))
                        {
                            Console.WriteLine($"❌ CAPTCHA lub strona błędu wykryta na próbie {attempt + 1}.");
                            IsCaptchaEncountered = true;
                            // Kontynuuj pętlę, aby zresetować i spróbować ponownie
                            continue;
                        }

                        // Sprawdzamy, czy nie zostaliśmy przekierowani na inną stronę (np. główną stronę produktu)
                        if (!currentUrl.Contains(cid) || !currentUrl.Contains("/offers"))
                        {
                            Console.WriteLine($"❌ Wykryto przekierowanie na niespodziewany URL: {currentUrl}. Próbuję ponownie.");
                            // Kontynuuj pętlę, aby zresetować i spróbować ponownie
                            continue;
                        }

                        // Jeśli dotarliśmy tutaj, nawigacja się powiodła
                        navigationSuccess = true;
                        break; // Wyjdź z pętli ponowień
                    }
                    catch (Exception navEx)
                    {
                        Console.WriteLine($"❌ Błąd krytyczny podczas nawigacji (próba {attempt + 1}): {navEx.Message}");
                        // Pętla będzie kontynuowana, co spowoduje reset w następnej iteracji
                    }
                }

                // Jeśli po wszystkich próbach nawigacja się nie powiodła, przerwij scrapowanie tego produktu
                if (!navigationSuccess)
                {
                    Console.WriteLine($"BŁĄD KRYTYCZNY: Nie udało się załadować strony ofert dla CID: {cid} po {maxRetries + 1} próbach.");
                    return ScraperResult<List<string>>.Fail("Nie udało się załadować strony ofert po wielokrotnych próbach.", allStoreUrls);
                }
                // =============================================================
                // Koniec nowej logiki
                // =============================================================

                // Istniejąca logika ekstrakcji danych (teraz wykonuje się tylko po udanej nawigacji)
                var extractResult = await ExtractStoreOffersAsync(_page);

                if (extractResult.CaptchaEncountered)
                    return ScraperResult<List<string>>.Captcha(allStoreUrls);

                if (!extractResult.IsSuccess)
                    return ScraperResult<List<string>>.Fail(extractResult.ErrorMessage, allStoreUrls);

                var urls = extractResult.Data;
                Console.WriteLine($"– Zebrano {urls.Count} linków na stronie start={start}.");
                allStoreUrls.AddRange(urls);

                if (urls.Count < pageSize)
                    break; // To była ostatnia strona

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
                await page.WaitForSelectorAsync("a.sh-osd__seller-link", new WaitForSelectorOptions { Timeout = 1000 });
            }
            catch (WaitTaskTimeoutException)
            {
                try
                {
                    await page.WaitForSelectorAsync("div.UAVKwf > a.UxuaJe.shntl.FkMp", new WaitForSelectorOptions { Timeout = 1000 });
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
}



// Kod bez restartu przegladarki gdy wykryje przekierowanie na Google Shopping

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
//                element => {

//                    if (element.dataset.cid) return element.dataset.cid;

//                    const linkWithCid = element.querySelector('a[data-cid]');
//                    if (linkWithCid) return linkWithCid.dataset.cid;

//                    const linkWithDocid = element.querySelector('a[data-docid]');
//                    if (linkWithDocid) return linkWithDocid.dataset.docid;

//                    return null;
//                }
//            ");

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
//}

