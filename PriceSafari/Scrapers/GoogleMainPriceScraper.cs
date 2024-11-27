using PriceSafari.Models;
using PuppeteerSharp;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

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
        Console.WriteLine("Przeglądarka zainicjalizowana.");
    }

    public async Task<List<CoOfrPriceHistoryClass>> ScrapePricesAsync(string googleOfferUrl)
    {
        var scrapedData = new List<CoOfrPriceHistoryClass>();
        var storeBestOffers = new Dictionary<string, CoOfrPriceHistoryClass>();

        // Inicjalizacja URL na pierwszą stronę
        string nextPageUrl = googleOfferUrl;
        bool hasNextPage = true;
        int totalOffersCount = 0;
        int currentPage = 0;

        try
        {
            while (hasNextPage && currentPage < 3)
            {
                Console.WriteLine($"Odwiedzanie URL: {nextPageUrl}");
                await _page.GoToAsync(nextPageUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
                await Task.Delay(1000);

                // Kliknij przyciski "Jeszcze oferty"
                var moreOffersButtons = await _page.QuerySelectorAllAsync("div.cNMlI");
                foreach (var button in moreOffersButtons)
                {
                    Console.WriteLine("Znaleziono przycisk 'Jeszcze oferty'. Klikam, aby rozwinąć.");
                    await button.ClickAsync();
                    await Task.Delay(500);
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
                    var priceSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(4) > span";
                    var priceWithDeliverySelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(5) > div > div.drzWO";
                    var offerUrlSelector = storeNameSelector;

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
                            if (priceWithDeliveryDecimal < existingOffer.GooglePriceWithDelivery)
                            {
                                storeBestOffers[storeName] = new CoOfrPriceHistoryClass
                                {
                                    GoogleStoreName = storeName,
                                    GooglePrice = priceDecimal,
                                    GooglePriceWithDelivery = priceWithDeliveryDecimal,
                                    GoogleOfferUrl = offerUrl,
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
                                GoogleOfferUrl = offerUrl,
                            };
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Nie znaleziono elementu nazwy sklepu w wierszu {i}.");
                    }
                }

                // Scraping ukrytych ofert po rozwinięciu
                var hiddenOfferRowsSelector = "tr.sh-osd__offer-row[data-hveid][data-is-grid-offer='true']";
                var hiddenOfferRows = await _page.QuerySelectorAllAsync(hiddenOfferRowsSelector);
                for (int j = 0; j < hiddenOfferRows.Length; j++)
                {
                    var hiddenRowElement = hiddenOfferRows[j];

                    var hiddenStoreNameSelector = "td:nth-child(1) > div._-ez > a";
                    var hiddenPriceSelector = "td:nth-child(4) > span";
                    var hiddenPriceWithDeliverySelector = "td:nth-child(5) > div";
                    var hiddenOfferUrlSelector = hiddenStoreNameSelector;

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

                        // Sprawdzenie, czy oferta ukryta dla tego sklepu jest lepsza
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
                                    GoogleOfferUrl = hiddenOfferUrl,
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
                                GoogleOfferUrl = hiddenOfferUrl,
                            };
                        }
                    }
                }

                // Sprawdzenie, czy istnieje kolejna strona
                var nextPageElement = await _page.QuerySelectorAsync("a.sh-fp__pagination-button[aria-label='Następna strona']");
                if (nextPageElement != null)
                {
                    nextPageUrl = await nextPageElement.EvaluateFunctionAsync<string>("node => node.href");
                    currentPage++;
                    Console.WriteLine($"Przechodzę do następnej strony: {currentPage}");
                    hasNextPage = true;
                }
                else
                {
                    Console.WriteLine("Brak kolejnej strony.");
                    hasNextPage = false;
                }

                await Task.Delay(500);
            }

            // Dodajemy wszystkie najlepsze oferty do listy scrapedData
            scrapedData.AddRange(storeBestOffers.Values);
            Console.WriteLine($"Zakończono przetwarzanie {scrapedData.Count} ofert.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Błąd podczas scrapowania: {ex.Message}");
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
