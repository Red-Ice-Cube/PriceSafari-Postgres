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

        public async Task<List<PriceData>> ScrapePricesAsync(GoogleScrapingProduct scrapingProduct)
        {
            var scrapedData = new List<PriceData>();
            var seenStoreNames = new HashSet<string>();

            // Pobieranie identyfikatora produktu z URL
            string productId = ExtractProductId(scrapingProduct.GoogleUrl);

            // Tworzenie URL z "/offers" oraz dynamicznym parametrem ?prds=cid:<productId>,cond:1
            string productOffersUrl = $"{scrapingProduct.GoogleUrl}/offers?prds=cid:{productId},cond:1";
            string googleBaseUrl = "https://www.google.com";
            bool hasNextPage = true;
            int totalOffersCount = 0;

            try
            {
                while (hasNextPage)
                {
                    Console.WriteLine($"Odwiedzanie URL: {productOffersUrl}");
                    await _page.GoToAsync(productOffersUrl, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
                    await Task.Delay(50);

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
                        var offerUrlSelector = $"#sh-osd__online-sellers-cont > tr:nth-child({i}) > td:nth-child(1) > div.kPMwsc > a";

                        var storeNameElement = await _page.QuerySelectorAsync(storeNameSelector);
                        if (storeNameElement != null)
                        {
                            var storeName = await storeNameElement.EvaluateFunctionAsync<string>("node => node.firstChild.textContent.trim()");
                            Console.WriteLine($"Znaleziono sklep: {storeName}");

                            if (seenStoreNames.Contains(storeName))
                            {
                                Console.WriteLine("Znaleziono już zebrany sklep. Zakończono scrapowanie dla tego produktu.");
                                return scrapedData;
                            }

                            seenStoreNames.Add(storeName);

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

                            scrapedData.Add(new PriceData
                            {
                                StoreName = storeName,
                                Price = priceDecimal,
                                PriceWithDelivery = priceWithDeliveryDecimal,
                                OfferUrl = offerUrl,
                                ScrapingProductId = scrapingProduct.ScrapingProductId,
                                RegionId = scrapingProduct.RegionId
                            });

                            await Task.Delay(100);
                        }
                        else
                        {
                            Console.WriteLine($"Nie znaleziono elementu nazwy sklepu w wierszu {i}.");
                        }
                    }

                    var paginationElement = await _page.QuerySelectorAsync("#sh-fp__pagination-button-wrapper");
                    if (paginationElement != null)
                    {
                        Console.WriteLine("Znaleziono element paginacji.");
                        var nextPageElement = await paginationElement.QuerySelectorAsync("a[aria-label='Next'], span.R9e18b a.internal-link");

                        if (nextPageElement != null)
                        {
                            string nextPageUrl = await nextPageElement.EvaluateFunctionAsync<string>("node => node.getAttribute('data-url') || node.href");

                            if (!nextPageUrl.StartsWith("http"))
                            {
                                nextPageUrl = googleBaseUrl + nextPageUrl;
                            }

                            productOffersUrl = nextPageUrl;
                            Console.WriteLine($"Przechodzę do następnej strony: {productOffersUrl}");
                            hasNextPage = true;
                        }
                        else
                        {
                            Console.WriteLine("Brak przycisku 'Next'.");
                            hasNextPage = false;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Nie znaleziono elementu paginacji.");
                        hasNextPage = false;
                    }

                    await Task.Delay(50);
                }

                Console.WriteLine($"Zakończono przetwarzanie {scrapedData.Count} ofert.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas scrapowania: {ex.Message}");
            }

            scrapingProduct.OffersCount = totalOffersCount;

            return scrapedData;
        }

        // Funkcja pomocnicza do wyciągania identyfikatora produktu z URL
        private string ExtractProductId(string url)
        {
            // Zakładam, że URL ma format: https://www.google.com/shopping/product/<productId>/offers
            var match = Regex.Match(url, @"product/(\d+)/offers");
            if (match.Success)
            {
                return match.Groups[1].Value; // Zwracamy productId
            }
            return string.Empty; // Zwracamy pusty string, jeśli nie znaleziono
        }



        // Funkcja pomocnicza do wyciągania liczbowej części ceny
        private decimal ExtractPrice(string priceText)
        {
            try
            {
                // Wyciąganie liczby z tekstu ceny, uwzględniającej separatory tysięcy (spacje) oraz przecinek lub kropkę jako separator dziesiętny
                var priceMatch = Regex.Match(priceText, @"[\d\s,]+");
                if (priceMatch.Success)
                {
                    // Zamiana spacji (separator tysięcy) oraz przecinka na kropkę (separator dziesiętny)
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

            return 0; // Zwracamy 0, jeśli nie udało się przetworzyć ceny
        }




        // Zamknięcie przeglądarki
        public async Task CloseAsync()
        {
            await _page.CloseAsync();
            await _browser.CloseAsync();
            Console.WriteLine("Przeglądarka zamknięta.");
        }
    }
}
