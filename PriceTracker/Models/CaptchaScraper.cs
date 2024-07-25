


using Microsoft.Playwright;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PriceTracker.Models
{
    public class CaptchaScraper
    {
        private IBrowser _browser;
        private IPage _page;
        private readonly HttpClient _httpClient;

        public CaptchaScraper(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task InitializeBrowserAsync(string browserType = "chromium")
        {
            var playwright = await Playwright.CreateAsync();
            switch (browserType.ToLower())
            {
                case "firefox":
                    _browser = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = false,
                        Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu" }
                    });
                    break;
                case "webkit":
                    _browser = await playwright.Webkit.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = false,
                        Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu" }
                    });
                    break;
                case "chromium":
                default:
                    _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = false,
                        Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu" }
                    });
                    break;
            }

            var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 690, Height = 590 }
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
                    await _page.GotoAsync("https://www.ceneo.pl/859");

                    Console.WriteLine("Navigating back to the target URL...");
                    await _page.ReloadAsync();

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
