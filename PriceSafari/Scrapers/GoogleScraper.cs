using PuppeteerSharp;

public class GoogleScraper
{
    private IBrowser _browser;
    private IPage _page;

    public async Task InitializeBrowserAsync()
    {
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
                "--disable-infobars",
                "--use-gl=swiftshader",
                "--disable-webgl",
                "--ignore-gpu-blocklist"
            }
        });

        _page = await _browser.NewPageAsync();
        await _page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
        await _page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
        {
            { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" }
        });
    }

    public async Task InitializeAndSearchAsync(string title, string googleMiG)
    {
        if (string.IsNullOrEmpty(googleMiG))
        {
            throw new ArgumentException("GoogleMiG cannot be null or empty.", nameof(googleMiG));
        }

        Console.WriteLine("Navigating to Google Shopping with product title...");

        int retries = 0;
        const int maxRetries = 3;

        while (retries < maxRetries)
        {
            try
            {
                if (_browser == null || _page == null)
                {
                    await InitializeBrowserAsync();
                }

                var encodedTitle = System.Web.HttpUtility.UrlEncode(title);
                var url = $"https://www.google.com/search?gl=pl&tbm=shop&tbs=merchagg:{googleMiG}&q={encodedTitle}";

                await _page.GoToAsync(url, new NavigationOptions
                {
                    Timeout = 60000,
                    WaitUntil = new[] { WaitUntilNavigation.Load }
                });

                var currentUrl = _page.Url;
                if (currentUrl.Contains("/sorry/"))
                {
                    Console.WriteLine("Natrafiono na stronę CAPTCHA. Oczekiwanie 15 sekund na rozwiązanie...");
                    await Task.Delay(15000);  
                    currentUrl = _page.Url;   // ponownie pobierz URL

                    // Jeśli dalej jesteśmy na stronie /sorry/, to CAPTCHA nie została rozwiązana
                    if (currentUrl.Contains("/sorry/"))
                    {
                        Console.WriteLine("CAPTCHA nie została rozwiązana. Restartowanie przeglądarki...");
                        await CloseBrowserAsync();
                        retries++;
                        continue;
                    }
                    else
                    {
                        Console.WriteLine("CAPTCHA została rozwiązana. Kontynuuję...");
                    }
                }

                // Próbujemy kliknąć „Odrzuć wszystko”, jeśli jest dostępny
                var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
                if (rejectButton != null)
                {
                    await rejectButton.ClickAsync();
                }

                Console.WriteLine("Strona załadowana pomyślnie.");
                break;
            }
            catch (TimeoutException ex)
            {
                Console.WriteLine($"Błąd: Przekroczono czas podczas ładowania strony Google Shopping: {ex.Message}");
                await CloseBrowserAsync();
                retries++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd: {ex.Message}");
                await CloseBrowserAsync();
                retries++;
            }
        }

        if (retries == maxRetries)
        {
            throw new Exception("Osiągnięto maksymalną liczbę prób nawigacji do Google Shopping.");
        }
    }

    public async Task<List<(string storeUrl, string googleProductUrl)>> SearchForMatchingProductUrlsAsync(List<string> searchUrls)
    {
        var matchedUrls = new List<(string storeUrl, string googleProductUrl)>();
        Console.WriteLine("Wyszukiwanie pasujących URL produktów na stronie wyników...");

        try
        {
            await _page.WaitForSelectorAsync("div.sh-dgr__grid-result", new WaitForSelectorOptions { Timeout = 500 });
            var productContainers = await _page.QuerySelectorAllAsync("div.sh-dgr__grid-result");
            Console.WriteLine($"Znaleziono {productContainers.Length} boxów produktowych na stronie wyników.");

            foreach (var container in productContainers)
            {
                string googleProductUrl = null;
                string storeUrl = null;

                try
                {
                    // Pobierz i wyczyść URL z Google Shopping
                    var googleProductLinkElement = await container.QuerySelectorAsync("a[href^='/shopping/product/']");
                    if (googleProductLinkElement != null)
                    {
                        googleProductUrl = await googleProductLinkElement.EvaluateFunctionAsync<string>("el => el.href");
                        googleProductUrl = CleanGoogleUrl(googleProductUrl);
                        Console.WriteLine($"Znaleziono URL Google Shopping: {googleProductUrl}");
                    }

                    // Pobierz URL sklepu
                    var storeLinkElement = await container.QuerySelectorAsync("a[href*='/url?url=']");
                    if (storeLinkElement != null)
                    {
                        var googleRedirectUrl = await storeLinkElement.EvaluateFunctionAsync<string>("el => el.href");
                        storeUrl = ExtractStoreUrlFromGoogleRedirect(googleRedirectUrl);
                        Console.WriteLine($"Znaleziono URL sklepu: {storeUrl}");

                        foreach (var searchUrl in searchUrls)
                        {
                            if (!string.IsNullOrEmpty(storeUrl) && !string.IsNullOrEmpty(searchUrl))
                            {
                                // Sprawdź, czy przekazany URL jest zawarty w znalezionym URL sklepu
                                if (storeUrl.Contains(searchUrl, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!string.IsNullOrEmpty(googleProductUrl))
                                    {
                                        matchedUrls.Add((searchUrl, googleProductUrl));
                                        Console.WriteLine($"Dopasowano URL produktu w Google: {googleProductUrl} do URL sklepu: {searchUrl}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Błąd podczas przetwarzania kontenera produktu: {ex.Message}");
                }
            }

            Console.WriteLine($"Łączna liczba dopasowanych URL: {matchedUrls.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd podczas wyszukiwania URL produktów: {ex.Message}");
        }

        return matchedUrls;
    }

    private string ExtractStoreUrlFromGoogleRedirect(string googleRedirectUrl)
    {
        try
        {
            var uri = new Uri(googleRedirectUrl);
            var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var storeUrlEncoded = queryParams["url"];

            if (!string.IsNullOrEmpty(storeUrlEncoded))
            {
                var storeUrl = System.Web.HttpUtility.UrlDecode(storeUrlEncoded);
                return storeUrl; // Zwracamy URL sklepu bez usuwania parametrów
            }
            else
            {
                return googleRedirectUrl; // W razie braku parametru 'url'
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd podczas ekstrakcji URL sklepu: {ex.Message}");
            return googleRedirectUrl;
        }
    }

    private string CleanGoogleUrl(string url)
    {
        int questionMarkIndex = url.IndexOf("?");
        string cleanedUrl = questionMarkIndex > 0 ? url.Substring(0, questionMarkIndex) : url;

        if (!cleanedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            cleanedUrl = "https://www.google.com" + cleanedUrl;
        }

        return cleanedUrl;
    }

    public async Task CloseBrowserAsync()
    {
        if (_page != null && !_page.IsClosed)
        {
            await _page.CloseAsync();
        }
        if (_browser != null)
        {
            await _browser.CloseAsync();
        }
        _browser = null;
        _page = null;
    }
}



//using PuppeteerSharp;


//public class GoogleScraper
//{
//    private IBrowser _browser;
//    private IPage _page;

//    public async Task InitializeBrowserAsync()
//    {
//        var browserFetcher = new BrowserFetcher();
//        await browserFetcher.DownloadAsync();

//        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
//        {
//            Headless = false,
//            Args = new[]
//            {
//                "--no-sandbox",
//                "--disable-setuid-sandbox",
//                "--disable-gpu",
//                "--disable-blink-features=AutomationControlled",
//                "--disable-software-rasterizer",
//                "--disable-extensions",
//                "--disable-dev-shm-usage",
//                "--disable-features=IsolateOrigins,site-per-process",
//                "--disable-infobars",
//                "--use-gl=swiftshader",
//                "--disable-webgl",
//                "--ignore-gpu-blocklist"
//            }
//        });

//        _page = await _browser.NewPageAsync();

//        await _page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
//        await _page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
//        {
//            { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" }
//        });
//    }

//    public async Task InitializeAndSearchAsync(string title, string googleMiG)
//    {
//        if (string.IsNullOrEmpty(googleMiG))
//        {
//            throw new ArgumentException("GoogleMiG cannot be null or empty.", nameof(googleMiG));
//        }

//        Console.WriteLine("Navigating to Google Shopping with product title...");

//        int retries = 0;
//        const int maxRetries = 3;

//        while (retries < maxRetries)
//        {
//            try
//            {
//                if (_browser == null || _page == null)
//                {
//                    await InitializeBrowserAsync();
//                }

//                var encodedTitle = System.Web.HttpUtility.UrlEncode(title);

//                var url = $"https://www.google.com/search?gl=pl&tbm=shop&tbs=merchagg:{googleMiG}&q={encodedTitle}";

//                await _page.GoToAsync(url, new NavigationOptions { Timeout = 60000, WaitUntil = new[] { WaitUntilNavigation.Load } });

//                var currentUrl = _page.Url;
//                if (currentUrl.Contains("/sorry/"))
//                {
//                    Console.WriteLine("Encountered CAPTCHA page. Restarting browser...");
//                    await CloseBrowserAsync();
//                    retries++;
//                    continue;
//                }

//                var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
//                if (rejectButton != null)
//                {
//                    await rejectButton.ClickAsync();
//                }

//                Console.WriteLine("Page loaded.");
//                break;
//            }
//            catch (TimeoutException ex)
//            {
//                Console.WriteLine($"Error: Timeout while navigating to Google Shopping: {ex.Message}");
//                await CloseBrowserAsync();
//                retries++;
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Error: {ex.Message}");
//                await CloseBrowserAsync();
//                retries++;
//            }
//        }

//        if (retries == maxRetries)
//        {
//            throw new Exception("Maximum retries reached while trying to navigate to Google Shopping.");
//        }
//    }

//    public async Task<List<(string storeUrl, string googleProductUrl)>> SearchForMatchingProductUrlsAsync(List<string> searchUrls)
//    {
//        var matchedUrls = new List<(string storeUrl, string googleProductUrl)>();
//        Console.WriteLine("Searching for matching product URLs on the search results page...");

//        try
//        {
//            await _page.WaitForSelectorAsync("div.sh-dgr__grid-result", new WaitForSelectorOptions { Timeout = 500 });

//            var productContainers = await _page.QuerySelectorAllAsync("div.sh-dgr__grid-result");
//            Console.WriteLine($"Found {productContainers.Length} product boxes on the search results page.");

//            foreach (var container in productContainers)
//            {
//                string googleProductUrl = null;
//                string storeUrl = null;

//                try
//                {
//                    // Get and clean the Google Shopping URL
//                    var googleProductLinkElement = await container.QuerySelectorAsync("a[href^='/shopping/product/']");
//                    if (googleProductLinkElement != null)
//                    {
//                        googleProductUrl = await googleProductLinkElement.EvaluateFunctionAsync<string>("el => el.href");
//                        googleProductUrl = CleanGoogleUrl(googleProductUrl);
//                        Console.WriteLine($"Found Google Shopping URL: {googleProductUrl}");
//                    }

//                    // Get the Store URL
//                    var storeLinkElement = await container.QuerySelectorAsync("a[href*='/url?url=']");
//                    if (storeLinkElement != null)
//                    {
//                        var googleRedirectUrl = await storeLinkElement.EvaluateFunctionAsync<string>("el => el.href");
//                        storeUrl = ExtractStoreUrlFromGoogleRedirect(googleRedirectUrl);
//                        Console.WriteLine($"Found Store URL: {storeUrl}");

//                        foreach (var searchUrl in searchUrls)
//                        {
//                            if (!string.IsNullOrEmpty(storeUrl) && !string.IsNullOrEmpty(searchUrl))
//                            {
//                                // Check if the provided URL is entirely contained within the found Store URL
//                                if (storeUrl.Contains(searchUrl, StringComparison.OrdinalIgnoreCase))
//                                {
//                                    if (!string.IsNullOrEmpty(googleProductUrl))
//                                    {
//                                        matchedUrls.Add((searchUrl, googleProductUrl));
//                                        Console.WriteLine($"Matched Google Product URL: {googleProductUrl} with store URL: {searchUrl}");
//                                    }
//                                }
//                            }
//                        }
//                    }
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"Error processing product container: {ex.Message}");
//                }
//            }

//            Console.WriteLine($"Total matched URLs: {matchedUrls.Count}");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error searching for product URLs: {ex.Message}");
//        }

//        return matchedUrls;
//    }

//    private string ExtractStoreUrlFromGoogleRedirect(string googleRedirectUrl)
//    {
//        try
//        {
//            var uri = new Uri(googleRedirectUrl);
//            var queryParams = System.Web.HttpUtility.ParseQueryString(uri.Query);
//            var storeUrlEncoded = queryParams["url"];

//            if (!string.IsNullOrEmpty(storeUrlEncoded))
//            {
//                var storeUrl = System.Web.HttpUtility.UrlDecode(storeUrlEncoded);
//                return storeUrl; // Return the store URL without cleaning or removing query parameters
//            }
//            else
//            {
//                return googleRedirectUrl; // In case 'url' parameter is missing
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error extracting store URL: {ex.Message}");
//            return googleRedirectUrl;
//        }
//    }

//    private string CleanGoogleUrl(string url)
//    {
//        int questionMarkIndex = url.IndexOf("?");
//        string cleanedUrl = questionMarkIndex > 0 ? url.Substring(0, questionMarkIndex) : url;

//        if (!cleanedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
//        {
//            cleanedUrl = "https://www.google.com" + cleanedUrl;
//        }

//        return cleanedUrl;
//    }

//    public async Task CloseBrowserAsync()
//    {
//        if (_page != null && !_page.IsClosed)
//        {
//            await _page.CloseAsync();
//        }
//        if (_browser != null)
//        {
//            await _browser.CloseAsync();
//        }
//        _browser = null;
//        _page = null;
//    }
//}




