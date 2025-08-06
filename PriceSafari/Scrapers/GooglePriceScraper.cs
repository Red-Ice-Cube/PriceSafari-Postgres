using PriceSafari.Models;
using PuppeteerSharp;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PriceSafari.Scrapers
{
    public class GooglePriceScraper
    {
        private Browser _browser;
        private Page _page;
        private Settings _scraperSettings; // Przechowujemy ustawienia do ponownego uruchomienia

        // === NOWE I ZMODYFIKOWANE METODY DO OBSŁUGI RESETU ===

        // Prywatna metoda zawierająca logikę uruchamiania przeglądarki
        private async Task LaunchNewBrowserAsync()
        {
            var browserFetcher = new BrowserFetcher();
            await browserFetcher.DownloadAsync();

            _browser = (Browser)await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = _scraperSettings.HeadLess,
                Args = new[]
                {
                    "--no-sandbox", "--disable-setuid-sandbox", "--disable-gpu",
                    "--disable-blink-features=AutomationControlled", "--disable-software-rasterizer",
                    "--disable-extensions", "--disable-dev-shm-usage",
                    "--disable-features=IsolateOrigins,site-per-process", "--disable-infobars"
                }
            });

            _page = (Page)await _browser.NewPageAsync();
            Console.WriteLine("Przeglądarka zainicjalizowana.");
        }

        // Zmodyfikowana metoda InitializeAsync
        public async Task InitializeAsync(Settings settings)
        {
            _scraperSettings = settings;
            await LaunchNewBrowserAsync();
        }

        // Nowa metoda do wykonywania pełnego resetu przeglądarki
        public async Task ResetBrowserAndPageAsync()
        {
            Console.WriteLine("Wykonywanie PEŁNEGO RESETU PRZEGLĄDARKI...");
            await CloseAsync(); // Używamy istniejącej, ulepszonej metody do posprzątania
            await LaunchNewBrowserAsync(); // Uruchamiamy całkowicie nową instancję
            Console.WriteLine("Pełny reset przeglądarki zakończony.");
        }

        // Nowa metoda do weryfikacji nawigacji
        private async Task<bool> TryNavigateAndVerifyUrlAsync(string targetUrl, string expectedUrlIdentifier, NavigationOptions options)
        {
            if (_page == null || _page.IsClosed)
            {
                throw new InvalidOperationException("Strona jest niedostępna. Reset może być konieczny.");
            }

            await _page.GoToAsync(targetUrl, options);

            // Prosta weryfikacja przekierowania na podstawie obecności ID produktu w URL
            if (_page.Url.Contains(expectedUrlIdentifier))
            {
                return true; // Sukces
            }
            else
            {
                Console.WriteLine($"Wykryto przekierowanie! Oczekiwano URL zawierającego '{expectedUrlIdentifier}', ale finalny URL to: '{_page.Url}'.");
                return false; // Porażka, przekierowanie
            }
        }

        // === GŁÓWNA METODA SCRAPUJĄCA Z NOWĄ LOGIKĄ ===

        public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct, string countryCode)
        {
            var scrapedData = new List<PriceData>();
            var storeBestOffers = new Dictionary<string, PriceData>();
            const int MAX_RETRIES_PER_PAGE = 2; // 1 próba + 2 ponowienia

            string productId = ExtractProductId(scrapingProduct.GoogleUrl);
            if (string.IsNullOrEmpty(productId))
            {
                Console.WriteLine($"Nie można wyodrębnić ID produktu z URL: {scrapingProduct.GoogleUrl}");
                return scrapedData;
            }

            bool hasNextPage = true;
            int totalOffersCount = 0;
            int currentPage = 0;
            var navOptions = new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } };

            try
            {
                while (hasNextPage && currentPage < 3)
                {
                    string paginatedUrl = $"https://www.google.com/shopping/product/{productId}/offers?prds=cid:{productId},cond:1,start:{currentPage * 20}&gl={countryCode}&hl=pl";
                    if (currentPage == 0)
                    {
                        paginatedUrl = $"https://www.google.com/shopping/product/{productId}/offers?prds=cid:{productId},cond:1&gl={countryCode}&hl=pl";
                    }

                    bool navigationSuccess = false;
                    for (int attempt = 0; attempt <= MAX_RETRIES_PER_PAGE; attempt++)
                    {
                        try
                        {
                            if (attempt > 0)
                            {
                                await ResetBrowserAndPageAsync(); // Reset całej przeglądarki
                            }

                            Console.WriteLine($"Nawigacja do strony {currentPage + 1} (Próba {attempt + 1}): {paginatedUrl}");

                            if (await TryNavigateAndVerifyUrlAsync(paginatedUrl, productId, navOptions))
                            {
                                navigationSuccess = true;
                                break; // Sukces! Wychodzimy z pętli ponawiania.
                            }
                        }
                        catch (Exception navEx)
                        {
                            Console.WriteLine($"Błąd krytyczny podczas próby nawigacji ({attempt + 1}): {navEx.Message}");
                        }
                    }

                    if (!navigationSuccess)
                    {
                        Console.WriteLine($"BŁĄD KRYTYCZNY: Nie udało się załadować strony dla produktu {productId} po {MAX_RETRIES_PER_PAGE + 1} próbach. Pomijam produkt.");
                        break; // Przerwij scraping tego produktu
                    }

                    // =========================================================================
                    // Oryginalny kod scrapujący, który wykona się po udanej nawigacji
                    // =========================================================================

                    await Task.Delay(1111);

                    var moreOffersButtons = await _page.QuerySelectorAllAsync("div.cNMlI");
                    if (moreOffersButtons.Length > 0)
                    {
                        foreach (var button in moreOffersButtons)
                        {
                            Console.WriteLine("Znaleziono przycisk 'Jeszcze oferty'. Klikam, aby rozwinąć.");
                            await button.ClickAsync();
                            await Task.Delay(557);
                        }
                    }

                    var offerRowsSelector = "#sh-osd__online-sellers-cont > tr";
                    var offerRows = await _page.QuerySelectorAllAsync(offerRowsSelector);
                    var offersCount = offerRows.Length;
                    totalOffersCount += offersCount;

                    if (offersCount == 0)
                    {
                        Console.WriteLine("Brak ofert na stronie.");
                        break;
                    }

                    Console.WriteLine($"Znaleziono {offersCount} ofert. Rozpoczynam scrapowanie...");

                    // Pętla po ofertach i cała reszta logiki pozostaje bez zmian...
                    for (int i = 1; i <= offersCount; i++)
                    {
                        var storeNameSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";
                        var priceSelector = "";
                        var priceWithDeliverySelector = "";

                        if (countryCode == "ua" || countryCode == "tr" || countryCode == "by")
                        {
                            priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(3) > span";
                            priceWithDeliverySelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > div > div.drzWO";
                        }
                        else
                        {
                            priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > span";
                            priceWithDeliverySelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(5) > div > div.drzWO";
                        }

                        var offerUrlSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";
                        var storeNameElement = await _page.QuerySelectorAsync(storeNameSelector);
                        if (storeNameElement != null)
                        {
                            var storeName = await storeNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
                            var priceElement = await _page.QuerySelectorAsync(priceSelector);
                            var priceText = priceElement != null ? await priceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "Brak ceny";
                            var priceWithDeliveryElement = await _page.QuerySelectorAsync(priceWithDeliverySelector);
                            var priceWithDeliveryText = priceWithDeliveryElement != null ? await priceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : priceText;
                            var priceDecimal = ExtractPrice(priceText);
                            var priceWithDeliveryDecimal = ExtractPrice(priceWithDeliveryText);
                            var offerUrlElement = await _page.QuerySelectorAsync(offerUrlSelector);
                            var offerUrl = offerUrlElement != null ? await offerUrlElement.EvaluateFunctionAsync<string>("node => node.href") : "Brak URL";

                            if (IsOutletOffer(offerUrl))
                            {
                                Console.WriteLine("Oferta outlet, pomijam.");
                                continue;
                            }

                            if (storeBestOffers.ContainsKey(storeName))
                            {
                                if (priceWithDeliveryDecimal < storeBestOffers[storeName].PriceWithDelivery)
                                {
                                    storeBestOffers[storeName].Price = priceDecimal;
                                    storeBestOffers[storeName].PriceWithDelivery = priceWithDeliveryDecimal;
                                }
                            }
                            else
                            {
                                storeBestOffers[storeName] = new PriceData { StoreName = storeName, Price = priceDecimal, PriceWithDelivery = priceWithDeliveryDecimal, OfferUrl = offerUrl, ScrapingProductId = scrapingProduct.ScrapingProductId, RegionId = scrapingProduct.RegionId };
                            }
                        }
                    }

                    // Zbieranie ofert ukrytych po rozwinięciu
                    var hiddenOfferRowsSelector = "tr.sh-osd__offer-row[data-hveid][data-is-grid-offer='true']";
                    var hiddenOfferRows = await _page.QuerySelectorAllAsync(hiddenOfferRowsSelector);
                    for (int j = 0; j < hiddenOfferRows.Length; j++)
                    {
                        var hiddenRowElement = hiddenOfferRows[j];


                        var hiddenStoreNameSelector = "td:nth-child(1) > div._-ez > a";
                        var hiddenPriceSelector = "";
                        var hiddenPriceWithDeliverySelector = "";
                        var hiddenOfferUrlSelector = "";

                        if (countryCode == "ua" || countryCode == "tr" || countryCode == "by")
                        {

                            hiddenPriceSelector = "td:nth-child(3) > span";
                            hiddenPriceWithDeliverySelector = "td:nth-child(4) > div";
                            hiddenOfferUrlSelector = "td:nth-child(5) > div > a";

                        }
                        else
                        {

                            hiddenPriceSelector = "td:nth-child(4) > span";
                            hiddenPriceWithDeliverySelector = "td:nth-child(5) > div";
                            hiddenOfferUrlSelector = "td:nth-child(6) > div > a";

                        }



                        var hiddenStoreNameElement = await hiddenRowElement.QuerySelectorAsync(hiddenStoreNameSelector);
                        if (hiddenStoreNameElement != null)
                        {
                            var hiddenStoreName = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
                            Console.WriteLine($"Znaleziono ukryty sklep: {hiddenStoreName}");

                            var hiddenPriceElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceSelector);
                            var hiddenPriceText = hiddenPriceElement != null ? await hiddenPriceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "Brak ceny";
                            Console.WriteLine($"Ukryta cena: {hiddenPriceText}");

                            var hiddenPriceWithDeliveryElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceWithDeliverySelector);
                            var hiddenPriceWithDeliveryText = hiddenPriceWithDeliveryElement != null ? await hiddenPriceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : hiddenPriceText;
                            Console.WriteLine($"Ukryta cena z dostawą: {hiddenPriceWithDeliveryText}");

                            var hiddenPriceDecimal = ExtractPrice(hiddenPriceText);
                            var hiddenPriceWithDeliveryDecimal = ExtractPrice(hiddenPriceWithDeliveryText);

                            // ... wcześniejszy kod w pętli ofert ukrytych
                            var hiddenOfferUrlElement = await hiddenRowElement.QuerySelectorAsync(hiddenOfferUrlSelector);
                            var hiddenOfferUrl = hiddenOfferUrlElement != null ? await hiddenOfferUrlElement.EvaluateFunctionAsync<string>("node => node.href") : "Brak URL";
                            Console.WriteLine($"Ukryty URL oferty: {hiddenOfferUrl}");

                            // Sprawdzamy, czy oferta nie jest outlet
                            if (IsOutletOffer(hiddenOfferUrl))
                            {
                                Console.WriteLine("Ukryta oferta outlet, pomijam.");
                                continue; // Pomijamy tę ofertę
                            }

                            if (storeBestOffers.ContainsKey(hiddenStoreName))
                            {
                                var existingOffer = storeBestOffers[hiddenStoreName];
                                if (hiddenPriceWithDeliveryDecimal < existingOffer.PriceWithDelivery)
                                {
                                    storeBestOffers[hiddenStoreName] = new PriceData
                                    {
                                        StoreName = hiddenStoreName,
                                        Price = hiddenPriceDecimal,
                                        PriceWithDelivery = hiddenPriceWithDeliveryDecimal,
                                        OfferUrl = hiddenOfferUrl,
                                        ScrapingProductId = scrapingProduct.ScrapingProductId,
                                        RegionId = scrapingProduct.RegionId,
                                    };
                                }
                            }
                            else
                            {
                                storeBestOffers[hiddenStoreName] = new PriceData
                                {
                                    StoreName = hiddenStoreName,
                                    Price = hiddenPriceDecimal,
                                    PriceWithDelivery = hiddenPriceWithDeliveryDecimal,
                                    OfferUrl = hiddenOfferUrl,
                                    ScrapingProductId = scrapingProduct.ScrapingProductId,
                                    RegionId = scrapingProduct.RegionId,
                                };
                            }

                        }
                    }

                    // Sprawdzenie, czy istnieje kolejna strona
                    var paginationElement = await _page.QuerySelectorAsync("#sh-fp__pagination-button-wrapper");
                    if (paginationElement != null)
                    {
                        var nextPageElement = await paginationElement.QuerySelectorAsync("a.internal-link[data-url*='start']");
                        if (nextPageElement != null)
                        {
                            currentPage++;
                            Console.WriteLine($"Przechodzę do następnej strony: {currentPage}");
                            hasNextPage = true;
                        }
                        else
                        {
                            Console.WriteLine("Brak kolejnej strony.");
                            hasNextPage = false;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Nie znaleziono elementu paginacji.");
                        hasNextPage = false;
                    }

                    await Task.Delay(125);
                }

                // Dodajemy wszystkie najlepsze oferty do listy scrapedData
                scrapedData.AddRange(storeBestOffers.Values);
                Console.WriteLine($"Zakończono przetwarzanie {scrapedData.Count} ofert.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas scrapowania: {ex.Message}");
            }

            scrapingProduct.OffersCount = totalOffersCount;
            return scrapedData;
        }


        private string ExtractProductId(string url)
        {
            // Dopasowanie do ciągu "/product/" a następnie numeru produktu
            var match = Regex.Match(url, @"product/(\d+)");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return string.Empty;
        }

        private bool IsOutletOffer(string url)
        {
            return !string.IsNullOrEmpty(url) && url.IndexOf("outlet", StringComparison.OrdinalIgnoreCase) >= 0;
        }



        private decimal ExtractPrice(string priceText)
        {
            try
            {

                var priceMatch = Regex.Match(priceText, @"[\d\s,]+");
                if (priceMatch.Success)
                {

                    var priceString = priceMatch.Value.Replace(" ", "").Replace(",", ".");
                    if (decimal.TryParse(priceString, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal priceDecimal))
                    {
                        return priceDecimal;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas przetwarzania ceny: {ex.Message}");
            }

            return 0;
        }




        public async Task CloseAsync()
        {
            try
            {
                if (_page != null && !_page.IsClosed)
                {
                    await _page.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Nieszkodliwy błąd podczas zamykania strony: {ex.Message}");
            }

            try
            {
                if (_browser != null && _browser.IsConnected)
                {
                    await _browser.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Nieszkodliwy błąd podczas zamykania przeglądarki: {ex.Message}");
            }
            Console.WriteLine("Zasoby przeglądarki zamknięte.");
        }
    }
}




