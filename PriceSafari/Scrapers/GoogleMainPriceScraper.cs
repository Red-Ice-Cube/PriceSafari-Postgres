// KOD DO STREJ STRUKTORY GOOGLE 



//using PriceSafari.Models;
//using PuppeteerSharp;
//using System;
//using System.Globalization;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;
//using System.Collections.Generic;
//using System.Linq;

//public class CaptchaDetectedException : Exception
//{
//    public CaptchaDetectedException(string message) : base(message) { }
//}

//public class GoogleMainPriceScraper
//{
//    private Browser _browser;
//    private Page _page;
//    private bool _expandAndCompareGoogleOffersSetting;
//    private Settings _scraperSettings;

//    public event EventHandler CaptchaDetected;

//    protected virtual void OnCaptchaDetected(string url)
//    {
//        CaptchaDetected?.Invoke(this, EventArgs.Empty);
//        throw new CaptchaDetectedException($"CAPTCHA page detected at {url}");
//    }

//    // NOWA PRYWATNA METODA: Logika uruchamiania nowej przeglądarki
//    private async Task LaunchNewBrowserAsync()
//    {
//        var browserFetcher = new BrowserFetcher();
//        await browserFetcher.DownloadAsync();

//        _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
//        {
//            Headless = _scraperSettings.HeadLess,
//            Args = new[]
//            {
//                "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu",
//                "--disable-blink-features=AutomationControlled", "--disable-software-rasterizer",
//                "--disable-extensions", "--disable-dev-shm-usage",
//                "--disable-features=IsolateOrigins,site-per-process", "--disable-infobars"
//            }
//        });

//        _page = (Page)await _browser.NewPageAsync();
//        Console.WriteLine($"Browser initialized. Headless: {_scraperSettings.HeadLess}, ExpandAndCompareGoogleOffers: {_expandAndCompareGoogleOffersSetting}");
//    }

//    // ZMIENIONA METODA: Teraz tylko zapisuje ustawienia i wywołuje nową metodę uruchamiającą
//    public async Task InitializeAsync(Settings settings)
//    {
//        _scraperSettings = settings;
//        _expandAndCompareGoogleOffersSetting = settings.ExpandAndCompareGoogleOffers;
//        await LaunchNewBrowserAsync();
//    }

//    // NOWA METODA RESETU: Zamyka starą przeglądarkę i uruchamia nową
//    public async Task ResetBrowserAndPageAsync()
//    {
//        Console.WriteLine($"Worker {Task.CurrentId}: Performing FULL BROWSER RESET...");
//        await CloseAsync(); // Używamy istniejącej metody do posprzątania
//        await LaunchNewBrowserAsync(); // Uruchamiamy całkowicie nową instancję
//        Console.WriteLine($"Worker {Task.CurrentId}: Full browser reset completed.");
//    }

//    private async Task<bool> TryNavigateAndVerifyUrlAsync(string targetUrl, string expectedUrlIdentifier, NavigationOptions options)
//    {
//        if (_page == null || _page.IsClosed)
//        {
//            throw new InvalidOperationException("Page is not available for navigation. A reset might be needed.");
//        }

//        await _page.GoToAsync(targetUrl, options);

//        if (_page.Url.Contains("google.com/sorry/", StringComparison.OrdinalIgnoreCase))
//        {
//            Console.WriteLine($"CAPTCHA detected! Current URL: {_page.Url}");
//            OnCaptchaDetected(_page.Url);
//        }

//        if (_page.Url.Contains(expectedUrlIdentifier))
//        {
//            return true;
//        }
//        else
//        {
//            Console.WriteLine($"Redirection detected! Expected URL to contain '{expectedUrlIdentifier}', but current URL is '{_page.Url}'.");
//            return false;
//        }
//    }

//    public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(string googleOfferUrl)
//    {
//        var scrapedData = new List<CoOfrPriceHistoryClass>();
//        var storeBestOffers = new Dictionary<string, CoOfrPriceHistoryClass>();
//        const int MAX_RETRIES_PER_PAGE = 2;

//        string productId = ExtractProductId(googleOfferUrl);
//        if (string.IsNullOrEmpty(productId))
//        {
//            Console.WriteLine($"Product ID not found in URL: {googleOfferUrl}. Skipping.");
//            return scrapedData;
//        }

//        bool hasNextPage = true;
//        int currentPage = 0;
//        int positionCounter = 1;
//        var navOptions = new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } };

//        while (hasNextPage && currentPage < 3)
//        {
//            string paginatedUrl = $"https://www.google.com/shopping/product/{productId}/offers?prds=cid:{productId},cond:1,start:{currentPage * 20}&gl=pl&hl=pl";
//            if (currentPage == 0)
//            {
//                paginatedUrl = $"https://www.google.com/shopping/product/{productId}/offers?prds=cid:{productId},cond:1&gl=pl&hl=pl";
//            }

//            bool navigationSuccess = false;
//            for (int attempt = 0; attempt <= MAX_RETRIES_PER_PAGE; attempt++)
//            {
//                try
//                {
//                    if (attempt > 0)
//                    {
//                        // ZMIANA: Wywołujemy reset całej przeglądarki
//                        await ResetBrowserAndPageAsync();
//                    }

//                    Console.WriteLine($"Worker {Task.CurrentId}: Navigating to page {currentPage + 1} (Attempt {attempt + 1}). URL: {paginatedUrl}");

//                    if (await TryNavigateAndVerifyUrlAsync(paginatedUrl, productId, navOptions))
//                    {
//                        navigationSuccess = true;
//                        break;
//                    }
//                }
//                catch (CaptchaDetectedException) { throw; }
//                catch (Exception ex)
//                {
//                    Console.WriteLine($"Worker {Task.CurrentId}: An unexpected error occurred during navigation attempt {attempt + 1}: {ex.Message}");
//                }
//            }

//            if (!navigationSuccess)
//            {
//                Console.WriteLine($"FATAL: Worker {Task.CurrentId} failed to load URL for product {productId} after {MAX_RETRIES_PER_PAGE + 1} attempts. Skipping this product.");
//                break;
//            }

//            // Reszta kodu bez zmian...
//            Console.WriteLine($"Worker {Task.CurrentId}: Successfully navigated to page {currentPage + 1}. Scraping data...");
//            if (_expandAndCompareGoogleOffersSetting)
//            {
//                var moreOffersButtons = await _page.QuerySelectorAllAsync("div.cNMlI");
//                if (moreOffersButtons.Any())
//                {
//                    Console.WriteLine($"Found {moreOffersButtons.Length} 'More offers' buttons. Clicking to expand.");
//                    foreach (var button in moreOffersButtons)
//                    {
//                        try { await button.ClickAsync(); await Task.Delay(500); }
//                        catch (Exception ex) { Console.WriteLine($"Error clicking 'More offers' button: {ex.Message}"); }
//                    }
//                }
//                else { Console.WriteLine("No 'More offers' (div.cNMlI) buttons found to expand."); }
//            }
//            else { Console.WriteLine("Skipping click on 'More offers' (div.cNMlI) as ExpandAndCompareGoogleOffers is false."); }

//            var offerRowsSelector = "#sh-osd__online-sellers-cont > tr";
//            var offerRows = await _page.QuerySelectorAllAsync(offerRowsSelector);
//            var offersCountOnPage = offerRows.Length;
//            Console.WriteLine($"Found {offersCountOnPage} offers on current page.");

//            if (offersCountOnPage == 0 && currentPage > 0) { Console.WriteLine("No offers found on this page, likely end of results."); hasNextPage = false; continue; }
//            if (offersCountOnPage == 0 && currentPage == 0) { Console.WriteLine("No offers found on the first page. Product might have no offers."); break; }

//            for (int i = 1; i <= offersCountOnPage; i++)
//            {
//                var storeNameSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";
//                var priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > span";
//                var priceWithDeliverySelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(5) > div > div.drzWO";
//                var storeNameElement = await _page.QuerySelectorAsync(storeNameSelector);
//                if (storeNameElement != null)
//                {
//                    var storeName = await storeNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
//                    var offerUrl = await storeNameElement.EvaluateFunctionAsync<string>("node => node.href");
//                    if (IsOutletOffer(offerUrl)) { Console.WriteLine($"Store: {storeName} - Offer is outlet, skipping."); continue; }
//                    var priceElement = await _page.QuerySelectorAsync(priceSelector);
//                    var priceText = priceElement != null ? await priceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "No price";
//                    var priceWithDeliveryElement = await _page.QuerySelectorAsync(priceWithDeliverySelector);
//                    var priceWithDeliveryText = priceWithDeliveryElement != null ? await priceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : priceText;
//                    var priceDecimal = ExtractPrice(priceText);
//                    var priceWithDeliveryDecimal = ExtractPrice(priceWithDeliveryText);

//                    // <<< NOWY LOG DIAGNOSTYCZNY >>>
//                    Console.WriteLine($"[DEBUG-OFFER] Store: '{storeName}', RawPrice: '{priceText}', ParsedPrice: {priceDecimal}");

//                    var currentPositionInPage = positionCounter++;
//                    var offer = new CoOfrPriceHistoryClass { GoogleStoreName = storeName, GooglePrice = priceDecimal, GooglePriceWithDelivery = priceWithDeliveryDecimal, GooglePosition = currentPositionInPage.ToString() };
//                    if (storeBestOffers.TryGetValue(storeName, out var existingOffer)) { if (priceWithDeliveryDecimal < existingOffer.GooglePriceWithDelivery) { storeBestOffers[storeName] = offer; } }
//                    else { storeBestOffers[storeName] = offer; }
//                }
//            }
//            if (_expandAndCompareGoogleOffersSetting)
//            {
//                Console.WriteLine("Processing hidden/expanded offers...");
//                var hiddenOfferRowsSelector = "tr.sh-osd__offer-row[data-hveid][data-is-grid-offer='true']";
//                var hiddenOfferRows = await _page.QuerySelectorAllAsync(hiddenOfferRowsSelector);
//                if (hiddenOfferRows.Any())
//                {
//                    Console.WriteLine($"Found {hiddenOfferRows.Length} hidden/expanded offer rows.");
//                    foreach (var hiddenRowElement in hiddenOfferRows)
//                    {
//                        var hiddenStoreNameSelector = "td:nth-child(1) > div._-ez > a";
//                        var hiddenPriceSelector = "td:nth-child(4) > span";
//                        var hiddenPriceWithDeliverySelector = "td:nth-child(5) > div > div._-f3";
//                        var hiddenStoreNameElement = await hiddenRowElement.QuerySelectorAsync(hiddenStoreNameSelector);
//                        if (hiddenStoreNameElement != null)
//                        {
//                            var hiddenStoreName = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
//                            var hiddenOfferUrl = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.href");
//                            if (IsOutletOffer(hiddenOfferUrl)) { Console.WriteLine($"Hidden Store: {hiddenStoreName} - Offer is outlet, skipping."); continue; }
//                            var hiddenPriceElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceSelector);
//                            var hiddenPriceText = hiddenPriceElement != null ? await hiddenPriceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "No price";
//                            var hiddenPriceWithDeliveryElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceWithDeliverySelector);
//                            var hiddenPriceWithDeliveryText = hiddenPriceWithDeliveryElement != null ? await hiddenPriceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : hiddenPriceText;
//                            var hiddenPriceDecimal = ExtractPrice(hiddenPriceText);
//                            var hiddenPriceWithDeliveryDecimal = ExtractPrice(hiddenPriceWithDeliveryText);

//                            // <<< NOWY LOG DIAGNOSTYCZNY >>>
//                            Console.WriteLine($"[DEBUG-HIDDEN-OFFER] Store: '{hiddenStoreName}', RawPrice: '{hiddenPriceText}', ParsedPrice: {hiddenPriceDecimal}");

//                            var currentHiddenPosition = positionCounter++;
//                            var offer = new CoOfrPriceHistoryClass { GoogleStoreName = hiddenStoreName, GooglePrice = hiddenPriceDecimal, GooglePriceWithDelivery = hiddenPriceWithDeliveryDecimal, GooglePosition = currentHiddenPosition.ToString() };
//                            if (storeBestOffers.TryGetValue(hiddenStoreName, out var existingOffer)) { if (hiddenPriceWithDeliveryDecimal < existingOffer.GooglePriceWithDelivery) { storeBestOffers[hiddenStoreName] = offer; Console.WriteLine($"Updated hidden offer for {hiddenStoreName}: Price {hiddenPriceDecimal}, Delivery {hiddenPriceWithDeliveryDecimal}, Position {currentHiddenPosition}"); } }
//                            else { storeBestOffers[hiddenStoreName] = offer; Console.WriteLine($"Added new hidden offer for {hiddenStoreName}: Price {hiddenPriceDecimal}, Delivery {hiddenPriceWithDeliveryDecimal}, Position {currentHiddenPosition}"); }
//                        }
//                    }
//                }
//                else { Console.WriteLine("No hidden/expanded offer rows found with the specified selector."); }
//            }
//            else { Console.WriteLine("Skipping processing of hidden/expanded offers as ExpandAndCompareGoogleOffers is false."); }


//            var paginationElement = await _page.QuerySelectorAsync("#sh-fp__pagination-button-wrapper");
//            if (paginationElement != null)
//            {
//                var nextPageElement = await paginationElement.QuerySelectorAsync("a.internal-link[data-url*='start']");
//                if (nextPageElement != null) { currentPage++; Console.WriteLine($"Moving to next page (old logic): {currentPage}"); hasNextPage = true; }
//                else { Console.WriteLine("No next page link found with old logic (a.internal-link[data-url*='start'])."); hasNextPage = false; }
//            }
//            else { Console.WriteLine("Pagination element #sh-fp__pagination-button-wrapper not found (old logic)."); hasNextPage = false; }
//        }

//        scrapedData.AddRange(storeBestOffers.Values);
//        Console.WriteLine($"Finished processing for {googleOfferUrl}. Found {scrapedData.Count} unique store offers.");
//        return scrapedData;
//    }

//    private string ExtractProductId(string url)
//    {
//        if (string.IsNullOrEmpty(url)) return string.Empty;
//        var match = Regex.Match(url, @"product/(\d+)");
//        return match.Success ? match.Groups[1].Value : string.Empty;
//    }

//    private decimal ExtractPrice(string priceText)
//    {
//        if (string.IsNullOrWhiteSpace(priceText)) return 0;
//        try
//        {
//            var sanitizedPriceText = Regex.Replace(priceText, @"[^\d\s,.]", "");
//            var textWithDot = sanitizedPriceText.Replace(",", ".");
//            var noSpacesText = Regex.Replace(textWithDot, @"\s+", "");
//            string finalPriceToParse = noSpacesText;
//            int lastDotIndex = noSpacesText.LastIndexOf('.');
//            if (lastDotIndex != -1)
//            {
//                if (noSpacesText.IndexOf('.') < lastDotIndex)
//                {
//                    string integerPart = noSpacesText.Substring(0, lastDotIndex).Replace(".", "");
//                    string decimalPart = noSpacesText.Substring(lastDotIndex + 1);
//                    finalPriceToParse = $"{integerPart}.{decimalPart}";
//                }
//            }
//            if (decimal.TryParse(finalPriceToParse, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal priceDecimal))
//            {
//                return priceDecimal;
//            }
//            else
//            {
//                Console.WriteLine($"Failed to parse price. Original: '{priceText}' | Sanitized: '{sanitizedPriceText}' | WithDot: '{textWithDot}' | NoSpaces: '{noSpacesText}' | FinalToParse: '{finalPriceToParse}'");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error extracting price from '{priceText}': {ex.Message}");
//        }
//        return 0;
//    }

//    private bool IsOutletOffer(string url)
//    {
//        return !string.IsNullOrEmpty(url) && url.Contains("outlet", StringComparison.OrdinalIgnoreCase);
//    }

//    public async Task CloseAsync()
//    {
//        Console.WriteLine("Attempting to close scraper resources...");
//        try
//        {
//            if (_page != null && !_page.IsClosed)
//            {
//                await _page.CloseAsync();
//                Console.WriteLine("Page closed.");
//            }
//            _page = null;
//        }
//        catch (Exception ex) { Console.WriteLine($"Error closing page: {ex.Message}"); }

//        try
//        {
//            if (_browser != null && _browser.IsConnected)
//            {
//                await _browser.CloseAsync();
//                Console.WriteLine("Browser closed.");
//            }
//            _browser = null;
//        }
//        catch (Exception ex) { Console.WriteLine($"Error closing browser: {ex.Message}"); }
//        Console.WriteLine("Scraper resources closing attempt finished.");
//    }
//}



using PriceSafari.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

// Rekord do przechowywania tymczasowych danych z nowego API
public record TempOffer(string Seller, string Price, string Url, string? Delivery, string Condition);

public class GoogleMainPriceScraper
{
    private static readonly HttpClient _httpClient;

    static GoogleMainPriceScraper()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.0.0 Safari/537.36"
        );
    }

    public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(CoOfrClass coOfr)
    {
        var finalPriceHistory = new List<CoOfrPriceHistoryClass>();

        // Ta metoda musi istnieć, aby poniższa linia działała
        string? catalogId = ExtractProductId(coOfr.GoogleOfferUrl);

        if (string.IsNullOrEmpty(catalogId))
        {
            Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {coOfr.GoogleOfferUrl}");
            return finalPriceHistory;
        }

        string urlTemplate = $"https://www.google.com/async/oapv?udm=28&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";

        var allFoundOffers = new List<TempOffer>();
        int startIndex = 0;
        const int pageSize = 10;
        int lastFetchCount;
        const int maxRetries = 3;

        do
        {
            string currentUrl = string.Format(urlTemplate, startIndex);
            List<TempOffer> newOffers = new List<TempOffer>();

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    string rawResponse = await _httpClient.GetStringAsync(currentUrl);
                    newOffers = GoogleShoppingApiParser.Parse(rawResponse);
                    if (newOffers.Any()) break;
                    if (attempt < maxRetries) await Task.Delay(2000);
                }
                catch (HttpRequestException)
                {
                    if (attempt == maxRetries)
                    {
                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Nie udało się pobrać ofert z {currentUrl} po {maxRetries} próbach.");
                    }
                    else
                    {
                        await Task.Delay(2500);
                    }
                }
            }

            lastFetchCount = newOffers.Count;
            allFoundOffers.AddRange(newOffers);
            startIndex += pageSize;

            if (lastFetchCount == pageSize)
            {
                await Task.Delay(new Random().Next(800, 1500));
            }

        } while (lastFetchCount == pageSize);

        var acceptedOffers = allFoundOffers.Where(o => o.Condition == "Nowy").ToList();
        var groupedBySeller = acceptedOffers.GroupBy(o => o.Seller);
        var finalList = new List<TempOffer>();

        foreach (var group in groupedBySeller)
        {
            var cheapestOffer = group.OrderBy(o => ParsePrice(o.Price)).First();
            finalList.Add(cheapestOffer);
        }

        int positionCounter = 1;
        foreach (var offer in finalList.OrderBy(o => ParsePrice(o.Price)))
        {
            finalPriceHistory.Add(new CoOfrPriceHistoryClass
            {
                GoogleStoreName = offer.Seller,
                GooglePrice = ParsePrice(offer.Price),
                GooglePriceWithDelivery = ParsePrice(offer.Price) + ParseDeliveryPrice(offer.Delivery),
                GooglePosition = (positionCounter++).ToString(),
            });
        }

        return finalPriceHistory;
    }

    #region Helper Methods

    // ### POPRAWKA ###
    // Ta metoda została ponownie dodana.
    private string? ExtractProductId(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var match = Regex.Match(url, @"product/(\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private decimal ParsePrice(string priceText)
    {
        if (string.IsNullOrWhiteSpace(priceText)) return 0;
        var cleanedText = Regex.Replace(priceText, @"[^\d,.]", "").Replace(",", ".");
        if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
        {
            return result;
        }
        return 0;
    }

    private decimal ParseDeliveryPrice(string? deliveryText)
    {
        if (string.IsNullOrWhiteSpace(deliveryText) || deliveryText.ToLower().Contains("bezpłatna") || deliveryText.ToLower().Contains("darmowa"))
        {
            return 0;
        }
        return ParsePrice(deliveryText);
    }

    #endregion
}

// Druga klasa (parser) pozostaje bez zmian
public static class GoogleShoppingApiParser
{
    private static readonly Regex PricePattern = new(@"\d[\d\s,.]*\s*(?:PLN|zł)", RegexOptions.Compiled);

    public static List<TempOffer> Parse(string rawResponse)
    {
        string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
        using JsonDocument doc = JsonDocument.Parse(cleanedJson);
        var allOffers = new List<TempOffer>();
        JsonElement? offersContainer = FindOffersContainer(doc.RootElement.Clone());

        if (offersContainer.HasValue)
        {
            foreach (JsonElement potentialOffer in offersContainer.Value.EnumerateArray())
            {
                TempOffer? offer = ParseSingleOffer(potentialOffer);
                if (offer != null) allOffers.Add(offer);
            }
        }
        return allOffers;
    }

    private static JsonElement? FindOffersContainer(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            int offerLikeChildren = node.EnumerateArray().Count(IsPotentialSingleOffer);
            if (offerLikeChildren > 5) return node;
        }

        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in node.EnumerateArray())
            {
                var found = FindOffersContainer(element);
                if (found.HasValue) return found;
            }
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                var found = FindOffersContainer(property.Value);
                if (found.HasValue) return found;
            }
        }
        return null;
    }

    private static bool IsPotentialSingleOffer(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() < 5) return false;
        bool hasUrl = false, hasPrice = false;
        foreach (var item in Flatten(node))
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string s = item.GetString()!;
                if (!hasUrl && s.StartsWith("http")) hasUrl = true;
                if (!hasPrice && PricePattern.IsMatch(s)) hasPrice = true;
            }
            if (hasUrl && hasPrice) return true;
        }
        return false;
    }

    private static TempOffer? ParseSingleOffer(JsonElement offerData)
    {
        string? seller = null, price = null, url = null, delivery = null;
        string condition = "Nowy";

        try
        {
            var flatStrings = Flatten(offerData)
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();

            url = flatStrings.FirstOrDefault(s => s.StartsWith("https://") && !s.Contains("google.com") && !s.Contains("gstatic.com"));
            price = flatStrings.FirstOrDefault(s => PricePattern.IsMatch(s) && !s.Trim().StartsWith("+"));

            string? rawDeliveryText = flatStrings.FirstOrDefault(s => s.Trim().StartsWith("+") && PricePattern.IsMatch(s));
            if (rawDeliveryText == null)
            {
                rawDeliveryText = flatStrings.FirstOrDefault(s => s.Contains("dostawa", StringComparison.OrdinalIgnoreCase) || s.Contains("delivery", StringComparison.OrdinalIgnoreCase));
            }

            if (rawDeliveryText != null)
            {
                if (rawDeliveryText.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) || rawDeliveryText.Contains("Free", StringComparison.OrdinalIgnoreCase) || rawDeliveryText.Contains("Darmowa", StringComparison.OrdinalIgnoreCase))
                    delivery = "Bezpłatna";
                else
                {
                    Match priceMatch = PricePattern.Match(rawDeliveryText);
                    if (priceMatch.Success) delivery = priceMatch.Value.Trim();
                }
            }

            var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony" };
            if (flatStrings.Any(text => usedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
            {
                condition = "Używany/Inny";
            }

            if (offerData.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in offerData.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 1 && item[0].ValueKind == JsonValueKind.String && item[1].ValueKind == JsonValueKind.String && item[1].GetString()!.All(char.IsDigit))
                    {
                        seller = item[0].GetString();
                        break;
                    }
                }
            }

            if (seller != null && price != null && url != null)
            {
                return new TempOffer(seller, price, url, delivery, condition);
            }
        }
        catch { /* Ignorujemy błędy parsowania pojedynczej oferty */ }

        return null;
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement node)
    {
        var stack = new Stack<JsonElement>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
            }
            else if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
            }
            else
            {
                yield return current;
            }
        }
    }
}