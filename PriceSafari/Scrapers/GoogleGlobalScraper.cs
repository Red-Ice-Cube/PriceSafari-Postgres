//using PriceSafari.Models;
//using PuppeteerSharp;
//using System.Globalization;
//using System.Text.RegularExpressions;

//namespace PriceSafari.Scrapers
//{
//    public class GooglePriceScraper
//    {
//        private Browser _browser;
//        private Page _page;
//        private Settings _scraperSettings;

//        private async Task LaunchNewBrowserAsync()
//        {
//            var browserFetcher = new BrowserFetcher();
//            await browserFetcher.DownloadAsync();

//            _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
//            {
//                Headless = _scraperSettings.HeadLess,
//                Args = new[]
//                {
//                    "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu",
//                    "--disable-blink-features=AutomationControlled", "--disable-software-rasterizer",
//                    "--disable-extensions", "--disable-dev-shm-usage",
//                    "--disable-features=IsolateOrigins,site-per-process", "--disable-infobars"
//                }
//            });

//            _page = (Page)await _browser.NewPageAsync();
//            Console.WriteLine("Przeglądarka zainicjalizowana.");
//        }

//        public async Task InitializeAsync(Settings settings)
//        {
//            _scraperSettings = settings;
//            await LaunchNewBrowserAsync();
//        }

//        public async Task ResetBrowserAndPageAsync()
//        {
//            Console.WriteLine("Wykonywanie PEŁNEGO RESETU PRZEGLĄDARKI...");
//            await CloseAsync();
//            await LaunchNewBrowserAsync();
//            Console.WriteLine("Pełny reset przeglądarki zakończony.");
//        }

//        private async Task<bool> TryNavigateAndVerifyUrlAsync(string targetUrl, string expectedUrlIdentifier, NavigationOptions options)
//        {
//            if (_page == null || _page.IsClosed)
//            {
//                throw new InvalidOperationException("Strona jest niedostępna. Reset może być konieczny.");
//            }

//            await _page.GoToAsync(targetUrl, options);

//            if (_page.Url.Contains(expectedUrlIdentifier))
//            {
//                return true;
//            }
//            else
//            {
//                Console.WriteLine($"Wykryto przekierowanie! Oczekiwano URL zawierającego '{expectedUrlIdentifier}', ale finalny URL to: '{_page.Url}'.");
//                return false;
//            }
//        }

//        public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct, string countryCode)
//        {
//            var scrapedData = new List<PriceData>();
//            var storeBestOffers = new Dictionary<string, PriceData>();
//            const int MAX_RETRIES_PER_PAGE = 2;

//            string productId = ExtractProductId(scrapingProduct.GoogleUrl);
//            if (string.IsNullOrEmpty(productId))
//            {
//                Console.WriteLine($"Nie można wyodrębnić ID produktu z URL: {scrapingProduct.GoogleUrl}");
//                return scrapedData;
//            }

//            bool hasNextPage = true;
//            int totalOffersCount = 0;
//            int currentPage = 0;
//            var navOptions = new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } };

//            try
//            {
//                while (hasNextPage && currentPage < 3)
//                {
//                    string paginatedUrl = $"https://www.google.com/shopping/product/{productId}/offers?prds=cid:{productId},cond:1,start:{currentPage * 20}&gl={countryCode}&hl=pl";
//                    if (currentPage == 0)
//                    {
//                        paginatedUrl = $"https://www.google.com/shopping/product/{productId}/offers?prds=cid:{productId},cond:1&gl={countryCode}&hl=pl";
//                    }

//                    bool navigationSuccess = false;
//                    for (int attempt = 0; attempt <= MAX_RETRIES_PER_PAGE; attempt++)
//                    {
//                        try
//                        {
//                            if (attempt > 0)
//                            {
//                                await ResetBrowserAndPageAsync();
//                            }

//                            Console.WriteLine($"Nawigacja do strony {currentPage + 1} (Próba {attempt + 1}): {paginatedUrl}");

//                            if (await TryNavigateAndVerifyUrlAsync(paginatedUrl, productId, navOptions))
//                            {
//                                navigationSuccess = true;
//                                break;
//                            }
//                        }
//                        catch (Exception navEx)
//                        {
//                            Console.WriteLine($"Błąd krytyczny podczas próby nawigacji ({attempt + 1}): {navEx.Message}");
//                        }
//                    }

//                    if (!navigationSuccess)
//                    {
//                        Console.WriteLine($"BŁĄD KRYTYCZNY: Nie udało się załadować strony dla produktu {productId} po {MAX_RETRIES_PER_PAGE + 1} próbach. Pomijam produkt.");
//                        break;
//                    }

//                    await Task.Delay(1111);

//                    var moreOffersButtons = await _page.QuerySelectorAllAsync("div.cNMlI");
//                    if (moreOffersButtons.Length > 0)
//                    {
//                        foreach (var button in moreOffersButtons)
//                        {
//                            Console.WriteLine("Znaleziono przycisk 'Jeszcze oferty'. Klikam, aby rozwinąć.");
//                            await button.ClickAsync();
//                            await Task.Delay(557);
//                        }
//                    }

//                    var offerRowsSelector = "#sh-osd__online-sellers-cont > tr";
//                    var offerRows = await _page.QuerySelectorAllAsync(offerRowsSelector);
//                    var offersCount = offerRows.Length;
//                    totalOffersCount += offersCount;

//                    if (offersCount == 0)
//                    {
//                        Console.WriteLine("Brak ofert na stronie.");
//                        break;
//                    }

//                    Console.WriteLine($"Znaleziono {offersCount} ofert. Rozpoczynam scrapowanie...");

//                    for (int i = 1; i <= offersCount; i++)
//                    {
//                        var storeNameSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";
//                        var priceSelector = "";
//                        var priceWithDeliverySelector = "";

//                        if (countryCode == "ua" || countryCode == "tr" || countryCode == "by")
//                        {
//                            priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(3) > span";
//                            priceWithDeliverySelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > div > div.drzWO";
//                        }
//                        else
//                        {
//                            priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > span";
//                            priceWithDeliverySelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(5) > div > div.drzWO";
//                        }

//                        var offerUrlSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";
//                        var storeNameElement = await _page.QuerySelectorAsync(storeNameSelector);
//                        if (storeNameElement != null)
//                        {
//                            var storeName = await storeNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
//                            var priceElement = await _page.QuerySelectorAsync(priceSelector);
//                            var priceText = priceElement != null ? await priceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "Brak ceny";
//                            var priceWithDeliveryElement = await _page.QuerySelectorAsync(priceWithDeliverySelector);
//                            var priceWithDeliveryText = priceWithDeliveryElement != null ? await priceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : priceText;
//                            var priceDecimal = ExtractPrice(priceText);
//                            var priceWithDeliveryDecimal = ExtractPrice(priceWithDeliveryText);
//                            var offerUrlElement = await _page.QuerySelectorAsync(offerUrlSelector);
//                            var offerUrl = offerUrlElement != null ? await offerUrlElement.EvaluateFunctionAsync<string>("node => node.href") : "Brak URL";

//                            if (IsOutletOffer(offerUrl))
//                            {
//                                Console.WriteLine("Oferta outlet, pomijam.");
//                                continue;
//                            }

//                            if (storeBestOffers.ContainsKey(storeName))
//                            {
//                                if (priceWithDeliveryDecimal < storeBestOffers[storeName].PriceWithDelivery)
//                                {
//                                    storeBestOffers[storeName].Price = priceDecimal;
//                                    storeBestOffers[storeName].PriceWithDelivery = priceWithDeliveryDecimal;
//                                }
//                            }
//                            else
//                            {
//                                storeBestOffers[storeName] = new PriceData { StoreName = storeName, Price = priceDecimal, PriceWithDelivery = priceWithDeliveryDecimal, OfferUrl = offerUrl, ScrapingProductId = scrapingProduct.ScrapingProductId, RegionId = scrapingProduct.RegionId };
//                            }
//                        }
//                    }

//                    var hiddenOfferRowsSelector = "tr.sh-osd__offer-row[data-hveid][data-is-grid-offer='true']";
//                    var hiddenOfferRows = await _page.QuerySelectorAllAsync(hiddenOfferRowsSelector);
//                    for (int j = 0; j < hiddenOfferRows.Length; j++)
//                    {
//                        var hiddenRowElement = hiddenOfferRows[j];

//                        var hiddenStoreNameSelector = "td:nth-child(1) > div._-ez > a";
//                        var hiddenPriceSelector = "";
//                        var hiddenPriceWithDeliverySelector = "";
//                        var hiddenOfferUrlSelector = "";

//                        if (countryCode == "ua" || countryCode == "tr" || countryCode == "by")
//                        {

//                            hiddenPriceSelector = "td:nth-child(3) > span";
//                            hiddenPriceWithDeliverySelector = "td:nth-child(4) > div";
//                            hiddenOfferUrlSelector = "td:nth-child(5) > div > a";

//                        }
//                        else
//                        {

//                            hiddenPriceSelector = "td:nth-child(4) > span";
//                            hiddenPriceWithDeliverySelector = "td:nth-child(5) > div";
//                            hiddenOfferUrlSelector = "td:nth-child(6) > div > a";

//                        }

//                        var hiddenStoreNameElement = await hiddenRowElement.QuerySelectorAsync(hiddenStoreNameSelector);
//                        if (hiddenStoreNameElement != null)
//                        {
//                            var hiddenStoreName = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
//                            Console.WriteLine($"Znaleziono ukryty sklep: {hiddenStoreName}");

//                            var hiddenPriceElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceSelector);
//                            var hiddenPriceText = hiddenPriceElement != null ? await hiddenPriceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "Brak ceny";
//                            Console.WriteLine($"Ukryta cena: {hiddenPriceText}");

//                            var hiddenPriceWithDeliveryElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceWithDeliverySelector);
//                            var hiddenPriceWithDeliveryText = hiddenPriceWithDeliveryElement != null ? await hiddenPriceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : hiddenPriceText;
//                            Console.WriteLine($"Ukryta cena z dostawą: {hiddenPriceWithDeliveryText}");

//                            var hiddenPriceDecimal = ExtractPrice(hiddenPriceText);
//                            var hiddenPriceWithDeliveryDecimal = ExtractPrice(hiddenPriceWithDeliveryText);

//                            var hiddenOfferUrlElement = await hiddenRowElement.QuerySelectorAsync(hiddenOfferUrlSelector);
//                            var hiddenOfferUrl = hiddenOfferUrlElement != null ? await hiddenOfferUrlElement.EvaluateFunctionAsync<string>("node => node.href") : "Brak URL";
//                            Console.WriteLine($"Ukryty URL oferty: {hiddenOfferUrl}");

//                            if (IsOutletOffer(hiddenOfferUrl))
//                            {
//                                Console.WriteLine("Ukryta oferta outlet, pomijam.");
//                                continue;
//                            }

//                            if (storeBestOffers.ContainsKey(hiddenStoreName))
//                            {
//                                var existingOffer = storeBestOffers[hiddenStoreName];
//                                if (hiddenPriceWithDeliveryDecimal < existingOffer.PriceWithDelivery)
//                                {
//                                    storeBestOffers[hiddenStoreName] = new PriceData
//                                    {
//                                        StoreName = hiddenStoreName,
//                                        Price = hiddenPriceDecimal,
//                                        PriceWithDelivery = hiddenPriceWithDeliveryDecimal,
//                                        OfferUrl = hiddenOfferUrl,
//                                        ScrapingProductId = scrapingProduct.ScrapingProductId,
//                                        RegionId = scrapingProduct.RegionId,
//                                    };
//                                }
//                            }
//                            else
//                            {
//                                storeBestOffers[hiddenStoreName] = new PriceData
//                                {
//                                    StoreName = hiddenStoreName,
//                                    Price = hiddenPriceDecimal,
//                                    PriceWithDelivery = hiddenPriceWithDeliveryDecimal,
//                                    OfferUrl = hiddenOfferUrl,
//                                    ScrapingProductId = scrapingProduct.ScrapingProductId,
//                                    RegionId = scrapingProduct.RegionId,
//                                };
//                            }

//                        }
//                    }

//                    var paginationElement = await _page.QuerySelectorAsync("#sh-fp__pagination-button-wrapper");
//                    if (paginationElement != null)
//                    {
//                        var nextPageElement = await paginationElement.QuerySelectorAsync("a.internal-link[data-url*='start']");
//                        if (nextPageElement != null)
//                        {
//                            currentPage++;
//                            Console.WriteLine($"Przechodzę do następnej strony: {currentPage}");
//                            hasNextPage = true;
//                        }
//                        else
//                        {
//                            Console.WriteLine("Brak kolejnej strony.");
//                            hasNextPage = false;
//                        }
//                    }
//                    else
//                    {
//                        Console.WriteLine("Nie znaleziono elementu paginacji.");
//                        hasNextPage = false;
//                    }

//                    await Task.Delay(125);
//                }

//                scrapedData.AddRange(storeBestOffers.Values);
//                Console.WriteLine($"Zakończono przetwarzanie {scrapedData.Count} ofert.");
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Błąd podczas scrapowania: {ex.Message}");
//            }

//            scrapingProduct.OffersCount = totalOffersCount;
//            return scrapedData;
//        }

//        private string ExtractProductId(string url)
//        {

//            var match = Regex.Match(url, @"product/(\d+)");
//            if (match.Success)
//            {
//                return match.Groups[1].Value;
//            }
//            return string.Empty;
//        }

//        private bool IsOutletOffer(string url)
//        {
//            return !string.IsNullOrEmpty(url) && url.IndexOf("outlet", StringComparison.OrdinalIgnoreCase) >= 0;
//        }

//        private decimal ExtractPrice(string priceText)
//        {
//            try
//            {

//                var priceMatch = Regex.Match(priceText, @"[\d\s,]+");
//                if (priceMatch.Success)
//                {

//                    var priceString = priceMatch.Value.Replace(" ", "").Replace(",", ".");
//                    if (decimal.TryParse(priceString, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal priceDecimal))
//                    {
//                        return priceDecimal;
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Błąd podczas przetwarzania ceny: {ex.Message}");
//            }

//            return 0;
//        }

//        public async Task CloseAsync()
//        {
//            try
//            {
//                if (_page != null && !_page.IsClosed)
//                {
//                    await _page.CloseAsync();
//                }
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Nieszkodliwy błąd podczas zamykania strony: {ex.Message}");
//            }

//            try
//            {
//                if (_browser != null && _browser.IsConnected)
//                {
//                    await _browser.CloseAsync();
//                }
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"Nieszkodliwy błąd podczas zamykania przeglądarki: {ex.Message}");
//            }
//            Console.WriteLine("Zasoby przeglądarki zamknięte.");
//        }
//    }
//}









































//using PriceSafari.Models;
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net.Http;
//using System.Text.Json;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//public record TempOfferGlobal(string Seller, string Price, string Url, string? Delivery, bool IsInStock);

//public class GoogleGlobalScraper
//{
//    private static readonly HttpClient _httpClient;

//    static GoogleGlobalScraper()
//    {
//        _httpClient = new HttpClient();
//        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.0.0 Safari/537.36");
//    }

//    public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct, string countryCode)
//    {
//        var finalPriceData = new List<PriceData>();
//        string? catalogId = ExtractProductId(scrapingProduct.GoogleUrl);

//        if (string.IsNullOrEmpty(catalogId))
//        {
//            Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {scrapingProduct.GoogleUrl}");
//            return finalPriceData;
//        }

//        string urlTemplate = $"https://www.google.com/async/oapv?udm=28&gl={countryCode}&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";
//        Console.WriteLine($"[GoogleGlobalScraper] Używam CID: {catalogId} dla kraju: {countryCode} | URL: {string.Format(urlTemplate, 0)}");

//        var allFoundOffers = new List<TempOfferGlobal>();
//        int startIndex = 0;
//        const int pageSize = 10;
//        int lastFetchCount;
//        const int maxRetries = 3;

//        do
//        {
//            string currentUrl = string.Format(urlTemplate, startIndex);
//            List<TempOfferGlobal> newOffers = new List<TempOfferGlobal>();
//            bool requestSucceeded = false;

//            for (int attempt = 1; attempt <= maxRetries; attempt++)
//            {
//                try
//                {
//                    Console.WriteLine($"[GoogleGlobalScraper] Próba pobrania strony {startIndex / pageSize + 1} (attempt {attempt})...");
//                    string rawResponse = await _httpClient.GetStringAsync(currentUrl);

//                    newOffers = GoogleShoppingApiParserGlobal.Parse(rawResponse);

//                    Console.WriteLine($"[GoogleGlobalScraper] Parser zwrócił {newOffers.Count} ofert.");

//                    if (newOffers.Any() || rawResponse.Length < 100)
//                    {
//                        requestSucceeded = true;
//                        break;
//                    }
//                    if (attempt < maxRetries) await Task.Delay(600);
//                }
//                catch (HttpRequestException ex)
//                {
//                    if (attempt == maxRetries)
//                    {
//                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Nie udało się pobrać ofert z {currentUrl} po {maxRetries} próbach. Błąd: {ex.Message}");
//                    }
//                    else
//                    {
//                        await Task.Delay(700);
//                    }
//                }
//            }

//            if (!requestSucceeded)
//            {
//                Console.WriteLine($"[GoogleGlobalScraper] Żądanie nie powiodło się dla {currentUrl}. Przerywam paginację.");
//                break;
//            }

//            lastFetchCount = newOffers.Count;
//            allFoundOffers.AddRange(newOffers);
//            startIndex += pageSize;

//            if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(50, 80));

//        } while (lastFetchCount == pageSize);

//        var groupedBySeller = allFoundOffers.GroupBy(o => o.Seller);

//        foreach (var group in groupedBySeller)
//        {
//            var cheapestOffer = group.OrderBy(o => ParsePrice(o.Price)).First();

//            finalPriceData.Add(new PriceData
//            {
//                StoreName = cheapestOffer.Seller,
//                Price = ParsePrice(cheapestOffer.Price),
//                PriceWithDelivery = ParsePrice(cheapestOffer.Price) + ParseDeliveryPrice(cheapestOffer.Delivery),
//                OfferUrl = cheapestOffer.Url,
//                ScrapingProductId = scrapingProduct.ScrapingProductId,
//                RegionId = scrapingProduct.RegionId
//            });
//        }

//        Console.WriteLine($"[GoogleGlobalScraper] Zakończono dla CID: {catalogId}. Znaleziono {finalPriceData.Count} unikalnych ofert sprzedawców.");
//        return finalPriceData;
//    }

//    #region Helper Methods

//    private string? ExtractProductId(string url)
//    {
//        if (string.IsNullOrEmpty(url)) return null;
//        var match = Regex.Match(url, @"product/(\d+)");
//        return match.Success ? match.Groups[1].Value : null;
//    }

//    private decimal ParsePrice(string priceText)
//    {
//        if (string.IsNullOrWhiteSpace(priceText)) return 0;

//        var cleanedText = Regex.Replace(priceText, @"[^\d,.]", "");

//        bool hasComma = cleanedText.Contains(',');
//        bool hasDot = cleanedText.Contains('.');

//        if (hasDot && hasComma)
//        {

//            if (cleanedText.LastIndexOf('.') < cleanedText.LastIndexOf(','))
//            {
//                cleanedText = cleanedText.Replace(".", "").Replace(",", ".");
//            }

//            else
//            {
//                cleanedText = cleanedText.Replace(",", "");
//            }
//        }

//        else if (hasComma)
//        {

//            if (cleanedText.Count(c => c == ',') > 1)
//            {
//                cleanedText = cleanedText.Replace(",", "");
//            }

//            else
//            {
//                cleanedText = cleanedText.Replace(",", ".");
//            }
//        }

//        else if (hasDot)
//        {

//            if (cleanedText.Count(c => c == '.') > 1)
//            {
//                cleanedText = cleanedText.Replace(".", "");
//            }

//        }

//        if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
//        {
//            return result;
//        }

//        Console.WriteLine($"[ParsePrice] BŁĄD: Nie udało się sparsować '{priceText}' (po czyszczeniu: '{cleanedText}')");
//        return 0;
//    }

//    private decimal ParseDeliveryPrice(string? deliveryText)
//    {
//        if (string.IsNullOrWhiteSpace(deliveryText) ||
//            deliveryText.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Darmowa", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Free", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Kostenlos", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Gratuit", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Zdarma", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Gratis", StringComparison.OrdinalIgnoreCase))
//        {
//            return 0;
//        }

//        return ParsePrice(deliveryText);
//    }
//    #endregion
//}

//public static class GoogleShoppingApiParserGlobal
//{

//    private static readonly Regex PricePattern = new(

//        @"^(\+)?\s*([\d][\d\s,.]*[\d])\s+([A-Z]{3}|\p{Sc}|zł|\?)\s*$"
//        + "|" +

//        @"^(\+)?\s*([A-Z]{3}|\p{Sc}|zł|\?)\s*([\d][\d\s,.]*[\d])\s*$",
//        RegexOptions.Compiled | RegexOptions.IgnoreCase);

//    private static readonly string[] deliveryKeywords = new[] { "dostawa", "delivery", "doprava", "lieferung", "shipping", "livraison", "versand" };
//    private static readonly string[] freeKeywords = new[] { "bezpłatna", "darmowa", "free", "zdarma", "kostenlos", "gratuit", "gratis" };

//    private static readonly HashSet<string> sellerStopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
//    {
//        "EUR", "PLN", "CZK", "USD", "GBP", "CHF",
//        "en_US", "de_DE", "pl_PL",
//        "hg", "pvflt", "damc",
//        "Auf Lager", "Online auf Lager", "W magazynie",
//        "Lieferung:", "Aktueller Preis:", "Typ", "Marke", "Farbe",
//        "Fassungsvermögen", "Images", "Title", "ScoreCard", "Variants",
//        "UnifiedOffers", "ProductInsights", "Details", "Insights", "Reviews",
//        "ProductVideos", "LifestyleImages", "RelatedSets", "P2PHighDensityUnit",
//        "WebSearchLink", "RelatedSearches", "Weitere Optionen", "Więcej opcji"
//    };

//    public static List<TempOfferGlobal> Parse(string rawResponse)
//    {
//        if (string.IsNullOrWhiteSpace(rawResponse))
//        {
//            Console.WriteLine("[Parser] BŁĄD: Otrzymano pustą odpowiedź.");
//            return new List<TempOfferGlobal>();
//        }

//        try
//        {
//            string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
//            using JsonDocument doc = JsonDocument.Parse(cleanedJson);
//            JsonElement root = doc.RootElement.Clone();

//            var allOffers = new List<TempOfferGlobal>();
//            FindAndParseAllOffers(root, root, allOffers);
//            Console.WriteLine($"[Parser] Zakończono parsowanie. Znaleziono łącznie {allOffers.Count} unikalnych ofert.");
//            return allOffers;
//        }
//        catch (JsonException ex)
//        {
//            Console.WriteLine($"[Parser] BŁĄD KRYTYCZNY: Nie udało się sparsować JSON. Błąd: {ex.Message}");
//            return new List<TempOfferGlobal>();
//        }
//    }

//    private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOfferGlobal> allOffers)
//    {
//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            if (node.EnumerateArray().Any(IsPotentialSingleOffer))
//            {
//                foreach (JsonElement potentialOffer in node.EnumerateArray())
//                {
//                    TempOfferGlobal? offer = ParseSingleOffer(root, potentialOffer);
//                    if (offer != null)
//                    {
//                        if (!allOffers.Any(o => o.Url == offer.Url))
//                        {
//                            allOffers.Add(offer);
//                        }
//                    }
//                }
//            }
//        }

//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            foreach (var element in node.EnumerateArray())
//            {
//                FindAndParseAllOffers(root, element, allOffers);
//            }
//        }
//        else if (node.ValueKind == JsonValueKind.Object)
//        {
//            foreach (var property in node.EnumerateObject())
//            {
//                FindAndParseAllOffers(root, property.Value, allOffers);
//            }
//        }
//    }

//    private static bool IsPotentialSingleOffer(JsonElement node)
//    {
//        JsonElement offerData = node;

//        if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array)
//        {
//            offerData = node[0];
//        }

//        if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;

//        var flatStrings = Flatten(offerData)
//            .Where(e => e.ValueKind == JsonValueKind.String)
//            .Select(e => e.GetString()!)
//            .ToList();

//        bool hasUrl = flatStrings.Any(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));

//        bool hasPrice = flatStrings.Any(s => PricePattern.IsMatch(s));

//        return hasUrl && hasPrice;
//    }

//    private static TempOfferGlobal? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
//    {
//        Console.WriteLine("[Parser] ----- Rozpoczynam parsowanie (metoda 'Flatten') nowej oferty -----");
//        JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array)
//                                ? offerContainer[0]
//                                : offerContainer;

//        if (offerData.ValueKind != JsonValueKind.Array)
//        {
//            Console.WriteLine("[Parser] Odrzucono: Dane oferty nie są tablicą.");
//            return null;
//        }

//        try
//        {
//            var flatStrings = Flatten(offerData)
//                .Where(e => e.ValueKind == JsonValueKind.String)
//                .Select(e => e.GetString()!)
//                .ToList();

//            var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "použité", "gebraucht" };
//            if (flatStrings.Any(text => usedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//            {
//                Console.WriteLine("[Parser] Odrzucono: Oferta używana/outlet.");
//                return null;
//            }

//            bool isInStock = true;
//            var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny", "není skladem", "online vergriffen", "nicht auf lager" };
//            if (flatStrings.Any(text => outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//            {
//                isInStock = false;
//                Console.WriteLine("[Parser] Oznaczono: Brak w magazynie.");
//            }

//            string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));
//            Console.WriteLine($"[Parser] Znaleziono URL: {url ?? "BRAK"}");

//            var allPriceStrings = flatStrings.Where(s => PricePattern.IsMatch(s)).ToList();
//            Console.WriteLine($"[Parser] Znaleziono {allPriceStrings.Count} ciągów pasujących do ceny (NOWY REGEX): {string.Join(" | ", allPriceStrings)}");

//            string? price = allPriceStrings.FirstOrDefault(s => !s.Trim().StartsWith("+"));
//            Console.WriteLine($"[Parser] Znaleziono Cenę Produktu: {price ?? "BRAK"}");

//            string? seller = null;
//            var offerElements = offerData.EnumerateArray().ToList();
//            for (int i = 0; i < offerElements.Count - 1; i++)
//            {
//                if (offerElements[i].ValueKind == JsonValueKind.Number && offerElements[i + 1].ValueKind == JsonValueKind.String)
//                {
//                    string potentialSeller = offerElements[i + 1].GetString()!;
//                    if (!potentialSeller.StartsWith("http") && !PricePattern.IsMatch(potentialSeller) && potentialSeller.Length > 2)
//                    {
//                        seller = potentialSeller;
//                        break;
//                    }
//                }
//            }
//            if (seller == null)
//            {
//                var sellerNode = offerData.EnumerateArray().FirstOrDefault(item => item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 1 && item[0].ValueKind == JsonValueKind.String && item[1].ValueKind == JsonValueKind.String && item[1].GetString()!.All(char.IsDigit));
//                if (sellerNode.ValueKind != JsonValueKind.Undefined)
//                {
//                    var potentialSeller = sellerNode[0].GetString()!;
//                    if (!int.TryParse(potentialSeller, out _))
//                    {
//                        seller = potentialSeller;
//                    }
//                }
//            }
//            if (seller == null && url != null)
//            {
//                var docIdMatch = Regex.Match(url, @"shopping_docid(?:%253D|=)(\d+)|docid(?:%3D|=)(\d+)");
//                if (docIdMatch.Success)
//                {
//                    string offerId = docIdMatch.Groups[1].Success ? docIdMatch.Groups[1].Value : docIdMatch.Groups[2].Value;
//                    var sellerInfoNodes = FindNodesById(root, offerId);
//                    foreach (var sellerInfoNode in sellerInfoNodes)
//                    {
//                        if (sellerInfoNode.ValueKind == JsonValueKind.Array && sellerInfoNode.GetArrayLength() > 1 && sellerInfoNode[1].ValueKind == JsonValueKind.Array)
//                        {
//                            var potentialSellerName = sellerInfoNode[1].EnumerateArray().FirstOrDefault(e => e.ValueKind == JsonValueKind.String);
//                            if (potentialSellerName.ValueKind == JsonValueKind.String)
//                            {
//                                seller = potentialSellerName.GetString();
//                                break;
//                            }
//                        }
//                    }
//                }
//            }

//            if (seller == null)
//            {
//                Console.WriteLine("[Parser] Logika JSON nie znalazła sprzedawcy. Próbuję metody awaryjnej (LastOrDefault)...");

//                seller = flatStrings.LastOrDefault(s =>
//                    !string.IsNullOrWhiteSpace(s) &&
//                    !s.StartsWith("http") &&
//                    !PricePattern.IsMatch(s) &&
//                    s.Length > 2 && s.Length < 40 &&
//                    !s.All(c => char.IsDigit(c) || c == ' ') &&
//                    !usedKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)) &&
//                    !outOfStockKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)) &&
//                    !deliveryKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)) &&
//                    !freeKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)) &&
//                    !s.Contains("/") && !s.Contains("_") && !s.Contains(":") && !s.Contains("?") &&
//                    !sellerStopWords.Contains(s.Trim())
//                );
//            }
//            Console.WriteLine($"[Parser] Znaleziono Sprzedawcę: {seller ?? "BRAK"}");

//            string? delivery = null;
//            string? rawDeliveryText = allPriceStrings.FirstOrDefault(s => s.Trim().StartsWith("+"))
//                                  ?? flatStrings.FirstOrDefault(s => deliveryKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)));

//            Console.WriteLine($"[Parser] Znaleziono surowy tekst dostawy: {rawDeliveryText ?? "BRAK"}");

//            if (rawDeliveryText != null)
//            {
//                if (freeKeywords.Any(kw => rawDeliveryText.Contains(kw, StringComparison.OrdinalIgnoreCase)))
//                {
//                    delivery = "Bezpłatna";
//                    Console.WriteLine($"[Parser] Uznano dostawę za darmową.");
//                }
//                else
//                {
//                    Match priceMatch = PricePattern.Match(rawDeliveryText);
//                    if (priceMatch.Success)
//                    {
//                        delivery = priceMatch.Value.Trim();
//                        Console.WriteLine($"[Parser] Wyodrębniono cenę dostawy: {delivery}");
//                    }
//                }
//            }
//            else
//            {
//                if (flatStrings.Any(s => freeKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase))))
//                {
//                    delivery = "Bezpłatna";
//                    Console.WriteLine($"[Parser] Znaleziono słowo kluczowe darmowej dostawy (bez słowa 'dostawa').");
//                }
//            }

//            if (!string.IsNullOrWhiteSpace(seller) && price != null && url != null)
//            {
//                Console.WriteLine($"[Parser] SUKCES. Tworzenie oferty: Sprzedawca='{seller}', Cena='{price}', Dostawa='{delivery ?? "0"}'");
//                return new TempOfferGlobal(seller, price, url, delivery, isInStock);
//            }
//            else
//            {
//                Console.WriteLine($"[Parser] ODRZUCONO OFERTĘ. Brakuje kluczowych danych: Sprzedawca? {!string.IsNullOrWhiteSpace(seller)}, Cena? {price != null}, URL? {url != null}");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"[Parser] BŁĄD KRYTYCZNY podczas parsowania pojedynczej oferty: {ex.Message}");
//        }

//        return null;
//    }

//    private static List<JsonElement> FindNodesById(JsonElement node, string id)
//    {
//        var results = new List<JsonElement>();
//        var stack = new Stack<JsonElement>();
//        stack.Push(node);

//        while (stack.Count > 0)
//        {
//            var current = stack.Pop();
//            if (current.ValueKind == JsonValueKind.Array)
//            {
//                if (current.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.String && e.GetString() == id))
//                {
//                    results.Add(current);
//                }
//                foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
//            }
//            else if (current.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
//            }
//        }
//        return results;
//    }

//    private static IEnumerable<JsonElement> Flatten(JsonElement node)
//    {
//        var stack = new Stack<JsonElement>();
//        stack.Push(node);
//        while (stack.Count > 0)
//        {
//            var current = stack.Pop();
//            if (current.ValueKind == JsonValueKind.Array)
//            {
//                foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
//            }
//            else if (current.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
//            }
//            else
//            {
//                yield return current;
//            }
//        }
//    }
//}


































































//using PriceSafari.Models;
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net.Http;
//using System.Text.Json;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//public record TempOfferGlobal(string Seller, string Price, string Url, string? Delivery, bool IsInStock);

//public class GoogleGlobalScraper
//{
//    private static readonly HttpClient _httpClient;

//    static GoogleGlobalScraper()
//    {
//        _httpClient = new HttpClient();
//        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.0.0 Safari/537.36");
//    }

//    public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct, string countryCode)
//    {
//        var finalPriceData = new List<PriceData>();
//        string? catalogId = ExtractProductId(scrapingProduct.GoogleUrl);

//        if (string.IsNullOrEmpty(catalogId))
//        {
//            Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {scrapingProduct.GoogleUrl}");
//            return finalPriceData;
//        }

//        string urlTemplate = $"https://www.google.com/async/oapv?udm=28&gl={countryCode}&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";
//        Console.WriteLine($"[GoogleGlobalScraper] Używam CID: {catalogId} dla kraju: {countryCode} | URL: {string.Format(urlTemplate, 0)}");

//        var allFoundOffers = new List<TempOfferGlobal>();
//        int startIndex = 0;
//        const int pageSize = 10;
//        int lastFetchCount;
//        const int maxRetries = 3;

//        do
//        {
//            string currentUrl = string.Format(urlTemplate, startIndex);
//            List<TempOfferGlobal> newOffers = new List<TempOfferGlobal>();
//            bool requestSucceeded = false;

//            for (int attempt = 1; attempt <= maxRetries; attempt++)
//            {
//                try
//                {
//                    Console.WriteLine($"[GoogleGlobalScraper] Próba pobrania strony {startIndex / pageSize + 1} (attempt {attempt})...");
//                    string rawResponse = await _httpClient.GetStringAsync(currentUrl);

//                    Console.WriteLine($"[GoogleGlobalScraper] === POCZĄTEK SUROWEJ ODPOWIEDZI (Strona {startIndex / pageSize + 1}) ===");
//                    Console.WriteLine(rawResponse);
//                    Console.WriteLine($"[GoogleGlobalScraper] === KONIEC SUROWEJ ODPOWIEDZI (Strona {startIndex / pageSize + 1}) ===");

//                    newOffers = GoogleShoppingApiParserGlobal.Parse(rawResponse);

//                    Console.WriteLine($"[GoogleGlobalScraper] Parser zwrócił {newOffers.Count} ofert.");

//                    if (newOffers.Any() || rawResponse.Length < 100)
//                    {
//                        requestSucceeded = true;
//                        break;
//                    }
//                    if (attempt < maxRetries) await Task.Delay(600);
//                }
//                catch (HttpRequestException ex)
//                {
//                    if (attempt == maxRetries)
//                    {
//                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Nie udało się pobrać ofert z {currentUrl} po {maxRetries} próbach. Błąd: {ex.Message}");
//                    }
//                    else
//                    {
//                        await Task.Delay(700);
//                    }
//                }
//            }

//            if (!requestSucceeded)
//            {
//                Console.WriteLine($"[GoogleGlobalScraper] Żądanie nie powiodło się dla {currentUrl}. Przerywam paginację.");
//                break;
//            }

//            lastFetchCount = newOffers.Count;
//            allFoundOffers.AddRange(newOffers);
//            startIndex += pageSize;

//            if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(50, 80));

//        } while (lastFetchCount == pageSize);

//        var groupedBySeller = allFoundOffers.GroupBy(o => o.Seller);

//        foreach (var group in groupedBySeller)
//        {
//            var cheapestOffer = group.OrderBy(o => ParsePrice(o.Price)).First();

//            Console.WriteLine($"[GoogleGlobalScraper] WYBRANA NAJTAŃSZA OFERTA DLA SPRZEDAWCY '{group.Key}':");
//            Console.WriteLine($"    > Surowa Cena: '{cheapestOffer.Price}'");
//            Console.WriteLine($"    > Surowa Dostawa: '{cheapestOffer.Delivery ?? "BRAK"}'");
//            Console.WriteLine($"    > URL: '{cheapestOffer.Url}'");
//            Console.WriteLine($"    > W magazynie: {cheapestOffer.IsInStock}");

//            var price = ParsePrice(cheapestOffer.Price);
//            var deliveryPrice = ParseDeliveryPrice(cheapestOffer.Delivery);
//            var priceWithDelivery = price + deliveryPrice;

//            Console.WriteLine($"[GoogleGlobalScraper] TWORZENIE FINALNEGO REKORDU:");
//            Console.WriteLine($"    > Sprzedawca: '{cheapestOffer.Seller}'");
//            Console.WriteLine($"    > Parsowana Cena: {price}");
//            Console.WriteLine($"    > Parsowana Dostawa: {deliveryPrice}");
//            Console.WriteLine($"    > Cena z Dostawą: {priceWithDelivery}");

//            finalPriceData.Add(new PriceData
//            {
//                StoreName = cheapestOffer.Seller,
//                Price = price,
//                PriceWithDelivery = priceWithDelivery,
//                OfferUrl = cheapestOffer.Url,
//                ScrapingProductId = scrapingProduct.ScrapingProductId,
//                RegionId = scrapingProduct.RegionId
//            });
//        }

//        Console.WriteLine($"[GoogleGlobalScraper] Zakończono dla CID: {catalogId}. Znaleziono {finalPriceData.Count} unikalnych ofert sprzedawców.");
//        return finalPriceData;
//    }

//    #region Helper Methods

//    private string? ExtractProductId(string url)
//    {
//        if (string.IsNullOrEmpty(url)) return null;
//        var match = Regex.Match(url, @"product/(\d+)");
//        return match.Success ? match.Groups[1].Value : null;
//    }

//    private decimal ParsePrice(string priceText)
//    {
//        if (string.IsNullOrWhiteSpace(priceText)) return 0;

//        Console.WriteLine($"[ParsePrice] Próba parsowania: '{priceText}'");

//        var cleanedText = Regex.Replace(priceText, @"[^\d,.]", "");

//        bool hasComma = cleanedText.Contains(',');
//        bool hasDot = cleanedText.Contains('.');

//        if (hasDot && hasComma)
//        {

//            if (cleanedText.LastIndexOf('.') < cleanedText.LastIndexOf(','))
//            {
//                cleanedText = cleanedText.Replace(".", "").Replace(",", ".");
//            }

//            else
//            {
//                cleanedText = cleanedText.Replace(",", "");
//            }
//        }

//        else if (hasComma)
//        {

//            if (cleanedText.Count(c => c == ',') > 1)
//            {
//                cleanedText = cleanedText.Replace(",", "");
//            }

//            else
//            {
//                cleanedText = cleanedText.Replace(",", ".");
//            }
//        }

//        else if (hasDot)
//        {

//            if (cleanedText.Count(c => c == '.') > 1)
//            {
//                cleanedText = cleanedText.Replace(".", "");
//            }

//        }

//        Console.WriteLine($"[ParsePrice] Oczyszczony tekst: '{cleanedText}'");

//        if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
//        {

//            Console.WriteLine($"[ParsePrice] Sukces: '{cleanedText}' -> {result}");
//            return result;
//        }

//        Console.WriteLine($"[ParsePrice] BŁĄD: Nie udało się sparsować '{priceText}' (po czyszczeniu: '{cleanedText}')");
//        return 0;
//    }

//    private decimal ParseDeliveryPrice(string? deliveryText)
//    {

//        Console.WriteLine($"[ParseDeliveryPrice] Próba parsowania dostawy: '{deliveryText ?? "NULL"}'");

//        if (string.IsNullOrWhiteSpace(deliveryText) ||
//            deliveryText.Contains("Bezpłatna", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Darmowa", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Free", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Kostenlos", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Gratuit", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Zdarma", StringComparison.OrdinalIgnoreCase) ||
//            deliveryText.Contains("Gratis", StringComparison.OrdinalIgnoreCase))
//        {

//            Console.WriteLine($"[ParseDeliveryPrice] Uznano za 0 (Darmowa/Pusta).");
//            return 0;
//        }

//        Console.WriteLine($"[ParseDeliveryPrice] Przekazuję '{deliveryText}' do ParsePrice...");
//        return ParsePrice(deliveryText);
//    }
//    #endregion
//}

//public static class GoogleShoppingApiParserGlobal
//{

//    private static readonly Regex PricePattern = new(

//        @"^(\+)?\s*([\d][\d\s,.]*[\d])\s+([A-Z]{3}|\p{Sc}|zł|\?)\s*$"
//        + "|" +

//        @"^(\+)?\s*([A-Z]{3}|\p{Sc}|zł|\?)\s*([\d][\d\s,.]*[\d])\s*$",
//        RegexOptions.Compiled | RegexOptions.IgnoreCase);

//    private static readonly string[] deliveryKeywords = new[] { "dostawa", "delivery", "doprava", "lieferung", "shipping", "livraison", "versand" };
//    private static readonly string[] freeKeywords = new[] { "bezpłatna", "darmowa", "free", "zdarma", "kostenlos", "gratuit", "gratis" };

//    private static readonly HashSet<string> sellerStopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
//    {
//        "EUR", "PLN", "CZK", "USD", "GBP", "CHF",
//        "en_US", "de_DE", "pl_PL",
//        "hg", "pvflt", "damc",
//        "Auf Lager", "Online auf Lager", "W magazynie",
//        "Lieferung:", "Aktueller Preis:", "Typ", "Marke", "Farbe",
//        "Fassungsvermögen", "Images", "Title", "ScoreCard", "Variants",
//        "UnifiedOffers", "ProductInsights", "Details", "Insights", "Reviews",
//        "ProductVideos", "LifestyleImages", "RelatedSets", "P2PHighDensityUnit",
//        "WebSearchLink", "RelatedSearches", "Weitere Optionen", "Więcej opcji"
//    };

//    public static List<TempOfferGlobal> Parse(string rawResponse)
//    {
//        if (string.IsNullOrWhiteSpace(rawResponse))
//        {
//            Console.WriteLine("[Parser] BŁĄD: Otrzymano pustą odpowiedź.");
//            return new List<TempOfferGlobal>();
//        }

//        try
//        {
//            string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
//            using JsonDocument doc = JsonDocument.Parse(cleanedJson);
//            JsonElement root = doc.RootElement.Clone();

//            var allOffers = new List<TempOfferGlobal>();
//            FindAndParseAllOffers(root, root, allOffers);
//            Console.WriteLine($"[Parser] Zakończono parsowanie. Znaleziono łącznie {allOffers.Count} unikalnych ofert.");
//            return allOffers;
//        }
//        catch (JsonException ex)
//        {
//            Console.WriteLine($"[Parser] BŁĄD KRYTYCZNY: Nie udało się sparsować JSON. Błąd: {ex.Message}");
//            return new List<TempOfferGlobal>();
//        }
//    }

//    private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOfferGlobal> allOffers)
//    {
//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            if (node.EnumerateArray().Any(IsPotentialSingleOffer))
//            {
//                foreach (JsonElement potentialOffer in node.EnumerateArray())
//                {
//                    TempOfferGlobal? offer = ParseSingleOffer(root, potentialOffer);
//                    if (offer != null)
//                    {
//                        if (!allOffers.Any(o => o.Url == offer.Url))
//                        {
//                            allOffers.Add(offer);
//                        }
//                    }
//                }
//            }
//        }

//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            foreach (var element in node.EnumerateArray())
//            {
//                FindAndParseAllOffers(root, element, allOffers);
//            }
//        }
//        else if (node.ValueKind == JsonValueKind.Object)
//        {
//            foreach (var property in node.EnumerateObject())
//            {
//                FindAndParseAllOffers(root, property.Value, allOffers);
//            }
//        }
//    }

//    private static bool IsPotentialSingleOffer(JsonElement node)
//    {
//        JsonElement offerData = node;

//        if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array)
//        {
//            offerData = node[0];
//        }

//        if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;

//        var flatStrings = Flatten(offerData)
//            .Where(e => e.ValueKind == JsonValueKind.String)
//            .Select(e => e.GetString()!)
//            .ToList();

//        bool hasUrl = flatStrings.Any(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));

//        bool hasPrice = flatStrings.Any(s => PricePattern.IsMatch(s));

//        return hasUrl && hasPrice;
//    }

//    private static TempOfferGlobal? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
//    {
//        Console.WriteLine("[Parser] ----- Rozpoczynam parsowanie (metoda 'Flatten') nowej oferty -----");

//        try
//        {
//            string rawOfferJson = offerContainer.GetRawText();
//            Console.WriteLine("[Parser] === SUROWY BLOK JSON OFERTY ===");
//            Console.WriteLine(rawOfferJson);
//            Console.WriteLine("[Parser] === KONIEC SUROWEgo BLOKU JSON ===");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"[Parser] BŁĄD logowania surowego bloku JSON: {ex.Message}");
//        }

//        JsonElement offerData = (offerContainer.ValueKind == JsonValueKind.Array && offerContainer.GetArrayLength() > 0 && offerContainer[0].ValueKind == JsonValueKind.Array)
//                                ? offerContainer[0]
//                                : offerContainer;

//        if (offerData.ValueKind != JsonValueKind.Array)
//        {
//            Console.WriteLine("[Parser] Odrzucono: Dane oferty nie są tablicą.");
//            return null;
//        }

//        try
//        {
//            var flatStrings = Flatten(offerData)
//                .Where(e => e.ValueKind == JsonValueKind.String)
//                .Select(e => e.GetString()!)
//                .ToList();

//            Console.WriteLine($"[Parser] --- Rozpłaszczone stringi (flatStrings) [{flatStrings.Count} szt.] ---");
//            try
//            {

//                Console.WriteLine(JsonSerializer.Serialize(flatStrings, new JsonSerializerOptions { WriteIndented = true }));
//            }
//            catch
//            {

//                foreach (var str in flatStrings) { Console.WriteLine($"[Parser_flat] {str}"); }
//            }
//            Console.WriteLine("[Parser] --- Koniec flatStrings ---");

//            var usedKeywords = new[] { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "použité", "gebraucht" };
//            if (flatStrings.Any(text => usedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//            {
//                Console.WriteLine("[Parser] Odrzucono: Oferta używana/outlet.");
//                return null;
//            }

//            bool isInStock = true;
//            var outOfStockKeywords = new[] { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny", "není skladem", "online vergriffen", "nicht auf lager" };
//            if (flatStrings.Any(text => outOfStockKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//            {
//                isInStock = false;
//                Console.WriteLine("[Parser] Oznaczono: Brak w magazynie.");
//            }

//            string? url = flatStrings.FirstOrDefault(s => s.StartsWith("http") && !s.Contains("google.com") && !s.Contains("gstatic.com"));
//            Console.WriteLine($"[Parser] Znaleziono URL: {url ?? "BRAK"}");

//            var allPriceStrings = flatStrings.Where(s => PricePattern.IsMatch(s)).ToList();
//            Console.WriteLine($"[Parser] Znaleziono {allPriceStrings.Count} ciągów pasujących do ceny (NOWY REGEX): {string.Join(" | ", allPriceStrings)}");

//            string? price = allPriceStrings.FirstOrDefault(s => !s.Trim().StartsWith("+"));
//            Console.WriteLine($"[Parser] Znaleziono Cenę Produktu: {price ?? "BRAK"}");

//            string? seller = null;
//            var offerElements = offerData.EnumerateArray().ToList();
//            for (int i = 0; i < offerElements.Count - 1; i++)
//            {
//                if (offerElements[i].ValueKind == JsonValueKind.Number && offerElements[i + 1].ValueKind == JsonValueKind.String)
//                {
//                    string potentialSeller = offerElements[i + 1].GetString()!;
//                    if (!potentialSeller.StartsWith("http") && !PricePattern.IsMatch(potentialSeller) && potentialSeller.Length > 2)
//                    {
//                        seller = potentialSeller;
//                        break;
//                    }
//                }
//            }
//            if (seller == null)
//            {
//                var sellerNode = offerData.EnumerateArray().FirstOrDefault(item => item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 1 && item[0].ValueKind == JsonValueKind.String && item[1].ValueKind == JsonValueKind.String && item[1].GetString()!.All(char.IsDigit));
//                if (sellerNode.ValueKind != JsonValueKind.Undefined)
//                {
//                    var potentialSeller = sellerNode[0].GetString()!;
//                    if (!int.TryParse(potentialSeller, out _))
//                    {
//                        seller = potentialSeller;
//                    }
//                }
//            }
//            if (seller == null && url != null)
//            {
//                var docIdMatch = Regex.Match(url, @"shopping_docid(?:%253D|=)(\d+)|docid(?:%3D|=)(\d+)");
//                if (docIdMatch.Success)
//                {
//                    string offerId = docIdMatch.Groups[1].Success ? docIdMatch.Groups[1].Value : docIdMatch.Groups[2].Value;
//                    var sellerInfoNodes = FindNodesById(root, offerId);
//                    foreach (var sellerInfoNode in sellerInfoNodes)
//                    {
//                        if (sellerInfoNode.ValueKind == JsonValueKind.Array && sellerInfoNode.GetArrayLength() > 1 && sellerInfoNode[1].ValueKind == JsonValueKind.Array)
//                        {
//                            var potentialSellerName = sellerInfoNode[1].EnumerateArray().FirstOrDefault(e => e.ValueKind == JsonValueKind.String);
//                            if (potentialSellerName.ValueKind == JsonValueKind.String)
//                            {
//                                seller = potentialSellerName.GetString();
//                                break;
//                            }
//                        }
//                    }
//                }
//            }

//            if (seller == null)
//            {
//                Console.WriteLine("[Parser] Logika JSON nie znalazła sprzedawcy. Próbuję metody awaryjnej (LastOrDefault)...");

//                seller = flatStrings.LastOrDefault(s =>
//                    !string.IsNullOrWhiteSpace(s) &&
//                    !s.StartsWith("http") &&
//                    !PricePattern.IsMatch(s) &&
//                    s.Length > 2 && s.Length < 40 &&
//                    !s.All(c => char.IsDigit(c) || c == ' ') &&
//                    !usedKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)) &&
//                    !outOfStockKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)) &&
//                    !deliveryKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)) &&
//                    !freeKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)) &&
//                    !s.Contains("/") && !s.Contains("_") && !s.Contains(":") && !s.Contains("?") &&
//                    !sellerStopWords.Contains(s.Trim())
//                );
//            }
//            Console.WriteLine($"[Parser] Znaleziono Sprzedawcę: {seller ?? "BRAK"}");

//            string? delivery = null;
//            string? rawDeliveryText = allPriceStrings.FirstOrDefault(s => s.Trim().StartsWith("+"))
//                                    ?? flatStrings.FirstOrDefault(s => deliveryKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)));

//            Console.WriteLine($"[Parser] Znaleziono surowy tekst dostawy: {rawDeliveryText ?? "BRAK"}");

//            if (rawDeliveryText != null)
//            {
//                if (freeKeywords.Any(kw => rawDeliveryText.Contains(kw, StringComparison.OrdinalIgnoreCase)))
//                {
//                    delivery = "Bezpłatna";
//                    Console.WriteLine($"[Parser] Uznano dostawę za darmową.");
//                }
//                else
//                {
//                    Match priceMatch = PricePattern.Match(rawDeliveryText);
//                    if (priceMatch.Success)
//                    {
//                        delivery = priceMatch.Value.Trim();
//                        Console.WriteLine($"[Parser] Wyodrębniono cenę dostawy: {delivery}");
//                    }
//                }
//            }
//            else
//            {
//                if (flatStrings.Any(s => freeKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase))))
//                {
//                    delivery = "Bezpłatna";
//                    Console.WriteLine($"[Parser] Znaleziono słowo kluczowe darmowej dostawy (bez słowa 'dostawa').");
//                }
//            }

//            if (!string.IsNullOrWhiteSpace(seller) && price != null && url != null)
//            {
//                Console.WriteLine($"[Parser] SUKCES. Tworzenie oferty: Sprzedawca='{seller}', Cena='{price}', Dostawa='{delivery ?? "0"}'");
//                return new TempOfferGlobal(seller, price, url, delivery, isInStock);
//            }
//            else
//            {
//                Console.WriteLine($"[Parser] ODRZUCONO OFERTĘ. Brakuje kluczowych danych: Sprzedawca? {!string.IsNullOrWhiteSpace(seller)}, Cena? {price != null}, URL? {url != null}");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"[Parser] BŁĄD KRYTYCZNY podczas parsowania pojedynczej oferty: {ex.Message}");
//        }

//        return null;
//    }

//    private static List<JsonElement> FindNodesById(JsonElement node, string id)
//    {
//        var results = new List<JsonElement>();
//        var stack = new Stack<JsonElement>();
//        stack.Push(node);

//        while (stack.Count > 0)
//        {
//            var current = stack.Pop();
//            if (current.ValueKind == JsonValueKind.Array)
//            {
//                if (current.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.String && e.GetString() == id))
//                {
//                    results.Add(current);
//                }
//                foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
//            }
//            else if (current.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
//            }
//        }
//        return results;
//    }

//    private static IEnumerable<JsonElement> Flatten(JsonElement node)
//    {
//        var stack = new Stack<JsonElement>();
//        stack.Push(node);
//        while (stack.Count > 0)
//        {
//            var current = stack.Pop();
//            if (current.ValueKind == JsonValueKind.Array)
//            {
//                foreach (var element in current.EnumerateArray().Reverse()) stack.Push(element);
//            }
//            else if (current.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in current.EnumerateObject()) stack.Push(property.Value);
//            }
//            else
//            {
//                yield return current;
//            }
//        }
//    }
//}


















































































//using PriceSafari.Models;
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net.Http;
//using System.Text;
//using System.Text.Encodings.Web;
//using System.Text.Json;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//public record TempOfferGlobal(string Seller, string Price, string Url, string? Delivery, bool IsInStock);

//public class GoogleGlobalScraper
//{
//    private static readonly HttpClient _httpClient;

//    static GoogleGlobalScraper()
//    {
//        _httpClient = new HttpClient
//        {
//            Timeout = TimeSpan.FromSeconds(25)
//        };
//        _httpClient.DefaultRequestHeaders.Add("User-Agent",
//            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
//        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//    }

//    public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct, string countryCode)
//    {
//        var finalPriceData = new List<PriceData>();
//        string? catalogId = ExtractProductId(scrapingProduct.GoogleUrl);

//        if (string.IsNullOrEmpty(catalogId))
//        {
//            Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {scrapingProduct.GoogleUrl}");
//            return finalPriceData;
//        }

//        string urlTemplate =
//            $"https://www.google.com/async/oapv?udm=28&gl={countryCode}&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";

//        Console.WriteLine($"[GoogleGlobalScraper] Używam CID: {catalogId} dla kraju: {countryCode} | URL: {string.Format(urlTemplate, 0)}");

//        var allFoundOffers = new List<TempOfferGlobal>();
//        int startIndex = 0;
//        const int pageSize = 10;
//        int lastFetchCount;
//        const int maxRetries = 3;

//        do
//        {
//            string currentUrl = string.Format(urlTemplate, startIndex);
//            List<TempOfferGlobal> newOffers = new();
//            bool requestSucceeded = false;

//            for (int attempt = 1; attempt <= maxRetries; attempt++)
//            {
//                try
//                {
//                    Console.WriteLine($"[GoogleGlobalScraper] Próba pobrania strony {startIndex / pageSize + 1} (attempt {attempt})...");
//                    string rawResponse = await _httpClient.GetStringAsync(currentUrl);

//                    // log kontrolowany – przy problemach z parsingiem włącz
//                    // Console.WriteLine(rawResponse);

//                    newOffers = GoogleShoppingApiParserGlobal.Parse(rawResponse);

//                    Console.WriteLine($"[GoogleGlobalScraper] Parser zwrócił {newOffers.Count} ofert.");

//                    // jeżeli coś przyszło lub ewidentnie pusta odpowiedź
//                    if (newOffers.Any() || rawResponse.Length < 100)
//                    {
//                        requestSucceeded = true;
//                        break;
//                    }

//                    if (attempt < maxRetries) await Task.Delay(600);
//                }
//                catch (HttpRequestException ex)
//                {
//                    if (attempt == maxRetries)
//                    {
//                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Nie udało się pobrać ofert z {currentUrl} po {maxRetries} próbach. Błąd: {ex.Message}");
//                    }
//                    else
//                    {
//                        await Task.Delay(700);
//                    }
//                }
//                catch (TaskCanceledException)
//                {
//                    if (attempt == maxRetries)
//                    {
//                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Timeout dla {currentUrl} po {maxRetries} próbach.");
//                    }
//                    else
//                    {
//                        await Task.Delay(700);
//                    }
//                }
//            }

//            if (!requestSucceeded)
//            {
//                Console.WriteLine($"[GoogleGlobalScraper] Żądanie nie powiodło się dla {currentUrl}. Przerywam paginację.");
//                break;
//            }

//            lastFetchCount = newOffers.Count;
//            allFoundOffers.AddRange(newOffers);
//            startIndex += pageSize;

//            if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(50, 80));

//        } while (lastFetchCount == pageSize);

//        // grupowanie po sprzedawcy – wybieramy najniższą cenę (z dostawą)
//        foreach (var group in allFoundOffers.GroupBy(o => o.Seller))
//        {
//            // wybór po łącznym koszcie (cena + dostawa), a jak brak dostawy — po samej cenie
//            var cheapestOffer = group
//                .Select(o =>
//                {
//                    var basePrice = ParsePrice(o.Price);
//                    var del = ParseDeliveryPrice(o.Delivery);
//                    return new
//                    {
//                        Offer = o,
//                        Base = basePrice,
//                        Ship = del,
//                        Total = basePrice + del
//                    };
//                })
//                .OrderBy(x => x.Total)
//                .ThenBy(x => x.Base)
//                .First();

//            var price = cheapestOffer.Base;
//            var deliveryPrice = cheapestOffer.Ship;
//            var priceWithDelivery = cheapestOffer.Total;

//            Console.WriteLine($"[GoogleGlobalScraper] WYBRANA OFERTA DLA SPRZEDAWCY '{group.Key}': price={price}, ship={deliveryPrice}, total={priceWithDelivery}");

//            finalPriceData.Add(new PriceData
//            {
//                StoreName = cheapestOffer.Offer.Seller,
//                Price = price,
//                PriceWithDelivery = priceWithDelivery,
//                OfferUrl = UnwrapGoogleRedirectUrl(cheapestOffer.Offer.Url) ?? cheapestOffer.Offer.Url,
//                ScrapingProductId = scrapingProduct.ScrapingProductId,
//                RegionId = scrapingProduct.RegionId
//            });
//        }

//        Console.WriteLine($"[GoogleGlobalScraper] Zakończono dla CID: {catalogId}. Znaleziono {finalPriceData.Count} unikalnych sprzedawców.");
//        return finalPriceData;
//    }

//    #region Helper Methods

//    private string? ExtractProductId(string url)
//    {
//        if (string.IsNullOrEmpty(url)) return null;

//        // 1) /product/1234567890
//        var m1 = Regex.Match(url, @"(?:/|-)product/(\d+)", RegexOptions.IgnoreCase);
//        if (m1.Success) return m1.Groups[1].Value;

//        // 2) cid=1234567890 (czasem w query/prds)
//        var m2 = Regex.Match(url, @"[?&]cid=(\d+)", RegexOptions.IgnoreCase);
//        if (m2.Success) return m2.Groups[1].Value;

//        // 3) prds:(cid:1234567890,...) – z linków Google Shopping
//        var m3 = Regex.Match(url, @"cid:(\d+)", RegexOptions.IgnoreCase);
//        if (m3.Success) return m3.Groups[1].Value;

//        // 4) shopping/product/123... (inne ścieżki)
//        var m4 = Regex.Match(url, @"shopping/(?:product|offers)/(\d+)", RegexOptions.IgnoreCase);
//        if (m4.Success) return m4.Groups[1].Value;

//        return null;
//    }

//    private static string? UnwrapGoogleRedirectUrl(string? url)
//    {
//        if (string.IsNullOrWhiteSpace(url)) return url;

//        try
//        {
//            // Przykłady wrapów: .../url?q=https%3A%2F%2Fsklep.pl%2F... lub .../url?url=...
//            var uri = new Uri(url);
//            var query = uri.Query.TrimStart('?');

//            // prosta ekstrakcja q= lub url=
//            var matchQ = Regex.Match(query, @"(?:^|&)(?:q|url)=([^&]+)", RegexOptions.IgnoreCase);
//            if (matchQ.Success)
//            {
//                var val = Uri.UnescapeDataString(matchQ.Groups[1].Value);
//                // niektóre wartości są podwójnie kodowane
//                try { val = Uri.UnescapeDataString(val); } catch { /* ignore */ }
//                if (val.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return val;
//            }

//            // Jeżeli to już bezpośredni link — zwracamy oryginał
//            return url;
//        }
//        catch
//        {
//            return url;
//        }
//    }

//    private static decimal ParsePrice(string priceText)
//    {
//        if (string.IsNullOrWhiteSpace(priceText)) return 0;

//        var cleanedText = Regex.Replace(priceText, @"[^\d,.\-]", ""); // zostaw tylko cyfry i separatory
//        // normalizacja separatorów (wspiera formy: 1.234,56 | 1,234.56 | 1234.56 | 1234,56 | 1 234,56 itp.)
//        bool hasComma = cleanedText.Contains(',');
//        bool hasDot = cleanedText.Contains('.');

//        if (hasDot && hasComma)
//        {
//            // jeżeli kropka przed przecinkiem: 1.234,56 => usuń kropki (tysiące), przecinek => kropka
//            if (cleanedText.LastIndexOf('.') < cleanedText.LastIndexOf(','))
//            {
//                cleanedText = cleanedText.Replace(".", "").Replace(",", ".");
//            }
//            else
//            {
//                // 1,234.56 => usuń przecinki (tysiące)
//                cleanedText = cleanedText.Replace(",", "");
//            }
//        }
//        else if (hasComma)
//        {
//            // jeżeli wiele przecinków to raczej tysiące — usuń
//            if (cleanedText.Count(c => c == ',') > 1)
//            {
//                cleanedText = cleanedText.Replace(",", "");
//            }
//            else
//            {
//                cleanedText = cleanedText.Replace(",", ".");
//            }
//        }
//        else if (hasDot)
//        {
//            // jeżeli wiele kropek to raczej tysiące — usuń
//            if (cleanedText.Count(c => c == '.') > 1)
//            {
//                cleanedText = cleanedText.Replace(".", "");
//            }
//        }

//        if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
//            return result;

//        return 0;
//    }

//    private static decimal ParseDeliveryPrice(string? deliveryText)
//    {
//        if (string.IsNullOrWhiteSpace(deliveryText)) return 0;

//        // darmowa dostawa w różnych językach
//        var freeWords = new[] { "bezpłatna", "darmowa", "free", "zdarma", "kostenlos", "gratuit", "gratis" };
//        if (freeWords.Any(w => deliveryText.Contains(w, StringComparison.OrdinalIgnoreCase))) return 0;

//        // poszukaj wprost wzorca ceny
//        var m = GoogleShoppingApiParserGlobal.PricePattern.Match(deliveryText);
//        if (m.Success) return ParsePrice(m.Value);

//        // fallback – spróbuj potraktować cały string jako cenę
//        return ParsePrice(deliveryText);
//    }

//    #endregion
//}

//public static class GoogleShoppingApiParserGlobal
//{
//    // Wzorzec ceny: waluta po/ przed kwotą, z plusem (np. "+ 12,99 zł") też zadziała (do rozpoznania dostawy)
//    public static readonly Regex PricePattern = new(
//        // 1) [ + ] 1 234,56 [CUR]
//        @"^(\+)?\s*([\d][\d\s,\.]*[\d])\s+([A-Z]{3}|\p{Sc}|zł|\?)\s*$"
//        + "|" +
//        // 2) [ + ] [CUR] 1 234,56
//        @"^(\+)?\s*([A-Z]{3}|\p{Sc}|zł|\?)\s*([\d][\d\s,\.]*[\d])\s*$",
//        RegexOptions.Compiled | RegexOptions.IgnoreCase);

//    private static readonly string[] DeliveryKeywords = { "dostawa", "delivery", "doprava", "lieferung", "shipping", "livraison", "versand" };
//    private static readonly string[] FreeKeywords = { "bezpłatna", "darmowa", "free", "zdarma", "kostenlos", "gratuit", "gratis" };
//    private static readonly string[] UsedKeywords = { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "použité", "gebraucht" };
//    private static readonly string[] OosKeywords = { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny", "není skladem", "online vergriffen", "nicht auf lager" };

//    // wykluczenia dla tekstów, które nie są sprzedawcą
//    private static readonly HashSet<string> SellerStopWords = new(StringComparer.OrdinalIgnoreCase)
//    {
//        "EUR","PLN","CZK","USD","GBP","CHF",
//        "en_US","de_DE","pl_PL",
//        "hg","pvflt","damc",
//        "Auf Lager","Online auf Lager","W magazynie",
//        "Lieferung:","Aktueller Preis:","Typ","Marke","Farbe",
//        "Fassungsvermögen","Images","Title","ScoreCard","Variants",
//        "UnifiedOffers","ProductInsights","Details","Insights","Reviews",
//        "ProductVideos","LifestyleImages","RelatedSets","P2PHighDensityUnit",
//        "WebSearchLink","RelatedSearches","Weitere Optionen","Więcej opcji"
//    };

//    public static List<TempOfferGlobal> Parse(string rawResponse)
//    {
//        if (string.IsNullOrWhiteSpace(rawResponse))
//        {
//            Console.WriteLine("[Parser] BŁĄD: Otrzymano pustą odpowiedź.");
//            return new List<TempOfferGlobal>();
//        }

//        try
//        {
//            // Google często poprzedza JSON prefiksem )]}'
//            string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
//            using var doc = JsonDocument.Parse(cleanedJson, new JsonDocumentOptions
//            {
//                AllowTrailingCommas = true
//            });
//            var root = doc.RootElement.Clone();

//            var allOffers = new List<TempOfferGlobal>();
//            FindAndParseAllOffers(root, root, allOffers);
//            Console.WriteLine($"[Parser] Zakończono parsowanie. Znaleziono łącznie {allOffers.Count} unikalnych ofert.");
//            return allOffers;
//        }
//        catch (JsonException ex)
//        {
//            Console.WriteLine($"[Parser] BŁĄD KRYTYCZNY: Nie udało się sparsować JSON. Błąd: {ex.Message}");
//            return new List<TempOfferGlobal>();
//        }
//    }

//    private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOfferGlobal> allOffers)
//    {
//        // Heurystyka: węzeł-kandydat na listę ofert: tablica zawierająca elementy,
//        // które po spłaszczeniu mają URL zewnętrzny oraz ciągi wyglądające jak ceny
//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            if (node.EnumerateArray().Any(IsPotentialSingleOffer))
//            {
//                foreach (var potentialOffer in node.EnumerateArray())
//                {
//                    var offer = ParseSingleOffer(root, potentialOffer);
//                    if (offer != null && !allOffers.Any(o => o.Url == offer.Url))
//                    {
//                        allOffers.Add(offer);
//                    }
//                }
//            }
//        }

//        // DFS po całym drzewie
//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            foreach (var element in node.EnumerateArray())
//                FindAndParseAllOffers(root, element, allOffers);
//        }
//        else if (node.ValueKind == JsonValueKind.Object)
//        {
//            foreach (var property in node.EnumerateObject())
//                FindAndParseAllOffers(root, property.Value, allOffers);
//        }
//    }

//    private static bool IsPotentialSingleOffer(JsonElement node)
//    {
//        JsonElement offerData = node;

//        if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array)
//            offerData = node[0];

//        if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;

//        var flatStrings = Flatten(offerData)
//            .Where(e => e.ValueKind == JsonValueKind.String)
//            .Select(e => e.GetString()!)
//            .ToList();

//        bool hasUrl = flatStrings.Any(IsExternalUrl);
//        bool hasPrice = flatStrings.Any(s => PricePattern.IsMatch(s));

//        return hasUrl && hasPrice;
//    }

//    private static TempOfferGlobal? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
//    {
//        try
//        {
//            JsonElement offerData =
//                (offerContainer.ValueKind == JsonValueKind.Array &&
//                 offerContainer.GetArrayLength() > 0 &&
//                 offerContainer[0].ValueKind == JsonValueKind.Array)
//                ? offerContainer[0]
//                : offerContainer;

//            if (offerData.ValueKind != JsonValueKind.Array)
//                return null;

//            var flatStrings = Flatten(offerData)
//                .Where(e => e.ValueKind == JsonValueKind.String)
//                .Select(e => e.GetString()!)
//                .ToList();

//            // filtruj oferty używane/outlet
//            if (flatStrings.Any(text => UsedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//                return null;

//            bool isInStock = !flatStrings.Any(text => OosKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)));

//            // URL oferty – pierwszy sensowny zewnętrzny link
//            string? url = flatStrings.FirstOrDefault(IsExternalUrl);
//            // spróbuj odwinąć redirect już tu
//            if (!string.IsNullOrEmpty(url))
//            {
//                var unwrapped = UnwrapGoogleRedirectUrl(url);
//                if (!string.IsNullOrEmpty(unwrapped)) url = unwrapped;
//            }

//            // ceny — wszystkie dopasowania (pierwsze bez + to cena główna; z + traktujemy jako dostawę)
//            var priceStrings = flatStrings.Where(s => PricePattern.IsMatch(s)).ToList();
//            string? itemPrice = priceStrings.FirstOrDefault(s => !s.TrimStart().StartsWith("+"));
//            string? deliveryRaw = priceStrings.FirstOrDefault(s => s.TrimStart().StartsWith("+"));

//            // wykrywanie sprzedawcy
//            string? seller = DetectSeller(offerData, flatStrings);

//            // dostawa – jeżeli nie wyłapało po „+…”, to łap po słowach-kluczach lub „free”
//            string? delivery = null;
//            if (!string.IsNullOrEmpty(deliveryRaw))
//            {
//                delivery = deliveryRaw.Trim();
//            }
//            else
//            {
//                var deliveryLine = flatStrings.FirstOrDefault(s => DeliveryKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)));
//                if (!string.IsNullOrEmpty(deliveryLine))
//                {
//                    if (FreeKeywords.Any(w => deliveryLine.Contains(w, StringComparison.OrdinalIgnoreCase)))
//                        delivery = "Bezpłatna";
//                    else
//                    {
//                        var m = PricePattern.Match(deliveryLine);
//                        if (m.Success) delivery = m.Value.Trim();
//                    }
//                }
//                else if (flatStrings.Any(s => FreeKeywords.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase))))
//                {
//                    delivery = "Bezpłatna";
//                }
//            }

//            if (!string.IsNullOrWhiteSpace(seller) && itemPrice != null && url != null)
//            {
//                return new TempOfferGlobal(seller, itemPrice, url, delivery, isInStock);
//            }

//            return null;
//        }
//        catch
//        {
//            return null;
//        }
//    }

//    private static string? DetectSeller(JsonElement offerData, List<string> flatStrings)
//    {
//        // 1) Heurystyka: liczba -> string (często para [someIndex, "SellerName"])
//        var arr = offerData.ValueKind == JsonValueKind.Array ? offerData.EnumerateArray().ToList() : new List<JsonElement>();
//        for (int i = 0; i < arr.Count - 1; i++)
//        {
//            if (arr[i].ValueKind == JsonValueKind.Number && arr[i + 1].ValueKind == JsonValueKind.String)
//            {
//                string potential = arr[i + 1].GetString()!;
//                if (IsLikelySellerName(potential)) return potential;
//            }
//        }

//        // 2) Węzeł: ["Seller", "12345"] — pierwszy string, drugi numeryczny
//        var sellerNode = arr.FirstOrDefault(item =>
//            item.ValueKind == JsonValueKind.Array &&
//            item.GetArrayLength() > 1 &&
//            item[0].ValueKind == JsonValueKind.String &&
//            item[1].ValueKind == JsonValueKind.String &&
//            item[1].GetString()!.All(char.IsDigit));

//        if (sellerNode.ValueKind != JsonValueKind.Undefined)
//        {
//            var potential = sellerNode[0].GetString()!;
//            if (!int.TryParse(potential, out _) && IsLikelySellerName(potential)) return potential;
//        }

//        // 3) Fallback po płaskich stringach (na końcu bloku zwykle jest nazwa sklepu)
//        var lastOk = flatStrings.LastOrDefault(s => IsLikelySellerName(s));
//        if (!string.IsNullOrWhiteSpace(lastOk)) return lastOk;

//        return null;
//    }

//    private static bool IsLikelySellerName(string s)
//    {
//        if (string.IsNullOrWhiteSpace(s)) return false;
//        s = s.Trim();

//        if (s.Length < 2 || s.Length > 60) return false;
//        if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
//        if (PricePattern.IsMatch(s)) return false;
//        if (SellerStopWords.Contains(s)) return false;
//        if (s.Any(ch => ch == '/' || ch == '_' || ch == ':' || ch == '?' || ch == ';' || ch == '&')) return false;
//        if (s.All(ch => char.IsDigit(ch) || char.IsWhiteSpace(ch))) return false;

//        // odfiltrokuj oczywiste słowa/dane nie-biznesowe
//        var badBits = new[] { "Marka", "Typ", "Farbe", "Color", "Rozmiar", "Size", "Variants", "Images", "Reviews", "Więcej opcji" };
//        if (badBits.Any(bb => s.Contains(bb, StringComparison.OrdinalIgnoreCase))) return false;

//        return true;
//    }

//    private static bool IsExternalUrl(string s)
//    {
//        if (string.IsNullOrWhiteSpace(s)) return false;
//        if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;

//        // odfiltruj linki do Google/gstatic
//        if (s.Contains("google.", StringComparison.OrdinalIgnoreCase)) return true; // może to być redirect, rozpakujemy później
//        if (s.Contains("gstatic.com", StringComparison.OrdinalIgnoreCase)) return false;

//        return true;
//    }

//    private static string? UnwrapGoogleRedirectUrl(string? url)
//    {
//        if (string.IsNullOrWhiteSpace(url)) return url;

//        try
//        {
//            var uri = new Uri(url);
//            var query = uri.Query.TrimStart('?');
//            var m = Regex.Match(query, @"(?:^|&)(?:q|url)=([^&]+)", RegexOptions.IgnoreCase);
//            if (m.Success)
//            {
//                var val = Uri.UnescapeDataString(m.Groups[1].Value);
//                try { val = Uri.UnescapeDataString(val); } catch { /* ignore */ }
//                if (val.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return val;
//            }
//            return url;
//        }
//        catch
//        {
//            return url;
//        }
//    }

//    private static IEnumerable<JsonElement> Flatten(JsonElement node)
//    {
//        var stack = new Stack<JsonElement>();
//        stack.Push(node);
//        while (stack.Count > 0)
//        {
//            var current = stack.Pop();
//            if (current.ValueKind == JsonValueKind.Array)
//            {
//                foreach (var element in current.EnumerateArray().Reverse())
//                    stack.Push(element);
//            }
//            else if (current.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in current.EnumerateObject())
//                    stack.Push(property.Value);
//            }
//            else
//            {
//                yield return current;
//            }
//        }
//    }
//}






//using PriceSafari.Models;
//using System;
//using System.Collections.Generic;
//using System.Globalization;
//using System.Linq;
//using System.Net.Http;
//using System.Text;
//using System.Text.Encodings.Web;
//using System.Text.Json;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//public record TempOfferGlobal(string Seller, string Price, string Url, string? Delivery, bool IsInStock);

//public class GoogleGlobalScraper
//{
//    private static readonly HttpClient _httpClient;

//    static GoogleGlobalScraper()
//    {
//        _httpClient = new HttpClient
//        {
//            Timeout = TimeSpan.FromSeconds(25)
//        };
//        _httpClient.DefaultRequestHeaders.Add("User-Agent",
//            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
//        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
//    }

//    public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct, string countryCode)
//    {
//        var finalPriceData = new List<PriceData>();
//        string? catalogId = ExtractProductId(scrapingProduct.GoogleUrl);

//        if (string.IsNullOrEmpty(catalogId))
//        {
//            Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {scrapingProduct.GoogleUrl}");
//            return finalPriceData;
//        }

//        string urlTemplate =
//            $"https://www.google.com/async/oapv?udm=28&gl={countryCode}&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";

//        Console.WriteLine($"[GoogleGlobalScraper] Używam CID: {catalogId} dla kraju: {countryCode} | URL: {string.Format(urlTemplate, 0)}");

//        var allFoundOffers = new List<TempOfferGlobal>();
//        int startIndex = 0;
//        const int pageSize = 10;
//        int lastFetchCount;
//        const int maxRetries = 3;

//        do
//        {
//            string currentUrl = string.Format(urlTemplate, startIndex);
//            List<TempOfferGlobal> newOffers = new();
//            bool requestSucceeded = false;

//            for (int attempt = 1; attempt <= maxRetries; attempt++)
//            {
//                try
//                {
//                    Console.WriteLine($"[GoogleGlobalScraper] Próba pobrania strony {startIndex / pageSize + 1} (attempt {attempt})...");
//                    string rawResponse = await _httpClient.GetStringAsync(currentUrl);

//                    newOffers = GoogleShoppingApiParserGlobal.Parse(rawResponse);

//                    Console.WriteLine($"[GoogleGlobalScraper] Parser zwrócił {newOffers.Count} ofert.");

//                    if (newOffers.Any() || rawResponse.Length < 100)
//                    {
//                        requestSucceeded = true;
//                        break;
//                    }

//                    if (attempt < maxRetries) await Task.Delay(600);
//                }
//                catch (HttpRequestException ex)
//                {
//                    if (attempt == maxRetries)
//                    {
//                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Nie udało się pobrać ofert z {currentUrl} po {maxRetries} próbach. Błąd: {ex.Message}");
//                    }
//                    else
//                    {
//                        await Task.Delay(700);
//                    }
//                }
//                catch (TaskCanceledException)
//                {
//                    if (attempt == maxRetries)
//                    {
//                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Timeout dla {currentUrl} po {maxRetries} próbach.");
//                    }
//                    else
//                    {
//                        await Task.Delay(700);
//                    }
//                }
//            }

//            if (!requestSucceeded)
//            {
//                Console.WriteLine($"[GoogleGlobalScraper] Żądanie nie powiodło się dla {currentUrl}. Przerywam paginację.");
//                break;
//            }

//            lastFetchCount = newOffers.Count;
//            allFoundOffers.AddRange(newOffers);
//            startIndex += pageSize;

//            if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(50, 80));

//        } while (lastFetchCount == pageSize);

//        foreach (var group in allFoundOffers.GroupBy(o => o.Seller))
//        {

//            var cheapestOffer = group
//                .Select(o =>
//                {
//                    var basePrice = ParsePrice(o.Price);
//                    var del = ParseDeliveryPrice(o.Delivery);
//                    return new
//                    {
//                        Offer = o,
//                        Base = basePrice,
//                        Ship = del,
//                        Total = basePrice + del
//                    };
//                })
//                .OrderBy(x => x.Total)
//                .ThenBy(x => x.Base)
//                .First();

//            var price = cheapestOffer.Base;
//            var deliveryPrice = cheapestOffer.Ship;
//            var priceWithDelivery = cheapestOffer.Total;

//            Console.WriteLine($"[GoogleGlobalScraper] WYBRANA OFERTA DLA SPRZEDAWCY '{group.Key}': price={price}, ship={deliveryPrice}, total={priceWithDelivery}");

//            finalPriceData.Add(new PriceData
//            {
//                StoreName = cheapestOffer.Offer.Seller,
//                Price = price,
//                PriceWithDelivery = priceWithDelivery,
//                OfferUrl = UnwrapGoogleRedirectUrl(cheapestOffer.Offer.Url) ?? cheapestOffer.Offer.Url,
//                ScrapingProductId = scrapingProduct.ScrapingProductId,
//                RegionId = scrapingProduct.RegionId
//            });
//        }

//        Console.WriteLine($"[GoogleGlobalScraper] Zakończono dla CID: {catalogId}. Znaleziono {finalPriceData.Count} unikalnych sprzedawców.");
//        return finalPriceData;
//    }

//    #region Helper Methods

//    private string? ExtractProductId(string url)
//    {
//        if (string.IsNullOrEmpty(url)) return null;

//        var m1 = Regex.Match(url, @"(?:/|-)product/(\d+)", RegexOptions.IgnoreCase);
//        if (m1.Success) return m1.Groups[1].Value;

//        var m2 = Regex.Match(url, @"[?&]cid=(\d+)", RegexOptions.IgnoreCase);
//        if (m2.Success) return m2.Groups[1].Value;

//        var m3 = Regex.Match(url, @"cid:(\d+)", RegexOptions.IgnoreCase);
//        if (m3.Success) return m3.Groups[1].Value;

//        var m4 = Regex.Match(url, @"shopping/(?:product|offers)/(\d+)", RegexOptions.IgnoreCase);
//        if (m4.Success) return m4.Groups[1].Value;

//        return null;
//    }

//    private static string? UnwrapGoogleRedirectUrl(string? url)
//    {
//        if (string.IsNullOrWhiteSpace(url)) return url;

//        try
//        {

//            var uri = new Uri(url);
//            var query = uri.Query.TrimStart('?');

//            var matchQ = Regex.Match(query, @"(?:^|&)(?:q|url)=([^&]+)", RegexOptions.IgnoreCase);
//            if (matchQ.Success)
//            {
//                var val = Uri.UnescapeDataString(matchQ.Groups[1].Value);

//                try { val = Uri.UnescapeDataString(val); } catch { }
//                if (val.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return val;
//            }

//            return url;
//        }
//        catch
//        {
//            return url;
//        }
//    }

//    private static decimal ParsePrice(string priceText)
//    {
//        if (string.IsNullOrWhiteSpace(priceText)) return 0;

//        var cleanedText = Regex.Replace(priceText, @"[^\d,.\-]", "");

//        bool hasComma = cleanedText.Contains(',');
//        bool hasDot = cleanedText.Contains('.');

//        if (hasDot && hasComma)
//        {

//            if (cleanedText.LastIndexOf('.') < cleanedText.LastIndexOf(','))
//            {
//                cleanedText = cleanedText.Replace(".", "").Replace(",", ".");
//            }
//            else
//            {

//                cleanedText = cleanedText.Replace(",", "");
//            }
//        }
//        else if (hasComma)
//        {

//            if (cleanedText.Count(c => c == ',') > 1)
//            {
//                cleanedText = cleanedText.Replace(",", "");
//            }
//            else
//            {
//                cleanedText = cleanedText.Replace(",", ".");
//            }
//        }
//        else if (hasDot)
//        {

//            if (cleanedText.Count(c => c == '.') > 1)
//            {
//                cleanedText = cleanedText.Replace(".", "");
//            }
//        }

//        if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
//            return result;

//        return 0;
//    }

//    private static decimal ParseDeliveryPrice(string? deliveryText)
//    {
//        if (string.IsNullOrWhiteSpace(deliveryText)) return 0;

//        var freeWords = new[] { "bezpłatna", "darmowa", "free", "zdarma", "kostenlos", "gratuit", "gratis" };
//        if (freeWords.Any(w => deliveryText.Contains(w, StringComparison.OrdinalIgnoreCase))) return 0;

//        var m = GoogleShoppingApiParserGlobal.PricePattern.Match(deliveryText);
//        if (m.Success) return ParsePrice(m.Value);

//        return ParsePrice(deliveryText);
//    }

//    #endregion
//}

//public static class GoogleShoppingApiParserGlobal
//{

//    public static readonly Regex PricePattern = new(

//        @"^(\+)?\s*([\d][\d\s,\.]*[\d])\s+([A-Z]{3}|\p{Sc}|zł|\?)\s*$"
//        + "|" +

//        @"^(\+)?\s*([A-Z]{3}|\p{Sc}|zł|\?)\s*([\d][\d\s,\.]*[\d])\s*$",
//        RegexOptions.Compiled | RegexOptions.IgnoreCase);

//    private static readonly string[] DeliveryKeywords = { "dostawa", "delivery", "doprava", "lieferung", "shipping", "livraison", "versand" };
//    private static readonly string[] FreeKeywords = { "bezpłatna", "darmowa", "free", "zdarma", "kostenlos", "gratuit", "gratis" };
//    private static readonly string[] UsedKeywords = { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "použité", "gebraucht" };
//    private static readonly string[] OosKeywords = { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny", "není skladem", "online vergriffen", "nicht auf lager" };

//    private static readonly HashSet<string> SellerStopWords = new(StringComparer.OrdinalIgnoreCase)
//    {
//        "EUR","PLN","CZK","USD","GBP","CHF",
//        "en_US","de_DE","pl_PL",
//        "hg","pvflt","damc",
//        "Auf Lager","Online auf Lager","W magazynie",
//        "Lieferung:","Aktueller Preis:","Typ","Marke","Farbe",
//        "Fassungsvermögen","Images","Title","ScoreCard","Variants",
//        "UnifiedOffers","ProductInsights","Details","Insights","Reviews",
//        "ProductVideos","LifestyleImages","RelatedSets","P2PHighDensityUnit",
//        "WebSearchLink","RelatedSearches","Weitere Optionen","Więcej opcji"
//    };

//    public static List<TempOfferGlobal> Parse(string rawResponse)
//    {
//        if (string.IsNullOrWhiteSpace(rawResponse))
//        {
//            Console.WriteLine("[Parser] BŁĄD: Otrzymano pustą odpowiedź.");
//            return new List<TempOfferGlobal>();
//        }

//        try
//        {

//            string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
//            using var doc = JsonDocument.Parse(cleanedJson, new JsonDocumentOptions
//            {
//                AllowTrailingCommas = true
//            });
//            var root = doc.RootElement.Clone();

//            var allOffers = new List<TempOfferGlobal>();
//            FindAndParseAllOffers(root, root, allOffers);
//            Console.WriteLine($"[Parser] Zakończono parsowanie. Znaleziono łącznie {allOffers.Count} unikalnych ofert.");
//            return allOffers;
//        }
//        catch (JsonException ex)
//        {
//            Console.WriteLine($"[Parser] BŁĄD KRYTYCZNY: Nie udało się sparsować JSON. Błąd: {ex.Message}");
//            return new List<TempOfferGlobal>();
//        }
//    }

//    private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOfferGlobal> allOffers)
//    {

//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            if (node.EnumerateArray().Any(IsPotentialSingleOffer))
//            {
//                foreach (var potentialOffer in node.EnumerateArray())
//                {
//                    var offer = ParseSingleOffer(root, potentialOffer);
//                    if (offer != null && !allOffers.Any(o => o.Url == offer.Url))
//                    {
//                        allOffers.Add(offer);
//                    }
//                }
//            }
//        }

//        if (node.ValueKind == JsonValueKind.Array)
//        {
//            foreach (var element in node.EnumerateArray())
//                FindAndParseAllOffers(root, element, allOffers);
//        }
//        else if (node.ValueKind == JsonValueKind.Object)
//        {
//            foreach (var property in node.EnumerateObject())
//                FindAndParseAllOffers(root, property.Value, allOffers);
//        }
//    }

//    private static bool IsPotentialSingleOffer(JsonElement node)
//    {
//        JsonElement offerData = node;

//        if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array)
//            offerData = node[0];

//        if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;

//        var flatStrings = Flatten(offerData)
//            .Where(e => e.ValueKind == JsonValueKind.String)
//            .Select(e => e.GetString()!)
//            .ToList();

//        bool hasUrl = flatStrings.Any(IsExternalUrl);
//        bool hasPrice = flatStrings.Any(s => PricePattern.IsMatch(s));

//        return hasUrl && hasPrice;
//    }

//    private static TempOfferGlobal? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
//    {
//        try
//        {
//            JsonElement offerData =
//                (offerContainer.ValueKind == JsonValueKind.Array &&
//                 offerContainer.GetArrayLength() > 0 &&
//                 offerContainer[0].ValueKind == JsonValueKind.Array)
//                ? offerContainer[0]
//                : offerContainer;

//            if (offerData.ValueKind != JsonValueKind.Array)
//                return null;

//            var flatStrings = Flatten(offerData)
//                .Where(e => e.ValueKind == JsonValueKind.String)
//                .Select(e => e.GetString()!)
//                .ToList();

//            if (flatStrings.Any(text => UsedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
//                return null;

//            bool isInStock = !flatStrings.Any(text => OosKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)));

//            string? url = flatStrings.FirstOrDefault(IsExternalUrl);

//            if (!string.IsNullOrEmpty(url))
//            {
//                var unwrapped = UnwrapGoogleRedirectUrl(url);
//                if (!string.IsNullOrEmpty(unwrapped)) url = unwrapped;
//            }

//            var priceStrings = flatStrings.Where(s => PricePattern.IsMatch(s)).ToList();
//            string? itemPrice = priceStrings.FirstOrDefault(s => !s.TrimStart().StartsWith("+"));
//            string? deliveryRaw = priceStrings.FirstOrDefault(s => s.TrimStart().StartsWith("+"));

//            string? seller = DetectSeller(offerData, flatStrings);

//            string? delivery = null;
//            if (!string.IsNullOrEmpty(deliveryRaw))
//            {
//                delivery = deliveryRaw.Trim();
//            }
//            else
//            {
//                var deliveryLine = flatStrings.FirstOrDefault(s => DeliveryKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)));
//                if (!string.IsNullOrEmpty(deliveryLine))
//                {
//                    if (FreeKeywords.Any(w => deliveryLine.Contains(w, StringComparison.OrdinalIgnoreCase)))
//                        delivery = "Bezpłatna";
//                    else
//                    {
//                        var m = PricePattern.Match(deliveryLine);
//                        if (m.Success) delivery = m.Value.Trim();
//                    }
//                }
//                else if (flatStrings.Any(s => FreeKeywords.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase))))
//                {
//                    delivery = "Bezpłatna";
//                }
//            }

//            if (!string.IsNullOrWhiteSpace(seller) && itemPrice != null && url != null)
//            {
//                return new TempOfferGlobal(seller, itemPrice, url, delivery, isInStock);
//            }

//            return null;
//        }
//        catch
//        {
//            return null;
//        }
//    }

//    private static string? DetectSeller(JsonElement offerData, List<string> flatStrings)
//    {

//        var arr = offerData.ValueKind == JsonValueKind.Array ? offerData.EnumerateArray().ToList() : new List<JsonElement>();
//        for (int i = 0; i < arr.Count - 1; i++)
//        {
//            if (arr[i].ValueKind == JsonValueKind.Number && arr[i + 1].ValueKind == JsonValueKind.String)
//            {
//                string potential = arr[i + 1].GetString()!;
//                if (IsLikelySellerName(potential)) return potential;
//            }
//        }

//        var sellerNode = arr.FirstOrDefault(item =>
//            item.ValueKind == JsonValueKind.Array &&
//            item.GetArrayLength() > 1 &&
//            item[0].ValueKind == JsonValueKind.String &&
//            item[1].ValueKind == JsonValueKind.String &&
//            item[1].GetString()!.All(char.IsDigit));

//        if (sellerNode.ValueKind != JsonValueKind.Undefined)
//        {
//            var potential = sellerNode[0].GetString()!;
//            if (!int.TryParse(potential, out _) && IsLikelySellerName(potential)) return potential;
//        }

//        var lastOk = flatStrings.LastOrDefault(s => IsLikelySellerName(s));
//        if (!string.IsNullOrWhiteSpace(lastOk)) return lastOk;

//        return null;
//    }

//    private static bool IsLikelySellerName(string s)
//    {
//        if (string.IsNullOrWhiteSpace(s)) return false;
//        s = s.Trim();

//        if (s.Length < 2 || s.Length > 60) return false;
//        if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
//        if (PricePattern.IsMatch(s)) return false;
//        if (SellerStopWords.Contains(s)) return false;
//        if (s.Any(ch => ch == '/' || ch == '_' || ch == ':' || ch == '?' || ch == ';' || ch == '&')) return false;
//        if (s.All(ch => char.IsDigit(ch) || char.IsWhiteSpace(ch))) return false;

//        var badBits = new[] { "Marka", "Typ", "Farbe", "Color", "Rozmiar", "Size", "Variants", "Images", "Reviews", "Więcej opcji" };
//        if (badBits.Any(bb => s.Contains(bb, StringComparison.OrdinalIgnoreCase))) return false;

//        return true;
//    }

//    private static bool IsExternalUrl(string s)
//    {
//        if (string.IsNullOrWhiteSpace(s)) return false;
//        if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;

//        if (s.Contains("google.", StringComparison.OrdinalIgnoreCase)) return true;
//        if (s.Contains("gstatic.com", StringComparison.OrdinalIgnoreCase)) return false;

//        return true;
//    }

//    private static string? UnwrapGoogleRedirectUrl(string? url)
//    {
//        if (string.IsNullOrWhiteSpace(url)) return url;

//        try
//        {
//            var uri = new Uri(url);
//            var query = uri.Query.TrimStart('?');
//            var m = Regex.Match(query, @"(?:^|&)(?:q|url)=([^&]+)", RegexOptions.IgnoreCase);
//            if (m.Success)
//            {
//                var val = Uri.UnescapeDataString(m.Groups[1].Value);
//                try { val = Uri.UnescapeDataString(val); } catch { }
//                if (val.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return val;
//            }
//            return url;
//        }
//        catch
//        {
//            return url;
//        }
//    }

//    private static IEnumerable<JsonElement> Flatten(JsonElement node)
//    {
//        var stack = new Stack<JsonElement>();
//        stack.Push(node);
//        while (stack.Count > 0)
//        {
//            var current = stack.Pop();
//            if (current.ValueKind == JsonValueKind.Array)
//            {
//                foreach (var element in current.EnumerateArray().Reverse())
//                    stack.Push(element);
//            }
//            else if (current.ValueKind == JsonValueKind.Object)
//            {
//                foreach (var property in current.EnumerateObject())
//                    stack.Push(property.Value);
//            }
//            else
//            {
//                yield return current;
//            }
//        }
//    }
//}











using PriceSafari.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public record TempOfferGlobal(string Seller, string Price, string Url, string? Delivery, bool IsInStock);

public class GoogleGlobalScraper
{
    private static readonly HttpClient _httpClient;

    static GoogleGlobalScraper()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pl-PL,pl;q=0.9,en-US;q=0.8,en;q=0.7");
    }

    public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct, string countryCode)
    {
        var finalPriceData = new List<PriceData>();
        string? catalogId = ExtractProductId(scrapingProduct.GoogleUrl);

        if (string.IsNullOrEmpty(catalogId))
        {
            Console.WriteLine($"[BŁĄD] Nie można wyodrębnić ID produktu z URL: {scrapingProduct.GoogleUrl}");
            return finalPriceData;
        }

        string urlTemplate =
            $"https://www.google.com/async/oapv?udm=28&gl={countryCode}&yv=3&q=1&async_context=MORE_STORES&pvorigin=3&cs=1&async=catalogid:{catalogId},pvo:3,fs:%2Fshopping%2Foffers,sori:{{0}},mno:10,isp:true,query:1,pvt:hg,_fmt:jspb";

        Console.WriteLine($"[GoogleGlobalScraper] Używam CID: {catalogId} dla kraju: {countryCode} | URL: {string.Format(urlTemplate, 0)}");

        var allFoundOffers = new List<TempOfferGlobal>();
        int startIndex = 0;
        const int pageSize = 10;
        int lastFetchCount;
        const int maxRetries = 3;

        do
        {
            string currentUrl = string.Format(urlTemplate, startIndex);
            List<TempOfferGlobal> newOffers = new();
            bool requestSucceeded = false;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Console.WriteLine($"[GoogleGlobalScraper] Próba pobrania strony {startIndex / pageSize + 1} (attempt {attempt})...");
                    string rawResponse = await _httpClient.GetStringAsync(currentUrl);

                    newOffers = GoogleShoppingApiParserGlobal.Parse(rawResponse);

                    Console.WriteLine($"[GoogleGlobalScraper] Parser zwrócił {newOffers.Count} ofert.");

                    if (newOffers.Any() || rawResponse.Length < 100)
                    {
                        requestSucceeded = true;
                        break;
                    }

                    if (attempt < maxRetries) await Task.Delay(600);
                }
                catch (HttpRequestException ex)
                {
                    if (attempt == maxRetries)
                    {
                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Nie udało się pobrać ofert z {currentUrl} po {maxRetries} próbach. Błąd: {ex.Message}");
                    }
                    else
                    {
                        await Task.Delay(700);
                    }
                }
                catch (TaskCanceledException)
                {
                    if (attempt == maxRetries)
                    {
                        Console.WriteLine($"[BŁĄD KRYTYCZNY] Timeout dla {currentUrl} po {maxRetries} próbach.");
                    }
                    else
                    {
                        await Task.Delay(700);
                    }
                }
            }

            if (!requestSucceeded)
            {
                Console.WriteLine($"[GoogleGlobalScraper] Żądanie nie powiodło się dla {currentUrl}. Przerywam paginację.");
                break;
            }

            lastFetchCount = newOffers.Count;
            allFoundOffers.AddRange(newOffers);
            startIndex += pageSize;

            if (lastFetchCount == pageSize) await Task.Delay(new Random().Next(50, 80));

        } while (lastFetchCount == pageSize);

        foreach (var group in allFoundOffers.GroupBy(o => o.Seller))
        {
            var cheapestOffer = group
                .Select(o =>
                {
                    var basePrice = ParsePrice(o.Price);
                    var del = ParseDeliveryPrice(o.Delivery);
                    return new
                    {
                        Offer = o,
                        Base = basePrice,
                        Ship = del,
                        Total = basePrice + del
                    };
                })
                .OrderBy(x => x.Total)
                .ThenBy(x => x.Base)
                .First();

            var price = cheapestOffer.Base;
            var deliveryPrice = cheapestOffer.Ship;
            var priceWithDelivery = cheapestOffer.Total;

            Console.WriteLine($"[GoogleGlobalScraper] WYBRANA OFERTA DLA SPRZEDAWCY '{group.Key}': price={price}, ship={deliveryPrice}, total={priceWithDelivery}");

            finalPriceData.Add(new PriceData
            {
                StoreName = cheapestOffer.Offer.Seller,
                Price = price,
                PriceWithDelivery = priceWithDelivery,
                OfferUrl = UnwrapGoogleRedirectUrl(cheapestOffer.Offer.Url) ?? cheapestOffer.Offer.Url,
                ScrapingProductId = scrapingProduct.ScrapingProductId,
                RegionId = scrapingProduct.RegionId
            });
        }

        Console.WriteLine($"[GoogleGlobalScraper] Zakończono dla CID: {catalogId}. Znaleziono {finalPriceData.Count} unikalnych sprzedawców.");
        return finalPriceData;
    }

    #region Helper Methods

    private string? ExtractProductId(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        var m1 = Regex.Match(url, @"(?:/|-)product/(\d+)", RegexOptions.IgnoreCase);
        if (m1.Success) return m1.Groups[1].Value;

        var m2 = Regex.Match(url, @"[?&]cid=(\d+)", RegexOptions.IgnoreCase);
        if (m2.Success) return m2.Groups[1].Value;

        var m3 = Regex.Match(url, @"cid:(\d+)", RegexOptions.IgnoreCase);
        if (m3.Success) return m3.Groups[1].Value;

        var m4 = Regex.Match(url, @"shopping/(?:product|offers)/(\d+)", RegexOptions.IgnoreCase);
        if (m4.Success) return m4.Groups[1].Value;

        return null;
    }

    private static string? UnwrapGoogleRedirectUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        try
        {
            var uri = new Uri(url);
            var query = uri.Query.TrimStart('?');

            var matchQ = Regex.Match(query, @"(?:^|&)(?:q|url)=([^&]+)", RegexOptions.IgnoreCase);
            if (matchQ.Success)
            {
                var val = Uri.UnescapeDataString(matchQ.Groups[1].Value);

                try { val = Uri.UnescapeDataString(val); } catch { }
                if (val.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return val;
            }

            return url;
        }
        catch
        {
            return url;
        }
    }

    private static decimal ParsePrice(string priceText)
    {
        if (string.IsNullOrWhiteSpace(priceText)) return 0;

        var cleanedText = Regex.Replace(priceText, @"[^\d,.\-]", "");

        bool hasComma = cleanedText.Contains(',');
        bool hasDot = cleanedText.Contains('.');

        if (hasDot && hasComma)
        {
            if (cleanedText.LastIndexOf('.') < cleanedText.LastIndexOf(','))
            {
                cleanedText = cleanedText.Replace(".", "").Replace(",", ".");
            }
            else
            {
                cleanedText = cleanedText.Replace(",", "");
            }
        }
        else if (hasComma)
        {
            if (cleanedText.Count(c => c == ',') > 1)
            {
                cleanedText = cleanedText.Replace(",", "");
            }
            else
            {
                cleanedText = cleanedText.Replace(",", ".");
            }
        }
        else if (hasDot)
        {
            if (cleanedText.Count(c => c == '.') > 1)
            {
                cleanedText = cleanedText.Replace(".", "");
            }
        }

        if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            return result;

        return 0;
    }

    private static decimal ParseDeliveryPrice(string? deliveryText)
    {
        if (string.IsNullOrWhiteSpace(deliveryText)) return 0;

        var freeWords = new[] { "bezpłatna", "darmowa", "free", "zdarma", "kostenlos", "gratuit", "gratis" };
        if (freeWords.Any(w => deliveryText.Contains(w, StringComparison.OrdinalIgnoreCase))) return 0;

        var m = GoogleShoppingApiParserGlobal.PricePattern.Match(deliveryText);
        if (m.Success) return ParsePrice(m.Value);

        return ParsePrice(deliveryText);
    }

    #endregion
}

public static class GoogleShoppingApiParserGlobal
{

    public static readonly Regex PricePattern = new(

        @"^(\+)?\s*([\d][\d\s,\.]*[\d])\s+([A-Z]{3}|\p{Sc}|zł|\?)\s*$"
        + "|" +

        @"^(\+)?\s*([A-Z]{3}|\p{Sc}|zł|\?)\s*([\d][\d\s,\.]*[\d])\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] DeliveryKeywords = { "dostawa", "delivery", "doprava", "lieferung", "shipping", "livraison", "versand" };
    private static readonly string[] FreeKeywords = { "bezpłatna", "darmowa", "free", "zdarma", "kostenlos", "gratuit", "gratis" };
    private static readonly string[] UsedKeywords = { "pre-owned", "used", "używany", "outlet", "renewed", "refurbished", "odnowiony", "použité", "gebraucht" };
    private static readonly string[] OosKeywords = { "out of stock", "niedostępny", "brak w magazynie", "asortyment niedostępny", "není skladem", "online vergriffen", "nicht auf lager" };

    public static List<TempOfferGlobal> Parse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            Console.WriteLine("[Parser] BŁĄD: Otrzymano pustą odpowiedź.");
            return new List<TempOfferGlobal>();
        }

        try
        {

            string cleanedJson = rawResponse.StartsWith(")]}'") ? rawResponse.Substring(5) : rawResponse;
            using var doc = JsonDocument.Parse(cleanedJson, new JsonDocumentOptions
            {
                AllowTrailingCommas = true
            });
            var root = doc.RootElement.Clone();

            var allOffers = new List<TempOfferGlobal>();
            FindAndParseAllOffers(root, root, allOffers);
            Console.WriteLine($"[Parser] Zakończono parsowanie. Znaleziono łącznie {allOffers.Count} unikalnych ofert.");
            return allOffers;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[Parser] BŁĄD KRYTYCZNY: Nie udało się sparsować JSON. Błąd: {ex.Message}");
            return new List<TempOfferGlobal>();
        }
    }

    private static void FindAndParseAllOffers(JsonElement root, JsonElement node, List<TempOfferGlobal> allOffers)
    {

        if (node.ValueKind == JsonValueKind.Array)
        {
            if (node.EnumerateArray().Any(IsPotentialSingleOffer))
            {
                foreach (var potentialOffer in node.EnumerateArray())
                {
                    var offer = ParseSingleOffer(root, potentialOffer);
                    if (offer != null && !allOffers.Any(o => o.Url == offer.Url))
                    {
                        allOffers.Add(offer);
                    }
                }
            }
        }

        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in node.EnumerateArray())
                FindAndParseAllOffers(root, element, allOffers);
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
                FindAndParseAllOffers(root, property.Value, allOffers);
        }
    }

    private static bool IsPotentialSingleOffer(JsonElement node)
    {
        JsonElement offerData = node;

        if (node.ValueKind == JsonValueKind.Array && node.GetArrayLength() > 0 && node[0].ValueKind == JsonValueKind.Array)
            offerData = node[0];

        if (offerData.ValueKind != JsonValueKind.Array || offerData.GetArrayLength() < 4) return false;

        var flatStrings = Flatten(offerData)
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();

        bool hasUrl = flatStrings.Any(IsExternalUrl);
        bool hasPrice = flatStrings.Any(s => PricePattern.IsMatch(s));

        return hasUrl && hasPrice;
    }

    private static TempOfferGlobal? ParseSingleOffer(JsonElement root, JsonElement offerContainer)
    {
        try
        {
            JsonElement offerData =
                (offerContainer.ValueKind == JsonValueKind.Array &&
                 offerContainer.GetArrayLength() > 0 &&
                 offerContainer[0].ValueKind == JsonValueKind.Array)
                ? offerContainer[0]
                : offerContainer;

            if (offerData.ValueKind != JsonValueKind.Array)
                return null;

            var flatStrings = Flatten(offerData)
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();

            if (flatStrings.Any(text => UsedKeywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
                return null;

            bool isInStock = !flatStrings.Any(text => OosKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)));

            string? url = flatStrings.FirstOrDefault(IsExternalUrl);
            if (!string.IsNullOrEmpty(url))
            {
                var unwrapped = UnwrapGoogleRedirectUrl(url);
                if (!string.IsNullOrEmpty(unwrapped)) url = unwrapped;
            }

            var priceStrings = flatStrings.Where(s => PricePattern.IsMatch(s)).ToList();
            string? itemPrice = priceStrings.FirstOrDefault(s => !s.TrimStart().StartsWith("+"));
            string? deliveryRaw = priceStrings.FirstOrDefault(s => s.TrimStart().StartsWith("+"));

            string? seller = !string.IsNullOrWhiteSpace(url) ? SellerFromUrl(url) : null;

            string? delivery = null;
            if (!string.IsNullOrEmpty(deliveryRaw))
            {
                delivery = deliveryRaw.Trim();
            }
            else
            {
                var deliveryLine = flatStrings.FirstOrDefault(s => DeliveryKeywords.Any(kw => s.Contains(kw, StringComparison.OrdinalIgnoreCase)));
                if (!string.IsNullOrEmpty(deliveryLine))
                {
                    if (FreeKeywords.Any(w => deliveryLine.Contains(w, StringComparison.OrdinalIgnoreCase)))
                        delivery = "Bezpłatna";
                    else
                    {
                        var m = PricePattern.Match(deliveryLine);
                        if (m.Success) delivery = m.Value.Trim();
                    }
                }
                else if (flatStrings.Any(s => FreeKeywords.Any(w => s.Contains(w, StringComparison.OrdinalIgnoreCase))))
                {
                    delivery = "Bezpłatna";
                }
            }

            if (!string.IsNullOrWhiteSpace(seller) && itemPrice != null && url != null)
            {
                return new TempOfferGlobal(seller, itemPrice, url, delivery, isInStock);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string SellerFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host;

            host = Regex.Replace(host, @"^(www|m|amp|click|l)\.", "", RegexOptions.IgnoreCase);

            var registrable = GetRegistrableDomain(host);
            return ToSellerCase(registrable);
        }
        catch
        {
            return "Sklep";
        }
    }

    private static string GetRegistrableDomain(string host)
    {
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return host;

        int n = parts.Length;
        var sldMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "co", "com", "org", "net", "gov", "edu" };

        if (parts[n - 1].Length == 2 && sldMarkers.Contains(parts[n - 2]) && n >= 3)
        {
            return $"{parts[n - 3]}.{parts[n - 2]}.{parts[n - 1]}";
        }

        return $"{parts[n - 2]}.{parts[n - 1]}";
    }

    private static string ToSellerCase(string domain)
    {
        var parts = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return domain;

        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        var first = textInfo.ToTitleCase(parts[0].ToLowerInvariant());
        var rest = parts.Skip(1).Select(p => p.ToLowerInvariant());

        return string.Join(".", (new[] { first }).Concat(rest));
    }

    private static bool IsExternalUrl(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;

        if (s.Contains("google.", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Contains("gstatic.com", StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    private static string? UnwrapGoogleRedirectUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;

        try
        {
            var uri = new Uri(url);
            var query = uri.Query.TrimStart('?');
            var m = Regex.Match(query, @"(?:^|&)(?:q|url)=([^&]+)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var val = Uri.UnescapeDataString(m.Groups[1].Value);
                try { val = Uri.UnescapeDataString(val); } catch { }
                if (val.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return val;
            }
            return url;
        }
        catch
        {
            return url;
        }
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
                foreach (var element in current.EnumerateArray().Reverse())
                    stack.Push(element);
            }
            else if (current.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in current.EnumerateObject())
                    stack.Push(property.Value);
            }
            else
            {
                yield return current;
            }
        }
    }
}