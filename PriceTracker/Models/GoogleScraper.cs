using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class GoogleScraper
{
    private IBrowser _browser;
    private IPage _page;

    private List<int> storeReviewCounts = new List<int>();
    private List<(string Url, int ReviewCount)> urlReviewCounts = new List<(string, int)>();

    public async Task InitializeBrowserAsync()
    {
        var browserFetcher = new BrowserFetcher();
        await browserFetcher.DownloadAsync();
        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = false,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu", "--window-size=1920,1080" }
        });
        _page = await _browser.NewPageAsync();
        await _page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });
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
        await _page.TypeAsync(searchInputSelector, title);
        await _page.Keyboard.PressAsync("Enter");

        Console.WriteLine("Waiting for the page to load...");
        await _page.WaitForSelectorAsync("div.KZmu8e");
    }

    private int ExtractReviewCount(string reviewText)
    {
        var match = Regex.Match(reviewText, @"(\d[\d\s]*)\s+opini");
        if (match.Success)
        {
            // Remove any non-breaking spaces and standard spaces in large numbers (e.g., "2 812" -> "2812")
            string cleanNumber = match.Groups[1].Value.Replace(" ", "").Replace(" ", "");
            return int.Parse(cleanNumber);
        }
        return 0; // Return 0 if no number is found
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
                }
            }
        }
    }

    public async Task CloseBrowserAsync()
    {
        await _page.CloseAsync();
        await _browser.CloseAsync();
    }
}
