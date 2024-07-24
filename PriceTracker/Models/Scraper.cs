using HtmlAgilityPack;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PriceTracker.Services
{
    public class Scraper
    {
        private readonly HttpClient _httpClient;
        private IBrowser _browser;
        private IPage _page;

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
                    return (prices, "CAPTCHA encountered.", new List<(string Reason, string Url)> { ("CAPTCHA", url) });
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




        public async Task InitializeBrowserAsync()
        {
            var playwright = await Playwright.CreateAsync();
            _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false,
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu" }
            });

            var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1000, Height = 500 }
            });

            _page = await context.NewPageAsync();
        }

        public async Task CloseBrowserAsync()
        {
            await _browser.CloseAsync();
        }

        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> HandleCaptchaAndScrapePricesAsync(string url)
        {
            var priceResults = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            string log;

            try
            {
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
                        log = $"Failed to solve CAPTCHA for URL: {url}";
                        rejectedProducts.Add(("CAPTCHA not solved", url));
                        return (priceResults, log, rejectedProducts);
                    }
                }

                var rejectButton = await _page.QuerySelectorAsync("button.cookie-consent__buttons__action.js_cookie-consent-necessary[data-role='reject-rodo']");
                if (rejectButton != null)
                {
                    await rejectButton.ClickAsync();
                    Console.WriteLine("Clicked 'Nie zgadzam się' button.");
                }

                var (prices, scrapeLog, scrapeRejectedProducts) = await ScrapePricesFromPage(url);
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
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Net;
//using System.Net.Http;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;
//using Microsoft.Playwright;

//namespace PriceTracker.Services
//{
//    public class Scraper
//    {
//        private readonly HttpClient _httpClient;
//        private IBrowser _browser;
//        private IPage _page;
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

//                                    if (shippingText.Contains("Darmowa wysyłka"))
//                                    {
//                                        shippingCostNum = 0;
//                                    }
//                                    else if (shippingText.Contains("szczegóły dostawy"))
//                                    {
//                                        shippingCostNum = null;
//                                    }
//                                    else
//                                    {
//                                        var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
//                                        if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedShippingCost))
//                                        {
//                                            shippingCostNum = parsedShippingCost;
//                                        }
//                                        else
//                                        {
//                                            shippingCostNum = null;
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



//        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> HandleCaptchaAsync(string url)
//        {
//            var priceResults = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
//            var rejectedProducts = new List<(string Reason, string Url)>();
//            string log;

//            try
//            {
//                var playwright = await Playwright.CreateAsync();
//                _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
//                {
//                    Headless = false,
//                    Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu" }
//                });
//                _page = await _browser.NewPageAsync();

//                bool captchaSolved = await NavigateAndSolveCaptcha(url);

//                if (captchaSolved)
//                {
//                    var (prices, scrapeLog, scrapeRejectedProducts) = await ScrapePricesFromPage(url);
//                    priceResults.AddRange(prices);
//                    log = scrapeLog;
//                    rejectedProducts.AddRange(scrapeRejectedProducts);
//                }
//                else
//                {
//                    log = $"Failed to solve CAPTCHA for URL: {url}";
//                    rejectedProducts.Add(("CAPTCHA not solved", url));
//                }

//                await _browser.CloseAsync();
//            }
//            catch (Exception ex)
//            {
//                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
//                rejectedProducts.Add(($"Exception: {ex.Message}", url));
//            }

//            return (priceResults, log, rejectedProducts);
//        }

//        private async Task<bool> NavigateAndSolveCaptcha(string url)
//        {
//            try
//            {
//                Console.WriteLine("Navigating to the target URL...");
//                await _page.GotoAsync(url);

//                var currentUrl = _page.Url;
//                if (currentUrl.Contains("/Captcha/Add"))
//                {
//                    Console.WriteLine("CAPTCHA detected, navigating to error page...");
//                    await _page.GotoAsync("https://www.ceneo.pl/777");

//                    Console.WriteLine("Navigating back to the target URL...");
//                    await _page.GotoAsync(url);

//                    currentUrl = _page.Url;
//                    if (currentUrl.Contains("/Captcha/Add"))
//                    {
//                        return false;
//                    }
//                }

//                var rejectButton = await _page.WaitForSelectorAsync("button.cookie-consent__buttons__action.js_cookie-consent-necessary[data-role='reject-rodo']", new PageWaitForSelectorOptions { Timeout = 5000 });
//                if (rejectButton != null)
//                {
//                    await rejectButton.ClickAsync();
//                    Console.WriteLine("Clicked 'Nie zgadzam się' button.");
//                }

//                return true;
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine("Error navigating through CAPTCHA: " + ex.Message);
//                return false;
//            }
//        }

//        private async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> ScrapePricesFromPage(string url)
//        {
//            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
//            var rejectedProducts = new List<(string Reason, string Url)>();
//            string log;

//            Console.WriteLine("Navigating to the target URL...");
//            await _page.GotoAsync(url);

//            Console.WriteLine("Querying for offer nodes...");
//            var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");

//            if (offerNodes.Count > 0)
//            {
//                Console.WriteLine($"Found {offerNodes.Count} offer nodes.");
//                foreach (var offerNode in offerNodes)
//                {
//                    var storeName = await (await offerNode.QuerySelectorAsync("div.product-offer__store img"))?.GetAttributeAsync("alt");
//                    var priceNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.value");
//                    var pennyNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.penny");
//                    var shippingNode = await offerNode.QuerySelectorAsync("div.free-delivery-label") ??
//                                       await offerNode.QuerySelectorAsync("span.product-delivery-info.js_deliveryInfo");
//                    var availabilityNode = await offerNode.QuerySelectorAsync("span.instock") ??
//                                           await offerNode.QuerySelectorAsync("span.product-delivery-info");

//                    var offerContainer = await offerNode.QuerySelectorAsync(".product-offer__container");
//                    var offerType = await offerContainer?.GetAttributeAsync("data-offertype");
//                    var isBidding = (offerType == "CPC_Bid" || offerType == "CPC_Bid_Basket") ? "1" : "0";
//                    var position = await offerContainer?.GetAttributeAsync("data-position");

//                    if (storeName == null)
//                    {
//                        rejectedProducts.Add(("Store name is null", url));
//                        continue;
//                    }

//                    if (priceNode == null || pennyNode == null)
//                    {
//                        rejectedProducts.Add(("Price node or penny node is null", url));
//                        continue;
//                    }

//                    decimal? shippingCostNum = null;
//                    var priceText = (await priceNode.InnerTextAsync()).Trim() + (await pennyNode.InnerTextAsync()).Trim();
//                    var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

//                    if (decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
//                    {
//                        if (shippingNode != null)
//                        {
//                            var shippingText = await shippingNode.InnerTextAsync();
//                            if (shippingText.Contains("Darmowa wysyłka"))
//                            {
//                                shippingCostNum = 0;
//                            }
//                            else
//                            {
//                                var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
//                                if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedShippingCost))
//                                {
//                                    shippingCostNum = parsedShippingCost;
//                                }
//                                else
//                                {
//                                    shippingCostNum = null;
//                                }
//                            }
//                        }

//                        int? availabilityNum = null;
//                        if (availabilityNode != null)
//                        {
//                            var availabilityText = await availabilityNode.InnerTextAsync();
//                            if (availabilityText.Contains("Wysyłka w 1 dzień"))
//                            {
//                                availabilityNum = 1;
//                            }
//                            else if (availabilityText.Contains("Wysyłka do"))
//                            {
//                                var daysText = Regex.Match(availabilityText, @"\d+").Value;
//                                if (int.TryParse(daysText, out var parsedDays))
//                                {
//                                    availabilityNum = parsedDays;
//                                }
//                            }
//                        }

//                        prices.Add((storeName, price, shippingCostNum, availabilityNum, isBidding, position));
//                    }
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

