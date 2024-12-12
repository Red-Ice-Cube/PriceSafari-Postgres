using PuppeteerSharp;
using static PriceScrapingController;

public class ResolveCaptchaScraper
{
    private Browser _browser;
    private Page _page;

    public async Task InitializeNormalBrowserAsync()
    {
        try
        {
            Console.WriteLine("Starting browser initialization...");

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
                }
            });

            if (_browser == null)
            {
                throw new Exception("Browser failed to launch.");
            }

            _page = (Page)await _browser.NewPageAsync();

            if (_page == null)
            {
                throw new Exception("Failed to create a new page.");
            }

            // Wyłączamy JS
            await _page.SetJavaScriptEnabledAsync(false);

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

            // Ustawienie rozdzielczości
            var commonResolutions = new List<(int width, int height)>
            {
                (1366, 768)
            };

            var random = new Random();
            var randomResolution = commonResolutions[random.Next(commonResolutions.Count)];
            await _page.SetViewportAsync(new ViewPortOptions { Width = randomResolution.width, Height = randomResolution.height });

            await _page.EvaluateFunctionOnNewDocumentAsync(@"() => {
                [...document.querySelectorAll('link[rel=stylesheet], style')].forEach(e => e.remove());
                const origCreateElement = document.createElement;
                document.createElement = function(tagName, ...args) {
                    const el = origCreateElement.call(document, tagName, ...args);
                    if (tagName.toLowerCase() === 'link' || tagName.toLowerCase() === 'style') {
                        el.setAttribute('disabled', 'true');
                    }
                    return el;
                };
            }");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in InitializeBrowserAsync: {ex.Message}");
            throw;
        }
    }

    public async Task NavigateToCaptchaAsync()
    {
        await _page.GoToAsync("https://www.ceneo.pl/captcha/add", new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
        Console.WriteLine("Otworzono stronę z Captchą. Rozwiąż ją ręcznie w otwartym oknie...");
    }

    public async Task WaitForCaptchaSolutionAsync()
    {
        // Czekamy aż strona przejdzie dalej po rozwiązaniu captchy
        await _page.WaitForNavigationAsync(new NavigationOptions
        {
            WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
        });
        Console.WriteLine("Captcha rozwiązana, przechodzimy dalej.");
    }

    public async Task<CaptchaSessionData> GetSessionDataAsync()
    {
        // Zbieramy TYLKO COOKIES, bo JS jest wyłączony i nie wykonamy kodu do localStorage/sessionStorage
        var cookies = await _page.GetCookiesAsync();

        // Logowanie danych
        Console.WriteLine("=== COOKIES ===");
        foreach (var c in cookies)
        {
            Console.WriteLine($"{c.Name} = {c.Value}");
        }

        // Zwracamy tylko cookies, LocalStorage i SessionStorage puste
        return new CaptchaSessionData
        {
            Cookies = cookies,
        
        };
    }

    public async Task CloseBrowserAsync()
    {
        if (_page != null) await _page.CloseAsync();
        if (_browser != null) await _browser.CloseAsync();
    }
}
