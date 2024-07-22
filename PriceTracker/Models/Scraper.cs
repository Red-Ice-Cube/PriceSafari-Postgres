using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace PriceTracker.Services
{
    public class Scraper
    {
        private readonly HttpClient _httpClient;
        private IBrowser _browser;
        private IPage _page;
        private List<(string Reason, string Url)> rejectedProducts = new List<(string Reason, string Url)>();

        public Scraper(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> GetProductPricesAsync(string url, int tryCount = 1)
        {
            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            string log;

            try
            {
                var response = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                if (response.Contains("/Captcha/Add"))
                {
                    (prices, log, rejectedProducts) = await HandleCaptchaAsync(url);
                    return (prices, log, rejectedProducts);
                }

                var offerNodes = doc.DocumentNode.SelectNodes("//li[contains(@class, 'product-offers__list__item')]");

                if (offerNodes != null)
                {
                    foreach (var offerNode in offerNodes)
                    {
                        var storeName = offerNode.SelectSingleNode(".//div[@class='product-offer__store']//img")?.GetAttributeValue("alt", "");
                        var priceNode = offerNode.SelectSingleNode(".//span[@class='price-format nowrap']//span[@class='price']//span[@class='value']");
                        var pennyNode = offerNode.SelectSingleNode(".//span[@class='price-format nowrap']//span[@class='price']//span[@class='penny']");
                        var shippingNode = offerNode.SelectSingleNode(".//div[contains(@class, 'free-delivery-label')]") ??
                                            offerNode.SelectSingleNode(".//span[contains(@class, 'product-delivery-info js_deliveryInfo')]");
                        var availabilityNode = offerNode.SelectSingleNode(".//span[contains(@class, 'instock')]") ??
                                               offerNode.SelectSingleNode(".//span[contains(text(), 'Wysyłka')]");

                        var offerContainer = offerNode.SelectSingleNode(".//div[contains(@class, 'product-offer__container')]");
                        var offerType = offerContainer?.GetAttributeValue("data-offertype", "");
                        var isBidding = (offerType == "CPC_Bid" || offerType == "CPC_Bid_Basket") ? "1" : "0";
                        var position = offerContainer?.GetAttributeValue("data-position", "");

                        decimal? shippingCostNum = null;
                        if (priceNode != null && pennyNode != null && !string.IsNullOrEmpty(storeName))
                        {
                            var priceText = priceNode.InnerText.Trim() + pennyNode.InnerText.Trim();
                            var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

                            if (decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
                            {
                                if (shippingNode != null)
                                {
                                    var shippingText = WebUtility.HtmlDecode(shippingNode.InnerText.Trim());

                                    if (shippingText.Contains("Darmowa wysyłka"))
                                    {
                                        shippingCostNum = 0;
                                    }
                                    else if (shippingText.Contains("szczegóły dostawy"))
                                    {
                                        shippingCostNum = null;
                                    }
                                    else
                                    {
                                        var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
                                        if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedShippingCost))
                                        {
                                            shippingCostNum = parsedShippingCost;
                                        }
                                        else
                                        {
                                            shippingCostNum = null;
                                        }
                                    }
                                }

                                int? availabilityNum = null;
                                if (availabilityNode != null)
                                {
                                    if (availabilityNode.InnerText.Contains("Wysyłka w 1 dzień"))
                                    {
                                        availabilityNum = 1;
                                    }
                                    else if (availabilityNode.InnerText.Contains("Wysyłka do"))
                                    {
                                        var daysText = Regex.Match(availabilityNode.InnerText, @"\d+").Value;
                                        if (int.TryParse(daysText, out var parsedDays))
                                        {
                                            availabilityNum = parsedDays;
                                        }
                                    }
                                }

                                prices.Add((storeName, price, shippingCostNum, availabilityNum, isBidding, position));
                            }
                        }
                        else
                        {
                            string reason = "Missing store name or price information.";
                            rejectedProducts.Add((reason, url));
                        }
                    }
                    log = $"Successfully scraped prices from URL: {url}";
                }
                else
                {
                    log = $"Failed to find prices on URL: {url}";
                    string reason = "No offer nodes found.";
                    rejectedProducts.Add((reason, url));
                }
            }
            catch (Exception ex)
            {
                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
                string reason = $"Exception: {ex.Message}";
                rejectedProducts.Add((reason, url));
            }

            return (prices, log, rejectedProducts);
        }







        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> HandleCaptchaAsync(string url)
        {
            var priceResults = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            string log;

            try
            {
                var playwright = await Playwright.CreateAsync();
                _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = false,
                    Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu" }
                });
                _page = await _browser.NewPageAsync();

                bool captchaSolved = await NavigateAndSolveCaptcha(url);

                if (captchaSolved)
                {
                    var (prices, scrapeLog, scrapeRejectedProducts) = await ScrapePricesFromPage(url);
                    priceResults.AddRange(prices);
                    log = scrapeLog;
                    rejectedProducts.AddRange(scrapeRejectedProducts);
                }
                else
                {
                    log = $"Failed to solve CAPTCHA for URL: {url}";
                    rejectedProducts.Add(("CAPTCHA not solved", url));
                }

                await _browser.CloseAsync();
            }
            catch (Exception ex)
            {
                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
                rejectedProducts.Add(($"Exception: {ex.Message}", url));
            }

            return (priceResults, log, rejectedProducts);
        }

        private async Task<bool> NavigateAndSolveCaptcha(string url)
        {
            try
            {
                Console.WriteLine("Navigating to the target URL...");
                await _page.GotoAsync(url);

                var currentUrl = _page.Url;
                if (currentUrl.Contains("/Captcha/Add"))
                {
                    Console.WriteLine("CAPTCHA detected, navigating to error page...");
                    await _page.GotoAsync("https://www.ceneo.pl/777");

                    Console.WriteLine("Navigating back to the target URL...");
                    await _page.GotoAsync(url);

                    currentUrl = _page.Url;
                    if (currentUrl.Contains("/Captcha/Add"))
                    {
                        return false;
                    }
                }

                var rejectButton = await _page.WaitForSelectorAsync("button.cookie-consent__buttons__action.js_cookie-consent-necessary[data-role='reject-rodo']", new PageWaitForSelectorOptions { Timeout = 5000 });
                if (rejectButton != null)
                {
                    await rejectButton.ClickAsync();
                    Console.WriteLine("Clicked 'Nie zgadzam się' button.");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error navigating through CAPTCHA: " + ex.Message);
                return false;
            }
        }

        private async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> ScrapePricesFromPage(string url)
        {
            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            string log;

            Console.WriteLine("Navigating to the target URL...");
            await _page.GotoAsync(url);

            Console.WriteLine("Querying for offer nodes...");
            var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");

            if (offerNodes.Count > 0)
            {
                Console.WriteLine($"Found {offerNodes.Count} offer nodes.");
                foreach (var offerNode in offerNodes)
                {
                    var storeName = await (await offerNode.QuerySelectorAsync("div.product-offer__store img"))?.GetAttributeAsync("alt");
                    var priceNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.value");
                    var pennyNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.penny");
                    var shippingNode = await offerNode.QuerySelectorAsync("div.free-delivery-label") ??
                                       await offerNode.QuerySelectorAsync("span.product-delivery-info.js_deliveryInfo");
                    var availabilityNode = await offerNode.QuerySelectorAsync("span.instock") ??
                                           await offerNode.QuerySelectorAsync("span.product-delivery-info");

                    var offerContainer = await offerNode.QuerySelectorAsync(".product-offer__container");
                    var offerType = await offerContainer?.GetAttributeAsync("data-offertype");
                    var isBidding = (offerType == "CPC_Bid" || offerType == "CPC_Bid_Basket") ? "1" : "0";
                    var position = await offerContainer?.GetAttributeAsync("data-position");

                    if (storeName == null)
                    {
                        rejectedProducts.Add(("Store name is null", url));
                        continue;
                    }

                    if (priceNode == null || pennyNode == null)
                    {
                        rejectedProducts.Add(("Price node or penny node is null", url));
                        continue;
                    }

                    decimal? shippingCostNum = null;
                    var priceText = (await priceNode.InnerTextAsync()).Trim() + (await pennyNode.InnerTextAsync()).Trim();
                    var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

                    if (decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
                    {
                        if (shippingNode != null)
                        {
                            var shippingText = await shippingNode.InnerTextAsync();
                            if (shippingText.Contains("Darmowa wysyłka"))
                            {
                                shippingCostNum = 0;
                            }
                            else
                            {
                                var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
                                if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedShippingCost))
                                {
                                    shippingCostNum = parsedShippingCost;
                                }
                                else
                                {
                                    shippingCostNum = null;
                                }
                            }
                        }

                        int? availabilityNum = null;
                        if (availabilityNode != null)
                        {
                            var availabilityText = await availabilityNode.InnerTextAsync();
                            if (availabilityText.Contains("Wysyłka w 1 dzień"))
                            {
                                availabilityNum = 1;
                            }
                            else if (availabilityText.Contains("Wysyłka do"))
                            {
                                var daysText = Regex.Match(availabilityText, @"\d+").Value;
                                if (int.TryParse(daysText, out var parsedDays))
                                {
                                    availabilityNum = parsedDays;
                                }
                            }
                        }

                        prices.Add((storeName, price, shippingCostNum, availabilityNum, isBidding, position));
                    }
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





//using HtmlAgilityPack;
//using PuppeteerSharp;
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Net;
//using System.Net.Http;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//namespace PriceTracker.Services
//{
//    public class Scraper
//    {
//        private readonly HttpClient _httpClient;
//        //private const string ChromeExecutablePath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
//        private const string ChromeExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
//        private List<(string Reason, string Url)> rejectedProducts = new List<(string Reason, string Url)>();

//        public Scraper(HttpClient httpClient)
//        {
//            _httpClient = httpClient;
//        }

//        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> GetProductPricesAsync(string url, int tryCount = 1)
//        {
//            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
//            var rejectedProducts = new List<(string Reason, string Url)>();
//            string log;

//            try
//            {
//                var response = await _httpClient.GetStringAsync(url);
//                var doc = new HtmlDocument();
//                doc.LoadHtml(response);

//                if (response.Contains("/Captcha/Add"))
//                {
//                    (prices, log, rejectedProducts) = await HandleCaptchaAsync(url);
//                    return (prices, log, rejectedProducts);
//                }

//                var offerNodes = doc.DocumentNode.SelectNodes("//li[contains(@class, 'product-offers__list__item')]");

//                if (offerNodes != null)
//                {
//                    foreach (var offerNode in offerNodes)
//                    {
//                        var storeName = offerNode.SelectSingleNode(".//div[@class='product-offer__store']//img")?.GetAttributeValue("alt", "");
//                        var priceNode = offerNode.SelectSingleNode(".//span[@class='price-format nowrap']//span[@class='price']//span[@class='value']");
//                        var pennyNode = offerNode.SelectSingleNode(".//span[@class='price-format nowrap']//span[@class='price']//span[@class='penny']");
//                        var shippingNode = offerNode.SelectSingleNode(".//div[contains(@class, 'free-delivery-label')]") ??
//                                            offerNode.SelectSingleNode(".//span[contains(@class, 'product-delivery-info js_deliveryInfo')]");
//                        var availabilityNode = offerNode.SelectSingleNode(".//span[contains(@class, 'instock')]") ??
//                                               offerNode.SelectSingleNode(".//span[contains(text(), 'Wysyłka')]");

//                        var offerContainer = offerNode.SelectSingleNode(".//div[contains(@class, 'product-offer__container')]");
//                        var offerType = offerContainer?.GetAttributeValue("data-offertype", "");
//                        var isBidding = (offerType == "CPC_Bid" || offerType == "CPC_Bid_Basket") ? "1" : "0";
//                        var position = offerContainer?.GetAttributeValue("data-position", "");

//                        Console.WriteLine($"storeName: {storeName}");
//                        Console.WriteLine($"priceNode: {priceNode?.InnerText}");
//                        Console.WriteLine($"pennyNode: {pennyNode?.InnerText}");
//                        Console.WriteLine($"shippingNode: {shippingNode?.InnerText}");
//                        Console.WriteLine($"availabilityNode: {availabilityNode?.InnerText}");
//                        Console.WriteLine($"isBidding: {isBidding}");
//                        Console.WriteLine($"position: {position}");

//                        decimal? shippingCostNum = null;
//                        if (priceNode != null && pennyNode != null && !string.IsNullOrEmpty(storeName))
//                        {
//                            var priceText = priceNode.InnerText.Trim() + pennyNode.InnerText.Trim();
//                            var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

//                            if (decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
//                            {
//                                if (shippingNode != null)
//                                {
//                                    var shippingText = WebUtility.HtmlDecode(shippingNode.InnerText.Trim());
//                                    Console.WriteLine($"Shipping text found: {shippingText}");  

//                                    if (shippingText.Contains("Darmowa wysyłka"))
//                                    {
//                                        shippingCostNum = 0;
//                                        Console.WriteLine("Shipping cost set to 0 due to 'Darmowa wysyłka'");
//                                    }
//                                    else if (shippingText.Contains("szczegóły dostawy"))
//                                    {
//                                        shippingCostNum = null;
//                                        Console.WriteLine("Shipping cost set to null due to 'szczegóły dostawy'");
//                                    }
//                                    else
//                                    {
//                                        var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
//                                        if (!string.IsNullOrEmpty(shippingCostText))
//                                        {
//                                            Console.WriteLine($"Shipping cost found: {shippingCostText}");  
//                                            if (decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedShippingCost))
//                                            {
//                                                shippingCostNum = parsedShippingCost;
//                                                Console.WriteLine($"Parsed shipping cost: {shippingCostNum}");  
//                                            }
//                                            else
//                                            {
//                                                shippingCostNum = null;
//                                                Console.WriteLine("Shipping cost set to null due to parsing failure");
//                                            }
//                                        }
//                                        else
//                                        {
//                                            shippingCostNum = null;
//                                            Console.WriteLine("Shipping cost set to null due to empty cost text");
//                                        }
//                                    }
//                                }

//                                int? availabilityNum = null;
//                                if (availabilityNode != null)
//                                {
//                                    if (availabilityNode.InnerText.Contains("Wysyłka w 1 dzień"))
//                                    {
//                                        availabilityNum = 1;
//                                    }
//                                    else if (availabilityNode.InnerText.Contains("Wysyłka do"))
//                                    {
//                                        var daysText = Regex.Match(availabilityNode.InnerText, @"\d+").Value;
//                                        if (int.TryParse(daysText, out var parsedDays))
//                                        {
//                                            availabilityNum = parsedDays;
//                                        }
//                                    }
//                                }

//                                prices.Add((storeName, price, shippingCostNum, availabilityNum, isBidding, position));
//                            }
//                        }
//                        else
//                        {
//                            string reason = "Missing store name or price information.";
//                            rejectedProducts.Add((reason, url));
//                        }
//                    }
//                    log = $"Successfully scraped prices from URL: {url}";
//                }
//                else
//                {
//                    log = $"Failed to find prices on URL: {url}";
//                    string reason = "No offer nodes found.";
//                    rejectedProducts.Add((reason, url));
//                }
//            }
//            catch (Exception ex)
//            {
//                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
//                string reason = $"Exception: {ex.Message}";
//                rejectedProducts.Add((reason, url));
//            }

//            return (prices, log, rejectedProducts);
//        }


//        private async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> HandleCaptchaAsync(string url)
//        {
//            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
//            var rejectedProducts = new List<(string Reason, string Url)>();
//            string log;

//            try
//            {
//                Console.WriteLine("Launching browser...");
//                var browser = await Puppeteer.LaunchAsync(new LaunchOptions
//                {
//                    Headless = true,
//                    ExecutablePath = ChromeExecutablePath,
//                    Args = new string[]
//                    {
//                "--no-sandbox",
//                "--disable-setuid-sandbox",
//                "--disable-extensions",
//                "--disable-gpu",
//                "--disable-dev-shm-usage",
//                "--disable-software-rasterizer",
//                "--disable-features=site-per-process",
//                "--disable-features=VizDisplayCompositor",
//                "--disable-blink-features=AutomationControlled"
//                    }
//                });

//                var page = await browser.NewPageAsync();

//                await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

//                await page.SetViewportAsync(new ViewPortOptions
//                {
//                    Width = 1366,
//                    Height = 768
//                });

//                Console.WriteLine("Navigating to CAPTCHA page...");
//                await page.GoToAsync("https://www.ceneo.pl/24ado");
//                await Task.Delay(5000);

//                Console.WriteLine("Navigating back to the target URL...");
//                await page.GoToAsync(url);

//                Console.WriteLine("Waiting for page to load...");
//                await Task.Delay(5000);

//                try
//                {
//                    var rejectButton = await page.QuerySelectorAsync("button.cookie-consent__buttons__action.js_cookie-consent-necessary[data-role='reject-rodo']");
//                    if (rejectButton != null)
//                    {
//                        await rejectButton.ClickAsync();
//                        Console.WriteLine("Clicked 'Nie zgadzam się' button.");
//                    }
//                    else
//                    {
//                        Console.WriteLine("'Nie zgadzam się' button not found.");
//                    }
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine("Could not find or click 'Nie zgadzam się' button: " + ex.Message);
//                }

//                Console.WriteLine("Querying for offer nodes...");
//                var offerNodes = await page.QuerySelectorAllAsync("li.product-offers__list__item");

//                if (offerNodes.Length > 0)
//                {
//                    Console.WriteLine($"Found {offerNodes.Length} offer nodes.");
//                    foreach (var offerNode in offerNodes)
//                    {
//                        var storeName = await (await offerNode.QuerySelectorAsync("div.product-offer__store img"))?.EvaluateFunctionAsync<string>("el => el.alt");
//                        var priceNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.value");
//                        var pennyNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.penny");
//                        var shippingNode = await offerNode.QuerySelectorAsync("div.free-delivery-label") ??
//                                           await offerNode.QuerySelectorAsync("span.product-delivery-info.js_deliveryInfo");
//                        var availabilityNode = await offerNode.QuerySelectorAsync("span.instock") ??
//                                               (await offerNode.XPathAsync(".//span[contains(text(), 'Wysyłka')]")).FirstOrDefault();

//                        var offerContainer = await offerNode.QuerySelectorAsync(".product-offer__container");
//                        var offerType = await (await offerContainer.GetPropertyAsync("data-offertype"))?.JsonValueAsync<string>();
//                        var isBidding = (offerType == "CPC_Bid" || offerType == "CPC_Bid_Basket") ? "1" : "0";
//                        var position = await (await offerContainer.GetPropertyAsync("data-position"))?.JsonValueAsync<string>();

//                        Console.WriteLine($"Processing offer from store: {storeName}");
//                        if (storeName == null)
//                        {
//                            Console.WriteLine("Store name is null. Skipping this offer.");
//                            rejectedProducts.Add(("Store name is null", url));
//                            continue;
//                        }

//                        if (priceNode == null || pennyNode == null)
//                        {
//                            Console.WriteLine("Price node or penny node is null. Skipping this offer.");
//                            rejectedProducts.Add(("Price node or penny node is null", url));
//                            continue;
//                        }

//                        decimal? shippingCostNum = null;
//                        var priceText = (await priceNode.EvaluateFunctionAsync<string>("el => el.textContent")).Trim() +
//                                        (await pennyNode.EvaluateFunctionAsync<string>("el => el.textContent")).Trim();
//                        var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

//                        Console.WriteLine($"Parsed price value: {priceValue}");
//                        if (decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
//                        {
//                            if (shippingNode != null)
//                            {
//                                var shippingText = await shippingNode.EvaluateFunctionAsync<string>("el => el.textContent");
//                                if (shippingText.Contains("Darmowa wysyłka"))
//                                {
//                                    shippingCostNum = 0;
//                                }
//                                else
//                                {
//                                    var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
//                                    if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedShippingCost))
//                                    {
//                                        var shippingCostDifference = parsedShippingCost - price;
//                                        if (shippingCostDifference > 0)
//                                        {
//                                            shippingCostNum = shippingCostDifference;
//                                        }
//                                        else
//                                        {
//                                            shippingCostNum = null;
//                                        }
//                                    }
//                                    else
//                                    {
//                                        shippingCostNum = null;
//                                    }
//                                }
//                            }

//                            int? availabilityNum = null;
//                            if (availabilityNode != null)
//                            {
//                                var availabilityText = await availabilityNode.EvaluateFunctionAsync<string>("el => el.textContent");
//                                if (availabilityText.Contains("Wysyłka w 1 dzień"))
//                                {
//                                    availabilityNum = 1;
//                                }
//                                else if (availabilityText.Contains("Wysyłka do"))
//                                {
//                                    var daysText = Regex.Match(availabilityText, @"\d+").Value;
//                                    if (int.TryParse(daysText, out var parsedDays))
//                                    {
//                                        availabilityNum = parsedDays;
//                                    }
//                                }
//                            }

//                            prices.Add((storeName, price, shippingCostNum, availabilityNum, isBidding, position));
//                        }
//                    }
//                    log = $"Successfully scraped prices from URL: {url}";
//                    Console.WriteLine(log);
//                }
//                else
//                {
//                    log = $"Failed to find prices on URL: {url}";
//                    Console.WriteLine(log);
//                    rejectedProducts.Add(("No offer nodes found", url));
//                }

//                await browser.CloseAsync();
//            }
//            catch (Exception ex)
//            {
//                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
//                Console.WriteLine(log);
//                rejectedProducts.Add(($"Exception: {ex.Message}", url));
//            }

//            return (prices, log, rejectedProducts);
//        }



//    }
//}

