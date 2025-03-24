using PriceSafari.Models;
using PuppeteerSharp;
using System.Globalization;
using System.Text.RegularExpressions;
using static PriceScrapingController;

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

    

        public Page Page => _page;

        public async Task InitializeBrowserAsync(Settings settings)
        {
            try
            {
                Console.WriteLine("Starting browser initialization...");

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
                        "--blink-settings=imagesEnabled=false"
                    }
                });

                if (_browser == null)
                {
                    throw new Exception("Browser failed to launch.");
                }

                _page = (Page)await _browser.NewPageAsync();

                if (_page == null)
                {
                    throw new Exception("Failed to create a new page.");
                }

                await _page.SetJavaScriptEnabledAsync(settings.JavaScript);

                await _page.EvaluateFunctionOnNewDocumentAsync(@"() => {
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

                // Ustawienie rozdzielczości
                var commonResolutions = new List<(int width, int height)>
                {
                    (1366, 768)
                };

                var random = new Random();
                var randomResolution = commonResolutions[random.Next(commonResolutions.Count)];
                await _page.SetViewportAsync(new ViewPortOptions { Width = randomResolution.width, Height = randomResolution.height });

                await _page.EvaluateFunctionOnNewDocumentAsync(@"() => {
                    [...document.querySelectorAll('link[rel=stylesheet], style')].forEach(e => e.remove());
                    const origCreateElement = document.createElement;
                    document.createElement = function(tagName, ...args) {
                        const el = origCreateElement.call(document, tagName, ...args);
                        if (tagName.toLowerCase() === 'link' || tagName.toLowerCase() === 'style') {
                            el.setAttribute('disabled', 'true');
                        }
                        return el;
                    };
                }");

                Console.WriteLine($"Bot gotowy, teraz rozgrzewka przez {settings.WarmUpTime} sekund...");
                await Task.Delay(settings.WarmUpTime * 1000);
                Console.WriteLine("Rozgrzewka zakończona. Bot gotowy do scrapowania.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InitializeBrowserAsync: {ex.Message}");
                throw; // Rethrow the exception to be handled by the caller
            }
        }

 
        public async Task ApplySessionData(CaptchaSessionData sessionData)
        {
            if (_page == null)
                throw new InvalidOperationException("Page is not initialized. Call InitializeBrowserAsync first.");

            // Ustawiamy cookies
            if (sessionData.Cookies != null && sessionData.Cookies.Any())
            {
                await _page.SetCookieAsync(sessionData.Cookies);
            }

           
        }

        public async Task CloseBrowserAsync()
        {
            if (_page != null)
            {
                await _page.CloseAsync();
            }
            if (_browser != null)
            {
                await _browser.CloseAsync();
            }
        }

        public async Task<(List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position, string? ceneoName)> Prices, string Log, List<(string Reason, string Url)> RejectedProducts)>
    HandleCaptchaAndScrapePricesAsync(string url, bool getCeneoName, List<string> storeNames, List<string> storeProfiles)
        {
            var priceResults = new List<(string storeName, decimal price, decimal? shippingCostNum, int? availabilityNum, string isBidding, string? position, string? ceneoName)>();
            var rejectedProducts = new List<(string Reason, string Url)>();
            string log;

            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    log = "The URL provided is null or empty.";
                    Console.WriteLine(log);
                    return (priceResults, log, rejectedProducts);
                }

                if (_page == null)
                {
                    throw new Exception("Browser page is not initialized.");
                }

                // Główne przejście na stronę (krótki timeout)
                await _page.GoToAsync(url, new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
                });

              
                await _page.WaitForSelectorAsync("li.product-offers__list__item", new WaitForSelectorOptions
                {
                    Timeout = 3000
                });

                var currentUrl = _page.Url;
                // Sprawdzamy, czy trafiliśmy na Captchę.
                // Jeśli tak – przerywamy natychmiast (bez pętli).
                if (currentUrl.Contains("/Captcha/Add", StringComparison.OrdinalIgnoreCase))
                {
                    // Możesz albo rzucać wyjątek:
                    // throw new Exception("CAPTCHA_DETECTED");

                    // Albo po prostu zwrócić pustą listę z komunikatem:
                    var logMsg = "Captcha encountered at " + currentUrl;
                    Console.WriteLine(logMsg);

                    return (new List<(string, decimal, decimal?, int?, string, string?, string?)>(),
                            logMsg,
                            new List<(string Reason, string Url)> { ("Captcha", url) });
                }


                // Liczba wszystkich ofert
                var totalOffersCount = await GetTotalOffersCountAsync();
                Console.WriteLine($"Total number of offers: {totalOffersCount}");

                // 1) Wczytujemy oferty z bieżącej strony
                var (mainPrices, scrapeLog, scrapeRejectedProducts) = await ScrapePricesFromCurrentPage(url, true, getCeneoName);
                priceResults.AddRange(mainPrices);
                log = scrapeLog;
                rejectedProducts.AddRange(scrapeRejectedProducts);

                // 2) Jeżeli > 15 ofert, wtedy dopiero sprawdzamy strony z sortowaniem i fastestDelivery
                if (totalOffersCount > 15)
                {
                    var sortedUrl = $"{url};0281-0.htm";
                    await _page.GoToAsync(sortedUrl, new NavigationOptions
                    {
                        WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
                    });
                    await _page.WaitForSelectorAsync("li.product-offers__list__item", new WaitForSelectorOptions
                    {
                        Timeout = 3000
                    });

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

                    // Jeśli wciąż mamy mniej ofert, a totalOffersCount > 15, 
                    // wchodzimy na fastestDeliveryUrl
                    if (priceResults.Count < totalOffersCount)
                    {
                        var fastestDeliveryUrl = $"{url};0282-1;02516.htm";
                        await _page.GoToAsync(fastestDeliveryUrl, new NavigationOptions
                        {
                            WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
                        });
                        await _page.WaitForSelectorAsync("li.product-offers__list__item", new WaitForSelectorOptions
                        {
                            Timeout = 3000
                        });

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

                    // 3) Jeżeli mamy powyżej 15 ofert, to sprawdzajmy szczegółowe linki sklepów,
                    //    ale tylko wtedy, jeśli rzeczywiście czegoś brakuje
                    var foundStoreNames = priceResults.Select(p => p.storeName).Distinct().ToList();
                    var desiredStores = storeNames.Zip(storeProfiles, (name, profile) => new { StoreName = name, StoreProfile = profile }).ToList();
                    var notFoundStores = desiredStores.Where(ds => !foundStoreNames.Contains(ds.StoreName)).ToList();

                    foreach (var store in notFoundStores)
                    {
                        var storeSpecificUrl = $"{url};{store.StoreProfile}-0v.htm";
                        await _page.GoToAsync(storeSpecificUrl, new NavigationOptions
                        {
                            WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
                        });
                        await _page.WaitForSelectorAsync("li.product-offers__list__item", new WaitForSelectorOptions
                        {
                            Timeout = 3000
                        });

                        var (storePrices, storeLog, storeRejectedProducts) = await ScrapePricesFromCurrentPage(storeSpecificUrl, false, getCeneoName);
                        var storeSpecificOffers = storePrices
                            .Where(p => p.storeName == store.StoreName)
                            .Select(p => (
                                p.storeName,
                                p.price,
                                p.shippingCostNum,
                                p.availabilityNum,
                                p.isBidding,
                                position: (string?)null,
                                p.ceneoName
                            ))
                            .ToList();

                        priceResults.AddRange(storeSpecificOffers);
                        log += storeLog;
                        rejectedProducts.AddRange(storeRejectedProducts);
                    }
                }
                else
                {
                    // Jeżeli <= 15 ofert, to wszystko mamy na jednej stronie
                    log += "No need for additional pages or store-specific URLs (<= 15 offers). ";
                }

                log += $"Scraping completed, found {priceResults.Count} unique offers in total.";
            }
            catch (Exception ex)
            {
                log = $"Error scraping URL: {url}. Exception: {ex.Message}";
                Console.WriteLine(log);
                rejectedProducts.Add(($"Exception: {ex.Message}", url));
            }

            return (priceResults, log, rejectedProducts);
        }


        private async Task<int> GetTotalOffersCountAsync()
        {
            if (_page == null)
            {
                throw new Exception("Browser page is not initialized.");
            }

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

            if (_page == null)
            {
                throw new Exception("Browser page is not initialized.");
            }

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

                    // Collect product name from each individual offer
                    string? ceneoProductName = getCeneoName ? await GetCeneoProductNameFromOfferNodeAsync((ElementHandle)offerNode) : null;

                    // Add to the list of offers for each store with the Ceneo product name
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

            // Dodaj np. tutaj
            if (!string.IsNullOrEmpty(storeName))
            {
                storeName = storeName.ToLowerInvariant();
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

            return null; // Returns null if no name is found
        }
    }
}