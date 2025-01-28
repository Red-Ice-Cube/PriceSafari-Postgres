using PuppeteerSharp;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Collections.Generic;

public class ResolveCaptchaScraper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_SHOWNORMAL = 1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

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
                throw new Exception("Browser failed to launch.");

            _page = (Page)await _browser.NewPageAsync();
            if (_page == null)
                throw new Exception("Failed to create a new page.");

            await _page.SetJavaScriptEnabledAsync(false);

            // Przenoszenie okna aplikacji na wierzch
            IntPtr currentAppWindow = Process.GetCurrentProcess().MainWindowHandle;
            if (currentAppWindow != IntPtr.Zero)
            {
                ShowWindowAsync(currentAppWindow, SW_SHOWNORMAL);
                SetForegroundWindow(currentAppWindow);
            }

            // Przenoszenie okna przeglądarki
            var process = _browser.Process;
            if (process != null)
            {
                int maxAttempts = 20;
                for (int i = 0; i < maxAttempts; i++)
                {
                    IntPtr browserWindowHandle = FindBrowserWindowHandle(process.Id);
                    if (browserWindowHandle != IntPtr.Zero)
                    {
                        ShowWindowAsync(browserWindowHandle, SW_SHOWNORMAL);
                        SetWindowPos(browserWindowHandle, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW);
                        SetForegroundWindow(browserWindowHandle);
                        break;
                    }
                    await Task.Delay(500);
                }
            }

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

            var commonResolutions = new List<(int width, int height)>
            {
                (1366, 768)
            };
            var random = new Random();
            var randomResolution = commonResolutions[random.Next(commonResolutions.Count)];
            await _page.SetViewportAsync(new ViewPortOptions
            {
                Width = randomResolution.width,
                Height = randomResolution.height
            });

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

    private IntPtr FindBrowserWindowHandle(int processId)
    {
        IntPtr foundHandle = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            uint windowProcessId;
            GetWindowThreadProcessId(hWnd, out windowProcessId);
            if (windowProcessId == processId)
            {
                foundHandle = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return foundHandle;
    }

    public async Task NavigateToCaptchaAsync()
    {
        // 1. Otwieramy stronę z Captchą
        await _page.GoToAsync("https://www.ceneo.pl/captcha/add", new NavigationOptions
        {
            WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }
        });
        Console.WriteLine("Otworzono stronę z Captchą. Rozwiąż ją ręcznie w otwartym oknie...");
    }

    public async Task WaitAndNavigateToCeneoAsync()
    {
       
        await Task.Delay(TimeSpan.FromSeconds(10));

        await _page.GoToAsync("https://www.ceneo.pl/", new NavigationOptions
        {
            WaitUntil = new[] { WaitUntilNavigation.Networkidle2 }
        });
        Console.WriteLine("Minęło 10s, przenoszę na stronę główną Ceneo.");
        Console.WriteLine("Captcha rozwiązana, przechodzimy dalej.");
    }



    public async Task<CaptchaSessionData> GetSessionDataAsync()
    {
        var cookies = await _page.GetCookiesAsync();
        Console.WriteLine("=== COOKIES ===");
        foreach (var c in cookies)
        {
            Console.WriteLine($"{c.Name} = {c.Value}");
        }

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

public class CaptchaSessionData
{
    public CookieParam[] Cookies { get; set; }
}