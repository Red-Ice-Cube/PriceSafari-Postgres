using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

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
                "--enable-webgl",
                "--ignore-gpu-blocklist"
            }
        });

        _page = await _browser.NewPageAsync();

        await _page.SetViewportAsync(new ViewPortOptions { Width = 1440, Height = 900 });
        await _page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
        {
            { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" }
        });
    }

    public async Task InitializeAndSearchAsync(string title)
    {
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
                var url = $"https://www.google.com/search?tbm=shop&tbs=merchagg:g5341299802&q={encodedTitle}";

                await _page.GoToAsync(url, new NavigationOptions { Timeout = 60000, WaitUntil = new[] { WaitUntilNavigation.Load } });

                var currentUrl = _page.Url;
                if (currentUrl.Contains("/sorry/"))
                {
                    Console.WriteLine("Encountered CAPTCHA page. Restarting browser...");
                    await CloseBrowserAsync();
                    retries++;
                    continue;
                }

                var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
                if (rejectButton != null)
                {
                    await rejectButton.ClickAsync();
                }

                Console.WriteLine("Page loaded.");
                break;
            }
            catch (TimeoutException ex)
            {
                Console.WriteLine($"Error: Timeout while navigating to Google Shopping: {ex.Message}");
                await CloseBrowserAsync();
                retries++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                await CloseBrowserAsync();
                retries++;
            }
        }

        if (retries == maxRetries)
        {
            throw new Exception("Maximum retries reached while trying to navigate to Google Shopping.");
        }
    }

    public async Task<List<(string storeUrl, string googleProductUrl)>> SearchForMatchingProductUrlsAsync(List<string> searchUrls)
    {
        var matchedUrls = new List<(string storeUrl, string googleProductUrl)>();
        Console.WriteLine("Searching for matching product URLs on the search results page...");

        try
        {
            await _page.WaitForSelectorAsync("div.sh-dgr__grid-result", new WaitForSelectorOptions { Timeout = 500 });

            var productContainers = await _page.QuerySelectorAllAsync("div.sh-dgr__grid-result");
            Console.WriteLine($"Found {productContainers.Length} product boxes on the search results page.");

            foreach (var container in productContainers)
            {
                string googleProductUrl = null;
                string storeUrl = null;

                try
                {
                    // Get and clean the Google Shopping URL
                    var googleProductLinkElement = await container.QuerySelectorAsync("a[href^='/shopping/product/']");
                    if (googleProductLinkElement != null)
                    {
                        googleProductUrl = await googleProductLinkElement.EvaluateFunctionAsync<string>("el => el.href");
                        googleProductUrl = CleanGoogleUrl(googleProductUrl);
                        Console.WriteLine($"Found Google Shopping URL: {googleProductUrl}");
                    }

                    // Get the Store URL
                    var storeLinkElement = await container.QuerySelectorAsync("a[href*='/url?url=']");
                    if (storeLinkElement != null)
                    {
                        var googleRedirectUrl = await storeLinkElement.EvaluateFunctionAsync<string>("el => el.href");
                        storeUrl = ExtractStoreUrlFromGoogleRedirect(googleRedirectUrl);
                        Console.WriteLine($"Found Store URL: {storeUrl}");

                        foreach (var searchUrl in searchUrls)
                        {
                            if (!string.IsNullOrEmpty(storeUrl) && !string.IsNullOrEmpty(searchUrl))
                            {
                                // Check if the provided URL is entirely contained within the found Store URL
                                if (storeUrl.Contains(searchUrl, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!string.IsNullOrEmpty(googleProductUrl))
                                    {
                                        matchedUrls.Add((searchUrl, googleProductUrl));
                                        Console.WriteLine($"Matched Google Product URL: {googleProductUrl} with store URL: {searchUrl}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing product container: {ex.Message}");
                }
            }

            Console.WriteLine($"Total matched URLs: {matchedUrls.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching for product URLs: {ex.Message}");
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
                return storeUrl; // Return the store URL without cleaning or removing query parameters
            }
            else
            {
                return googleRedirectUrl; // In case 'url' parameter is missing
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting store URL: {ex.Message}");
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

//        _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
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
//                "--enable-webgl",
//                "--ignore-gpu-blocklist"
//            }
//        });

//        _page = (Page)await _browser.NewPageAsync();

//        await _page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 720 });
//        await _page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
//        {
//            { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" }
//        });
//    }

//    public async Task InitializeAndSearchAsync(string title)
//    {
//        Console.WriteLine("Navigating to Google Shopping...");
//        try
//        {
//            await _page.GoToAsync("https://shopping.google.com/", new NavigationOptions { Timeout = 25000, WaitUntil = new[] { WaitUntilNavigation.Load } }); 

//            var rejectButton = await _page.QuerySelectorAsync("button[aria-label='Odrzuć wszystko']");
//            if (rejectButton != null)
//            {
//                await rejectButton.ClickAsync();
//            }

//            var searchInputSelector = "input[name='q']";
//            await _page.WaitForSelectorAsync(searchInputSelector);


//            Console.WriteLine($"Entering product title in search bar: {title}");

//            foreach (char c in title)
//            {
//                await _page.TypeAsync(searchInputSelector, c.ToString());
//            }

//            await _page.Keyboard.PressAsync("Enter");
//            await _page.WaitForNavigationAsync(new NavigationOptions { Timeout = 60000, WaitUntil = new[] { WaitUntilNavigation.Load } }); // Zwiększono Timeout
//            Console.WriteLine("Page loaded.");
//        }
//        catch (TimeoutException ex)
//        {
//            Console.WriteLine($"Error: Timeout while navigating to Google Shopping: {ex.Message}");
//        }
//    }

//    public async Task SearchAndNavigateToStoreAsync(string storeName, List<string> searchUrls)
//    {
//        Console.WriteLine($"Searching for store: {storeName}");
//        await Task.Delay(500);

//        try
//        {

//            await _page.WaitForSelectorAsync("span.DON5yf", new WaitForSelectorOptions { Timeout = 15000 });
//            var sellerContainers = await _page.QuerySelectorAllAsync("span.DON5yf");
//            Console.WriteLine($"Found {sellerContainers.Length} seller elements.");


//            foreach (var container in sellerContainers)
//            {
//                var sellerElement = await container.QuerySelectorAsync("span.lg3aE");
//                if (sellerElement != null)
//                {
//                    var sellerText = await sellerElement.EvaluateFunctionAsync<string>("el => el.innerText");

//                    if (sellerText.Contains(storeName, StringComparison.OrdinalIgnoreCase))
//                    {
//                        var linkElement = await container.QuerySelectorAsync("a.vjtvke");
//                        if (linkElement != null)
//                        {
//                            var href = await linkElement.EvaluateFunctionAsync<string>("el => el.href");
//                            string fullUrl = href.StartsWith("http") ? href : $"https://www.google.com{href}";


//                            await _page.GoToAsync(fullUrl, new NavigationOptions { Timeout = 30000, WaitUntil = new[] { WaitUntilNavigation.Load } });
//                            Console.WriteLine("Navigated to the store. Waiting for 1 seconds...");
//                            await Task.Delay(500);


//                            return;
//                        }
//                    }
//                }
//            }
//        }
//        catch (MessageException ex)
//        {
//            Console.WriteLine($"Error during store search: {ex.Message}");
//        }
//    }

//    public async Task<List<(string storeUrl, string googleProductUrl)>> SearchForMatchingProductUrlsAsync(List<string> searchUrls)
//    {
//        var matchedUrls = new List<(string storeUrl, string googleProductUrl)>();
//        Console.WriteLine("Searching for matching product URLs on the store page...");

//        try
//        {

//            var productContainers = await _page.QuerySelectorAllAsync("div.sh-dgr__gr-auto.sh-dgr__grid-result");
//            Console.WriteLine($"Found {productContainers.Length} product boxes on the store page.");

//            foreach (var container in productContainers)
//            {
//                string? googleProductUrl = null;
//                string? storeUrl = null;

//                try
//                {

//                    var googleProductLinkElement = await container.QuerySelectorAsync("a.xCpuod");
//                    if (googleProductLinkElement != null)
//                    {
//                        googleProductUrl = await googleProductLinkElement.EvaluateFunctionAsync<string>("el => el.href");
//                        googleProductUrl = CleanGoogleUrl(googleProductUrl);
//                        Console.WriteLine($"Found Google Shopping URL: {googleProductUrl}");
//                    }

//                    var storeLinkElement = await container.QuerySelectorAsync("a[href*='/url?url=']");
//                    if (storeLinkElement != null)
//                    {
//                        storeUrl = await storeLinkElement.EvaluateFunctionAsync<string>("el => el.href");
//                        storeUrl = CleanUrl(storeUrl);
//                        Console.WriteLine($"Found Store URL: {storeUrl}");


//                        foreach (var searchUrl in searchUrls)
//                        {
//                            if (storeUrl.Contains(searchUrl) && !string.IsNullOrEmpty(googleProductUrl))
//                            {

//                                matchedUrls.Add((searchUrl, googleProductUrl));
//                                Console.WriteLine($"Matched Google Product URL: {googleProductUrl} with store URL: {searchUrl}");
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



//    private string CleanGoogleUrl(string url)
//    {
//        int questionMarkIndex = url.IndexOf("?");
//        return questionMarkIndex > 0 ? url.Substring(0, questionMarkIndex) : url;
//    }

//    private string CleanUrl(string url)
//    {
//        string decodedUrl = System.Web.HttpUtility.UrlDecode(url);
//        string cleanedUrl = decodedUrl.Replace("/url?url=", "");

//        if (cleanedUrl.StartsWith("https://www.google.com"))
//        {
//            cleanedUrl = cleanedUrl.Replace("https://www.google.com", "");
//        }

//        int questionMarkIndex = cleanedUrl.IndexOf("?");
//        return questionMarkIndex > 0 ? cleanedUrl.Substring(0, questionMarkIndex) : cleanedUrl;
//    }
//    public async Task CloseBrowserAsync()
//    {
//        await _page.CloseAsync();
//        await _browser.CloseAsync();
//    }
//}

