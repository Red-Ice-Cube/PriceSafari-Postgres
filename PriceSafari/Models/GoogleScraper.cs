using PuppeteerSharp;
using System.Text.RegularExpressions;

public class GoogleScraper
{
    private IBrowser _browser;
    private IPage _page;
    private string? _scrapedGoogleUrl;

    private List<int> storeReviewCounts = new List<int>();
    private List<(string Url, int ReviewCount)> urlReviewCounts = new List<(string, int)>();
    private List<string> matchedUrls = new List<string>();

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
                "--window-size=1920,1080",
                "--disable-blink-features=AutomationControlled"
            }
        });

        _page = await _browser.NewPageAsync();

        await _page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/113.0.5672.126 Safari/537.36");

        await _page.SetViewportAsync(new ViewPortOptions
        {
            Width = 1920,
            Height = 1080,
            IsMobile = false
        });

        await _page.EvaluateFunctionOnNewDocumentAsync(@"() => {
            Object.defineProperty(navigator, 'webdriver', {get: () => undefined});
            Object.defineProperty(navigator, 'languages', {get: () => ['pl-PL', 'pl']});
            Object.defineProperty(navigator, 'platform', {get: () => 'Win32'});
            Object.defineProperty(navigator, 'hardwareConcurrency', {get: () => 4});
            Object.defineProperty(navigator, 'deviceMemory', {get: () => 8});
            Object.defineProperty(navigator, 'doNotTrack', {get: () => '1'});
            Object.defineProperty(navigator, 'onLine', {get: () => true});
        }");

        await _page.EmulateTimezoneAsync("Europe/Warsaw");
    }

    public async Task InitializeAndSearchAsync(string title)
    {
        Console.WriteLine("Navigating to Google Shopping...");
        await _page.GoToAsync("https://shopping.google.com/");

        var rejectButtonSelector = "button[aria-label='Odrzuć wszystko']";
        var rejectButton = await _page.QuerySelectorAsync(rejectButtonSelector);
        if (rejectButton != null)
        {
            Console.WriteLine("Found 'Odrzuć wszystko' button, clicking...");
            await rejectButton.ClickAsync();
        }

        var searchInputSelector = "input[name='q']";
        await _page.WaitForSelectorAsync(searchInputSelector);
        Console.WriteLine($"Searching for: {title}");

        Random random = new Random();
        foreach (char c in title)
        {
            await _page.TypeAsync(searchInputSelector, c.ToString());
            await Task.Delay(random.Next(21, 59));
        }

        await _page.Keyboard.PressAsync("Enter");

        Console.WriteLine("Waiting for the page to load...");
        await _page.WaitForSelectorAsync("div.KZmu8e");
    }

    private int ExtractReviewCount(string reviewText)
    {
        var match = Regex.Match(reviewText, @"(\d[\d\s]*)\s+opini");
        if (match.Success)
        {
            string cleanNumber = match.Groups[1].Value.Replace(" ", "").Replace(" ", "");
            return int.Parse(cleanNumber);
        }
        return 0;
    }

    public async Task SearchStoreNameAsync(string storeName)
    {
        var resultsSelector = "div.KZmu8e";
        var results = await _page.QuerySelectorAllAsync(resultsSelector);
        Console.WriteLine($"Found {results.Length} elements with selector '{resultsSelector}'.");

        foreach (var result in results)
        {
            var sellerNameElement = await result.QuerySelectorAsync("div.sh-np__seller-container span.E5ocAb");
            var sellerName = await sellerNameElement?.EvaluateFunctionAsync<string>("el => el.innerText") ?? "Unknown";
            if (sellerName.Contains(storeName, StringComparison.OrdinalIgnoreCase))
            {
                var reviewElement = await result.QuerySelectorAsync("span.QIrs8");
                if (reviewElement != null)
                {
                    var reviewText = await reviewElement.EvaluateFunctionAsync<string>("el => el.innerText");
                    int reviewCount = ExtractReviewCount(reviewText);
                    storeReviewCounts.Add(reviewCount);
                    Console.WriteLine($"Opinie o produkcie: {reviewCount}");
                }
                else
                {
                    Console.WriteLine("Nie znaleziono opinii o produkcie.");
                }
            }
        }
    }

    public async Task SearchUrlAndReviewsWithFallbackAsync()
    {
        var timeoutTask = Task.Delay(5000);
        var searchTask = SearchUrlAndReviewsAsync();

        var completedTask = await Task.WhenAny(searchTask, timeoutTask);

        if (completedTask == timeoutTask)
        {
            Console.WriteLine("Initial search timed out, attempting fallback search...");
            await FallbackSearchUrlAndReviewsAsync();
        }
    }

    public async Task SearchUrlAndReviewsAsync()
    {
        await _page.WaitForSelectorAsync("div._-o0._-oZ");
        var productContainers = await _page.QuerySelectorAllAsync("div._-o0._-oZ");

        foreach (var container in productContainers)
        {
            var urlElement = await container.QuerySelectorAsync("a._-lD.sh-t__title.sh-t__title-popout.shntl.translate-content");
            var reviewElement = await container.QuerySelectorAsync("div._-pI a._-pJ");

            if (urlElement != null && reviewElement != null)
            {
                var url = await urlElement.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
                var reviewsText = await reviewElement.EvaluateFunctionAsync<string>("el => el.innerText");
                int reviewCount = ExtractReviewCount(reviewsText);

                var shortenedUrl = Regex.Match(url, @"product/(\d+)").Groups[1].Value;
                if (!string.IsNullOrEmpty(shortenedUrl))
                {
                    urlReviewCounts.Add((shortenedUrl, reviewCount));
                    Console.WriteLine($"Znaleziono URL: {shortenedUrl}");
                    Console.WriteLine($"Liczba opinii: {reviewCount}");
                }
            }
        }
    }

    private async Task FallbackSearchUrlAndReviewsAsync()
    {
        await _page.WaitForSelectorAsync("div.sh-dgr__content");
        var productContainers = await _page.QuerySelectorAllAsync("div.sh-dgr__content");

        foreach (var container in productContainers)
        {
            var urlElement = await container.QuerySelectorAsync("a.xCpuod");
            var reviewElement = await container.QuerySelectorAsync("span.QIrs8");

            if (urlElement != null && reviewElement != null)
            {
                var url = await urlElement.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
                var reviewsText = await reviewElement.EvaluateFunctionAsync<string>("el => el.innerText");
                int reviewCount = ExtractReviewCount(reviewsText);

                var shortenedUrl = Regex.Match(url, @"product/(\d+)").Groups[1].Value;
                if (!string.IsNullOrEmpty(shortenedUrl))
                {
                    urlReviewCounts.Add((shortenedUrl, reviewCount));
                    Console.WriteLine($"Znaleziono URL: {shortenedUrl}");
                    Console.WriteLine($"Liczba opinii: {reviewCount}");
                }
            }
        }
    }

    public void MatchReviews()
    {
        foreach (var storeReview in storeReviewCounts)
        {
            foreach (var urlReview in urlReviewCounts)
            {
                if (storeReview == urlReview.ReviewCount)
                {
                    Console.WriteLine($"ZnalezionyMatch: URL={urlReview.Url}, Liczba opinii={urlReview.ReviewCount}");
                    matchedUrls.Add(urlReview.Url);
                }
            }
        }
    }

    public async Task OpenAndScrapeMatchedOffersAsync(string targetUrl, string storeName)
    {
        bool targetUrlFound = false;

        foreach (var url in matchedUrls)
        {
            string matchedUrl = $"https://www.google.com/shopping/product/{url}/offers";
            Console.WriteLine($"Otwieranie URL: {matchedUrl}");

            targetUrlFound = await ScrapeOffersAsync(matchedUrl, targetUrl, storeName);

            if (targetUrlFound)
            {
                break;
            }
        }

        if (!targetUrlFound)
        {
            Console.WriteLine("Nie znaleziono targetUrl w matchedUrls, rozpoczynanie przeszukiwania wszystkich URL.");
            await OpenAndScrapeAllOffersAsync(targetUrl, storeName);
        }
    }






    public async Task<bool> ScrapeOffersAsync(string pageUrl, string targetUrl, string storeName)
    {
        bool targetUrlFound = false;
        string currentPageUrl = pageUrl;

        while (!targetUrlFound)
        {
            Console.WriteLine($"Odwiedzanie strony: {currentPageUrl}");
            await _page.GoToAsync(currentPageUrl);
            await _page.WaitForSelectorAsync("tr.sh-osd__offer-row");
            await _page.EvaluateFunctionAsync("() => { window.scrollBy(0, window.innerHeight); }");
            await Task.Delay(4000);

            var offerElements = await _page.QuerySelectorAllAsync("tr.sh-osd__offer-row");
            Console.WriteLine("Przeszukuję oferty na stronie.");

            bool storeNameFound = false;

            foreach (var offerElement in offerElements)
            {
                var storeElement = await offerElement.QuerySelectorAsync("a.b5ycib.shntl");
                if (storeElement != null)
                {
                    var storeText = await storeElement.EvaluateFunctionAsync<string>("el => el.innerText");
                    string cleanedStoreText = CleanText(storeText);
                    string cleanedStoreName = CleanText(storeName);

                    Console.WriteLine($"Znaleziono nazwę sklepu: {cleanedStoreText}");

                    if (cleanedStoreText.Contains(cleanedStoreName, StringComparison.OrdinalIgnoreCase))
                    {
                        storeNameFound = true;
                        Console.WriteLine($"Nazwa sklepu pasuje do {cleanedStoreName}.");

                        var linkElement = await offerElement.QuerySelectorAsync("a.b5ycib.shntl");
                        if (linkElement != null)
                        {
                            var offerUrl = await linkElement.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
                            if (!string.IsNullOrEmpty(offerUrl))
                            {
                                string cleanedUrl = CleanUrl(offerUrl);
                                Console.WriteLine($"Oczyszczony ProductStoreUrl: {cleanedUrl}");

                                if (cleanedUrl.Equals(targetUrl, StringComparison.OrdinalIgnoreCase))
                                {
                                    Console.WriteLine($"TargetUrlFound on: {pageUrl}");
                                    _scrapedGoogleUrl = pageUrl;
                                    targetUrlFound = true;
                                    break;
                                }
                            }
                        }

                        // Jeśli znaleziono nazwę sklepu, sprawdź ukryte oferty
                        var hiddenOfferButton = await offerElement.QuerySelectorAsync("div[data-url][role='button']");
                        if (hiddenOfferButton != null)
                        {
                            Console.WriteLine("Znaleziono przycisk do rozwinięcia ukrytych ofert.");
                            await hiddenOfferButton.ClickAsync();
                            await Task.Delay(2000); // Daj czas na rozwinięcie ukrytych ofert

                            // Sprawdź, czy kontener ukrytych ofert został prawidłowo załadowany
                            var hiddenContainerId = await hiddenOfferButton.EvaluateFunctionAsync<string>("el => el.getAttribute('data-container-id')");
                            Console.WriteLine($"ID kontenera ukrytych ofert: {hiddenContainerId}");

                            var hiddenOfferContainer = await _page.QuerySelectorAsync($"#{hiddenContainerId}");
                            if (hiddenOfferContainer != null)
                            {
                                Console.WriteLine("Kontener ukrytych ofert załadowany.");

                                var hiddenOfferElements = await hiddenOfferContainer.QuerySelectorAllAsync("tr.sh-osd__offer-row");
                                Console.WriteLine($"Znaleziono {hiddenOfferElements.Length} ukrytych ofert.");

                                foreach (var hiddenOfferElement in hiddenOfferElements)
                                {
                                    var hiddenLinkElement = await hiddenOfferElement.QuerySelectorAsync("a.b5ycib.shntl");
                                    if (hiddenLinkElement != null)
                                    {
                                        var hiddenOfferUrl = await hiddenLinkElement.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
                                        if (!string.IsNullOrEmpty(hiddenOfferUrl))
                                        {
                                            string hiddenCleanedUrl = CleanUrl(hiddenOfferUrl);
                                            Console.WriteLine($"Oczyszczony Ukryty ProductStoreUrl: {hiddenCleanedUrl}");

                                            if (hiddenCleanedUrl.Equals(targetUrl, StringComparison.OrdinalIgnoreCase))
                                            {
                                                Console.WriteLine($"TargetUrlFound on: {pageUrl}");
                                                _scrapedGoogleUrl = pageUrl;
                                                targetUrlFound = true;
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine("Nie udało się załadować kontenera ukrytych ofert.");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Nie znaleziono przycisku do rozwinięcia ukrytych ofert.");
                        }

                        if (targetUrlFound)
                        {
                            break;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Nazwa sklepu {cleanedStoreName} nie pasuje do znalezionej nazwy {cleanedStoreText}.");
                    }
                }
            }

            if (targetUrlFound || storeNameFound)
            {
                break;
            }

            var nextButton = await _page.QuerySelectorAsync("a.internal-link[data-url*='start']:last-child");
            if (nextButton == null || (await nextButton.EvaluateFunctionAsync<string>("el => el.innerText")) != "Dalej")
            {
                Console.WriteLine("Brak dalszych stron lub osiągnięto ostatnią stronę.");
                break;
            }

            string nextPageUrlPart = await nextButton.EvaluateFunctionAsync<string>("el => el.getAttribute('data-url')");
            currentPageUrl = "https://www.google.com" + nextPageUrlPart;
            Console.WriteLine($"Przechodzę do następnej strony: {currentPageUrl}");
            await Task.Delay(1000);
        }

        return targetUrlFound;
    }

    private string CleanText(string text)
    {
        
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }
     
        text = text.Replace("Otwiera się w nowym oknie", string.Empty);
        return text.Trim().ToLower();
    }







    public async Task OpenAndScrapeAllOffersAsync(string targetUrl, string storeName)
    {
        var productContainers = await _page.QuerySelectorAllAsync("div.sh-dgr__content");
        Console.WriteLine($"Przeszukiwanie wszystkich {productContainers.Length} produktów...");

        foreach (var container in productContainers)
        {
            var urlElement = await container.QuerySelectorAsync("a.xCpuod");
            if (urlElement != null)
            {
                var url = await urlElement.EvaluateFunctionAsync<string>("el => el.getAttribute('href')");
                if (!string.IsNullOrEmpty(url))
                {
                    string cleanedUrl = CleanUrl(url);
                    Console.WriteLine($"Oczyszczony URL produktu: {cleanedUrl}");

                    string offersPageUrl = $"https://www.google.com{cleanedUrl}/offers";
                    bool targetUrlFound = await ScrapeOffersAsync(offersPageUrl, targetUrl, storeName);

                    if (targetUrlFound)
                    {
                        break;
                    }
                }
            }
        }
    }





    private string CleanUrl(string url)
    {
        string decodedUrl = System.Web.HttpUtility.UrlDecode(url);

        string cleanedUrl = decodedUrl.Replace("/url?q=", "");

        int questionMarkIndex = cleanedUrl.IndexOf("?");
        if (questionMarkIndex > 0)
        {
            cleanedUrl = cleanedUrl.Substring(0, questionMarkIndex);
        }

        int ampersandIndex = cleanedUrl.IndexOf("&");
        if (ampersandIndex > 0)
        {
            cleanedUrl = cleanedUrl.Substring(0, ampersandIndex);
        }

        return cleanedUrl;
    }

    public string? GetScrapedGoogleUrl()
    {
        return _scrapedGoogleUrl;
    }

    public async Task CloseBrowserAsync()
    {
        await _page.CloseAsync();
        await _browser.CloseAsync();
    }

   
}
