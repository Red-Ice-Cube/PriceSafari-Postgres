//using PriceSafari.Models;
//using PuppeteerSharp;
//using System.Globalization;
//using System.Text.RegularExpressions;

//public class GoogleMainPriceScraper
//{
//    private Browser _browser;
//    private Page _page;
//    private bool _expandAndCompareGoogleOffersSetting; // Nowe pole do przechowywania ustawienia

//    // Zaktualizuj InitializeAsync, aby przechowywać ustawienie
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

//_page = (Page)await _browser.NewPageAsync();
//_expandAndCompareGoogleOffersSetting = settings.ExpandAndCompareGoogleOffers; // Zapisz ustawienie
//Console.WriteLine($"Browser initialized. ExpandAndCompareGoogleOffers set to: {_expandAndCompareGoogleOffersSetting}");
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
//        // int totalOffersCount = 0; // Usunięto, ponieważ nie był używany do logiki, a jedynie inkrementowany
//        int currentPage = 0;
//        int positionCounter = 1;

//        try
//        {
//            while (hasNextPage && currentPage < 3) // Ograniczenie do 3 stron dla bezpieczeństwa
//            {
//                string paginatedUrl = currentPage == 0
//                    ? productOffersUrl
//                    : $"{googleOfferUrl}/offers?prds=cid:{productId},cond:1,start:{currentPage * 20}&gl=pl&hl=pl";

//                await _page.GoToAsync(paginatedUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
//                // await Task.Delay(600); // Możesz odkomentować w razie potrzeby

//                // Warunkowe rozwijanie ofert
//                if (_expandAndCompareGoogleOffersSetting)
//                {
//                    var moreOffersButtons = await _page.QuerySelectorAllAsync("div.cNMlI");
//                    if (moreOffersButtons.Any())
//                    {
//                        Console.WriteLine($"Found {moreOffersButtons.Length} 'More offers' buttons. Clicking to expand as ExpandAndCompareGoogleOffers is true.");
//                        foreach (var button in moreOffersButtons)
//                        {
//                            try
//                            {
//                                await button.ClickAsync();
//                                await Task.Delay(500); // Czekaj na załadowanie po kliknięciu
//                            }
//                            catch (Exception ex)
//                            {
//                                Console.WriteLine($"Could not click 'More offers' button: {ex.Message}");
//                            }
//                        }
//                    }
//                    else
//                    {
//                        Console.WriteLine("No 'More offers' buttons found to expand.");
//                    }
//                }
//                else
//                {
//                    Console.WriteLine("Skipping expansion of 'More offers' as ExpandAndCompareGoogleOffers is false.");
//                }


//                var offerRowsSelector = "#sh-osd__online-sellers-cont > tr";
//                var offerRows = await _page.QuerySelectorAllAsync(offerRowsSelector);
//                var offersCount = offerRows.Length;
//                // totalOffersCount += offersCount; // Usunięto

//                if (offersCount == 0 && currentPage == 0 && !_expandAndCompareGoogleOffersSetting)
//                {
//                    Console.WriteLine("No initial offers found and not expanding. Stopping scrape for this URL.");
//                    break; // Jeśli nie ma ofert na pierwszej stronie i nie rozwijamy, przerwij
//                }
//                if (offersCount == 0 && currentPage > 0)
//                {
//                    Console.WriteLine("No offers found on subsequent page. Stopping scrape for this URL.");
//                    break; // Jeśli nie ma ofert na kolejnych stronach, przerwij
//                }


//                for (int i = 1; i <= offersCount; i++) // Pętla po widocznych ofertach
//                {
//                    var storeNameSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";
//                    var priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > span";
//                    var priceWithDeliverySelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(5) > div > div.drzWO";

//                    var storeNameElement = await _page.QuerySelectorAsync(storeNameSelector);
//                    if (storeNameElement != null)
//                    {
//                        var storeName = await storeNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
//                        var offerUrl = await storeNameElement.EvaluateFunctionAsync<string>("node => node.href");

//                        if (IsOutletOffer(offerUrl))
//                        {
//                            Console.WriteLine($"Store: {storeName} - Offer is outlet, skipping.");
//                            continue;
//                        }

//                        var priceElement = await _page.QuerySelectorAsync(priceSelector);
//                        var priceText = priceElement != null ? await priceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "No price";
//                        var priceWithDeliveryElement = await _page.QuerySelectorAsync(priceWithDeliverySelector);
//                        var priceWithDeliveryText = priceWithDeliveryElement != null ? await priceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : priceText;

//                        var priceDecimal = ExtractPrice(priceText);
//                        var priceWithDeliveryDecimal = ExtractPrice(priceWithDeliveryText);
//                        var currentPosition = positionCounter; // Nie inkrementuj tutaj, zrobimy to na końcu pętli

//                        if (storeBestOffers.TryGetValue(storeName, out var existingOffer))
//                        {
//                            if (priceWithDeliveryDecimal < existingOffer.GooglePriceWithDelivery)
//                            {
//                                storeBestOffers[storeName] = new CoOfrPriceHistoryClass
//                                {
//                                    GoogleStoreName = storeName,
//                                    GooglePrice = priceDecimal,
//                                    GooglePriceWithDelivery = priceWithDeliveryDecimal,
//                                    GooglePosition = currentPosition.ToString() // Pozycja jest aktualizowana
//                                };
//                                Console.WriteLine($"Updated offer for {storeName}: Price {priceDecimal}, Delivery {priceWithDeliveryDecimal}, Position {currentPosition}");
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
//                            Console.WriteLine($"Added new offer for {storeName}: Price {priceDecimal}, Delivery {priceWithDeliveryDecimal}, Position {currentPosition}");
//                        }
//                        positionCounter++; // Inkrementuj pozycję po przetworzeniu oferty
//                    }
//                }

//                // Warunkowe scrapowanie ukrytych ofert (tych, które mogły pojawić się po kliknięciu "Więcej ofert" lub są domyślnie zwinięte)
//if (_expandAndCompareGoogleOffersSetting)
//{
//    Console.WriteLine("Processing hidden/expanded offers as ExpandAndCompareGoogleOffers is true.");
//    var hiddenOfferRowsSelector = "tr.sh-osd__offer-row[data-hveid][data-is-grid-offer='true']"; // Selektor dla ukrytych ofert
//    var hiddenOfferRows = await _page.QuerySelectorAllAsync(hiddenOfferRowsSelector);

//    if (hiddenOfferRows.Any())
//    {
//        Console.WriteLine($"Found {hiddenOfferRows.Length} hidden/expanded offer rows.");
//        for (int j = 0; j < hiddenOfferRows.Length; j++)
//        {
//            var hiddenRowElement = hiddenOfferRows[j];

//            // Selektory dla ukrytych ofert - mogą być inne niż dla głównych
//            // Te selektory zakładają, że struktura jest podobna, dostosuj w razie potrzeby
//            var hiddenStoreNameSelector = "td:nth-child(1) > div._-ez > a"; // Często używany selektor dla nazwy sklepu w rozwiniętych
//            var hiddenPriceSelector = "td:nth-child(4) > span";
//            var hiddenPriceWithDeliverySelector = "td:nth-child(5) > div"; // Bardziej ogólny selektor

//            var hiddenStoreNameElement = await hiddenRowElement.QuerySelectorAsync(hiddenStoreNameSelector);
//            if (hiddenStoreNameElement != null)
//            {
//                var hiddenStoreName = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
//                var hiddenOfferUrl = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.href");

//                if (IsOutletOffer(hiddenOfferUrl))
//                {
//                    Console.WriteLine($"Hidden Store: {hiddenStoreName} - Offer is outlet, skipping.");
//                    continue;
//                }

//                var hiddenPriceElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceSelector);
//                var hiddenPriceText = hiddenPriceElement != null ? await hiddenPriceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "No price";
//                var hiddenPriceWithDeliveryElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceWithDeliverySelector);
//                var hiddenPriceWithDeliveryText = hiddenPriceWithDeliveryElement != null ? await hiddenPriceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : hiddenPriceText;

//                var hiddenPriceDecimal = ExtractPrice(hiddenPriceText);
//                var hiddenPriceWithDeliveryDecimal = ExtractPrice(hiddenPriceWithDeliveryText);
//                var currentPosition = positionCounter; // Użyj aktualnej pozycji

//                if (storeBestOffers.TryGetValue(hiddenStoreName, out var existingOffer))
//                {
//                    if (hiddenPriceWithDeliveryDecimal < existingOffer.GooglePriceWithDelivery)
//                    {
//                        storeBestOffers[hiddenStoreName] = new CoOfrPriceHistoryClass
//                        {
//                            GoogleStoreName = hiddenStoreName,
//                            GooglePrice = hiddenPriceDecimal,
//                            GooglePriceWithDelivery = hiddenPriceWithDeliveryDecimal,
//                            GooglePosition = currentPosition.ToString() // Pozycja jest aktualizowana
//                        };
//                        Console.WriteLine($"Updated hidden offer for {hiddenStoreName}: Price {hiddenPriceDecimal}, Delivery {hiddenPriceWithDeliveryDecimal}, Position {currentPosition}");
//                    }
//                }
//                else
//                {
//                    storeBestOffers[hiddenStoreName] = new CoOfrPriceHistoryClass
//                    {
//                        GoogleStoreName = hiddenStoreName,
//                        GooglePrice = hiddenPriceDecimal,
//                        GooglePriceWithDelivery = hiddenPriceWithDeliveryDecimal,
//                        GooglePosition = currentPosition.ToString()
//                    };
//                    Console.WriteLine($"Added new hidden offer for {hiddenStoreName}: Price {hiddenPriceDecimal}, Delivery {hiddenPriceWithDeliveryDecimal}, Position {currentPosition}");
//                }
//                positionCounter++; // Inkrementuj pozycję po przetworzeniu oferty
//            }
//        }
//    }
//    else
//    {
//        Console.WriteLine("No hidden/expanded offer rows found with the specified selector.");
//    }
//}
//else
//{
//    Console.WriteLine("Skipping processing of hidden/expanded offers as ExpandAndCompareGoogleOffers is false.");
//}

//                // Logika paginacji
//                var paginationNextButton = await _page.QuerySelectorAsync("a[aria-label='Następna strona'], a[aria-label='Next page']"); // Bardziej uniwersalny selektor

//                if (paginationNextButton != null &&
//                    await paginationNextButton.IsVisibleAsync() &&
//                    await paginationNextButton.IsIntersectingViewportAsync() &&
//                    !(await paginationNextButton.EvaluateFunctionAsync<bool>("node => node.hasAttribute('disabled') || node.getAttribute('aria-disabled') === 'true'")))
//                {
//                    currentPage++;
//                    Console.WriteLine($"Moving to next page: {currentPage}");
//                    hasNextPage = true;
//                }
//                else
//                {
//                    Console.WriteLine("No active 'Next page' button found or page limit reached.");
//                    hasNextPage = false;
//                }
//            }

//            scrapedData.AddRange(storeBestOffers.Values);
//            Console.WriteLine($"Finished processing for {googleOfferUrl}. Found {scrapedData.Count} unique best offers for stores.");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error during scraping URL {googleOfferUrl}: {ex.Message}{Environment.NewLine}StackTrace: {ex.StackTrace}");
//        }

//        return scrapedData;
//    }

//    private string ExtractProductId(string url)
//    {
//        if (string.IsNullOrEmpty(url)) return string.Empty;

//        // Próba #1: Standardowy format product/ID
//        var match = Regex.Match(url, @"product/(\d+)");
//        if (match.Success)
//        {
//            return match.Groups[1].Value;
//        }

//        // Próba #2: Format z parametrem prds=cid:ID
//        match = Regex.Match(url, @"prds=cid:([^,]+)");
//        if (match.Success)
//        {
//            return match.Groups[1].Value;
//        }

//        // Próba #3: Format z /shopping/product/ID_LITEROWO_NUMERYCZNE (często ID produktu może być bardziej złożone)
//        match = Regex.Match(url, @"/shopping/product/([^/?]+)");
//        if (match.Success)
//        {
//            return match.Groups[1].Value;
//        }

//        Console.WriteLine($"Could not extract Product ID from URL: {url} using known patterns.");
//        return string.Empty;
//    }

//    private decimal ExtractPrice(string priceText)
//    {
//        if (string.IsNullOrWhiteSpace(priceText)) return 0;

//        try
//        {
//            // Usuń "zł", wszelkie litery (oprócz ew. "gr" dla groszy, ale to rzadkość w takim formacie), spacje jako separatory tysięcy.
//            // Zachowaj cyfry, przecinek i kropkę.
//            var cleanedText = Regex.Replace(priceText, @"[^\d,.]", "").Trim();

//            // Standaryzuj separator dziesiętny na kropkę
//            // Jeśli jest przecinek i kropka, zakładamy, że kropka to separator tysięcy, a przecinek dziesiętny
//            if (cleanedText.Contains(',') && cleanedText.Contains('.'))
//            {
//                if (cleanedText.LastIndexOf('.') < cleanedText.LastIndexOf(',')) // np. 1.234,56
//                {
//                    cleanedText = cleanedText.Replace(".", "").Replace(',', '.');
//                }
//                else // np. 1,234.56 (mniej typowe dla PL, ale możliwe)
//                {
//                    cleanedText = cleanedText.Replace(",", "");
//                }
//            }
//            else if (cleanedText.Contains(',')) // Tylko przecinek, np. 1234,56
//            {
//                cleanedText = cleanedText.Replace(',', '.');
//            }
//            // Jeśli jest wiele kropek, np. 1.234.56, usuń wszystkie oprócz ostatniej
//            int dotCount = cleanedText.Count(c => c == '.');
//            if (dotCount > 1)
//            {
//                int lastDot = cleanedText.LastIndexOf('.');
//                cleanedText = cleanedText.Substring(0, lastDot).Replace(".", "") + cleanedText.Substring(lastDot);
//            }


//            if (decimal.TryParse(cleanedText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal priceDecimal))
//            {
//                return priceDecimal;
//            }
//            else
//            {
//                Console.WriteLine($"Failed to parse price: '{priceText}' (cleaned to: '{cleanedText}')");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error processing price string '{priceText}': {ex.Message}");
//        }
//        return 0;
//    }

//    private bool IsOutletOffer(string url)
//    {
//        if (string.IsNullOrEmpty(url))
//        {
//            return false;
//        }
//        // Użyj Regex.IsMatch dla dopasowania bez względu na wielkość liter i jako całe słowo
//        return Regex.IsMatch(url, @"\boutlet\b", RegexOptions.IgnoreCase);
//    }

//    public async Task CloseAsync()
//    {
//        try
//        {
//            if (_page != null && !_page.IsClosed)
//            {
//                await _page.CloseAsync();
//            }
//            if (_browser != null && _browser.IsConnected)
//            {
//                await _browser.CloseAsync();
//            }
//            Console.WriteLine("Browser resources released.");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error during browser close: {ex.Message}");
//        }
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
    private bool _expandAndCompareGoogleOffersSetting;

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
        _expandAndCompareGoogleOffersSetting = settings.ExpandAndCompareGoogleOffers; // Zapisz ustawienie
        Console.WriteLine($"Browser initialized. ExpandAndCompareGoogleOffers set to: {_expandAndCompareGoogleOffersSetting}");
    
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

                await _page.GoToAsync(paginatedUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
                //await Task.Delay(600);

                if (_expandAndCompareGoogleOffersSetting) // <<<< ---- DODAJ TEN WARUNEK
                {
                    var moreOffersButtons = await _page.QuerySelectorAllAsync("div.cNMlI");
                    if (moreOffersButtons.Any()) // Dobrze jest sprawdzić, czy są jakieś przyciski do kliknięcia
                    {
                        Console.WriteLine($"Found {moreOffersButtons.Length} 'More offers' buttons. Clicking to expand as ExpandAndCompareGoogleOffers is true.");
                        foreach (var button in moreOffersButtons)
                        {
                            try
                            {
                                await button.ClickAsync();
                                await Task.Delay(500); // Daj czas na załadowanie po kliknięciu
                            }
                            catch (Exception ex)
                            {
                                // Logowanie błędu kliknięcia, ale kontynuacja pracy
                                Console.WriteLine($"Error clicking 'More offers' button: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("No 'More offers' (div.cNMlI) buttons found to expand.");
                    }
                }
                else
                {
                    Console.WriteLine("Skipping click on 'More offers' (div.cNMlI) as ExpandAndCompareGoogleOffers is false.");
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

                if (_expandAndCompareGoogleOffersSetting)
                {
                    Console.WriteLine("Processing hidden/expanded offers as ExpandAndCompareGoogleOffers is true.");
                    var hiddenOfferRowsSelector = "tr.sh-osd__offer-row[data-hveid][data-is-grid-offer='true']"; // Selektor dla ukrytych ofert
                    var hiddenOfferRows = await _page.QuerySelectorAllAsync(hiddenOfferRowsSelector);

                    if (hiddenOfferRows.Any())
                    {
                        Console.WriteLine($"Found {hiddenOfferRows.Length} hidden/expanded offer rows.");
                        for (int j = 0; j < hiddenOfferRows.Length; j++)
                        {
                            var hiddenRowElement = hiddenOfferRows[j];

                            // Selektory dla ukrytych ofert - mogą być inne niż dla głównych
                            // Te selektory zakładają, że struktura jest podobna, dostosuj w razie potrzeby
                            var hiddenStoreNameSelector = "td:nth-child(1) > div._-ez > a"; // Często używany selektor dla nazwy sklepu w rozwiniętych
                            var hiddenPriceSelector = "td:nth-child(4) > span";
                            var hiddenPriceWithDeliverySelector = "td:nth-child(5) > div"; // Bardziej ogólny selektor

                            var hiddenStoreNameElement = await hiddenRowElement.QuerySelectorAsync(hiddenStoreNameSelector);
                            if (hiddenStoreNameElement != null)
                            {
                                var hiddenStoreName = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
                                var hiddenOfferUrl = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.href");

                                if (IsOutletOffer(hiddenOfferUrl))
                                {
                                    Console.WriteLine($"Hidden Store: {hiddenStoreName} - Offer is outlet, skipping.");
                                    continue;
                                }

                                var hiddenPriceElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceSelector);
                                var hiddenPriceText = hiddenPriceElement != null ? await hiddenPriceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "No price";
                                var hiddenPriceWithDeliveryElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceWithDeliverySelector);
                                var hiddenPriceWithDeliveryText = hiddenPriceWithDeliveryElement != null ? await hiddenPriceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : hiddenPriceText;

                                var hiddenPriceDecimal = ExtractPrice(hiddenPriceText);
                                var hiddenPriceWithDeliveryDecimal = ExtractPrice(hiddenPriceWithDeliveryText);
                                var currentPosition = positionCounter; // Użyj aktualnej pozycji

                                if (storeBestOffers.TryGetValue(hiddenStoreName, out var existingOffer))
                                {
                                    if (hiddenPriceWithDeliveryDecimal < existingOffer.GooglePriceWithDelivery)
                                    {
                                        storeBestOffers[hiddenStoreName] = new CoOfrPriceHistoryClass
                                        {
                                            GoogleStoreName = hiddenStoreName,
                                            GooglePrice = hiddenPriceDecimal,
                                            GooglePriceWithDelivery = hiddenPriceWithDeliveryDecimal,
                                            GooglePosition = currentPosition.ToString() // Pozycja jest aktualizowana
                                        };
                                        Console.WriteLine($"Updated hidden offer for {hiddenStoreName}: Price {hiddenPriceDecimal}, Delivery {hiddenPriceWithDeliveryDecimal}, Position {currentPosition}");
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
                                    Console.WriteLine($"Added new hidden offer for {hiddenStoreName}: Price {hiddenPriceDecimal}, Delivery {hiddenPriceWithDeliveryDecimal}, Position {currentPosition}");
                                }
                                positionCounter++; // Inkrementuj pozycję po przetworzeniu oferty
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("No hidden/expanded offer rows found with the specified selector.");
                    }
                }
                else
                {
                    Console.WriteLine("Skipping processing of hidden/expanded offers as ExpandAndCompareGoogleOffers is false.");
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
