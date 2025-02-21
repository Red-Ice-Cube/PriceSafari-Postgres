//using PriceSafari.Models;
//using PuppeteerSharp;
//using System.Globalization;
//using System.Text.RegularExpressions;

//public class GoogleMainPriceScraper
//{
//    private Browser _browser;
//    private Page _page;

//    public async Task InitializeAsync(Settings settings)
//    {
//        var browserFetcher = new BrowserFetcher();
//        await browserFetcher.DownloadAsync();

//        _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
//        {
//            Headless = settings.HeadLess,
//            Args = new[]
//            {
//                "--no-sandbox",
//                "--disable-setuid-sandbox",
//                "--disable-gpu",
//                "--disable-blink-features=AutomationControlled",
//                "--disable-software-rasterizer",
//                "--disable-extensions",
//                "--disable-dev-shm-usage",
//                "--disable-features=IsolateOrigins,site-per-process",
//                "--disable-infobars"
//            }
//        });

//        _page = (Page)await _browser.NewPageAsync();
//        Console.WriteLine("Browser initialized.");
//    }



//    public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(string googleOfferUrl)
//    {
//        var scrapedData = new List<CoOfrPriceHistoryClass>();
//        var storeBestOffers = new Dictionary<string, CoOfrPriceHistoryClass>();


//        string productId = ExtractProductId(googleOfferUrl);

//        if (string.IsNullOrEmpty(productId))
//        {
//            Console.WriteLine("Product ID not found in URL.");
//            return scrapedData;
//        }

//        string productOffersUrl = $"{googleOfferUrl}/offers?prds=cid:{productId},cond:1&gl=pl&hl=pl";
//        bool hasNextPage = true;
//        int totalOffersCount = 0;
//        int currentPage = 0;


//        int positionCounter = 1;

//        try
//        {
//            while (hasNextPage && currentPage < 3)
//            {
//                string paginatedUrl;

//                if (currentPage == 0)
//                {
//                    paginatedUrl = productOffersUrl;
//                }
//                else
//                {
//                    paginatedUrl = $"{googleOfferUrl}/offers?prds=cid:{productId},cond:1,start:{currentPage * 20}&gl=pl&hl=pl";
//                }

//                //Console.WriteLine($"Visiting URL: {paginatedUrl}");
//                await _page.GoToAsync(paginatedUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
//                //await Task.Delay(600);


//                var moreOffersButtons = await _page.QuerySelectorAllAsync("div.cNMlI");
//                foreach (var button in moreOffersButtons)
//                {
//                    Console.WriteLine("Found 'More offers' button. Clicking to expand.");
//                    await button.ClickAsync();
//                    await Task.Delay(500);
//                }

//                var offerRowsSelector = "#sh-osd__online-sellers-cont > tr";
//                var offerRows = await _page.QuerySelectorAllAsync(offerRowsSelector);
//                var offersCount = offerRows.Length;
//                totalOffersCount += offersCount;

//                if (offersCount == 0)
//                {
//                    //Console.WriteLine("No offers on the page.");
//                    break;
//                }

//                //Console.WriteLine($"Found {offersCount} offers. Starting scraping...");

//                for (int i = 1; i <= offersCount; i++)
//                {

//                    var storeNameSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";
//                    var priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > span";
//                    var priceWithDeliverySelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(5) > div > div.drzWO";

//                    var storeNameElement = await _page.QuerySelectorAsync(storeNameSelector);
//                    if (storeNameElement != null)
//                    {
//                        var storeName = await storeNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
//                        //Console.WriteLine($"Found store: {storeName}");

//                        var priceElement = await _page.QuerySelectorAsync(priceSelector);
//                        var priceText = priceElement != null ? await priceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "No price";
//                        //Console.WriteLine($"Price: {priceText}");

//                        var priceWithDeliveryElement = await _page.QuerySelectorAsync(priceWithDeliverySelector);
//                        var priceWithDeliveryText = priceWithDeliveryElement != null ? await priceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : priceText;
//                        //Console.WriteLine($"Price with delivery: {priceWithDeliveryText}");

//                        var priceDecimal = ExtractPrice(priceText);
//                        var priceWithDeliveryDecimal = ExtractPrice(priceWithDeliveryText);


//                        var currentPosition = positionCounter++;

//                        if (storeBestOffers.ContainsKey(storeName))
//                        {
//                            var existingOffer = storeBestOffers[storeName];
//                            if (priceWithDeliveryDecimal < existingOffer.GooglePriceWithDelivery)
//                            {
//                                storeBestOffers[storeName] = new CoOfrPriceHistoryClass
//                                {
//                                    GoogleStoreName = storeName,
//                                    GooglePrice = priceDecimal,
//                                    GooglePriceWithDelivery = priceWithDeliveryDecimal,
//                                    GooglePosition = currentPosition.ToString()
//                                };
//                            }
//                        }
//                        else
//                        {
//                            storeBestOffers[storeName] = new CoOfrPriceHistoryClass
//                            {
//                                GoogleStoreName = storeName,
//                                GooglePrice = priceDecimal,
//                                GooglePriceWithDelivery = priceWithDeliveryDecimal,
//                                GooglePosition = currentPosition.ToString()
//                            };
//                        }
//                    }
//                    else
//                    {
//                        //Console.WriteLine($"Store name element not found in row {i}.");
//                    }
//                }


//                var hiddenOfferRowsSelector = "tr.sh-osd__offer-row[data-hveid][data-is-grid-offer='true']";
//                var hiddenOfferRows = await _page.QuerySelectorAllAsync(hiddenOfferRowsSelector);
//                for (int j = 0; j < hiddenOfferRows.Length; j++)
//                {
//                    var hiddenRowElement = hiddenOfferRows[j];

//                    var hiddenStoreNameSelector = "td:nth-child(1) > div._-ez > a";
//                    var hiddenPriceSelector = "td:nth-child(4) > span";
//                    var hiddenPriceWithDeliverySelector = "td:nth-child(5) > div";

//                    var hiddenStoreNameElement = await hiddenRowElement.QuerySelectorAsync(hiddenStoreNameSelector);
//                    if (hiddenStoreNameElement != null)
//                    {
//                        var hiddenStoreName = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
//                        //Console.WriteLine($"Found hidden store: {hiddenStoreName}");

//                        var hiddenPriceElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceSelector);
//                        var hiddenPriceText = hiddenPriceElement != null ? await hiddenPriceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "No price";
//                        //Console.WriteLine($"Hidden price: {hiddenPriceText}");

//                        var hiddenPriceWithDeliveryElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceWithDeliverySelector);
//                        var hiddenPriceWithDeliveryText = hiddenPriceWithDeliveryElement != null ? await hiddenPriceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : hiddenPriceText;
//                        //Console.WriteLine($"Hidden price with delivery: {hiddenPriceWithDeliveryText}");

//                        var hiddenPriceDecimal = ExtractPrice(hiddenPriceText);
//                        var hiddenPriceWithDeliveryDecimal = ExtractPrice(hiddenPriceWithDeliveryText);


//                        var currentPosition = positionCounter++;


//                        if (storeBestOffers.ContainsKey(hiddenStoreName))
//                        {
//                            var existingOffer = storeBestOffers[hiddenStoreName];
//                            if (hiddenPriceWithDeliveryDecimal < existingOffer.GooglePriceWithDelivery)
//                            {
//                                storeBestOffers[hiddenStoreName] = new CoOfrPriceHistoryClass
//                                {
//                                    GoogleStoreName = hiddenStoreName,
//                                    GooglePrice = hiddenPriceDecimal,
//                                    GooglePriceWithDelivery = hiddenPriceWithDeliveryDecimal,
//                                    GooglePosition = currentPosition.ToString()
//                                };
//                            }
//                        }
//                        else
//                        {
//                            storeBestOffers[hiddenStoreName] = new CoOfrPriceHistoryClass
//                            {
//                                GoogleStoreName = hiddenStoreName,
//                                GooglePrice = hiddenPriceDecimal,
//                                GooglePriceWithDelivery = hiddenPriceWithDeliveryDecimal,
//                                GooglePosition = currentPosition.ToString()
//                            };
//                        }
//                    }
//                }

//                // Check if there is a next page
//                var paginationElement = await _page.QuerySelectorAsync("#sh-fp__pagination-button-wrapper");
//                if (paginationElement != null)
//                {
//                    var nextPageElement = await paginationElement.QuerySelectorAsync("a.internal-link[data-url*='start']");
//                    if (nextPageElement != null)
//                    {
//                        currentPage++;
//                        Console.WriteLine($"Moving to next page: {currentPage}");
//                        hasNextPage = true;
//                    }
//                    else
//                    {
//                        Console.WriteLine("No next page.");
//                        hasNextPage = false;
//                    }
//                }
//                else
//                {
//                    Console.WriteLine("Pagination element not found.");
//                    hasNextPage = false;
//                }

//                //await Task.Delay(500);
//            }


//            scrapedData.AddRange(storeBestOffers.Values);
//            Console.WriteLine($"Finished processing {scrapedData.Count} offers.");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error during scraping: {ex.Message}");
//        }

//        return scrapedData;
//    }




//    private string ExtractProductId(string url)
//    {
//        var match = Regex.Match(url, @"product/(\d+)");
//        if (match.Success)
//        {
//            return match.Groups[1].Value;
//        }
//        return string.Empty;
//    }

//    private decimal ExtractPrice(string priceText)
//    {
//        try
//        {
//            var priceMatch = Regex.Match(priceText, @"[\d\s,]+");
//            if (priceMatch.Success)
//            {
//                var priceString = priceMatch.Value.Replace(" ", "").Replace(",", ".").Replace(" ", "");
//                if (decimal.TryParse(priceString, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal priceDecimal))
//                {
//                    return priceDecimal;
//                }
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error processing price: {ex.Message}");
//        }

//        return 0;
//    }

//    public async Task CloseAsync()
//    {
//        await _page.CloseAsync();
//        await _browser.CloseAsync();
//        Console.WriteLine("Browser closed.");
//    }
//}

using PriceSafari.Models;
using PuppeteerSharp;
using System.Globalization;
using System.Text.RegularExpressions;

public class GoogleMainPriceScraper
{
    private Browser _browser;
    private Page _page;

    public async Task InitializeAsync(Settings settings)
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
        Console.WriteLine("Browser initialized.");
    }

    public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(string googleOfferUrl)
    {
        var scrapedData = new List<CoOfrPriceHistoryClass>();
        var storeBestOffers = new Dictionary<string, CoOfrPriceHistoryClass>();

        string productId = ExtractProductId(googleOfferUrl);
        if (string.IsNullOrEmpty(productId))
        {
            Console.WriteLine("Product ID not found in URL.");
            return scrapedData;
        }

        string productOffersUrl = $"{googleOfferUrl}/offers?prds=cid:{productId},cond:1&gl=pl&hl=pl";
        bool hasNextPage = true;
        int totalOffersCount = 0;
        int currentPage = 0;
        int positionCounter = 1;

        try
        {
            while (hasNextPage && currentPage < 3)
            {
                string paginatedUrl = currentPage == 0
                    ? productOffersUrl
                    : $"{googleOfferUrl}/offers?prds=cid:{productId},cond:1,start:{currentPage * 20}&gl=pl&hl=pl";

                await _page.GoToAsync(paginatedUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
                //await Task.Delay(600);

                var moreOffersButtons = await _page.QuerySelectorAllAsync("div.cNMlI");
                foreach (var button in moreOffersButtons)
                {
                    Console.WriteLine("Found 'More offers' button. Clicking to expand.");
                    await button.ClickAsync();
                    await Task.Delay(500);
                }

                var offerRowsSelector = "#sh-osd__online-sellers-cont > tr";
                var offerRows = await _page.QuerySelectorAllAsync(offerRowsSelector);
                var offersCount = offerRows.Length;
                totalOffersCount += offersCount;

                if (offersCount == 0)
                {
                    break;
                }

                for (int i = 1; i <= offersCount; i++)
                {
                    var storeNameSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";
                    var priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > span";
                    var priceWithDeliverySelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(5) > div > div.drzWO";

                    var storeNameElement = await _page.QuerySelectorAsync(storeNameSelector);
                    if (storeNameElement != null)
                    {
                        var storeName = await storeNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");

                        // Pobieramy URL oferty z elementu <a>
                        var offerUrl = await storeNameElement.EvaluateFunctionAsync<string>("node => node.href");
                        // Sprawdzamy, czy oferta nie jest outlet
                        if (IsOutletOffer(offerUrl))
                        {
                            Console.WriteLine("Offer is outlet, skipping.");
                            continue;
                        }

                        var priceElement = await _page.QuerySelectorAsync(priceSelector);
                        var priceText = priceElement != null ? await priceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "No price";
                        var priceWithDeliveryElement = await _page.QuerySelectorAsync(priceWithDeliverySelector);
                        var priceWithDeliveryText = priceWithDeliveryElement != null ? await priceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : priceText;

                        var priceDecimal = ExtractPrice(priceText);
                        var priceWithDeliveryDecimal = ExtractPrice(priceWithDeliveryText);
                        var currentPosition = positionCounter++;

                        if (storeBestOffers.ContainsKey(storeName))
                        {
                            var existingOffer = storeBestOffers[storeName];
                            if (priceWithDeliveryDecimal < existingOffer.GooglePriceWithDelivery)
                            {
                                storeBestOffers[storeName] = new CoOfrPriceHistoryClass
                                {
                                    GoogleStoreName = storeName,
                                    GooglePrice = priceDecimal,
                                    GooglePriceWithDelivery = priceWithDeliveryDecimal,
                                    GooglePosition = currentPosition.ToString()
                                };
                            }
                        }
                        else
                        {
                            storeBestOffers[storeName] = new CoOfrPriceHistoryClass
                            {
                                GoogleStoreName = storeName,
                                GooglePrice = priceDecimal,
                                GooglePriceWithDelivery = priceWithDeliveryDecimal,
                                GooglePosition = currentPosition.ToString()
                            };
                        }
                    }
                }

                var hiddenOfferRowsSelector = "tr.sh-osd__offer-row[data-hveid][data-is-grid-offer='true']";
                var hiddenOfferRows = await _page.QuerySelectorAllAsync(hiddenOfferRowsSelector);
                for (int j = 0; j < hiddenOfferRows.Length; j++)
                {
                    var hiddenRowElement = hiddenOfferRows[j];

                    var hiddenStoreNameSelector = "td:nth-child(1) > div._-ez > a";
                    var hiddenPriceSelector = "td:nth-child(4) > span";
                    var hiddenPriceWithDeliverySelector = "td:nth-child(5) > div";

                    var hiddenStoreNameElement = await hiddenRowElement.QuerySelectorAsync(hiddenStoreNameSelector);
                    if (hiddenStoreNameElement != null)
                    {
                        var hiddenStoreName = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
                        // Pobieramy URL ukrytej oferty
                        var hiddenOfferUrl = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.href");
                        if (IsOutletOffer(hiddenOfferUrl))
                        {
                            Console.WriteLine("Hidden offer is outlet, skipping.");
                            continue;
                        }

                        var hiddenPriceElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceSelector);
                        var hiddenPriceText = hiddenPriceElement != null ? await hiddenPriceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "No price";
                        var hiddenPriceWithDeliveryElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceWithDeliverySelector);
                        var hiddenPriceWithDeliveryText = hiddenPriceWithDeliveryElement != null ? await hiddenPriceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : hiddenPriceText;

                        var hiddenPriceDecimal = ExtractPrice(hiddenPriceText);
                        var hiddenPriceWithDeliveryDecimal = ExtractPrice(hiddenPriceWithDeliveryText);
                        var currentPosition = positionCounter++;

                        if (storeBestOffers.ContainsKey(hiddenStoreName))
                        {
                            var existingOffer = storeBestOffers[hiddenStoreName];
                            if (hiddenPriceWithDeliveryDecimal < existingOffer.GooglePriceWithDelivery)
                            {
                                storeBestOffers[hiddenStoreName] = new CoOfrPriceHistoryClass
                                {
                                    GoogleStoreName = hiddenStoreName,
                                    GooglePrice = hiddenPriceDecimal,
                                    GooglePriceWithDelivery = hiddenPriceWithDeliveryDecimal,
                                    GooglePosition = currentPosition.ToString()
                                };
                            }
                        }
                        else
                        {
                            storeBestOffers[hiddenStoreName] = new CoOfrPriceHistoryClass
                            {
                                GoogleStoreName = hiddenStoreName,
                                GooglePrice = hiddenPriceDecimal,
                                GooglePriceWithDelivery = hiddenPriceWithDeliveryDecimal,
                                GooglePosition = currentPosition.ToString()
                            };
                        }
                    }
                }

                var paginationElement = await _page.QuerySelectorAsync("#sh-fp__pagination-button-wrapper");
                if (paginationElement != null)
                {
                    var nextPageElement = await paginationElement.QuerySelectorAsync("a.internal-link[data-url*='start']");
                    if (nextPageElement != null)
                    {
                        currentPage++;
                        Console.WriteLine($"Moving to next page: {currentPage}");
                        hasNextPage = true;
                    }
                    else
                    {
                        Console.WriteLine("No next page.");
                        hasNextPage = false;
                    }
                }
                else
                {
                    Console.WriteLine("Pagination element not found.");
                    hasNextPage = false;
                }
            }

            scrapedData.AddRange(storeBestOffers.Values);
            Console.WriteLine($"Finished processing {scrapedData.Count} offers.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during scraping: {ex.Message}");
        }

        return scrapedData;
    }

    private string ExtractProductId(string url)
    {
        var match = Regex.Match(url, @"product/(\d+)");
        if (match.Success)
        {
            return match.Groups[1].Value;
        }
        return string.Empty;
    }

    private decimal ExtractPrice(string priceText)
    {
        try
        {
            var priceMatch = Regex.Match(priceText, @"[\d\s,]+");
            if (priceMatch.Success)
            {
                var priceString = priceMatch.Value.Replace(" ", "").Replace(",", ".").Replace(" ", "");
                if (decimal.TryParse(priceString, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal priceDecimal))
                {
                    return priceDecimal;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing price: {ex.Message}");
        }

        return 0;
    }

    private bool IsOutletOffer(string url)
    {
        return !string.IsNullOrEmpty(url) && url.IndexOf("outlet", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public async Task CloseAsync()
    {
        await _page.CloseAsync();
        await _browser.CloseAsync();
        Console.WriteLine("Browser closed.");
    }
}
