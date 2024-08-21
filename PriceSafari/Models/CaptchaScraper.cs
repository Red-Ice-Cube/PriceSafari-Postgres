using PuppeteerSharp;
using System.Diagnostics;
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
                    "--disable-prompt-on-repost"
                }
            });

            _page = (Page)await _browser.NewPageAsync();
            await _page.SetViewportAsync(new ViewPortOptions { Width = 600, Height = 450 });

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
                if (currentUrl.Contains("/Captcha/Add"))
                {
                    bool captchaBypassed = false;

                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        await _page.GoToAsync("https://www.ceneo.pl/859");
                        await Task.Delay(1000);
                        await _page.GoToAsync(url);

                        currentUrl = _page.Url;
                        if (!currentUrl.Contains("/Captcha/Add"))
                        {
                            captchaBypassed = true;
                            break;
                        }
                    }

                    if (!captchaBypassed)
                    {
                        log = $"Failed to solve CAPTCHA for URL: {url}";
                        rejectedProducts.Add(("CAPTCHA not solved", url));
                        return (priceResults, log, rejectedProducts);
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

                // Scrapowanie cen
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

            var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");

            if (offerNodes.Length > 0)
            {
                var scrapeTasks = offerNodes.Select(async offerNode =>
                {
                    var parentList = await offerNode.EvaluateFunctionAsync<string>("el => el.closest('ul')?.className");
                    if (!string.IsNullOrEmpty(parentList) && parentList.Contains("similar-offers"))
                    {
                        rejectedProducts.Add(("Similar offer detected", url));
                        return;
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
                        rejectedProducts.Add(("Price node or penny node is null", url));
                        return;
                    }

                    var priceText = (await priceNode.EvaluateFunctionAsync<string>("el => el.innerText")).Trim() +
                                    (await pennyNode.EvaluateFunctionAsync<string>("el => el.innerText")).Trim();
                    var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

                    if (!decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
                    {
                        rejectedProducts.Add(("Failed to parse price", url));
                        return;
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
                });

                await Task.WhenAll(scrapeTasks);

                log = $"Successfully scraped prices from URL: {url}";
            }
            else
            {
                log = $"Failed to find prices on URL: {url}";
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


//            _page.Request += async (sender, e) =>
//            {

//                if (e.Request.ResourceType == ResourceType.Image ||
//                    e.Request.ResourceType == ResourceType.StyleSheet ||
//                    e.Request.ResourceType == ResourceType.Font)
//                {
//                    await e.Request.AbortAsync();
//                }
//                else
//                {
//                    await e.Request.ContinueAsync();
//                }
//            };
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
//                await _page.GoToAsync(url);
//                var currentUrl = _page.Url;

//                if (currentUrl.Contains("/Captcha/Add"))
//                {
//                    Console.WriteLine("CAPTCHA detected, navigating to error page...");
//                    await _page.GoToAsync("https://www.ceneo.pl/859");

//                    Console.WriteLine("Navigating back to the target URL...");
//                    await _page.ReloadAsync();

//                    currentUrl = _page.Url;
//                    if (currentUrl.Contains("/Captcha/Add"))
//                    {
//                        log = $"Failed to solve CAPTCHA for URL: {url}";
//                        rejectedProducts.Add(("CAPTCHA not solved", url));
//                        return (priceResults, log, rejectedProducts);
//                    }
//                }

//                var rejectButton = await _page.QuerySelectorAsync("button.cookie-consent__buttons__action.js_cookie-consent-necessary[data-role='reject-rodo']");
//                if (rejectButton != null)
//                {
//                    await rejectButton.ClickAsync();
//                    Console.WriteLine("Clicked 'Nie zgadzam się' button.");
//                }

//                var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");

//                if (offerNodes.Length == 15)
//                {
//                    bool allOffersLoaded = false;

//                    while (!allOffersLoaded)
//                    {
//                        await _page.EvaluateExpressionAsync("window.scrollBy(0, document.body.scrollHeight)");
//                        await Task.Delay(500);

//                        var showAllOffersButton = await _page.QuerySelectorAsync("span.show-remaining-offers__trigger.js_remainingTrigger");
//                        if (showAllOffersButton != null)
//                        {
//                            Console.WriteLine("Found 'Pokaż wszystkie oferty' button, clicking...");
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


