using PuppeteerSharp;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PriceSafari.Models
{
    public class CaptchaScraper
    {
        private Browser _browser;
        private Page _page;
        private readonly HttpClient _httpClient;

        public CaptchaScraper(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }


        public async Task InitializeBrowserAsync(Settings settings)
        {
            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = settings.HeadLess,
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
            "--use-gl=swiftshader",  // Użyj WebGL
            "--enable-webgl",        // Włącz WebGL
            "--ignore-gpu-blocklist" // Ignoruj blokowanie GPU
        }
            });

            _page = (Page)await _browser.NewPageAsync();

            // Ustawienie, czy włączyć JavaScript, na podstawie ustawień
            await _page.SetJavaScriptEnabledAsync(settings.JavaScript);

            // Ukrywanie Puppeteer i symulacja rzeczywistego środowiska przeglądarki
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

              
                if (navigator.userAgent.includes('Macintosh')) {
                    Object.defineProperty(navigator, 'languages', { get: () => ['en-US', 'en'], configurable: true });
                } else if (navigator.userAgent.includes('Linux')) {
                    Object.defineProperty(navigator, 'languages', { get: () => ['pl-PL', 'pl'], configurable: true });
                } else {
                    Object.defineProperty(navigator, 'languages', { get: () => ['pl-PL', 'pl'], configurable: true });
                }

                if (!window.chrome) {
                    Object.defineProperty(window, 'chrome', { get: () => ({ runtime: {} }) });
                }

           
                Object.defineProperty(navigator, 'getBattery', { get: () => Promise.resolve(null) });

             
                Object.defineProperty(navigator, 'hardwareConcurrency', { get: () => 4 });

              
                Object.defineProperty(navigator, 'doNotTrack', { get: () => '1' });

                const originalQuery = window.navigator.permissions.query;
                window.navigator.permissions.query = (parameters) => (
                    parameters.name === 'notifications' ?
                    Promise.resolve({ state: 'denied' }) :
                    originalQuery(parameters)
                );

              
                Object.defineProperty(navigator, 'userAgentData', { get: () => ({
                    brands: [{ brand: 'Google Chrome', version: '91' }],
                    mobile: false
                })});
            }");

           
            var commonResolutions = new List<(int width, int height)>
            {
                (1280, 720),
                (1366, 768),
                (1600, 900),
                (1920, 1080)
            };

          
            var random = new Random();
            var randomResolution = commonResolutions[random.Next(commonResolutions.Count)];
            await _page.SetViewportAsync(new ViewPortOptions { Width = randomResolution.width, Height = randomResolution.height });

            
            await _page.SetRequestInterceptionAsync(true);
            _page.Request += async (sender, e) =>
            {
                if (settings.Styles == false)
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
                else
                {
                   
                    await e.Request.ContinueAsync();
                }
            };

           
            await _page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
            {
                { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" }
            });

          
            var userAgentList = new List<string>
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.212 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.131 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.1.2 Safari/605.1.15",
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/88.0.4324.96 Safari/537.36"
            };

            var randomUserAgent = userAgentList[random.Next(userAgentList.Count)];
            await _page.SetUserAgentAsync(randomUserAgent);

            await _page.EmulateTimezoneAsync("Europe/Warsaw");

            Console.WriteLine($"Bot gotowy, teraz rozgrzewka przez {settings.WarmUpTime} sekund...");
            await Task.Delay(settings.WarmUpTime * 1000);
            Console.WriteLine("Rozgrzewka zakończona. Bot gotowy do scrapowania.");
        }





        public async Task CloseBrowserAsync()
        {
            await _page.CloseAsync();
            await _browser.CloseAsync();
        }

        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> HandleCaptchaAndScrapePricesAsync(string url)
        {
            var priceResults = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            string log;

            try
            {
                await _page.GoToAsync(url);
                var currentUrl = _page.Url;

                while (currentUrl.Contains("/Captcha/Add"))
                {
                    Console.WriteLine("Captcha detected. Please solve it manually in the browser.");

                    while (currentUrl.Contains("/Captcha/Add"))
                    {
                        await Task.Delay(15000);
                        currentUrl = _page.Url;
                    }
                }

                var totalOffersCount = await GetTotalOffersCountAsync();
                Console.WriteLine($"Total number of offers: {totalOffersCount}");

                var (mainPrices, scrapeLog, scrapeRejectedProducts) = await ScrapePricesFromCurrentPage(url, true);
                priceResults.AddRange(mainPrices);
                log = scrapeLog;
                rejectedProducts.AddRange(scrapeRejectedProducts);

                if (totalOffersCount > 15)
                {
                    var sortedUrl = $"{url};0281-1.htm";
                    await _page.GoToAsync(sortedUrl);

                    var (sortedPrices, sortedLog, sortedRejectedProducts) = await ScrapePricesFromCurrentPage(sortedUrl, false);
                    log += sortedLog;
                    rejectedProducts.AddRange(sortedRejectedProducts);

                    foreach (var sortedPrice in sortedPrices)
                    {
                        if (!priceResults.Any(p => p.storeName == sortedPrice.storeName && p.price == sortedPrice.price))
                        {
                            priceResults.Add(sortedPrice);
                        }
                    }

                    if (priceResults.Count < totalOffersCount)
                    {
                        var nextSortedUrl = $"{url};0281-0.htm";
                        await _page.GoToAsync(nextSortedUrl);

                        var (nextSortedPrices, nextSortedLog, nextSortedRejectedProducts) = await ScrapePricesFromCurrentPage(nextSortedUrl, false);
                        log += nextSortedLog;
                        rejectedProducts.AddRange(nextSortedRejectedProducts);

                        foreach (var nextSortedPrice in nextSortedPrices)
                        {
                            if (!priceResults.Any(p => p.storeName == nextSortedPrice.storeName && p.price == nextSortedPrice.price))
                            {
                                priceResults.Add(nextSortedPrice);
                            }
                        }
                    }
                }

                log += $"Scraping completed, found {priceResults.Count} unique offers in total.";
            }
            catch (Exception ex)
            {
                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
                rejectedProducts.Add(($"Exception: {ex.Message}", url));
            }

            return (priceResults, log, rejectedProducts);
        }

        private async Task<int> GetTotalOffersCountAsync()
        {
            var totalOffersText = await _page.QuerySelectorAsync("span.page-tab__title.js_prevent-middle-button-click");
            var totalOffersCount = 0;
            if (totalOffersText != null)
            {
                var textContent = await totalOffersText.EvaluateFunctionAsync<string>("el => el.innerText");
                var match = Regex.Match(textContent, @"\d+");
                if (match.Success)
                {
                    totalOffersCount = int.Parse(match.Value);
                }
            }
            return totalOffersCount;
        }

        private async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> ScrapePricesFromCurrentPage(string url, bool includePosition)
        {
            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            var storeOffers = new Dictionary<string, (decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)>();
            string log;
            int positionCounter = 1;

            Console.WriteLine("Querying for offer nodes...");
            var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");

            if (offerNodes.Length > 0)
            {
                Console.WriteLine($"Found {offerNodes.Length} offer nodes.");
                foreach (var offerNode in offerNodes)
                {
                    var parentList = await offerNode.EvaluateFunctionAsync<string>("el => el.closest('ul')?.className");
                    if (!string.IsNullOrEmpty(parentList) && parentList.Contains("similar-offers"))
                    {
                        Console.WriteLine("Ignoring similar offer.");
                        rejectedProducts.Add(("Similar offer detected", url));
                        continue;
                    }

                    var storeName = await GetStoreNameFromOfferNodeAsync((ElementHandle)offerNode);

                    var priceValue = await GetPriceFromOfferNodeAsync((ElementHandle)offerNode);
                    if (!priceValue.HasValue)
                    {
                        rejectedProducts.Add(("Failed to parse price", url));
                        continue;
                    }

                    decimal? shippingCostNum = await GetShippingCostFromOfferNodeAsync((ElementHandle)offerNode);

                    int? availabilityNum = await GetAvailabilityFromOfferNodeAsync((ElementHandle)offerNode);

                    var isBidding = await GetBiddingInfoFromOfferNodeAsync((ElementHandle)offerNode);

                    string? position = includePosition ? positionCounter.ToString() : null;
                    positionCounter++;

                    if (storeOffers.ContainsKey(storeName))
                    {
                        if (priceValue.Value < storeOffers[storeName].price)
                        {
                            storeOffers[storeName] = (priceValue.Value, shippingCostNum, availabilityNum, isBidding, position);
                        }
                    }
                    else
                    {
                        storeOffers[storeName] = (priceValue.Value, shippingCostNum, availabilityNum, isBidding, position);
                    }
                }

                prices = storeOffers.Select(x => (x.Key, x.Value.price, x.Value.shippingCostNum, x.Value.availabilityNum, x.Value.isBidding, x.Value.position)).ToList();
                log = $"Successfully scraped prices from URL: {url}";
            }
            else
            {
                log = $"Failed to find prices on URL: {url}";
                rejectedProducts.Add(("No offer nodes found", url));
            }

            return (prices, log, rejectedProducts);
        }

        private async Task<string> GetStoreNameFromOfferNodeAsync(ElementHandle offerNode)
        {
            var storeName = await offerNode.QuerySelectorAsync("div.product-offer__store img") is ElementHandle imgElement
                ? await imgElement.EvaluateFunctionAsync<string>("el => el.alt")
                : null;

            if (string.IsNullOrWhiteSpace(storeName))
            {
                var storeLink = await offerNode.QuerySelectorAsync("li.offer-shop-opinions a.link.js_product-offer-link");
                if (storeLink != null)
                {
                    var offerParameter = await storeLink.EvaluateFunctionAsync<string>("el => el.getAttribute('offer-parameter')");
                    if (!string.IsNullOrEmpty(offerParameter))
                    {
                        var match = Regex.Match(offerParameter, @"sklepy/([^;]+);");
                        if (match.Success)
                        {
                            storeName = match.Groups[1].Value;

                            var hyphenIndex = storeName.LastIndexOf('-');
                            if (hyphenIndex > 0)
                            {
                                storeName = storeName.Substring(0, hyphenIndex);
                            }
                        }
                    }
                }
            }

            return storeName;
        }

        private async Task<decimal?> GetPriceFromOfferNodeAsync(ElementHandle offerNode)
        {
            var priceNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.value");
            var pennyNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.penny");

            if (priceNode == null || pennyNode == null)
            {
                return null;
            }

            var priceText = (await priceNode.EvaluateFunctionAsync<string>("el => el.innerText")).Trim() +
                            (await pennyNode.EvaluateFunctionAsync<string>("el => el.innerText")).Trim();
            var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

            if (!decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
            {
                return null;
            }

            return price;
        }

        private async Task<decimal?> GetShippingCostFromOfferNodeAsync(ElementHandle offerNode)
        {
            var shippingNode = await offerNode.QuerySelectorAsync("div.free-delivery-label") ??
                               await offerNode.QuerySelectorAsync("span.product-delivery-info.js_deliveryInfo");

            if (shippingNode != null)
            {
                var shippingText = await shippingNode.EvaluateFunctionAsync<string>("el => el.innerText");
                if (shippingText.Contains("Darmowa wysyłka") || shippingText.Contains("bezpłatna dostawa"))
                {
                    return 0.00m;
                }
                else
                {
                    var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
                    if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsedShippingCost))
                    {
                        return parsedShippingCost;
                    }
                }
            }
            return null;
        }

        private async Task<int?> GetAvailabilityFromOfferNodeAsync(ElementHandle offerNode)
        {
            var availabilityNode = await offerNode.QuerySelectorAsync("span.instock") ??
                                   await offerNode.QuerySelectorAsync("div.product-availability span");

            if (availabilityNode != null)
            {
                var availabilityText = await availabilityNode.EvaluateFunctionAsync<string>("el => el.innerText");
                if (availabilityText.Contains("Wysyłka w 1 dzień"))
                {
                    return 1;
                }
                else if (availabilityText.Contains("Wysyłka do"))
                {
                    var daysText = Regex.Match(availabilityText, @"\d+").Value;
                    if (int.TryParse(daysText, out int parsedDays))
                    {
                        return parsedDays;
                    }
                }
            }
            return null;
        }

        private async Task<string> GetBiddingInfoFromOfferNodeAsync(ElementHandle offerNode)
        {
            var offerContainer = await offerNode.QuerySelectorAsync(".product-offer__container");
            var offerType = await offerContainer?.EvaluateFunctionAsync<string>("el => el.getAttribute('data-offertype')");
            return offerType?.Contains("Bid") == true ? "1" : "0";
        }
    }
}





//using Microsoft.EntityFrameworkCore;
//using Microsoft.Playwright;
//using PuppeteerSharp;
//using System.Globalization;
//using System.Text.RegularExpressions;

//namespace PriceSafari.Models
//{
//    public class CaptchaScraper
//    {
//        private IPlaywright _playwright;
//        private IBrowser _browser;
//        private IBrowserContext _context;
//        private IPage _page;
//        private readonly HttpClient _httpClient;

//        public CaptchaScraper(HttpClient httpClient)
//        {
//            _httpClient = httpClient;
//        }

//        public async Task InitializeBrowserAsync(string browserType = "chromium")
//        {
//            _playwright = await Playwright.CreateAsync();

//            IBrowserType browserTypeInstance;
//            BrowserTypeLaunchOptions launchOptions = new BrowserTypeLaunchOptions
//            {
//                Headless = false,
//                Args = new[]
//                {
//                    "--no-sandbox",
//                    "--disable-setuid-sandbox",
//                    "--disable-gpu",
//                    "--disable-blink-features=AutomationControlled" ,
//                     "--enable-http2"
//                }
//            };

//            if (browserType == "chromium")
//            {
//                //launchOptions.ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
//                launchOptions.ExecutablePath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
//                browserTypeInstance = _playwright.Chromium;
//            }
//            else if (browserType == "firefox")
//            {
//                browserTypeInstance = _playwright.Firefox;
//            }
//            else
//            {
//                browserTypeInstance = _playwright.Webkit;
//            }

//            _browser = await browserTypeInstance.LaunchAsync(launchOptions);

//            _context = await _browser.NewContextAsync(new BrowserNewContextOptions
//            {
//                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36",
//                ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
//                ExtraHTTPHeaders = new Dictionary<string, string>
//        {
//            { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" },
//            { "Referer", "https://www.google.pl/" }
//        },
//                TimezoneId = "Europe/Warsaw",

//                Locale = "pl-PL"
//            });

//            _page = await _context.NewPageAsync();

//            // Dodaj init script, żeby ukryć webdriver
//            await _page.EvaluateAsync(@"() => {
//                Object.defineProperty(navigator, 'webdriver', {
//                    get: () => false,
//                    configurable: true
//                });
//            }");

//            Console.WriteLine("Rozgrzewka bota. Czekam 5 minut...");
//            await Task.Delay(TimeSpan.FromSeconds(20));
//            Console.WriteLine("Rozgrzewka zakończona. Rozpoczynamy scrapowanie.");
//        }

//        private readonly List<string> _userAgents = new List<string>
//        {
//            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36"

//        };

//        public async Task SetRandomUserAgentAsync()
//        {
//            Random random = new Random();
//            var userAgent = _userAgents[random.Next(_userAgents.Count)];

//            _context = await _browser.NewContextAsync(new BrowserNewContextOptions
//            {
//                UserAgent = userAgent,
//                TimezoneId = "Europe/Warsaw",

//                Locale = "pl-PL",
//                ExtraHTTPHeaders = new Dictionary<string, string>
//        {
//            { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" },
//            { "Referer", "https://www.google.pl/" }
//        }
//            });

//            _page = await _context.NewPageAsync();
//        }

//        public async Task CloseBrowserAsync()
//        {
//            await _page.CloseAsync();
//            await _browser.CloseAsync();
//        }

//        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> HandleCaptchaAndScrapePricesAsync(string url)
//        {
//            var priceResults = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)>();
//            var rejectedProducts = new List<(string Reason, string Url)>();
//            string log;

//            try
//            {
//                // Przejdź do głównego URL
//                await _page.GotoAsync(url);
//                var currentUrl = _page.Url;

//                while (currentUrl.Contains("/Captcha/Add"))
//                {
//                    log = $"CAPTCHA detected for URL: {url}. Waiting for manual solution...";
//                    Console.WriteLine(log);

//                    // Czekaj, aż użytkownik ręcznie rozwiąże CAPTCHA
//                    while (currentUrl.Contains("/Captcha/Add"))
//                    {
//                        await Task.Delay(2000);
//                        currentUrl = _page.Url;
//                    }

//                    log = $"CAPTCHA solved for URL: {url}";
//                    Console.WriteLine(log);
//                }

//                // Pobieranie liczby ofert (np. "Oferty (20)")
//                var totalOffersText = await _page.QuerySelectorAsync("span.page-tab__title.js_prevent-middle-button-click");
//                var totalOffersCount = 0;
//                if (totalOffersText != null)
//                {
//                    var textContent = await totalOffersText.InnerTextAsync();
//                    var match = Regex.Match(textContent, @"\d+");
//                    if (match.Success)
//                    {
//                        totalOffersCount = int.Parse(match.Value);
//                    }
//                }

//                Console.WriteLine($"Total number of offers: {totalOffersCount}");

//                // Scrapowanie z głównego URL
//                var (mainPrices, scrapeLog, scrapeRejectedProducts) = await ScrapePricesFromCurrentPage(url, true); // Scrapowanie z głównego URL z pozycjami
//                priceResults.AddRange(mainPrices);
//                log = scrapeLog;
//                rejectedProducts.AddRange(scrapeRejectedProducts);

//                // Jeśli liczba ofert jest większa niż 15, przejdź do URL z filtrowaniem
//                if (totalOffersCount > 15)
//                {
//                    // Przejście do URL z sortowaniem najwyzej ocenieanych
//                    var sortedUrl = $"{url};0281-1.htm";
//                    await _page.GotoAsync(sortedUrl);

//                    var (sortedPrices, sortedLog, sortedRejectedProducts) = await ScrapePricesFromCurrentPage(sortedUrl, false); // Scrapowanie z posortowanego URL bez pozycji
//                    log += sortedLog;
//                    rejectedProducts.AddRange(sortedRejectedProducts);

//                    // Łączenie wyników
//                    foreach (var sortedPrice in sortedPrices)
//                    {
//                        if (!priceResults.Any(p => p.storeName == sortedPrice.storeName && p.price == sortedPrice.price))
//                        {
//                            // Dodajemy tylko nowe oferty, których jeszcze nie ma w priceResults
//                            priceResults.Add(sortedPrice);
//                        }
//                    }

//                    // Jeśli nadal brakuje ofert, przejdź do kolejnego sortowania
//                    if (priceResults.Count < totalOffersCount)
//                    {
//                        var nextSortedUrl = $"{url};0281-0.htm"; // Sortowanie od następnej strony
//                        await _page.GotoAsync(nextSortedUrl);

//                        var (nextSortedPrices, nextSortedLog, nextSortedRejectedProducts) = await ScrapePricesFromCurrentPage(nextSortedUrl, false);
//                        log += nextSortedLog;
//                        rejectedProducts.AddRange(nextSortedRejectedProducts);

//                        // Łączenie wyników z dodatkowego sortowania
//                        foreach (var nextSortedPrice in nextSortedPrices)
//                        {
//                            if (!priceResults.Any(p => p.storeName == nextSortedPrice.storeName && p.price == nextSortedPrice.price))
//                            {
//                                priceResults.Add(nextSortedPrice);
//                            }
//                        }
//                    }
//                }

//                log += $"Scraping completed, found {priceResults.Count} unique offers in total.";
//            }
//            catch (Exception ex)
//            {
//                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
//                rejectedProducts.Add(($"Exception: {ex.Message}", url));
//            }

//            return (priceResults, log, rejectedProducts);
//        }

//        // Funkcja scrapująca z opcją dodania lub pominięcia pozycji, z obsługą najtańszych ofert z wariantów
//        private async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> ScrapePricesFromCurrentPage(string url, bool includePosition)
//        {
//            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)>();
//            var rejectedProducts = new List<(string Reason, string Url)>();
//            var storeOffers = new Dictionary<string, (decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)>(); // Służy do przechowywania najtańszych ofert dla każdego sklepu
//            string log;
//            int positionCounter = 1;

//            Console.WriteLine("Querying for offer nodes...");
//            var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");

//            if (offerNodes.Count > 0)
//            {
//                Console.WriteLine($"Found {offerNodes.Count} offer nodes.");
//                foreach (var offerNode in offerNodes)
//                {
//                    var parentList = await offerNode.EvaluateAsync<string>("el => el.closest('ul')?.className");
//                    if (!string.IsNullOrEmpty(parentList) && parentList.Contains("similar-offers"))
//                    {
//                        Console.WriteLine("Ignoring similar offer.");
//                        rejectedProducts.Add(("Similar offer detected", url));
//                        continue;
//                    }

//                    var storeName = await offerNode.QuerySelectorAsync("div.product-offer__store img") is IElementHandle imgElement
//                        ? await imgElement.EvaluateAsync<string>("el => el.alt")
//                        : null;

//                    if (string.IsNullOrWhiteSpace(storeName))
//                    {
//                        var storeLink = await offerNode.QuerySelectorAsync("li.offer-shop-opinions a.link.js_product-offer-link");
//                        if (storeLink != null)
//                        {
//                            var offerParameter = await storeLink.EvaluateAsync<string>("el => el.getAttribute('offer-parameter')");
//                            if (!string.IsNullOrEmpty(offerParameter))
//                            {
//                                var match = Regex.Match(offerParameter, @"sklepy/([^;]+);");
//                                if (match.Success)
//                                {
//                                    storeName = match.Groups[1].Value;
//                                    var hyphenIndex = storeName.LastIndexOf('-');
//                                    if (hyphenIndex > 0)
//                                    {
//                                        storeName = storeName.Substring(0, hyphenIndex);
//                                    }
//                                }
//                            }
//                        }
//                    }

//                    var priceNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.value");
//                    var pennyNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.penny");

//                    if (priceNode == null || pennyNode == null)
//                    {
//                        Console.WriteLine("Price node or penny node is null, skipping this offer.");
//                        rejectedProducts.Add(("Price node or penny node is null", url));
//                        continue;
//                    }

//                    var priceText = (await priceNode.EvaluateAsync<string>("el => el.innerText")).Trim() +
//                                    (await pennyNode.EvaluateAsync<string>("el => el.innerText")).Trim();
//                    var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

//                    if (!decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
//                    {
//                        Console.WriteLine("Failed to parse price, skipping this offer.");
//                        rejectedProducts.Add(("Failed to parse price", url));
//                        continue;
//                    }

//                    decimal? shippingCostNum = null;
//                    var shippingNode = await offerNode.QuerySelectorAsync("div.free-delivery-label") ??
//                                       await offerNode.QuerySelectorAsync("span.product-delivery-info.js_deliveryInfo");

//                    if (shippingNode != null)
//                    {
//                        var shippingText = await shippingNode.EvaluateAsync<string>("el => el.innerText");

//                        // Dodajemy logowanie, aby zobaczyć, co dokładnie widzi bot w shippingText
//                        Console.WriteLine($"Shipping Text: {shippingText}");

//                        if (shippingText.Contains("DARMOWA WYSYŁKA") || shippingText.Contains("bezpłatna dostawa"))
//                        {
//                            shippingCostNum = 0.00m;
//                            Console.WriteLine($"Detected free shipping for: {shippingText}");
//                        }
//                        else
//                        {
//                            var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
//                            if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsedShippingCost))
//                            {
//                                shippingCostNum = parsedShippingCost;
//                                Console.WriteLine($"Parsed shipping cost: {shippingCostNum}");
//                            }
//                            else
//                            {
//                                Console.WriteLine($"Failed to parse shipping cost from: {shippingText}");
//                            }
//                        }
//                    }

//                    int? availabilityNum = null;
//                    var availabilityNode = await offerNode.QuerySelectorAsync("span.instock") ??
//                                           await offerNode.QuerySelectorAsync("div.product-availability span");

//                    if (availabilityNode != null)
//                    {
//                        var availabilityText = await availabilityNode.EvaluateAsync<string>("el => el.innerText");
//                        if (availabilityText.Contains("Wysyłka w 1 dzień"))
//                        {
//                            availabilityNum = 1;
//                        }
//                        else if (availabilityText.Contains("Wysyłka do"))
//                        {
//                            var daysText = Regex.Match(availabilityText, @"\d+").Value;
//                            if (int.TryParse(daysText, out int parsedDays))
//                            {
//                                availabilityNum = parsedDays;
//                            }
//                        }
//                    }

//                    var offerContainer = await offerNode.QuerySelectorAsync(".product-offer__container");
//                    var offerType = await offerContainer?.EvaluateAsync<string>("el => el.getAttribute('data-offertype')");
//                    var isBidding = offerType?.Contains("Bid") == true ? "1" : "0";

//                    string? position = includePosition ? positionCounter.ToString() : null;
//                    positionCounter++;

//                    // Sprawdź, czy oferta z tego sklepu już istnieje, i jeśli istnieje, sprawdź, czy nowa oferta jest tańsza
//                    if (storeOffers.ContainsKey(storeName))
//                    {
//                        if (price < storeOffers[storeName].price)
//                        {
//                            // Zaktualizuj ofertę, jeśli nowa cena jest niższa
//                            storeOffers[storeName] = (price, shippingCostNum, availabilityNum, isBidding, position);
//                            Console.WriteLine($"Updated price for store: StoreName={storeName}, Price={price}, ShippingCost={shippingCostNum}, Availability={availabilityNum}, IsBidding={isBidding}, Position={position}");
//                        }
//                        else
//                        {
//                            Console.WriteLine($"Found more expensive offer for store {storeName}, ignoring.");
//                        }
//                    }
//                    else
//                    {
//                        // Dodajemy nową ofertę
//                        storeOffers[storeName] = (price, shippingCostNum, availabilityNum, isBidding, position);
//                        Console.WriteLine($"Added price: StoreName={storeName}, Price={price}, ShippingCost={shippingCostNum}, Availability={availabilityNum}, IsBidding={isBidding}, Position={position}");
//                    }
//                }

//                // Zamiana dictionary na listę
//                prices = storeOffers.Select(x => (x.Key, x.Value.price, x.Value.shippingCostNum, x.Value.availabilityNum, x.Value.isBidding, x.Value.position)).ToList();

//                log = $"Successfully scraped prices from URL: {url}";
//                Console.WriteLine(log);
//            }
//            else
//            {
//                log = $"Failed to find prices on URL: {url}";
//                Console.WriteLine(log);
//                rejectedProducts.Add(("No offer nodes found", url));
//            }

//            return (prices, log, rejectedProducts);
//        }

//    }
//}

//using PuppeteerSharp;
//using System.Globalization;
//using System.Text.RegularExpressions;

//namespace PriceSafari.Models
//{
//    public class CaptchaScraper
//    {
//        private Browser _browser;
//        private Page _page;
//        private readonly HttpClient _httpClient;

//        public CaptchaScraper(HttpClient httpClient)
//        {
//            _httpClient = httpClient;
//        }

//        public async Task InitializeBrowserAsync()
//        {
//            var browserFetcher = new BrowserFetcher();
//            await browserFetcher.DownloadAsync();

//            // Uruchomienie przeglądarki z wyłączonymi elementami, które mogą wykryć automatyzację
//            _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
//            {
//                Headless = false,
//                Args = new[]
//                {
//                    "--no-sandbox",
//                    "--disable-setuid-sandbox",
//                    "--disable-gpu",
//                    "--disable-background-timer-throttling",
//                    "--disable-backgrounding-occluded-windows",
//                    "--mute-audio",
//                    "--disable-software-rasterizer",
//                    "--disable-extensions",
//                    "--disable-background-networking",
//                    "--disable-sync",
//                    "--disable-translate",
//                    "--disable-background-timer-throttling",
//                    "--disable-renderer-backgrounding",
//                    "--disable-device-discovery-notifications",
//                    "--disable-default-apps",
//                    "--no-default-browser-check",
//                    "--no-first-run",
//                    "--disable-hang-monitor",
//                    "--disable-prompt-on-repost",
//                    "--disable-blink-features=AutomationControlled" // Ukrywa automatyzację
//                }
//            });

//            _page = (Page)await _browser.NewPageAsync();

//            // Wstrzyknięcie skryptu na nowo otwartych stronach
//            await _page.EvaluateFunctionAsync(@"() => {
//                Object.defineProperty(navigator, 'webdriver', {
//                    get: () => false,
//                });
//                Object.defineProperty(navigator, 'languages', {
//                    get: () => ['pl-PL', 'pl'],
//                });
//                Object.defineProperty(navigator, 'plugins', {
//                    get: () => [1, 2, 3],
//                });
//            }");

//            // Ustawienie widoku
//            await _page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 800 });

//await _page.SetRequestInterceptionAsync(true);

//_page.Request += async (sender, e) =>
//{
//    if (e.Request.ResourceType == ResourceType.Image ||
//        e.Request.ResourceType == ResourceType.StyleSheet ||
//        e.Request.ResourceType == ResourceType.Font)
//    {
//        await e.Request.AbortAsync();
//    }
//    else
//    {
//        await e.Request.ContinueAsync();
//    }
//};

//            await _page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
//            {
//                { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" }
//            });
//            await _page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.131 Safari/537.36");

//            // Ustawienie strefy czasowej na Warszawę i geolokalizacji na Polskę
//            await _page.EmulateTimezoneAsync("Europe/Warsaw");

//            // Funkcja rozgrzewki - czekanie 3 minuty
//            Console.WriteLine("Rozgrzewka bota. Czekam 1 minuty...");
//            await Task.Delay(TimeSpan.FromMinutes(1));  // Czekanie 3 minut
//            Console.WriteLine("Rozgrzewka zakończona. Rozpoczynamy scrapowanie.");
//        }

//        public async Task CloseBrowserAsync()
//        {
//            await _page.CloseAsync();
//            await _browser.CloseAsync();
//        }

//        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> HandleCaptchaAndScrapePricesAsync(string url)
//        {
//            var priceResults = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
//            var rejectedProducts = new List<(string Reason, string Url)>();
//            string log;

//            try
//            {
//                // Przejdź do strony
//                await _page.GoToAsync(url);
//                var currentUrl = _page.Url;

//                // Sprawdź, czy przeglądarka została przekierowana na stronę z CAPTCHA
//                while (currentUrl.Contains("/Captcha/Add"))
//                {
//                    Console.WriteLine("Captcha detected. Please solve it manually in the browser.");

//                    // Czekaj, aż użytkownik ręcznie rozwiąże CAPTCHA
//                    while (currentUrl.Contains("/Captcha/Add"))
//                    {
//                        await Task.Delay(2000); // Czekaj 2 sekundy przed kolejnym sprawdzeniem
//                        currentUrl = _page.Url; // Aktualizuj URL, aby sprawdzić, czy CAPTCHA zostało rozwiązane
//                    }
//                }

//                // Jeśli pojawi się baner cookie, kliknij "Nie zgadzam się"
//                var rejectButton = await _page.QuerySelectorAsync("button.cookie-consent__buttons__action.js_cookie-consent-necessary[data-role='reject-rodo']");
//                if (rejectButton != null)
//                {
//                    await rejectButton.ClickAsync();
//                }

//                // Ładowanie wszystkich ofert
//                var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");
//                int initialOfferCount = offerNodes.Length;

//                // Sprawdź, czy liczba ofert wynosi 14 lub więcej
//                if (initialOfferCount >= 14)
//                {
//                    bool allOffersLoaded = false;

//                    while (!allOffersLoaded)
//                    {
//                        var showAllOffersButton = await _page.QuerySelectorAsync("span.show-remaining-offers__trigger.js_remainingTrigger");
//                        if (showAllOffersButton != null)
//                        {
//                            await showAllOffersButton.ClickAsync();
//                            await Task.Delay(2000); // Czas na załadowanie nowych ofert

//                            // Sprawdź ponownie, ile ofert zostało załadowanych
//                            offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");
//                            if (offerNodes.Length == initialOfferCount)
//                            {
//                                // Liczba ofert nie zmieniła się - problem w scrapowaniu
//                                log = $"Failed to load more offers for URL: {url}. Rejected.";
//                                rejectedProducts.Add(("Failed to load more offers", url));

//                                // Zakończ scrapowanie bez zapisywania wyników
//                                return (priceResults, log, rejectedProducts);
//                            }

//                            initialOfferCount = offerNodes.Length; // Zaktualizuj liczbę ofert
//                        }
//                        else
//                        {
//                            allOffersLoaded = true;
//                        }

//                        var scrollPosition = await _page.EvaluateFunctionAsync<int>("() => window.pageYOffset + window.innerHeight");
//                        var documentHeight = await _page.EvaluateFunctionAsync<int>("() => document.body.scrollHeight");
//                        if (scrollPosition >= documentHeight)
//                        {
//                            allOffersLoaded = true;
//                        }
//                    }
//                }

//                // Scrapowanie cen
//                var (prices, scrapeLog, scrapeRejectedProducts) = await ScrapePricesFromCurrentPage(url);
//                priceResults.AddRange(prices);
//                log = scrapeLog;
//                rejectedProducts.AddRange(scrapeRejectedProducts);
//            }
//            catch (Exception ex)
//            {
//                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
//                rejectedProducts.Add(($"Exception: {ex.Message}", url));
//            }

//            return (priceResults, log, rejectedProducts);
//        }

//        private async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> ScrapePricesFromCurrentPage(string url)
//        {
//            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
//            var rejectedProducts = new List<(string Reason, string Url)>();
//            string log;
//            int positionCounter = 1;

//            Console.WriteLine("Querying for offer nodes...");
//            var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");

//            if (offerNodes.Length > 0)
//            {
//                Console.WriteLine($"Found {offerNodes.Length} offer nodes.");
//                foreach (var offerNode in offerNodes)
//                {
//                    var parentList = await offerNode.EvaluateFunctionAsync<string>("el => el.closest('ul')?.className");
//                    if (!string.IsNullOrEmpty(parentList) && parentList.Contains("similar-offers"))
//                    {
//                        Console.WriteLine("Ignoring similar offer.");
//                        rejectedProducts.Add(("Similar offer detected", url));
//                        continue;
//                    }

//                    var storeName = await offerNode.QuerySelectorAsync("div.product-offer__store img") is ElementHandle imgElement
//                        ? await imgElement.EvaluateFunctionAsync<string>("el => el.alt")
//                        : null;

//                    if (string.IsNullOrWhiteSpace(storeName))
//                    {
//                        var storeLink = await offerNode.QuerySelectorAsync("li.offer-shop-opinions a.link.js_product-offer-link");
//                        if (storeLink != null)
//                        {
//                            var offerParameter = await storeLink.EvaluateFunctionAsync<string>("el => el.getAttribute('offer-parameter')");
//                            if (!string.IsNullOrEmpty(offerParameter))
//                            {
//                                var match = Regex.Match(offerParameter, @"sklepy/([^;]+);");
//                                if (match.Success)
//                                {
//                                    storeName = match.Groups[1].Value;
//                                    var hyphenIndex = storeName.LastIndexOf('-');
//                                    if (hyphenIndex > 0)
//                                    {
//                                        storeName = storeName.Substring(0, hyphenIndex);
//                                    }
//                                }
//                            }
//                        }
//                    }

//                    var priceNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.value");
//                    var pennyNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.penny");

//                    if (priceNode == null || pennyNode == null)
//                    {
//                        Console.WriteLine("Price node or penny node is null, skipping this offer.");
//                        rejectedProducts.Add(("Price node or penny node is null", url));
//                        continue;
//                    }

//                    var priceText = (await priceNode.EvaluateFunctionAsync<string>("el => el.innerText")).Trim() +
//                                    (await pennyNode.EvaluateFunctionAsync<string>("el => el.innerText")).Trim();
//                    var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

//                    if (!decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
//                    {
//                        Console.WriteLine("Failed to parse price, skipping this offer.");
//                        rejectedProducts.Add(("Failed to parse price", url));
//                        continue;
//                    }

//                    decimal? shippingCostNum = null;
//                    var shippingNode = await offerNode.QuerySelectorAsync("div.free-delivery-label") ??
//                                       await offerNode.QuerySelectorAsync("span.product-delivery-info.js_deliveryInfo");

//                    if (shippingNode != null)
//                    {
//                        var shippingText = await shippingNode.EvaluateFunctionAsync<string>("el => el.innerText");
//                        if (shippingText.Contains("Darmowa wysyłka") || shippingText.Contains("bezpłatna dostawa"))
//                        {
//                            shippingCostNum = 0.00m;
//                        }
//                        else
//                        {
//                            var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
//                            if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsedShippingCost))
//                            {
//                                shippingCostNum = parsedShippingCost;
//                            }
//                        }
//                    }

//                    int? availabilityNum = null;
//                    var availabilityNode = await offerNode.QuerySelectorAsync("span.instock") ??
//                                           await offerNode.QuerySelectorAsync("div.product-availability span");

//                    if (availabilityNode != null)
//                    {
//                        var availabilityText = await availabilityNode.EvaluateFunctionAsync<string>("el => el.innerText");
//                        if (availabilityText.Contains("Wysyłka w 1 dzień"))
//                        {
//                            availabilityNum = 1;
//                        }
//                        else if (availabilityText.Contains("Wysyłka do"))
//                        {
//                            var daysText = Regex.Match(availabilityText, @"\d+").Value;
//                            if (int.TryParse(daysText, out int parsedDays))
//                            {
//                                availabilityNum = parsedDays;
//                            }
//                        }
//                    }

//                    var offerContainer = await offerNode.QuerySelectorAsync(".product-offer__container");
//                    var offerType = await offerContainer?.EvaluateFunctionAsync<string>("el => el.getAttribute('data-offertype')");
//                    var isBidding = offerType?.Contains("Bid") == true ? "1" : "0";
//                    var position = positionCounter.ToString();
//                    positionCounter++;

//                    prices.Add((storeName, price, shippingCostNum, availabilityNum, isBidding, position));
//                    Console.WriteLine($"Added price: StoreName={storeName}, Price={price}, ShippingCost={shippingCostNum}, Availability={availabilityNum}, IsBidding={isBidding}, Position={position}");
//                }

//                log = $"Successfully scraped prices from URL: {url}";
//                Console.WriteLine(log);
//            }
//            else
//            {
//                log = $"Failed to find prices on URL: {url}";
//                Console.WriteLine(log);
//                rejectedProducts.Add(("No offer nodes found", url));
//            }

//            return (prices, log, rejectedProducts);
//        }

//    }

//}