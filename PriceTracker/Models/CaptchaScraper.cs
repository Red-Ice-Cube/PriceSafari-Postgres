using PuppeteerSharp;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PriceTracker.Models
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
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu" }
            });

            _page = (Page)await _browser.NewPageAsync();
            await _page.SetViewportAsync(new ViewPortOptions { Width = 780, Height = 590 });
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
                await _page.GoToAsync(url);
                var currentUrl = _page.Url;

                if (currentUrl.Contains("/Captcha/Add"))
                {
                    Console.WriteLine("CAPTCHA detected, navigating to error page...");
                    await _page.GoToAsync("https://www.ceneo.pl/859");

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

                // Sprawdź liczbę węzłów produktów
                var offerNodes = await _page.QuerySelectorAllAsync("li.product-offers__list__item");

                if (offerNodes.Length == 15)
                {
                    bool allOffersLoaded = false;

                    while (!allOffersLoaded)
                    {
                        // Scrolluj w dół
                        await _page.EvaluateExpressionAsync("window.scrollBy(0, document.body.scrollHeight)");
                        await Task.Delay(1000); // Czekaj chwilę po scrollowaniu

                        // Sprawdź, czy przycisk jest dostępny
                        var showAllOffersButton = await _page.QuerySelectorAsync("span.show-remaining-offers__trigger.js_remainingTrigger");
                        if (showAllOffersButton != null)
                        {
                            Console.WriteLine("Found 'Pokaż wszystkie oferty' button, clicking...");
                            await showAllOffersButton.ClickAsync();

                            // Czekaj na załadowanie dodatkowych ofert
                            await _page.WaitForSelectorAsync("li.product-offers__list__item", new WaitForSelectorOptions { Visible = true });
                        }
                        else
                        {
                            // Jeśli nie ma przycisku, oznacza to, że wszystkie oferty są załadowane
                            allOffersLoaded = true;
                        }

                        // Sprawdź, czy dotarłeś do końca strony
                        var scrollPosition = await _page.EvaluateFunctionAsync<int>("() => window.pageYOffset + window.innerHeight");
                        var documentHeight = await _page.EvaluateFunctionAsync<int>("() => document.body.scrollHeight");
                        if (scrollPosition >= documentHeight)
                        {
                            allOffersLoaded = true;
                        }
                    }
                }

                // Zbierz dane po załadowaniu wszystkich ofert
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
                    var storeName = await (await offerNode.QuerySelectorAsync("div.product-offer__store img"))?.EvaluateFunctionAsync<string>("el => el.alt");

                    if (string.IsNullOrWhiteSpace(storeName))
                    {
                        var storeLink = await offerNode.QuerySelectorAsync("li.offer-shop-opinions a.link.js_product-offer-link");
                        if (storeLink != null)
                        {
                            var offerParameter = await storeLink.EvaluateFunctionAsync<string>("el => el.getAttribute('offer-parameter')");
                            Console.WriteLine($"Found store name in offer-parameter: {offerParameter}");
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
                    var shippingNode = await offerNode.QuerySelectorAsync("div.free-delivery-label") ??
                                       await offerNode.QuerySelectorAsync("span.product-delivery-info.js_deliveryInfo");
                    var availabilityNode = await offerNode.QuerySelectorAsync("span.instock") ??
                                           await offerNode.QuerySelectorAsync("div.product-availability span");

                    var offerContainer = await offerNode.QuerySelectorAsync(".product-offer__container");
                    var offerType = await offerContainer?.EvaluateFunctionAsync<string>("el => el.getAttribute('data-offertype')");
                    var isBidding = offerType?.Contains("Bid") == true ? "1" : "0";
                    var position = positionCounter.ToString();
                    positionCounter++;

                    // Log the found store name and offer type
                    Console.WriteLine($"Store name found: {storeName}");
                    Console.WriteLine($"Offer type found: {offerType}");
                    Console.WriteLine($"Position assigned: {position}");

                    if (priceNode == null || pennyNode == null)
                    {
                        rejectedProducts.Add(("Price node or penny node is null", url));
                        continue;
                    }

                    decimal? shippingCostNum = null;
                    var priceText = (await priceNode.EvaluateFunctionAsync<string>("el => el.innerText")).Trim() + (await pennyNode.EvaluateFunctionAsync<string>("el => el.innerText")).Trim();
                    var priceValue = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".").Trim();

                    if (decimal.TryParse(priceValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal price))
                    {
                        if (shippingNode != null)
                        {
                            var shippingText = await shippingNode.EvaluateFunctionAsync<string>("el => el.innerText");
                            Console.WriteLine($"Shipping info found: {shippingText}");
                            if (shippingText.Contains("DARMOWA WYSYŁKA") || shippingText.Contains("bezpłatna dostawa"))
                            {
                                shippingCostNum = 0.00m;
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
                            var availabilityText = await availabilityNode.EvaluateFunctionAsync<string>("el => el.innerText");
                            Console.WriteLine($"Availability info found: {availabilityText}");
                            if (availabilityText.Contains("Wysyłka w 1 dzień"))
                            {
                                availabilityNum = 1;
                            }
                            else if (availabilityText.Contains("Wysyłka do"))
                            {
                                var daysText = Regex.Match(availabilityText, @"\d+").Value;
                                Console.WriteLine($"Parsed days: {daysText}");
                                if (int.TryParse(daysText, out var parsedDays))
                                {
                                    availabilityNum = parsedDays;
                                }
                                else
                                {
                                    availabilityNum = null;
                                }
                            }
                            else
                            {
                                Console.WriteLine("No matching text for availability found.");
                                availabilityNum = null;
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
