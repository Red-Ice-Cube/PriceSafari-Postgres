//using Microsoft.EntityFrameworkCore;
//using Microsoft.Playwright;
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
//            "--no-sandbox",
//            "--disable-setuid-sandbox",
//            "--disable-gpu",
//            "--disable-blink-features=AutomationControlled" ,
//             "--enable-http2"
//        }
//            };

//            if (browserType == "chromium")
//            {
//                launchOptions.ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
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
//                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.131 Safari/537.36",
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
//            await Task.Delay(TimeSpan.FromMinutes(1));
//            Console.WriteLine("Rozgrzewka zakończona. Rozpoczynamy scrapowanie.");
//        }


//        private readonly List<string> _userAgents = new List<string>
//        {
//            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.131 Safari/537.36",
//            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:90.0) Gecko/20100101 Firefox/90.0",
//            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.1.2 Safari/605.1.15"
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


//        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> HandleCaptchaAndScrapePricesAsync(string url)
//        {
//            var priceResults = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
//            var rejectedProducts = new List<(string Reason, string Url)>();
//            string log;

//            try
//            {
//                // Przejdź do strony
//                await _page.GotoAsync(url);
//                var currentUrl = _page.Url;


//                while (currentUrl.Contains("/Captcha/Add"))
//                {
//                    log = $"CAPTCHA detected for URL: {url}. Waiting for manual solution...";
//                    Console.WriteLine(log);

//                    // Czekaj, aż użytkownik ręcznie rozwiąże CAPTCHA
//                    while (currentUrl.Contains("/Captcha/Add"))
//                    {
//                        await Task.Delay(2000); // Czekaj 2 sekundy przed kolejnym sprawdzeniem
//                        currentUrl = _page.Url; // Aktualizuj URL, aby sprawdzić, czy CAPTCHA zostało rozwiązane
//                    }

//                    log = $"CAPTCHA solved for URL: {url}";
//                    Console.WriteLine(log);
//                }


//                var rejectButton = await _page.QuerySelectorAsync("button.cookie-consent__buttons__action.js_cookie-consent-necessary[data-role='reject-rodo']");
//                if (rejectButton != null)
//                {
//                    await rejectButton.ClickAsync();
//                }


//                var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");
//                int initialOfferCount = offerNodes.Count;

//                if (initialOfferCount == 15)
//                {
//                    bool allOffersLoaded = false;

//                    while (!allOffersLoaded)
//                    {
//                        await _page.EvaluateAsync("window.scrollBy(0, document.body.scrollHeight)");
//                        await Task.Delay(600);

//                        var showAllOffersButton = await _page.QuerySelectorAsync("span.show-remaining-offers__trigger.js_remainingTrigger");
//                        if (showAllOffersButton != null)
//                        {
//                            await showAllOffersButton.ClickAsync();
//                            await Task.Delay(2000); // Czas na załadowanie ofert
//                            offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");
//                        }
//                        else
//                        {
//                            allOffersLoaded = true;
//                        }

//                        if (offerNodes.Count > 15)
//                        {
//                            allOffersLoaded = true;
//                        }
//                        else if (offerNodes.Count == 15)
//                        {
//                            // Jeśli po kliknięciu liczba ofert nadal wynosi 15, oznacz URL jako odrzucony
//                            var coOfr = new CoOfrClass
//                            {
//                                OfferUrl = url,
//                                IsScraped = true,
//                                IsRejected = true,
//                                ScrapingMethod = "HandleCaptcha",
//                                PricesCount = 0
//                            };

//                            log = $"URL rejected after failing to load more than 15 offers: {url}";
//                            rejectedProducts.Add(("Failed to load more than 15 offers", url));

//                            // Zakończ scrapowanie bez zapisywania wyników
//                            return (priceResults, log, rejectedProducts);
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



using PuppeteerSharp;
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


        public async Task InitializeBrowserAsync()
        {
            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            // Uruchomienie przeglądarki z wyłączonymi elementami, które mogą wykryć automatyzację
            _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = false,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-gpu",
                    "--disable-background-timer-throttling",
                    "--disable-backgrounding-occluded-windows",
                    "--mute-audio",
                    "--disable-software-rasterizer",
                    "--disable-extensions",
                    "--disable-background-networking",
                    "--disable-sync",
                    "--disable-translate",
                    "--disable-background-timer-throttling",
                    "--disable-renderer-backgrounding",
                    "--disable-device-discovery-notifications",
                    "--disable-default-apps",
                    "--no-default-browser-check",
                    "--no-first-run",
                    "--disable-hang-monitor",
                    "--disable-prompt-on-repost",
                    "--disable-blink-features=AutomationControlled" // Ukrywa automatyzację
                }
            });

            _page = (Page)await _browser.NewPageAsync();

            // Wstrzyknięcie skryptu na nowo otwartych stronach
            await _page.EvaluateFunctionAsync(@"() => {
                Object.defineProperty(navigator, 'webdriver', {
                    get: () => false,
                });
                Object.defineProperty(navigator, 'languages', {
                    get: () => ['pl-PL', 'pl'],
                });
                Object.defineProperty(navigator, 'plugins', {
                    get: () => [1, 2, 3],
                });
            }");

            // Ustawienie widoku
            await _page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 800 });

            await _page.SetRequestInterceptionAsync(true);

            _page.Request += async (sender, e) =>
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
            };

            await _page.SetExtraHttpHeadersAsync(new Dictionary<string, string>
            {
                { "Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7" }
            });
            await _page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.131 Safari/537.36");

            // Ustawienie strefy czasowej na Warszawę i geolokalizacji na Polskę
            await _page.EmulateTimezoneAsync("Europe/Warsaw");

            // Funkcja rozgrzewki - czekanie 3 minuty
            Console.WriteLine("Rozgrzewka bota. Czekam 1 minuty...");
            await Task.Delay(TimeSpan.FromMinutes(1));  // Czekanie 3 minut
            Console.WriteLine("Rozgrzewka zakończona. Rozpoczynamy scrapowanie.");
        }

        public async Task CloseBrowserAsync()
        {
            await _page.CloseAsync();
            await _browser.CloseAsync();
        }
    



public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> HandleCaptchaAndScrapePricesAsync(string url)
        {
            var priceResults = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            string log;

            try
            {
                // Przejdź do strony
                await _page.GoToAsync(url);
                var currentUrl = _page.Url;

                // Sprawdź, czy przeglądarka została przekierowana na stronę z CAPTCHA
                while (currentUrl.Contains("/Captcha/Add"))
                {
                    Console.WriteLine("Captcha detected. Please solve it manually in the browser.");

                    // Czekaj, aż użytkownik ręcznie rozwiąże CAPTCHA
                    while (currentUrl.Contains("/Captcha/Add"))
                    {
                        await Task.Delay(2000); // Czekaj 2 sekundy przed kolejnym sprawdzeniem
                        currentUrl = _page.Url; // Aktualizuj URL, aby sprawdzić, czy CAPTCHA zostało rozwiązane
                    }
                }

                // Jeśli pojawi się baner cookie, kliknij "Nie zgadzam się"
                var rejectButton = await _page.QuerySelectorAsync("button.cookie-consent__buttons__action.js_cookie-consent-necessary[data-role='reject-rodo']");
                if (rejectButton != null)
                {
                    await rejectButton.ClickAsync();
                }

                // Ładowanie wszystkich ofert
                var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");

                if (offerNodes.Length == 15)
                {
                    bool allOffersLoaded = false;

                    while (!allOffersLoaded)
                    {
                        await _page.EvaluateExpressionAsync("window.scrollBy(0, document.body.scrollHeight)");
                        await Task.Delay(600);

                        var showAllOffersButton = await _page.QuerySelectorAsync("span.show-remaining-offers__trigger.js_remainingTrigger");
                        if (showAllOffersButton != null)
                        {
                            await showAllOffersButton.ClickAsync();
                            await _page.WaitForSelectorAsync("li.product-offers__list__item", new WaitForSelectorOptions { Visible = true });
                        }
                        else
                        {
                            allOffersLoaded = true;
                        }

                        var scrollPosition = await _page.EvaluateFunctionAsync<int>("() => window.pageYOffset + window.innerHeight");
                        var documentHeight = await _page.EvaluateFunctionAsync<int>("() => document.body.scrollHeight");
                        if (scrollPosition >= documentHeight)
                        {
                            allOffersLoaded = true;
                        }
                    }
                }

               
                var (prices, scrapeLog, scrapeRejectedProducts) = await ScrapePricesFromCurrentPage(url);
                priceResults.AddRange(prices);
                log = scrapeLog;
                rejectedProducts.AddRange(scrapeRejectedProducts);
            }
            catch (Exception ex)
            {
                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
                rejectedProducts.Add(($"Exception: {ex.Message}", url));
            }

            return (priceResults, log, rejectedProducts);
        }



        private async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> ScrapePricesFromCurrentPage(string url)
        {
            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
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

                    var priceNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.value");
                    var pennyNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.penny");

                    if (priceNode == null || pennyNode == null)
                    {
                        Console.WriteLine("Price node or penny node is null, skipping this offer.");
                        rejectedProducts.Add(("Price node or penny node is null", url));
                        continue;
                    }

                    var priceText = (await priceNode.EvaluateFunctionAsync<string>("el => el.innerText")).Trim() +
                                    (await pennyNode.EvaluateFunctionAsync<string>("el => el.innerText")).Trim();
                    var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

                    if (!decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
                    {
                        Console.WriteLine("Failed to parse price, skipping this offer.");
                        rejectedProducts.Add(("Failed to parse price", url));
                        continue;
                    }

                    decimal? shippingCostNum = null;
                    var shippingNode = await offerNode.QuerySelectorAsync("div.free-delivery-label") ??
                                       await offerNode.QuerySelectorAsync("span.product-delivery-info.js_deliveryInfo");

                    if (shippingNode != null)
                    {
                        var shippingText = await shippingNode.EvaluateFunctionAsync<string>("el => el.innerText");
                        if (shippingText.Contains("Darmowa wysyłka") || shippingText.Contains("bezpłatna dostawa"))
                        {
                            shippingCostNum = 0.00m;
                        }
                        else
                        {
                            var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
                            if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsedShippingCost))
                            {
                                shippingCostNum = parsedShippingCost;
                            }
                        }
                    }

                    int? availabilityNum = null;
                    var availabilityNode = await offerNode.QuerySelectorAsync("span.instock") ??
                                           await offerNode.QuerySelectorAsync("div.product-availability span");

                    if (availabilityNode != null)
                    {
                        var availabilityText = await availabilityNode.EvaluateFunctionAsync<string>("el => el.innerText");
                        if (availabilityText.Contains("Wysyłka w 1 dzień"))
                        {
                            availabilityNum = 1;
                        }
                        else if (availabilityText.Contains("Wysyłka do"))
                        {
                            var daysText = Regex.Match(availabilityText, @"\d+").Value;
                            if (int.TryParse(daysText, out int parsedDays))
                            {
                                availabilityNum = parsedDays;
                            }
                        }
                    }

                    var offerContainer = await offerNode.QuerySelectorAsync(".product-offer__container");
                    var offerType = await offerContainer?.EvaluateFunctionAsync<string>("el => el.getAttribute('data-offertype')");
                    var isBidding = offerType?.Contains("Bid") == true ? "1" : "0";
                    var position = positionCounter.ToString();
                    positionCounter++;

                    prices.Add((storeName, price, shippingCostNum, availabilityNum, isBidding, position));
                    Console.WriteLine($"Added price: StoreName={storeName}, Price={price}, ShippingCost={shippingCostNum}, Availability={availabilityNum}, IsBidding={isBidding}, Position={position}");
                }

                log = $"Successfully scraped prices from URL: {url}";
                Console.WriteLine(log);
            }
            else
            {
                log = $"Failed to find prices on URL: {url}";
                Console.WriteLine(log);
                rejectedProducts.Add(("No offer nodes found", url));
            }

            return (prices, log, rejectedProducts);
        }



    }

}

















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
//                    "--disable-prompt-on-repost"
//                }
//            });

//            _page = (Page)await _browser.NewPageAsync();
//            await _page.SetViewportAsync(new ViewPortOptions { Width = 600, Height = 450 });


//            await _page.SetRequestInterceptionAsync(true);


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
//                if (currentUrl.Contains("/Captcha/Add"))
//                {
//                    bool captchaBypassed = false;

//                    for (int attempt = 0; attempt < 2; attempt++)
//                    {
//                        await _page.GoToAsync("https://www.ceneo.pl/859");
//                        await Task.Delay(1000);
//                        await _page.GoToAsync(url);

//                        currentUrl = _page.Url;
//                        if (!currentUrl.Contains("/Captcha/Add"))
//                        {
//                            captchaBypassed = true;
//                            break;
//                        }
//                    }

//                    if (!captchaBypassed)
//                    {
//                        log = $"Failed to solve CAPTCHA for URL: {url}";
//                        rejectedProducts.Add(("CAPTCHA not solved", url));
//                        return (priceResults, log, rejectedProducts);
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

//                if (offerNodes.Length == 15)
//                {
//                    bool allOffersLoaded = false;

//                    while (!allOffersLoaded)
//                    {
//                        await _page.EvaluateExpressionAsync("window.scrollBy(0, document.body.scrollHeight)");
//                        await Task.Delay(600);

//                        var showAllOffersButton = await _page.QuerySelectorAsync("span.show-remaining-offers__trigger.js_remainingTrigger");
//                        if (showAllOffersButton != null)
//                        {
//                            await showAllOffersButton.ClickAsync();
//                            await _page.WaitForSelectorAsync("li.product-offers__list__item", new WaitForSelectorOptions { Visible = true });
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

