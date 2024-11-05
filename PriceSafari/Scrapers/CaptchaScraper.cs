using PriceSafari.Models;
using PuppeteerSharp;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PriceSafari.Scrapers
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
                    "--disable-infobars"
        }
            });

            _page = (Page)await _browser.NewPageAsync();


            await _page.SetJavaScriptEnabledAsync(settings.JavaScript);

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

            var commonResolutions = new List<(int width, int height)>
            {

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



            Console.WriteLine($"Bot gotowy, teraz rozgrzewka przez {settings.WarmUpTime} sekund...");
            await Task.Delay(settings.WarmUpTime * 1000);
            Console.WriteLine("Rozgrzewka zakończona. Bot gotowy do scrapowania.");
        }

        public async Task CloseBrowserAsync()
        {
            await _page.CloseAsync();
            await _browser.CloseAsync();
        }



        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position, string? ceneoName)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> HandleCaptchaAndScrapePricesAsync(string url, bool getCeneoName)
        {
            var priceResults = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position, string? ceneoName)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            string log;

            try
            {
                await _page.GoToAsync(url);
                var currentUrl = _page.Url;

                // Sprawdzanie Captcha
                while (currentUrl.Contains("/Captcha/Add"))
                {
                    Console.WriteLine("Captcha detected. Please solve it manually in the browser.");
                    while (currentUrl.Contains("/Captcha/Add"))
                    {
                        await Task.Delay(15000);
                        currentUrl = _page.Url;
                    }
                }

                // Pobranie liczby ofert
                var totalOffersCount = await GetTotalOffersCountAsync();
                Console.WriteLine($"Total number of offers: {totalOffersCount}");

                // Pobieranie cen z bieżącej strony i nazw produktów, jeśli jest to wymagane
                var (mainPrices, scrapeLog, scrapeRejectedProducts) = await ScrapePricesFromCurrentPage(url, true, getCeneoName);
                priceResults.AddRange(mainPrices);
                log = scrapeLog;
                rejectedProducts.AddRange(scrapeRejectedProducts);

                // Pobieranie kolejnych stron ofert, jeśli jest ich więcej
                if (totalOffersCount > 15)
                {
                    var sortedUrl = $"{url};0281-1.htm";
                    await _page.GoToAsync(sortedUrl);

                    var (sortedPrices, sortedLog, sortedRejectedProducts) = await ScrapePricesFromCurrentPage(sortedUrl, false, getCeneoName);
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

                        var (nextSortedPrices, nextSortedLog, nextSortedRejectedProducts) = await ScrapePricesFromCurrentPage(nextSortedUrl, false, getCeneoName);
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
                        await _page.GoToAsync(fastestDeliveryUrl);

                        var (fastestDeliveryPrices, fastestDeliveryLog, fastestDeliveryRejectedProducts) = await ScrapePricesFromCurrentPage(fastestDeliveryUrl, false, getCeneoName);
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
                var textContent = await totalOffersText.EvaluateFunctionAsync<string>("el => el.innerText");
                var match = Regex.Match(textContent, @"\d+");
                if (match.Success)
                {
                    totalOffersCount = int.Parse(match.Value);
                }
            }
            return totalOffersCount;
        }

        private async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position, string? ceneoName)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)> ScrapePricesFromCurrentPage(string url, bool includePosition, bool getCeneoName)
        {
            var prices = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position, string? ceneoName)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            var storeOffers = new Dictionary<string, (decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position, string? ceneoName)>();
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

                    // Zbieranie nazwy produktu z każdej indywidualnej oferty
                    string? ceneoProductName = getCeneoName ? await GetCeneoProductNameFromOfferNodeAsync((ElementHandle)offerNode) : null;

                    // Dodanie do listy cen ofert dla każdego sklepu z nazwą produktu Ceneo
                    if (storeOffers.ContainsKey(storeName))
                    {
                        if (priceValue.Value < storeOffers[storeName].price)
                        {
                            storeOffers[storeName] = (priceValue.Value, shippingCostNum, availabilityNum, isBidding, position, ceneoProductName);
                        }
                    }
                    else
                    {
                        storeOffers[storeName] = (priceValue.Value, shippingCostNum, availabilityNum, isBidding, position, ceneoProductName);
                    }
                }

                prices = storeOffers.Select(x => (x.Key, x.Value.price, x.Value.shippingCostNum, x.Value.availabilityNum, x.Value.isBidding, x.Value.position, x.Value.ceneoName)).ToList();
                log = $"Successfully scraped prices and names from URL: {url}";
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

        private async Task<string?> GetCeneoProductNameFromOfferNodeAsync(ElementHandle offerNode)
        {
            try
            {
                var productNameElement = await offerNode.QuerySelectorAsync("span.short-name__txt");
                if (productNameElement != null)
                {
                    var productName = await productNameElement.EvaluateFunctionAsync<string>("el => el.innerText");
                    return productName.Trim();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scraping product name from offer node: {ex.Message}");
            }

            return null; // Zwraca null, jeśli nie znaleziono nazwy
        }


    }
}











