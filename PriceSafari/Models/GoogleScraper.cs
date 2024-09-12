using PuppeteerSharp;

public class GoogleScraper
{
    private IBrowser _browser;
    private IPage _page;

    public async Task InitializeBrowserAsync()
    {
        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();

        _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
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
                "--enable-webgl",
                "--ignore-gpu-blocklist"
            }
        });

        _page = (Page)await _browser.NewPageAsync();

        await _page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 720 });
        await _page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
        {
            { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" }
        });
    }

    public async Task InitializeAndSearchAsync(string title)
    {
        Console.WriteLine("Navigating to Google Shopping...");
        try
        {
            await _page.GoToAsync("https://shopping.google.com/", new NavigationOptions { Timeout = 20000, WaitUntil = new[] { WaitUntilNavigation.Load } }); // Zwiększono Timeout do 60 sekund

            var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
            if (rejectButton != null)
            {
                await rejectButton.ClickAsync();
            }

            var searchInputSelector = "input[name='q']";
            await _page.WaitForSelectorAsync(searchInputSelector);

            // Logowanie tytułu, który będzie wpisywany w wyszukiwarkę
            Console.WriteLine($"Entering product title in search bar: {title}");

            foreach (char c in title)
            {
                await _page.TypeAsync(searchInputSelector, c.ToString());
            }

            await _page.Keyboard.PressAsync("Enter");
            await _page.WaitForNavigationAsync(new NavigationOptions { Timeout = 60000, WaitUntil = new[] { WaitUntilNavigation.Load } }); // Zwiększono Timeout
            Console.WriteLine("Page loaded.");
        }
        catch (TimeoutException ex)
        {
            Console.WriteLine($"Error: Timeout while navigating to Google Shopping: {ex.Message}");
        }
    }

    public async Task SearchAndNavigateToStoreAsync(string storeName, List<string> searchUrls)
    {
        Console.WriteLine($"Searching for store: {storeName}");
        await Task.Delay(5000);

        try
        {
            // Oczekiwanie na załadowanie elementów sklepu
            await _page.WaitForSelectorAsync("span.DON5yf", new WaitForSelectorOptions { Timeout = 15000 });
            var sellerContainers = await _page.QuerySelectorAllAsync("span.DON5yf");
            Console.WriteLine($"Found {sellerContainers.Length} seller elements.");

            // Szukanie sklepu w liście elementów
            foreach (var container in sellerContainers)
            {
                var sellerElement = await container.QuerySelectorAsync("span.lg3aE");
                if (sellerElement != null)
                {
                    var sellerText = await sellerElement.EvaluateFunctionAsync<string>("el => el.innerText");

                    if (sellerText.Contains(storeName, StringComparison.OrdinalIgnoreCase))
                    {
                        var linkElement = await container.QuerySelectorAsync("a.vjtvke");
                        if (linkElement != null)
                        {
                            var href = await linkElement.EvaluateFunctionAsync<string>("el => el.href");
                            string fullUrl = href.StartsWith("http") ? href : $"https://www.google.com{href}";

                            // Nawigacja do sklepu
                            await _page.GoToAsync(fullUrl, new NavigationOptions { Timeout = 30000, WaitUntil = new[] { WaitUntilNavigation.Load } });
                            Console.WriteLine("Navigated to the store. Waiting for 3 seconds...");
                            await Task.Delay(3000);

                            // Po nawigacji kończymy, a kontroler może sam wywołać SearchForMatchingProductUrlsAsync
                            return;
                        }
                    }
                }
            }
        }
        catch (MessageException ex)
        {
            Console.WriteLine($"Error during store search: {ex.Message}");
        }
    }

    public async Task<List<(string storeUrl, string googleProductUrl)>> SearchForMatchingProductUrlsAsync(List<string> searchUrls)
    {
        var matchedUrls = new List<(string storeUrl, string googleProductUrl)>();
        Console.WriteLine("Searching for matching product URLs on the store page...");

        try
        {
            // Pobierz wszystkie kontenery produktów na stronie sklepu
            var productContainers = await _page.QuerySelectorAllAsync("div.sh-dgr__gr-auto.sh-dgr__grid-result");
            Console.WriteLine($"Found {productContainers.Length} product boxes on the store page.");

            int matchCount = 0;  // Licznik dopasowanych URL

            foreach (var container in productContainers)
            {
                string? googleProductUrl = null;
                string? storeUrl = null;

                try
                {
                    // Pobierz Google Product URL
                    var googleProductLinkElement = await container.QuerySelectorAsync("a.xCpuod");
                    if (googleProductLinkElement != null)
                    {
                        googleProductUrl = await googleProductLinkElement.EvaluateFunctionAsync<string>("el => el.href");
                        googleProductUrl = CleanGoogleUrl(googleProductUrl);
                        Console.WriteLine($"Found Google Shopping URL: {googleProductUrl}");
                    }

                    // Pobierz Store URL
                    var storeLinkElement = await container.QuerySelectorAsync("a[href*='/url?url=']");
                    if (storeLinkElement != null)
                    {
                        storeUrl = await storeLinkElement.EvaluateFunctionAsync<string>("el => el.href");
                        storeUrl = CleanUrl(storeUrl);
                        Console.WriteLine($"Found Store URL: {storeUrl}");

                        // Sprawdź, czy storeUrl pasuje do któregoś z podanych searchUrls
                        foreach (var searchUrl in searchUrls)
                        {
                            if (storeUrl.Contains(searchUrl) && !string.IsNullOrEmpty(googleProductUrl))
                            {
                                matchedUrls.Add((storeUrl, googleProductUrl));
                                matchCount++;  // Zwiększ licznik, gdy znajdziemy dopasowanie
                                Console.WriteLine($"Matched Google Product URL: {googleProductUrl} with store URL: {storeUrl}");
                            }
                        }
                    }
                }
                catch (MessageException ex)
                {
                    Console.WriteLine($"Error processing product container: {ex.Message}");
                }
            }

            // Podsumowanie dopasowań
            Console.WriteLine($"Total matched URLs: {matchCount}");
        }
        catch (MessageException ex)
        {
            Console.WriteLine($"Error searching for product URLs: {ex.Message}");
        }

        return matchedUrls;
    }


    private string CleanGoogleUrl(string url)
    {
        int questionMarkIndex = url.IndexOf("?");
        return questionMarkIndex > 0 ? url.Substring(0, questionMarkIndex) : url;
    }

    private string CleanUrl(string url)
    {
        string decodedUrl = System.Web.HttpUtility.UrlDecode(url);
        string cleanedUrl = decodedUrl.Replace("/url?url=", "");

        if (cleanedUrl.StartsWith("https://www.google.com"))
        {
            cleanedUrl = cleanedUrl.Replace("https://www.google.com", "");
        }

        int questionMarkIndex = cleanedUrl.IndexOf("?");
        return questionMarkIndex > 0 ? cleanedUrl.Substring(0, questionMarkIndex) : cleanedUrl;
    }
    public async Task CloseBrowserAsync()
    {
        await _page.CloseAsync();
        await _browser.CloseAsync();
    }
}

