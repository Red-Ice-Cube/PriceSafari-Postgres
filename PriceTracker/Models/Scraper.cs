using HtmlAgilityPack;
using PuppeteerSharp;
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
        private const string ChromeExecutablePath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
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

                        // Logging the extracted values
                        Console.WriteLine($"storeName: {storeName}");
                        Console.WriteLine($"priceNode: {priceNode?.InnerText}");
                        Console.WriteLine($"pennyNode: {pennyNode?.InnerText}");
                        Console.WriteLine($"shippingNode: {shippingNode?.InnerText}");
                        Console.WriteLine($"availabilityNode: {availabilityNode?.InnerText}");
                        Console.WriteLine($"isBidding: {isBidding}");
                        Console.WriteLine($"position: {position}");

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
                                    Console.WriteLine($"Shipping text found: {shippingText}");  

                                    if (shippingText.Contains("Darmowa wysyłka"))
                                    {
                                        shippingCostNum = 0;
                                        Console.WriteLine("Shipping cost set to 0 due to 'Darmowa wysyłka'");
                                    }
                                    else if (shippingText.Contains("szczegóły dostawy"))
                                    {
                                        shippingCostNum = null;
                                        Console.WriteLine("Shipping cost set to null due to 'szczegóły dostawy'");
                                    }
                                    else
                                    {
                                        var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
                                        if (!string.IsNullOrEmpty(shippingCostText))
                                        {
                                            Console.WriteLine($"Shipping cost found: {shippingCostText}");  
                                            if (decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedShippingCost))
                                            {
                                                shippingCostNum = parsedShippingCost;
                                                Console.WriteLine($"Parsed shipping cost: {shippingCostNum}");  
                                            }
                                            else
                                            {
                                                shippingCostNum = null;
                                                Console.WriteLine("Shipping cost set to null due to parsing failure");
                                            }
                                        }
                                        else
                                        {
                                            shippingCostNum = null;
                                            Console.WriteLine("Shipping cost set to null due to empty cost text");
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


        private async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> HandleCaptchaAsync(string url)
        {
            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string position)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            string log;

            try
            {
                Console.WriteLine("Launching browser...");
                var browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = false,
                    ExecutablePath = ChromeExecutablePath,
                    Args = new string[]
                    {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-extensions",
                "--disable-gpu",
                "--disable-dev-shm-usage",
                "--disable-software-rasterizer",
                "--disable-features=site-per-process",
                "--disable-features=VizDisplayCompositor",
                "--disable-blink-features=AutomationControlled"
                    }
                });

                var page = await browser.NewPageAsync();

                await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

                await page.SetViewportAsync(new ViewPortOptions
                {
                    Width = 1366,
                    Height = 768
                });

                Console.WriteLine("Navigating to CAPTCHA page...");
                await page.GoToAsync("https://www.ceneo.pl/24ado");
                await Task.Delay(5000);

                Console.WriteLine("Navigating back to the target URL...");
                await page.GoToAsync(url);

                Console.WriteLine("Waiting for page to load...");
                await Task.Delay(5000);

                try
                {
                    var rejectButton = await page.QuerySelectorAsync("button.cookie-consent__buttons__action.js_cookie-consent-necessary[data-role='reject-rodo']");
                    if (rejectButton != null)
                    {
                        await rejectButton.ClickAsync();
                        Console.WriteLine("Clicked 'Nie zgadzam się' button.");
                    }
                    else
                    {
                        Console.WriteLine("'Nie zgadzam się' button not found.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Could not find or click 'Nie zgadzam się' button: " + ex.Message);
                }

                Console.WriteLine("Querying for offer nodes...");
                var offerNodes = await page.QuerySelectorAllAsync("li.product-offers__list__item");

                if (offerNodes.Length > 0)
                {
                    Console.WriteLine($"Found {offerNodes.Length} offer nodes.");
                    foreach (var offerNode in offerNodes)
                    {
                        var storeName = await (await offerNode.QuerySelectorAsync("div.product-offer__store img"))?.EvaluateFunctionAsync<string>("el => el.alt");
                        var priceNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.value");
                        var pennyNode = await offerNode.QuerySelectorAsync("span.price-format span.price span.penny");
                        var shippingNode = await offerNode.QuerySelectorAsync("div.free-delivery-label") ??
                                           await offerNode.QuerySelectorAsync("span.product-delivery-info.js_deliveryInfo");
                        var availabilityNode = await offerNode.QuerySelectorAsync("span.instock") ??
                                               (await offerNode.XPathAsync(".//span[contains(text(), 'Wysyłka')]")).FirstOrDefault();

                        var offerContainer = await offerNode.QuerySelectorAsync(".product-offer__container");
                        var offerType = await (await offerContainer.GetPropertyAsync("data-offertype"))?.JsonValueAsync<string>();
                        var isBidding = (offerType == "CPC_Bid" || offerType == "CPC_Bid_Basket") ? "1" : "0";
                        var position = await (await offerContainer.GetPropertyAsync("data-position"))?.JsonValueAsync<string>();

                        Console.WriteLine($"Processing offer from store: {storeName}");
                        if (storeName == null)
                        {
                            Console.WriteLine("Store name is null. Skipping this offer.");
                            rejectedProducts.Add(("Store name is null", url));
                            continue;
                        }

                        if (priceNode == null || pennyNode == null)
                        {
                            Console.WriteLine("Price node or penny node is null. Skipping this offer.");
                            rejectedProducts.Add(("Price node or penny node is null", url));
                            continue;
                        }

                        decimal? shippingCostNum = null;
                        var priceText = (await priceNode.EvaluateFunctionAsync<string>("el => el.textContent")).Trim() +
                                        (await pennyNode.EvaluateFunctionAsync<string>("el => el.textContent")).Trim();
                        var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

                        Console.WriteLine($"Parsed price value: {priceValue}");
                        if (decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
                        {
                            if (shippingNode != null)
                            {
                                var shippingText = await shippingNode.EvaluateFunctionAsync<string>("el => el.textContent");
                                if (shippingText.Contains("Darmowa wysyłka"))
                                {
                                    shippingCostNum = 0;
                                }
                                else
                                {
                                    var shippingCostText = Regex.Match(shippingText, @"\d+[.,]?\d*").Value;
                                    if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedShippingCost))
                                    {
                                        var shippingCostDifference = parsedShippingCost - price;
                                        if (shippingCostDifference > 0)
                                        {
                                            shippingCostNum = shippingCostDifference;
                                        }
                                        else
                                        {
                                            shippingCostNum = null;
                                        }
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
                                var availabilityText = await availabilityNode.EvaluateFunctionAsync<string>("el => el.textContent");
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

                await browser.CloseAsync();
            }
            catch (Exception ex)
            {
                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
                Console.WriteLine(log);
                rejectedProducts.Add(($"Exception: {ex.Message}", url));
            }

            return (prices, log, rejectedProducts);
        }



    }
}






//using PuppeteerSharp;
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.IO;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//namespace PriceTracker.Services
//{
//    public class PuppeteerScraper
//    {
//        private const string ChromeExecutablePath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
//        private const string CookiesFilePath = "cookies.json";

//        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum)> Prices, string Log)> GetProductPricesAsync(string url)
//        {
//            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum)>();
//            string log;

//            try
//            {
//                Console.WriteLine("Launching browser...");
//                var browser = await Puppeteer.LaunchAsync(new LaunchOptions
//                {
//                    Headless = false, // Zmieniamy na true, aby przeglądarka działała w tle
//                    ExecutablePath = ChromeExecutablePath,
//                    Args = new string[]
//                    {
//                        "--no-sandbox",
//                        "--disable-setuid-sandbox",
//                        "--disable-extensions",
//                        "--disable-gpu",
//                        "--disable-dev-shm-usage",
//                        "--disable-software-rasterizer",
//                        "--disable-features=site-per-process",
//                        "--disable-features=VizDisplayCompositor",
//                        "--disable-blink-features=AutomationControlled"
//                    }
//                });

//                var page = await browser.NewPageAsync();

//                // Ustawienie niestandardowego user-agent
//                await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

//                // Ustawienie niestandardowego viewport
//                await page.SetViewportAsync(new ViewPortOptions
//                {
//                    Width = 1366,
//                    Height = 768
//                });

//                if (File.Exists(CookiesFilePath))
//                {
//                    var cookies = System.Text.Json.JsonSerializer.Deserialize<List<CookieParam>>(File.ReadAllText(CookiesFilePath));
//                    await page.SetCookieAsync(cookies.ToArray());
//                }

//                Console.WriteLine($"Navigating to URL: {url}");
//                await page.GoToAsync(url);

//                // Kliknij przycisk "Nie zgadzam się"
//                try
//                {
//                    await page.WaitForSelectorAsync("button.cookie-consent__buttons__action.js_cookie-consent-necessary[data-role='reject-rodo']", new WaitForSelectorOptions { Timeout = 5000 });
//                    await page.ClickAsync("button.cookie-consent__buttons__action.js_cookie-consent-necessary[data-role='reject-rodo']");
//                    Console.WriteLine("Clicked 'Nie zgadzam się' button.");
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine("Could not find or click 'Nie zgadzam się' button: " + ex.Message);
//                }

//                // Sprawdź, czy pojawiła się CAPTCHA
//                var captchaDetected = page.Url.Contains("/Captcha/Add");

//                if (captchaDetected)
//                {
//                    // Przejdź na stronę 404
//                    Console.WriteLine("CAPTCHA detected, navigating to 404 page to bypass...");
//                    await page.GoToAsync("https://www.ceneo.pl/Ca");
//                    await Task.Delay(200);

//                    // Powrót na docelową stronę po ominięciu CAPTCHA
//                    Console.WriteLine("Navigating back to the target URL...");
//                    await page.GoToAsync(url);
//                }

//                Console.WriteLine("Waiting for page to load...");
//                await Task.Delay(5000);

//                try
//                {
//                    await page.WaitForSelectorAsync("li.product-offers__list__item");
//                }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"Error waiting for selector: {ex.Message}");
//                    var content = await page.GetContentAsync();
//                    Console.WriteLine("Page content:");
//                    Console.WriteLine(content);
//                    throw;
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

//                        Console.WriteLine($"Processing offer from store: {storeName}");
//                        if (storeName == null)
//                        {
//                            Console.WriteLine("Store name is null. Skipping this offer.");
//                            continue;
//                        }

//                        if (priceNode == null || pennyNode == null)
//                        {
//                            Console.WriteLine("Price node or penny node is null. Skipping this offer.");
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

//                            prices.Add((storeName, price, shippingCostNum, availabilityNum));
//                        }
//                    }
//                    log = $"Successfully scraped prices from URL: {url}";
//                    Console.WriteLine(log);
//                }
//                else
//                {
//                    log = $"Failed to find prices on URL: {url}";
//                    Console.WriteLine(log);
//                    throw new Exception("Prices not found on the page.");
//                }

//                await browser.CloseAsync();
//            }
//            catch (Exception ex)
//            {
//                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
//                Console.WriteLine(log);
//            }

//            return (prices, log);
//        }

//        public async Task ScrapeUrlsAsync(List<string> urlsToScrape)
//        {
//            foreach (var url in urlsToScrape)
//            {
//                var attempt = 0;
//                var maxAttempts = 3;
//                var success = false;

//                while (attempt < maxAttempts && !success)
//                {
//                    try
//                    {
//                        Console.WriteLine($"Scraping URL (Attempt {attempt + 1}/{maxAttempts}): {url}");
//                        var (prices, log) = await GetProductPricesAsync(url);
//                        Console.WriteLine(log);

//                        success = true;
//                    }
//                    catch (Exception ex)
//                    {
//                        Console.WriteLine($"Error scraping URL: {url}. Exception: {ex.Message}");
//                        attempt++;

//                        if (attempt < maxAttempts)
//                        {
//                            Console.WriteLine("Navigating to 404 page to bypass CAPTCHA...");
//                            var browser = await Puppeteer.LaunchAsync(new LaunchOptions
//                            {
//                                Headless = true,
//                                ExecutablePath = ChromeExecutablePath,
//                                Args = new string[]
//                                {
//                                    "--no-sandbox",
//                                    "--disable-setuid-sandbox",
//                                    "--disable-extensions",
//                                    "--disable-gpu",
//                                    "--disable-dev-shm-usage",
//                                    "--disable-software-rasterizer",
//                                    "--disable-features=site-per-process",
//                                    "--disable-features=VizDisplayCompositor",
//                                    "--disable-blink-features=AutomationControlled"
//                                }
//                            });

//                            var page = await browser.NewPageAsync();
//                            await page.GoToAsync("https://www.ceneo.pl/Ca");
//                            await Task.Delay(200); // Poczekaj 5 sekund, aby strona się załadowała
//                            await browser.CloseAsync();
//                        }
//                    }
//                }
//            }
//        }
//    }
//}








//using HtmlAgilityPack;
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Net.Http;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//namespace PriceTracker.Services
//{
//    public class Scraper
//    {
//        private readonly HttpClient _httpClient;

//        public Scraper(HttpClient httpClient)
//        {
//            _httpClient = httpClient;
//        }

//        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum)> Prices, string Log)> GetProductPricesAsync(string url)
//        {
//            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum)>();
//            string log;

//            try
//            {
//                var response = await _httpClient.GetStringAsync(url);
//                var doc = new HtmlDocument();
//                doc.LoadHtml(response);

//                var offerNodes = doc.DocumentNode.SelectNodes("//li[contains(@class, 'product-offers__list__item')]");

//                if (offerNodes != null)
//                {
//                    foreach (var offerNode in offerNodes)
//                    {
//                        var storeName = offerNode.SelectSingleNode(".//div[@class='product-offer__store']//img")?.GetAttributeValue("alt", "");
//                        var priceNode = offerNode.SelectSingleNode(".//span[@class='price-format nowrap']//span[@class='price']//span[@class='value']");
//                        var pennyNode = offerNode.SelectSingleNode(".//span[@class='price-format nowrap']//span[@class='price']//span[@class='penny']");
//                        var shippingNode = offerNode.SelectSingleNode(".//div[contains(@class, 'free-delivery-label')]") ??
//                                           offerNode.SelectSingleNode(".//span[contains(@class, 'product-delivery-info js_deliveryInfo')]");
//                        var availabilityNode = offerNode.SelectSingleNode(".//span[contains(@class, 'instock')]") ??
//                                              offerNode.SelectSingleNode(".//span[contains(text(), 'Wysyłka')]");

//                        decimal? shippingCostNum = null;
//                        if (priceNode != null && pennyNode != null && !string.IsNullOrEmpty(storeName))
//                        {
//                            var priceText = priceNode.InnerText.Trim() + pennyNode.InnerText.Trim();
//                            var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

//                            if (decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
//                            {
//                                if (shippingNode != null)
//                                {
//                                    if (shippingNode.InnerText.Contains("Darmowa wysyłka"))
//                                    {
//                                        shippingCostNum = 0;
//                                    }
//                                    else
//                                    {
//                                        var shippingCostText = Regex.Match(shippingNode.InnerText, @"\d+[.,]?\d*").Value;
//                                        if (!string.IsNullOrEmpty(shippingCostText) && decimal.TryParse(shippingCostText.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedShippingCost))
//                                        {
//                                            var shippingCostDifference = parsedShippingCost - price;
//                                            if (shippingCostDifference > 0)
//                                            {
//                                                shippingCostNum = shippingCostDifference;
//                                            }
//                                            else
//                                            {
//                                                shippingCostNum = null;
//                                            }
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

//                                prices.Add((storeName, price, shippingCostNum, availabilityNum));
//                            }
//                        }
//                    }
//                    log = $"Successfully scraped prices from URL: {url}";
//                    return (prices, log);
//                }

//                log = $"Failed to find prices on URL: {url}";
//                throw new Exception("Prices not found on the page.");
//            }
//            catch (Exception ex)
//            {
//                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
//                return (prices, log);
//            }
//        }
//    }
//}
