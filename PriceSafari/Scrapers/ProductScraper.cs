using Microsoft.AspNetCore.SignalR;
using PriceSafari.Data;
using PriceSafari.Hubs;
using PriceSafari.Models;
using PuppeteerSharp;
using System.Net;

namespace PriceSafari.Scrapers
{
    public class ProductScraper : IDisposable
    {
        private readonly PriceSafariContext _context;
        private readonly IHubContext<ScrapingHub> _hubContext;
        private Browser _browser;
        private IPage _page;
        private readonly Settings _settings;

        public ProductScraper(
            PriceSafariContext context,
            IHubContext<ScrapingHub> hubContext,
            Settings settings)
        {
            _context = context;
            _hubContext = hubContext;
            _settings = settings;

            // Initialize the browser and page synchronously
            Task.Run(async () => await InitializeBrowserAndPageAsync()).GetAwaiter().GetResult();
        }

        private async Task InitializeBrowserAndPageAsync()
        {
            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = _settings.HeadLess, // Use settings
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

            // Create a new page and keep it open
            _page = await _browser.NewPageAsync();

            // Enable or disable JavaScript based on settings
            await _page.SetJavaScriptEnabledAsync(_settings.JavaScript);

            // Anti-detection measures
            await _page.EvaluateFunctionAsync(@"() => {
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

            // Set viewport size
            var commonResolutions = new List<(int width, int height)>
            {
                (1920, 1080)
            };

            var random = new Random();
            var randomResolution = commonResolutions[random.Next(commonResolutions.Count)];
            await _page.SetViewportAsync(new ViewPortOptions
            {
                Width = randomResolution.width,
                Height = randomResolution.height
            });

            // Enable request interception based on settings
            if (!_settings.Styles)
            {
                await _page.SetRequestInterceptionAsync(true);
                _page.Request += OnRequest;
            }

            // Set extra HTTP headers
            await _page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
            {
                { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" }
            });

            Console.WriteLine($"Bot ready, warming up for {_settings.WarmUpTime} seconds...");
            await Task.Delay(_settings.WarmUpTime * 1000);
            Console.WriteLine("Warm-up complete. Bot is ready to scrape.");
        }


        public async Task NavigateToCaptchaAsync()
        {
            await _page.GoToAsync("https://www.ceneo.pl/captcha/add", new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
            Console.WriteLine("Otworzono stronę z Captchą. Rozwiąż ją ręcznie w otwartym oknie...");
        }

        public async Task WaitForCaptchaSolutionAsync()
        {
            // Czekamy aż strona się przeładuje po rozwiązaniu captchy
            await _page.WaitForNavigationAsync(new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
            });
            Console.WriteLine("Captcha rozwiązana, przechodzimy dalej.");
        }



        public async Task ScrapeCategoryProducts(int storeId, string categoryName, string baseUrlTemplate, bool isProducer = false)
        {
            int pageIndex = 0;
            int pageCount = 1;
            bool hasMorePages = true;
            HashSet<string> existingProductUrls = _context.Products
                .Where(p => p.StoreId == storeId)
                .Select(p => p.OfferUrl)
                .ToHashSet();
            HashSet<string> newProductUrls = new HashSet<string>();

            while (hasMorePages)
            {
                // NOWA LOGIKA BUDOWANIA URL
                string url;
                if (isProducer)
                {
                    // Dla producentów: base url wygląda np. tak: /producenci/mova/Male_AGD_do_domu
                    // A paginacja: /producenci/mova/Male_AGD_do_domu;0020-30-0-0-1.htm
                    if (pageIndex == 0)
                    {
                        url = string.Format(baseUrlTemplate, ""); // Pierwsza strona to czysty baseUrl
                    }
                    else
                    {
                        // Dodajemy przyrostek paginacji. W Ceneo strony indeksowane są od 0 wizualnie na pierwszej stronie, ale w linku zaczyna się od 1.
                        // Uwaga: zakładam, że baseUrlTemplate dla producenta to pełny link przed dodaniem ";0020...".
                        // Np. baseUrlTemplate = "https://www.ceneo.pl/producenci/mova/Male_AGD_do_domu"
                        url = $"{baseUrlTemplate};0020-30-0-0-{pageIndex}.htm";
                    }
                }
                else
                {
                    // Stara logika dla zwykłych sklepów
                    url = string.Format(baseUrlTemplate, pageIndex);
                }

                Console.WriteLine($"Processing page: {url}");

                try
                {
                    await _page.GoToAsync(url, WaitUntilNavigation.Networkidle0);
                    Console.WriteLine("Page loaded successfully.");

                    // Wait for products to load
                    var contentLoaded = await _page.WaitForSelectorAsync("div.cat-prod-box", new WaitForSelectorOptions { Timeout = 5000 });

                    if (contentLoaded == null)
                    {
                        Console.WriteLine($"No products found on page {pageIndex}.");
                        hasMorePages = false;
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading page {pageIndex}: {ex.Message}");
                    break;
                }

                // ... (Pobieranie pageCount pozostaje bez zmian) ...
                if (pageIndex == 0)
                {
                    try
                    {
                        var pageCountElement = await _page.QuerySelectorAsync("input#page-counter");
                        if (pageCountElement != null)
                        {
                            var pageCountValue = await pageCountElement.EvaluateFunctionAsync<string>("el => el.getAttribute('data-pagecount')");
                            if (int.TryParse(pageCountValue, out int pc))
                            {
                                pageCount = pc;
                                Console.WriteLine($"Total pages to process: {pageCount}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Page count element not found.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error retrieving total page count: {ex.Message}");
                    }
                }

                // Select all product elements
                IElementHandle[] productElements = null;
                try
                {
                    productElements = (await _page.QuerySelectorAllAsync("div.cat-prod-box")).ToArray();
                    Console.WriteLine($"Found {productElements.Length} products on page {pageIndex}.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error selecting product elements on page {pageIndex}: {ex.Message}");
                    continue;
                }

                if (productElements != null && productElements.Length > 0)
                {
                    foreach (var productElement in productElements)
                    {
                        try
                        {
                            // Wykonujemy jeden szybki skrypt JS bezpośrednio w przeglądarce dla każdego produktu
                            var extractedData = await productElement.EvaluateFunctionAsync<string[]>(@"el => {
                            let pid = el.getAttribute('data-pid');
                            let name = '';
                            let gaCategory = el.getAttribute('data-gacategoryname');

                            // 1. Szukamy nazwy produktu (obsługa wielu wariantów widoku Ceneo)
                            let nameNode = el.querySelector('.cat-prod-box__name a, .name a, strong a');
                            if (nameNode) {
                                name = nameNode.textContent.trim();
                            } else {
                                // Fallback: jeśli nie ma w tekście linku, szukamy w atrybucie title na przycisku lub alt w obrazku
                                let btn = el.querySelector('a.go-to-product');
                                if (btn && btn.getAttribute('title')) name = btn.getAttribute('title').trim();
                
                                if (!name) {
                                    let img = el.querySelector('img');
                                    if (img && img.getAttribute('alt')) name = img.getAttribute('alt').trim();
                                }
                            }

                            // 2. Szukamy PID, jeśli brak atrybutu data-pid (szukamy w href przycisku)
                            let linkNode = el.querySelector('a.go-to-product, a.js_seoUrl');
                            if (!pid && linkNode) {
                                let href = linkNode.getAttribute('href');
                                if (href) {
                                    // Magia Regex: wyciągamy ciąg cyfr zaraz po '/' (np. z /181110365##;02514?tag=...)
                                    let match = href.match(/\/(\d+)/);
                                    if (match && match.length > 1) {
                                        pid = match[1];
                                    }
                                }
                            }

                            return [pid || '', name || '', gaCategory || ''];
                        }");

                            var pid = extractedData[0];
                            var name = extractedData[1];
                            var gaCategoryName = extractedData[2];

                            // Sprawdzamy, czy udało się skompletować niezbędne minimum
                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(pid))
                            {
                                var offerUrl = "https://www.ceneo.pl/" + pid;

                                if (existingProductUrls.Contains(offerUrl) || newProductUrls.Contains(offerUrl))
                                {
                                    Console.WriteLine($"Product already exists: {offerUrl}");
                                    continue;
                                }

                                string trimmedCategoryName;
                                if (!string.IsNullOrEmpty(gaCategoryName))
                                {
                                    trimmedCategoryName = gaCategoryName.Split('/').LastOrDefault()?.Trim();
                                }
                                else
                                {
                                    trimmedCategoryName = categoryName;
                                }

                                var productEntity = new ProductClass
                                {
                                    StoreId = storeId,
                                    ProductName = WebUtility.HtmlDecode(name),
                                    Category = trimmedCategoryName,
                                    OfferUrl = offerUrl
                                };

                                _context.Products.Add(productEntity);
                                newProductUrls.Add(offerUrl);

                                Console.WriteLine($"Scraped Product - Name: {name}, Category: {trimmedCategoryName}, URL: {offerUrl}");
                            }
                            else
                            {
                                Console.WriteLine($"Incomplete product data on page {pageIndex}. PID: {pid}, Name: {name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing product on page {pageIndex}: {ex.Message}");
                            continue;
                        }
                    }

                    // ... (Zapis do bazy i pętla stron pozostają bez zmian) ...
                    try
                    {
                        await _context.SaveChangesAsync();
                        Console.WriteLine("Product data saved to database.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error saving product data to database: {ex.Message}");
                        throw;
                    }
                }
                else
                {
                    Console.WriteLine($"No product elements found on page {pageIndex}.");
                    hasMorePages = false;
                }

                await _hubContext.Clients.All.SendAsync("ReceiveProgressUpdate", newProductUrls.Count, existingProductUrls.Count + newProductUrls.Count, pageIndex + 1, pageCount, storeId);

                pageIndex++;
                if (pageIndex >= pageCount)
                {
                    hasMorePages = false;
                }
            }

            Console.WriteLine("Scraping completed.");
        }

        public async Task CloseBrowserAsync()
        {
            if (_page != null)
            {
                // Remove the request interception handler if it was set
                if (!_settings.Styles)
                {
                    _page.Request -= OnRequest;
                }

                await _page.CloseAsync();
                _page = null;
                Console.WriteLine("Page closed.");
            }

            if (_browser != null)
            {
                await _browser.CloseAsync();
                _browser = null;
                Console.WriteLine("Browser closed.");
            }
        }

        public void Dispose()
        {
            Task.Run(async () => await CloseBrowserAsync()).GetAwaiter().GetResult();
        }

        // Event handler for request interception
        private async void OnRequest(object sender, RequestEventArgs e)
        {
            if (e.Request.ResourceType == ResourceType.Image ||
                e.Request.ResourceType == ResourceType.StyleSheet ||
                e.Request.ResourceType == ResourceType.Font)
            {
                await e.Request.AbortAsync();
            }
            else
            {
                await e.Request.ContinueAsync();
            }
        }
    }
}
