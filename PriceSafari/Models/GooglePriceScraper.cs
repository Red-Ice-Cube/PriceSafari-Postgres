using PriceSafari.Models;
using PuppeteerSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PriceSafari.Services
{
    public class GooglePriceScraper
    {
        private Browser _browser;
        private Page _page;

        // Metoda do inicjalizacji przeglądarki
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
            //await _page.SetJavaScriptEnabledAsync(settings.JavaScript);
            Console.WriteLine("Przeglądarka zainicjalizowana.");
        }



        public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct, string countryCode)
        {
            var scrapedData = new List<PriceData>();
            var storeBestOffers = new Dictionary<string, PriceData>();

            // Wyciągamy productId z URL
            string productId = ExtractProductId(scrapingProduct.GoogleUrl);

            // Tworzymy URL na pierwszą stronę
            string productOffersUrl = $"{scrapingProduct.GoogleUrl}/offers?prds=cid:{productId},cond:1&gl={countryCode}&hl=pl";
            bool hasNextPage = true;
            int totalOffersCount = 0;
            int currentPage = 0;

            try
            {
                while (hasNextPage && currentPage < 3)
                {
                    string paginatedUrl = currentPage == 0
                        ? productOffersUrl
                        : $"{scrapingProduct.GoogleUrl}/offers?prds=cid:{productId},cond:1,start:{currentPage * 20}&gl={countryCode}&hl=pl";

                    Console.WriteLine($"Odwiedzanie URL: {paginatedUrl}");
                    await _page.GoToAsync(paginatedUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
                    await Task.Delay(50);

                    var moreOffersButtons = await _page.QuerySelectorAllAsync("div.cNMlI");
                    if (moreOffersButtons.Length > 0)
                    {
                        foreach (var button in moreOffersButtons)
                        {
                            Console.WriteLine("Znaleziono przycisk 'Jeszcze oferty'. Klikam, aby rozwinąć.");
                            await button.ClickAsync();
                            await Task.Delay(300);
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

                    for (int i = 1; i <= offersCount; i++)
                    {
                        var storeNameSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";
                        var priceSelector = "";
                        var priceWithDeliverySelector = "";

                        // Warunkowe sprawdzanie selektorów w zależności od kraju
                        if (countryCode == "ua" || countryCode == "tr" || countryCode == "by")
                        {
                            // Dla Ukrainy i Turcji
                            priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(3) > span";
                            priceWithDeliverySelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > div > div.drzWO";
                        }
                        else
                        {
                            // Dla innych krajów
                            priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > span";
                            priceWithDeliverySelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(5) > div > div.drzWO";
                        }

                        var offerUrlSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";

                        var storeNameElement = await _page.QuerySelectorAsync(storeNameSelector);
                        if (storeNameElement != null)
                        {
                            var storeName = await storeNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
                            Console.WriteLine($"Znaleziono sklep: {storeName}");

                            var priceElement = await _page.QuerySelectorAsync(priceSelector);
                            var priceText = priceElement != null ? await priceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "Brak ceny";
                            Console.WriteLine($"Cena: {priceText}");

                            var priceWithDeliveryElement = await _page.QuerySelectorAsync(priceWithDeliverySelector);
                            var priceWithDeliveryText = priceWithDeliveryElement != null ? await priceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : priceText;
                            Console.WriteLine($"Cena z dostawą: {priceWithDeliveryText}");

                            var priceDecimal = ExtractPrice(priceText);
                            var priceWithDeliveryDecimal = ExtractPrice(priceWithDeliveryText);

                            var offerUrlElement = await _page.QuerySelectorAsync(offerUrlSelector);
                            var offerUrl = offerUrlElement != null ? await offerUrlElement.EvaluateFunctionAsync<string>("node => node.href") : "Brak URL";
                            Console.WriteLine($"URL oferty: {offerUrl}");

                            if (storeBestOffers.ContainsKey(storeName))
                            {
                                var existingOffer = storeBestOffers[storeName];
                                if (priceWithDeliveryDecimal < existingOffer.PriceWithDelivery)
                                {
                                    storeBestOffers[storeName] = new PriceData
                                    {
                                        StoreName = storeName,
                                        Price = priceDecimal,
                                        PriceWithDelivery = priceWithDeliveryDecimal,
                                        OfferUrl = offerUrl,
                                        ScrapingProductId = scrapingProduct.ScrapingProductId,
                                        RegionId = scrapingProduct.RegionId,
                                        RawPriceText = priceText, // Dodajemy surowy tekst ceny
                                        RawPriceWithDeliveryText = priceWithDeliveryText // Dodajemy surowy tekst ceny z dostawą
                                    };

                                }
                            }
                            else
                            {
                                // Dodajemy nową ofertę do słownika
                                storeBestOffers[storeName] = new PriceData
                                {
                                    StoreName = storeName,
                                    Price = priceDecimal,
                                    PriceWithDelivery = priceWithDeliveryDecimal,
                                    OfferUrl = offerUrl,
                                    ScrapingProductId = scrapingProduct.ScrapingProductId,
                                    RegionId = scrapingProduct.RegionId,
                                    RawPriceText = priceText, // Dodajemy surowy tekst ceny
                                    RawPriceWithDeliveryText = priceWithDeliveryText // Dodajemy surowy tekst ceny z dostawą
                                };
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Nie znaleziono elementu nazwy sklepu w wierszu {i}.");
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

                            var hiddenOfferUrlElement = await hiddenRowElement.QuerySelectorAsync(hiddenOfferUrlSelector);
                            var hiddenOfferUrl = hiddenOfferUrlElement != null ? await hiddenOfferUrlElement.EvaluateFunctionAsync<string>("node => node.href") : "Brak URL";
                            Console.WriteLine($"Ukryty URL oferty: {hiddenOfferUrl}");

                            // Dodanie do logu informacji o znalezionej ofercie ukrytej
                            Console.WriteLine($"Oferta ukryta -> Sklep: {hiddenStoreName}, Cena: {hiddenPriceText}, URL: {hiddenOfferUrl}");

                            // Sprawdzenie, czy oferta ukryta dla tego sklepu jest lepsza
                            if (storeBestOffers.ContainsKey(hiddenStoreName))
                            {
                                var existingOffer = storeBestOffers[hiddenStoreName];
                                if (hiddenPriceWithDeliveryDecimal < existingOffer.PriceWithDelivery)
                                {
                                    // Zastępujemy ofertę, jeśli ukryta oferta ma niższą cenę z dostawą
                                    storeBestOffers[hiddenStoreName] = new PriceData
                                    {
                                        StoreName = hiddenStoreName,
                                        Price = hiddenPriceDecimal,
                                        PriceWithDelivery = hiddenPriceWithDeliveryDecimal,
                                        OfferUrl = hiddenOfferUrl,
                                        ScrapingProductId = scrapingProduct.ScrapingProductId,
                                        RegionId = scrapingProduct.RegionId,
                                        RawPriceText = hiddenPriceText, // Surowy tekst ukrytej ceny
                                        RawPriceWithDeliveryText = hiddenPriceWithDeliveryText // Surowy tekst ukrytej ceny z dostawą
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
                                    RawPriceText = hiddenPriceText, // Surowy tekst ukrytej ceny
                                    RawPriceWithDeliveryText = hiddenPriceWithDeliveryText // Surowy tekst ukrytej ceny z dostawą
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

                    await Task.Delay(10);
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
            await _page.CloseAsync();
            await _browser.CloseAsync();
            Console.WriteLine("Przeglądarka zamknięta.");
        }
    }
}




//public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct, string countryCode)
//{
//    var scrapedData = new List<PriceData>();

//    // Wyciągamy productId z URL
//    string productId = ExtractProductId(scrapingProduct.GoogleUrl);

//    // Tworzymy URL na pierwszą stronę
//    string productOffersUrl = $"{scrapingProduct.GoogleUrl}/offers?prds=cid:{productId},cond:1&gl={countryCode}&hl=pl";
//    bool hasNextPage = true;
//    int totalOffersCount = 0;
//    int currentPage = 0;

//    try
//    {
//        while (hasNextPage && currentPage < 3)
//        {
//            string paginatedUrl = currentPage == 0
//                ? productOffersUrl
//                : $"{scrapingProduct.GoogleUrl}/offers?prds=cid:{productId},cond:1,start:{currentPage * 20}&gl={countryCode}&hl=pl";

//            Console.WriteLine($"Odwiedzanie URL: {paginatedUrl}");
//            await _page.GoToAsync(paginatedUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
//            await Task.Delay(50);

//            var moreOffersButtons = await _page.QuerySelectorAllAsync("div.cNMlI");
//            if (moreOffersButtons.Length > 0)
//            {
//                foreach (var button in moreOffersButtons)
//                {
//                    Console.WriteLine("Znaleziono przycisk 'Jeszcze oferty'. Klikam, aby rozwinąć.");
//                    await button.ClickAsync();
//                    await Task.Delay(300);
//                }
//            }

//            var offerRowsSelector = "#sh-osd__online-sellers-cont > tr";
//            var offerRows = await _page.QuerySelectorAllAsync(offerRowsSelector);
//            var offersCount = offerRows.Length;
//            totalOffersCount += offersCount;

//            if (offersCount == 0)
//            {
//                Console.WriteLine("Brak ofert na stronie.");
//                break;
//            }

//            Console.WriteLine($"Znaleziono {offersCount} ofert. Rozpoczynam scrapowanie...");

//            for (int i = 1; i <= offersCount; i++)
//            {
//                var storeNameSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";
//                var priceSelector = countryCode == "ua" || countryCode == "tr" || countryCode == "by"
//                    ? $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(3) > span"
//                    : $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > span";
//                var priceWithDeliverySelector = countryCode == "ua" || countryCode == "tr" || countryCode == "by"
//                    ? $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > div > div.drzWO"
//                    : $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(5) > div > div.drzWO";
//                var offerUrlSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";

//                var storeNameElement = await _page.QuerySelectorAsync(storeNameSelector);
//                if (storeNameElement != null)
//                {
//                    var storeName = await storeNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
//                    Console.WriteLine($"Znaleziono sklep: {storeName}");

//                    var priceElement = await _page.QuerySelectorAsync(priceSelector);
//                    var priceText = priceElement != null ? await priceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "Brak ceny";
//                    Console.WriteLine($"Cena: {priceText}");

//                    var priceWithDeliveryElement = await _page.QuerySelectorAsync(priceWithDeliverySelector);
//                    var priceWithDeliveryText = priceWithDeliveryElement != null ? await priceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : priceText;
//                    Console.WriteLine($"Cena z dostawą: {priceWithDeliveryText}");

//                    var priceDecimal = ExtractPrice(priceText);
//                    var priceWithDeliveryDecimal = ExtractPrice(priceWithDeliveryText);

//                    var offerUrlElement = await _page.QuerySelectorAsync(offerUrlSelector);
//                    var offerUrl = offerUrlElement != null ? await offerUrlElement.EvaluateFunctionAsync<string>("node => node.href") : "Brak URL";
//                    Console.WriteLine($"URL oferty: {offerUrl}");

//                    scrapedData.Add(new PriceData
//                    {
//                        StoreName = storeName,
//                        Price = priceDecimal,
//                        PriceWithDelivery = priceWithDeliveryDecimal,
//                        OfferUrl = offerUrl,
//                        ScrapingProductId = scrapingProduct.ScrapingProductId,
//                        RegionId = scrapingProduct.RegionId,
//                        RawPriceText = priceText, // Surowy tekst ceny
//                        RawPriceWithDeliveryText = priceWithDeliveryText // Surowy tekst ceny z dostawą
//                    });
//                }
//                else
//                {
//                    Console.WriteLine($"Nie znaleziono elementu nazwy sklepu w wierszu {i}.");
//                }
//            }

//            // Zbieranie ofert ukrytych po rozwinięciu
//            var hiddenOfferRowsSelector = "tr.sh-osd__offer-row[data-hveid][data-is-grid-offer='true']";
//            var hiddenOfferRows = await _page.QuerySelectorAllAsync(hiddenOfferRowsSelector);
//            for (int j = 0; j < hiddenOfferRows.Length; j++)
//            {
//                var hiddenRowElement = hiddenOfferRows[j];
//                var hiddenStoreNameSelector = "td:nth-child(1) > div._-ez > a";
//                var hiddenPriceSelector = countryCode == "ua" || countryCode == "tr" || countryCode == "by"
//                    ? "td:nth-child(3) > span"
//                    : "td:nth-child(4) > span";
//                var hiddenPriceWithDeliverySelector = countryCode == "ua" || countryCode == "tr" || countryCode == "by"
//                    ? "td:nth-child(4) > div"
//                    : "td:nth-child(5) > div";
//                var hiddenOfferUrlSelector = countryCode == "ua" || countryCode == "tr" || countryCode == "by"
//                    ? "td:nth-child(5) > div > a"
//                    : "td:nth-child(6) > div > a";

//                var hiddenStoreNameElement = await hiddenRowElement.QuerySelectorAsync(hiddenStoreNameSelector);
//                if (hiddenStoreNameElement != null)
//                {
//                    var hiddenStoreName = await hiddenStoreNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
//                    Console.WriteLine($"Znaleziono ukryty sklep: {hiddenStoreName}");

//                    var hiddenPriceElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceSelector);
//                    var hiddenPriceText = hiddenPriceElement != null ? await hiddenPriceElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : "Brak ceny";
//                    Console.WriteLine($"Ukryta cena: {hiddenPriceText}");

//                    var hiddenPriceWithDeliveryElement = await hiddenRowElement.QuerySelectorAsync(hiddenPriceWithDeliverySelector);
//                    var hiddenPriceWithDeliveryText = hiddenPriceWithDeliveryElement != null ? await hiddenPriceWithDeliveryElement.EvaluateFunctionAsync<string>("node => node.textContent.trim()") : hiddenPriceText;
//                    Console.WriteLine($"Ukryta cena z dostawą: {hiddenPriceWithDeliveryText}");

//                    var hiddenPriceDecimal = ExtractPrice(hiddenPriceText);
//                    var hiddenPriceWithDeliveryDecimal = ExtractPrice(hiddenPriceWithDeliveryText);

//                    var hiddenOfferUrlElement = await hiddenRowElement.QuerySelectorAsync(hiddenOfferUrlSelector);
//                    var hiddenOfferUrl = hiddenOfferUrlElement != null ? await hiddenOfferUrlElement.EvaluateFunctionAsync<string>("node => node.href") : "Brak URL";
//                    Console.WriteLine($"Ukryty URL oferty: {hiddenOfferUrl}");

//                    scrapedData.Add(new PriceData
//                    {
//                        StoreName = hiddenStoreName,
//                        Price = hiddenPriceDecimal,
//                        PriceWithDelivery = hiddenPriceWithDeliveryDecimal,
//                        OfferUrl = hiddenOfferUrl,
//                        ScrapingProductId = scrapingProduct.ScrapingProductId,
//                        RegionId = scrapingProduct.RegionId,
//                        RawPriceText = hiddenPriceText, // Surowy tekst ukrytej ceny
//                        RawPriceWithDeliveryText = hiddenPriceWithDeliveryText // Surowy tekst ukrytej ceny z dostawą
//                    });
//                }
//            }

//            // Sprawdzenie, czy istnieje kolejna strona
//            var paginationElement = await _page.QuerySelectorAsync("#sh-fp__pagination-button-wrapper");
//            if (paginationElement != null)
//            {
//                var nextPageElement = await paginationElement.QuerySelectorAsync("a.internal-link[data-url*='start']");
//                if (nextPageElement != null)
//                {
//                    currentPage++;
//                    Console.WriteLine($"Przechodzę do następnej strony: {currentPage}");
//                    hasNextPage = true;
//                }
//                else
//                {
//                    Console.WriteLine("Brak kolejnej strony.");
//                    hasNextPage = false;
//                }
//            }
//            else
//            {
//                Console.WriteLine("Nie znaleziono elementu paginacji.");
//                hasNextPage = false;
//            }

//            await Task.Delay(10);
//        }

//        Console.WriteLine($"Zakończono przetwarzanie {scrapedData.Count} ofert.");
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine($"Błąd podczas scrapowania: {ex.Message}");
//    }

//    scrapingProduct.OffersCount = totalOffersCount;
//    return scrapedData;
//}
