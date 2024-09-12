using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PriceSafari.Models
{
    public class PlaywrightEngine
    {
        private IPlaywright _playwright;
        private IBrowser _browser;
        private IBrowserContext _context;
        private IPage _page;
        private readonly HttpClient _httpClient;

        public PlaywrightEngine(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task InitializeBrowserAsync(Settings settings)
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = settings.HeadLess,
                ExecutablePath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe", // Ścieżka do uruchomienia Chrome
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

            var randomUserAgent = GetRandomUserAgent();

            // Tworzymy nowy kontekst z opcją JavaScriptEnabled ustawioną na podstawie ustawień
            _context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = randomUserAgent,
                TimezoneId = "Europe/Warsaw",
                JavaScriptEnabled = settings.JavaScript // Ustawienie włączania lub wyłączania JavaScript
            });

            _page = await _context.NewPageAsync();

            await _page.EvaluateAsync(@"() => {
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
            await _page.SetViewportSizeAsync(randomResolution.width, randomResolution.height);

            await _page.RouteAsync("**/*", async route =>
            {
                var request = route.Request;
                if (!settings.Styles &&
                    (request.ResourceType == "image" ||
                     request.ResourceType == "stylesheet" ||
                     request.ResourceType == "font"))
                {
                    await route.AbortAsync();
                }
                else
                {
                    await route.ContinueAsync();
                }
            });

            Console.WriteLine($"Bot gotowy, teraz rozgrzewka przez {settings.WarmUpTime} sekund...");
            await Task.Delay(settings.WarmUpTime * 1000);
            Console.WriteLine("Rozgrzewka zakończona. Bot gotowy do scrapowania.");
        }

        private string GetRandomUserAgent()
        {
            var userAgentList = new List<string>
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.212 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.131 Safari/537.36",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/14.1.2 Safari/605.1.15",
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/88.0.4324.96 Safari/537.36"
            };

            var random = new Random();
            return userAgentList[random.Next(userAgentList.Count)];
        }

        public async Task CloseBrowserAsync()
        {
            await _page.CloseAsync();
            await _browser.CloseAsync();
        }

        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> PlaywrightScrapePricesAsync(string url)
        {
            var priceResults = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            string log;

            try
            {
                await _page.GotoAsync(url);
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

                var (mainPrices, scrapeLog, scrapeRejectedProducts) = await PlaywrightScrapePricesFromCurrentPage(url, true);
                priceResults.AddRange(mainPrices);
                log = scrapeLog;
                rejectedProducts.AddRange(scrapeRejectedProducts);

                if (totalOffersCount > 15)
                {
                    var sortedUrl = $"{url};0281-1.htm";
                    await _page.GotoAsync(sortedUrl);

                    var (sortedPrices, sortedLog, sortedRejectedProducts) = await PlaywrightScrapePricesFromCurrentPage(sortedUrl, false);
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
                        await _page.GotoAsync(nextSortedUrl);

                        var (nextSortedPrices, nextSortedLog, nextSortedRejectedProducts) = await PlaywrightScrapePricesFromCurrentPage(nextSortedUrl, false);
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

                    if (priceResults.Count < totalOffersCount)
                    {
                        var fastestDeliveryUrl = $"{url};0282-1;02516.htm";
                        await _page.GotoAsync(fastestDeliveryUrl);

                        var (fastestDeliveryPrices, fastestDeliveryLog, fastestDeliveryRejectedProducts) = await PlaywrightScrapePricesFromCurrentPage(fastestDeliveryUrl, false);
                        log += fastestDeliveryLog;
                        rejectedProducts.AddRange(fastestDeliveryRejectedProducts);

                        foreach (var fastestDeliveryPrice in fastestDeliveryPrices)
                        {
                            if (!priceResults.Any(p => p.storeName == fastestDeliveryPrice.storeName && p.price == fastestDeliveryPrice.price))
                            {
                                priceResults.Add(fastestDeliveryPrice);
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
                var textContent = await totalOffersText.EvaluateAsync<string>("el => el.innerText");
                var match = Regex.Match(textContent, @"\d+");
                if (match.Success)
                {
                    totalOffersCount = int.Parse(match.Value);
                }
            }
            return totalOffersCount;
        }

        private async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> PlaywrightScrapePricesFromCurrentPage(string url, bool includePosition)
        {
            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            var storeOffers = new Dictionary<string, (decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position)>();
            string log;
            int positionCounter = 1;

            Console.WriteLine("Querying for offer nodes...");
            var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");

            if (offerNodes.Count > 0)
            {
                Console.WriteLine($"Found {offerNodes.Count} offer nodes.");
                foreach (var offerNode in offerNodes)
                {
                    var parentList = await offerNode.EvaluateAsync<string>("el => el.closest('ul')?.className");
                    if (!string.IsNullOrEmpty(parentList) && parentList.Contains("similar-offers"))
                    {
                        Console.WriteLine("Ignoring similar offer.");
                        rejectedProducts.Add(("Similar offer detected", url));
                        continue;
                    }

                    var storeName = await GetStoreNameFromOfferNodeAsync(offerNode);
                    var priceValue = await GetPriceFromOfferNodeAsync(offerNode);
                    if (!priceValue.HasValue)
                    {
                        rejectedProducts.Add(("Failed to parse price", url));
                        continue;
                    }

                    decimal? shippingCostNum = await GetShippingCostFromOfferNodeAsync(offerNode);
                    int? availabilityNum = await GetAvailabilityFromOfferNodeAsync(offerNode);
                    var isBidding = await GetBiddingInfoFromOfferNodeAsync(offerNode);
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

        private async Task<string> GetStoreNameFromOfferNodeAsync(IElementHandle offerNode)
        {
            var storeName = await offerNode.QuerySelectorAsync("div.product-offer__store img") is IElementHandle imgElement
                ? await imgElement.EvaluateAsync<string>("el => el.alt")
                : null;

            if (string.IsNullOrWhiteSpace(storeName))
            {
                var storeLink = await offerNode.QuerySelectorAsync("li.offer-shop-opinions a.link.js_product-offer-link");
                if (storeLink != null)
                {
                    var offerParameter = await storeLink.EvaluateAsync<string>("el => el.getAttribute('offer-parameter')");
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

        private async Task<decimal?> GetPriceFromOfferNodeAsync(IElementHandle offerNode)
        {
            var priceNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.value");
            var pennyNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.penny");

            if (priceNode == null || pennyNode == null)
            {
                return null;
            }

            var priceText = (await priceNode.EvaluateAsync<string>("el => el.innerText")).Trim() +
                            (await pennyNode.EvaluateAsync<string>("el => el.innerText")).Trim();
            var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

            if (!decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
            {
                return null;
            }

            return price;
        }

        private async Task<decimal?> GetShippingCostFromOfferNodeAsync(IElementHandle offerNode)
        {
            var shippingNode = await offerNode.QuerySelectorAsync("div.free-delivery-label") ??
                               await offerNode.QuerySelectorAsync("span.product-delivery-info.js_deliveryInfo");

            if (shippingNode != null)
            {
                var shippingText = await shippingNode.EvaluateAsync<string>("el => el.innerText");
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

        private async Task<int?> GetAvailabilityFromOfferNodeAsync(IElementHandle offerNode)
        {
            var availabilityNode = await offerNode.QuerySelectorAsync("span.instock") ??
                                   await offerNode.QuerySelectorAsync("div.product-availability span");

            if (availabilityNode != null)
            {
                var availabilityText = await availabilityNode.EvaluateAsync<string>("el => el.innerText");
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

        private async Task<string> GetBiddingInfoFromOfferNodeAsync(IElementHandle offerNode)
        {
            var offerContainer = await offerNode.QuerySelectorAsync(".product-offer__container");
            var offerType = await offerContainer?.EvaluateAsync<string>("el => el.getAttribute('data-offertype')");
            return offerType?.Contains("Bid") == true ? "1" : "0";
        }
    }
}
